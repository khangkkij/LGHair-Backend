using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace MinhTamHair.Services;

/// <summary>
/// Giá trị trả về của GetOrCreateJsonAsync khi factory() trả null (không tìm thấy dữ liệu).
/// Dùng chung với ProductCacheConstants.NullMarker (cùng giá trị "__NULL__") nhưng khai báo
/// riêng để ServiceCacheService không phải phụ thuộc ngược vào namespace của module Sản phẩm.
/// </summary>
public static class ServiceCacheConstants
{
    public const string NullMarker = "__NULL__";
}

public interface IServiceCacheService
{
    /// <summary>
    /// Lấy JSON đã cache theo key (nếu có), hoặc chạy factory rồi cache lại chuỗi JSON kết quả.
    /// Trả về CHUỖI JSON THÔ (không phải object C#) — controller ghi thẳng chuỗi này ra response
    /// bằng "return Content(json, "application/json")" thay vì "return Ok(obj)".
    /// Cách này AN TOÀN với anonymous type (new { ... }), vì JsonSerializer.Deserialize&lt;T&gt;
    /// không tái tạo được anonymous type, nên nếu cache theo object sẽ throw exception ngay
    /// từ lần cache-hit thứ 2 trở đi.
    /// Nếu factory() trả null, phương thức trả về đúng chuỗi ServiceCacheConstants.NullMarker
    /// (không lưu vào Redis).
    /// </summary>
    Task<string> GetOrCreateJsonAsync(string key, Func<Task<object?>> factory, TimeSpan? ttl = null);

    /// <summary>
    /// Xóa toàn bộ cache khớp với 1 prefix bằng lệnh SCAN/KEYS. CẢNH BÁO: nhiều Redis managed
    /// service (đặc biệt Upstash ở chế độ mặc định) KHÔNG hỗ trợ đầy đủ SCAN/KEYS qua GetServer()
    /// — lệnh có thể bị từ chối hoặc âm thầm trả về rỗng, khiến hàm này "chạy xong" nhưng không
    /// xóa được gì. Vì vậy controller nên dùng InvalidateAllServiceCacheAsync() (cơ chế version)
    /// thay vì gọi trực tiếp hàm này. Giữ lại phương thức này cho Redis hỗ trợ SCAN đầy đủ
    /// (VD self-host).
    /// </summary>
    Task RemoveByPrefixAsync(string prefix);

    /// <summary>
    /// Đánh dấu toàn bộ cache liên quan tới module Dịch vụ là cũ, bằng cách tăng 1 số đếm version
    /// dùng chung (KHÔNG quét/xóa key — tương thích với Upstash và mọi Redis managed service khác,
    /// vì chỉ cần lệnh INCR/GET cơ bản). Mọi cache key ở DanhSachDichVuController và
    /// DanhMucDVController phải nhúng version hiện tại vào key (VD "sv:v7:getall:...") — sau khi
    /// version tăng, các key cũ tự "mồ côi" và tự hết hạn theo TTL, không ai đọc lại chúng nữa.
    /// Gọi hàm này sau mọi thao tác Create/Update/Delete trên Dịch vụ, Combo, Danh mục dịch vụ,
    /// hoặc Tiêu đề dịch vụ — vì các loại dữ liệu này luôn xuất hiện lồng nhau trong response.
    /// </summary>
    Task InvalidateAllServiceCacheAsync();

    /// <summary>
    /// Đọc version cache hiện tại (mặc định 1 nếu chưa từng invalidate lần nào). Dùng để build
    /// cache key ở DanhSachDichVuController/DanhMucDVController — mỗi khi version đổi, toàn bộ
    /// key cũ tự động không còn được đọc/ghi tới nữa dù vẫn còn nằm trong Redis tới khi hết TTL.
    /// </summary>
    Task<long> GetCurrentVersionAsync();
}

public class ServiceCacheService : IServiceCacheService
{
    // Prefix chung cho MỌI cache liên quan dịch vụ — tách riêng khỏi "sp:" (Sản phẩm) để
    // RemoveByPrefixAsync của 2 module không bao giờ đụng nhầm cache của nhau
    // (chỉ dùng được trên Redis hỗ trợ SCAN đầy đủ — xem cảnh báo ở RemoveByPrefixAsync).
    public const string RootPrefix = "sv:";

    // Key lưu số đếm version — KHÔNG có RootPrefix ở trước vì bản thân nó không phải cache dữ
    // liệu dịch vụ, mà là bộ đếm điều khiển việc cache dữ liệu dịch vụ.
    private const string VersionKey = "sv:cache-version";

    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    // PHẢI khớp với PropertyNamingPolicy = CamelCase đang cấu hình ở Program.cs
    // (builder.Services.AddControllers().AddJsonOptions(...)). Vì controller trả cache bằng
    // Content(json, "application/json") — KHÔNG đi qua pipeline serialize mặc định của MVC —
    // nên nếu dùng default JsonSerializer.Serialize (PascalCase) thì response cache-hit sẽ có
    // field dạng "TenDv" còn response cache-miss lại là "tenDv", làm Frontend Vue đọc sai/thiếu
    // field tùy lúc cache hit hay miss.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServiceCacheService(IDistributedCache cache, IConnectionMultiplexer redis)
    {
        _cache = cache;
        _redis = redis;
    }

    public async Task<string> GetOrCreateJsonAsync(string key, Func<Task<object?>> factory, TimeSpan? ttl = null)
    {
        var cached = await _cache.GetStringAsync(key);
        if (!string.IsNullOrEmpty(cached))
            return cached;

        var fresh = await factory();

        if (fresh == null)
            return ServiceCacheConstants.NullMarker;

        var payload = JsonSerializer.Serialize(fresh, JsonOptions);
        await _cache.SetStringAsync(key, payload, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
        });

        return payload;
    }

    public async Task RemoveByPrefixAsync(string prefix)
    {
        try
        {
            var endpoints = _redis.GetEndPoints();
            if (endpoints.Length == 0) return;

            var server = _redis.GetServer(endpoints[0]);
            var db = _redis.GetDatabase();

            // InstanceName "MinhTamHair:" được StackExchangeRedisCache tự thêm vào trước mọi key,
            // nên khi quét bằng SCAN phải khớp luôn phần prefix đó.
            var pattern = $"MinhTamHair:{prefix}*";

            var keysToDelete = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                keysToDelete.Add(key);
            }

            if (keysToDelete.Count > 0)
                await db.KeyDeleteAsync(keysToDelete.ToArray());
        }
        catch (Exception)
        {
            // Nhiều Redis managed service (Upstash ở chế độ mặc định...) từ chối lệnh SCAN/KEYS
            // qua GetServer(). KHÔNG để lỗi này làm crash luồng CRUD chính (Create/Update/Delete
            // dịch vụ/combo vẫn phải lưu DB thành công dù việc dọn cache thất bại) — nuốt lỗi ở đây.
            // Nếu cần theo dõi, có thể inject ILogger và log warning ở nhánh catch này.
        }
    }

    public async Task InvalidateAllServiceCacheAsync()
    {
        var db = _redis.GetDatabase();
        // INCR là lệnh cơ bản mọi Redis managed service (kể cả Upstash) đều hỗ trợ đầy đủ —
        // không cần quét/xóa key gì cả. Toàn bộ cache key cũ (đang nhúng version cũ trong tên
        // key) sẽ tự "mồ côi" ngay sau lệnh này, dù vẫn còn nằm trong Redis tới khi hết TTL.
        await db.StringIncrementAsync(VersionKey);
    }

    public async Task<long> GetCurrentVersionAsync()
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(VersionKey);
        // Chưa từng invalidate lần nào (key không tồn tại) -> coi như version 1.
        return value.HasValue ? (long)value : 1L;
    }
}
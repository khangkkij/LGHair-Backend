using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace MinhTamHair.SanPham.Controllers;

/// <summary>
/// Giá trị trả về của GetOrCreateJsonAsync khi factory() trả null (không tìm thấy dữ liệu).
/// Controller so sánh chuỗi trả về với hằng số này để biết khi nào trả NotFound thay vì Content(json).
/// Đặt ở class riêng (không phải trong interface) vì C# không hỗ trợ hằng số instance trong interface.
/// </summary>
public static class ProductCacheConstants
{
    public const string NullMarker = "__NULL__";
}

public interface IProductCacheService
{
    /// <summary>
    /// Lấy JSON đã cache theo key (nếu có), hoặc chạy factory rồi cache lại chuỗi JSON kết quả.
    /// Trả về CHUỖI JSON THÔ (không phải object C#) — controller ghi thẳng chuỗi này ra response
    /// bằng "return Content(json, "application/json")" thay vì "return Ok(obj)".
    /// Cách này AN TOÀN với anonymous type (new { ... }), vì never phải deserialize ngược lại
    /// thành 1 kiểu C# cụ thể — JsonSerializer.Deserialize&lt;T&gt; không tái tạo được anonymous type,
    /// nên nếu cache theo object sẽ throw exception ngay từ lần cache-hit thứ 2 trở đi.
    /// Nếu factory() trả null, phương thức trả về đúng chuỗi NullMarker (không lưu vào Redis).
    /// </summary>
    Task<string> GetOrCreateJsonAsync(string key, Func<Task<object?>> factory, TimeSpan? ttl = null);

    /// <summary>
    /// Xóa toàn bộ cache khớp với 1 prefix bằng lệnh SCAN/KEYS (VD: "sp:filter:" sẽ xóa mọi biến
    /// thể filter/phân trang). CẢNH BÁO: nhiều Redis managed service (đặc biệt Upstash ở chế độ
    /// mặc định) KHÔNG hỗ trợ đầy đủ SCAN/KEYS qua GetServer() — lệnh có thể bị từ chối hoặc âm
    /// thầm trả về rỗng, khiến hàm này "chạy xong" nhưng không xóa được gì. Vì vậy controller nên
    /// dùng InvalidateAllProductCacheAsync() (cơ chế version) thay vì gọi trực tiếp hàm này.
    /// Giữ lại phương thức này cho các trường hợp dùng Redis hỗ trợ SCAN đầy đủ (VD self-host).
    /// </summary>
    Task RemoveByPrefixAsync(string prefix);

    /// <summary>
    /// Đánh dấu toàn bộ cache liên quan tới module Sản phẩm là cũ, bằng cách tăng 1 số đếm version
    /// dùng chung (KHÔNG quét/xóa key — tương thích với Upstash và mọi Redis managed service khác,
    /// vì chỉ cần lệnh INCR/GET cơ bản). Mọi cache key ở SanPhamController phải nhúng version hiện
    /// tại vào key (VD "sp:v7:init") — sau khi version tăng, các key cũ tự "mồ côi" và tự hết hạn
    /// theo TTL, không ai đọc lại chúng nữa. Gọi hàm này sau mọi thao tác Create/Update/Delete/
    /// ToggleStatus trên sản phẩm HOẶC biến thể.
    /// </summary>
    Task InvalidateAllProductCacheAsync();

    /// <summary>
    /// Đọc version cache hiện tại (mặc định 1 nếu chưa từng invalidate lần nào). Dùng để build
    /// cache key ở SanPhamController — mỗi khi version đổi, toàn bộ key cũ tự động không còn được
    /// đọc/ghi tới nữa dù vẫn còn nằm trong Redis tới khi hết TTL.
    /// </summary>
    Task<long> GetCurrentVersionAsync();

    /// <summary>
    /// Đánh dấu toàn bộ cache liên quan tới module Danh mục sản phẩm là cũ (cùng cơ chế version
    /// như InvalidateAllProductCacheAsync, nhưng đếm RIÊNG — sửa 1 sản phẩm không cần bump version
    /// danh mục và ngược lại, tránh cache-miss dư thừa ở phần không hề thay đổi). Gọi sau mọi thao
    /// tác Create/Update/Delete/ToggleStatus/đổi-thứ-tự trên danh mục HOẶC banner của danh mục.
    /// </summary>
    Task InvalidateAllDanhMucCacheAsync();

    /// <summary>
    /// Đọc version cache hiện tại của module Danh mục (mặc định 1 nếu chưa từng invalidate lần
    /// nào). Dùng để build cache key ở DanhMucSPController.
    /// </summary>
    Task<long> GetCurrentDanhMucVersionAsync();
}

public class ProductCacheService : IProductCacheService
{
    // Prefix chung cho MỌI cache liên quan sản phẩm — để RemoveByPrefixAsync("sp:") xóa sạch 1 lần
    // (chỉ dùng được trên Redis hỗ trợ SCAN đầy đủ — xem cảnh báo ở RemoveByPrefixAsync).
    public const string RootPrefix = "sp:";

    // Key lưu số đếm version — KHÔNG có RootPrefix ở trước vì bản thân nó không phải cache dữ
    // liệu sản phẩm, mà là bộ đếm điều khiển việc cache dữ liệu sản phẩm. Đặt tên riêng để tránh
    // nhầm lẫn nếu sau này có ai đó thực sự chạy RemoveByPrefixAsync("sp:") trên Redis hỗ trợ SCAN.
    private const string VersionKey = "sp:cache-version";

    // Version RIÊNG cho module Danh mục sản phẩm — tách khỏi VersionKey ở trên vì 2 module CRUD
    // độc lập nhau (sửa sản phẩm không nên làm cache danh mục bị coi là cũ, và ngược lại).
    private const string DanhMucVersionKey = "dm:cache-version";

    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    // PHẢI khớp với PropertyNamingPolicy = CamelCase đang cấu hình ở Program.cs
    // (builder.Services.AddControllers().AddJsonOptions(...)). Vì controller trả cache bằng
    // Content(json, "application/json") — KHÔNG đi qua pipeline serialize mặc định của MVC —
    // nên nếu dùng default JsonSerializer.Serialize (PascalCase) thì response cache-hit sẽ có
    // field dạng "TenSp" còn response cache-miss (đi qua MVC bình thường ở phần khác của app)
    // lại là "tenSp", làm Frontend Vue đọc sai/thiếu field tùy lúc cache hit hay miss.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProductCacheService(IDistributedCache cache, IConnectionMultiplexer redis)
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
            return ProductCacheConstants.NullMarker;

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
            // sản phẩm vẫn phải lưu DB thành công dù việc dọn cache thất bại) — nuốt lỗi tại đây.
            // Nếu cần theo dõi, có thể inject ILogger và log warning ở nhánh catch này.
        }
    }

    public async Task InvalidateAllProductCacheAsync()
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

    public async Task InvalidateAllDanhMucCacheAsync()
    {
        var db = _redis.GetDatabase();
        await db.StringIncrementAsync(DanhMucVersionKey);
    }

    public async Task<long> GetCurrentDanhMucVersionAsync()
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(DanhMucVersionKey);
        return value.HasValue ? (long)value : 1L;
    }
}
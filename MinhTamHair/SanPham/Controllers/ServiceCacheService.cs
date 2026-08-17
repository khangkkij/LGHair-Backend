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
    /// Xóa toàn bộ cache khớp với 1 prefix. Dùng khi CRUD dịch vụ/combo để tránh trả cache cũ.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix);

    /// <summary>
    /// Xóa toàn bộ cache liên quan tới module Dịch vụ (gọi sau mọi thao tác Create/Update/Delete
    /// trên Dịch vụ, Combo, Danh mục dịch vụ, hoặc Tiêu đề dịch vụ — vì các loại dữ liệu này luôn
    /// xuất hiện lồng nhau trong response của DanhSachDichVuController).
    /// </summary>
    Task InvalidateAllServiceCacheAsync();
}

public class ServiceCacheService : IServiceCacheService
{
    // Prefix chung cho MỌI cache liên quan dịch vụ — tách riêng khỏi "sp:" (Sản phẩm) để
    // RemoveByPrefixAsync của 2 module không bao giờ đụng nhầm cache của nhau.
    public const string RootPrefix = "sv:";

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

    public Task InvalidateAllServiceCacheAsync() => RemoveByPrefixAsync(RootPrefix);
}

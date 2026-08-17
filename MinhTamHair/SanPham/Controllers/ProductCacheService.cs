using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace MinhTamHair.Services;

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
    /// Xóa toàn bộ cache khớp với 1 prefix (VD: "sp:filter:" sẽ xóa mọi biến thể filter/phân trang).
    /// Dùng khi CRUD sản phẩm/biến thể để tránh trả cache cũ.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix);

    /// <summary>
    /// Xóa toàn bộ cache liên quan tới module Sản phẩm (gọi sau mọi thao tác Create/Update/Delete/ToggleStatus
    /// trên sản phẩm HOẶC biến thể, vì 2 loại dữ liệu này luôn xuất hiện lồng nhau trong response).
    /// </summary>
    Task InvalidateAllProductCacheAsync();
}

public class ProductCacheService : IProductCacheService
{
    // Prefix chung cho MỌI cache liên quan sản phẩm — để RemoveByPrefixAsync("sp:") xóa sạch 1 lần.
    public const string RootPrefix = "sp:";

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

    public Task InvalidateAllProductCacheAsync() => RemoveByPrefixAsync(RootPrefix);
}

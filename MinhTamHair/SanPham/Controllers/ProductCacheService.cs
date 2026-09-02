using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
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
    /// </summary>
    Task<string> GetOrCreateJsonAsync(string key, Func<Task<object?>> factory, TimeSpan? ttl = null);

    /// <summary>
    /// Xóa toàn bộ cache khớp với 1 prefix bằng lệnh SCAN/KEYS.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix);

    /// <summary>
    /// Đánh dấu toàn bộ cache liên quan tới module Sản phẩm là cũ.
    /// </summary>
    Task InvalidateAllProductCacheAsync();

    /// <summary>
    /// Đọc version cache hiện tại của module Sản phẩm.
    /// </summary>
    Task<long> GetCurrentVersionAsync();

    /// <summary>
    /// Đánh dấu toàn bộ cache liên quan tới module Danh mục sản phẩm là cũ.
    /// </summary>
    Task InvalidateAllDanhMucCacheAsync();

    /// <summary>
    /// Đọc version cache hiện tại của module Danh mục.
    /// </summary>
    Task<long> GetCurrentDanhMucVersionAsync();
}

public class ProductCacheService : IProductCacheService
{
    public const string RootPrefix = "sp:";
    private const string VersionKey = "sp:cache-version";
    private const string DanhMucVersionKey = "dm:cache-version";
    private const string L1ProductVersionKey = "l1:sp:cache-version";
    private const string L1DanhMucVersionKey = "l1:dm:cache-version";

    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _memoryCache;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VersionL1Ttl = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProductCacheService(IDistributedCache cache, IConnectionMultiplexer redis, IMemoryCache memoryCache)
    {
        _cache = cache;
        _redis = redis;
        _memoryCache = memoryCache;
    }

    public async Task<string> GetOrCreateJsonAsync(string key, Func<Task<object?>> factory, TimeSpan? ttl = null)
    {
        // 1. Kiểm tra L1 MemoryCache (In-Process RAM - phản hồi < 1ms, không tốn network roundtrip)
        if (_memoryCache.TryGetValue(key, out string? memCached) && !string.IsNullOrEmpty(memCached))
        {
            return memCached;
        }

        // 2. Kiểm tra L2 Distributed / Redis Cache
        string? cached = null;
        try
        {
            cached = await _cache.GetStringAsync(key);
        }
        catch
        {
            // Tránh crash nếu Redis gặp sự cố tạm thời
        }

        if (!string.IsNullOrEmpty(cached))
        {
            // Nạp lại vào L1 MemoryCache với TTL ngắn (2 phút) để tăng tốc cho các request tiếp theo
            _memoryCache.Set(key, cached, TimeSpan.FromMinutes(2));
            return cached;
        }

        // 3. Cache Miss: Thực thi truy vấn cơ sở dữ liệu
        var fresh = await factory();

        if (fresh == null)
            return ProductCacheConstants.NullMarker;

        var payload = JsonSerializer.Serialize(fresh, JsonOptions);

        // Lưu vào L1 MemoryCache
        var effectiveTtl = ttl ?? DefaultTtl;
        _memoryCache.Set(key, payload, effectiveTtl);

        // Lưu vào L2 Distributed / Redis Cache
        try
        {
            await _cache.SetStringAsync(key, payload, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = effectiveTtl
            });
        }
        catch
        {
            // Không để lỗi ghi Redis làm ảnh hưởng phản hồi API
        }

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

            var pattern = $"MinhTamHair:{prefix}*";
            var keysToDelete = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                keysToDelete.Add(key);
            }

            if (keysToDelete.Count > 0)
                await db.KeyDeleteAsync(keysToDelete.ToArray());
        }
        catch
        {
            // Nuốt lỗi khi Redis SCAN không được hỗ trợ
        }
    }

    public async Task InvalidateAllProductCacheAsync()
    {
        _memoryCache.Remove(L1ProductVersionKey);
        try
        {
            var db = _redis.GetDatabase();
            await db.StringIncrementAsync(VersionKey);
        }
        catch
        {
        }
    }

    public async Task<long> GetCurrentVersionAsync()
    {
        if (_memoryCache.TryGetValue(L1ProductVersionKey, out long cachedVer))
            return cachedVer;

        long ver = 1L;
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(VersionKey);
            ver = value.HasValue ? (long)value : 1L;
        }
        catch
        {
            ver = 1L;
        }

        _memoryCache.Set(L1ProductVersionKey, ver, VersionL1Ttl);
        return ver;
    }

    public async Task InvalidateAllDanhMucCacheAsync()
    {
        _memoryCache.Remove(L1DanhMucVersionKey);
        try
        {
            var db = _redis.GetDatabase();
            await db.StringIncrementAsync(DanhMucVersionKey);
        }
        catch
        {
        }
    }

    public async Task<long> GetCurrentDanhMucVersionAsync()
    {
        if (_memoryCache.TryGetValue(L1DanhMucVersionKey, out long cachedVer))
            return cachedVer;

        long ver = 1L;
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(DanhMucVersionKey);
            ver = value.HasValue ? (long)value : 1L;
        }
        catch
        {
            ver = 1L;
        }

        _memoryCache.Set(L1DanhMucVersionKey, ver, VersionL1Ttl);
        return ver;
    }
}
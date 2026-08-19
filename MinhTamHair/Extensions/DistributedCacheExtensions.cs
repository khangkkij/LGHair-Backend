using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MinhTamHair.Extensions
{
    public static class DistributedCacheExtensions
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        };

        public static async Task SetObjectAsync<T>(
            this IDistributedCache cache,
            string key,
            T value,
            TimeSpan? absoluteExpireTime = null,
            CancellationToken cancellationToken = default)
        {
            if (value == null) return;

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpireTime ?? TimeSpan.FromHours(24)
            };

            var json = JsonSerializer.Serialize(value, JsonOptions);
            await cache.SetStringAsync(key, json, options, cancellationToken);
        }

        public static async Task<T?> GetObjectAsync<T>(
            this IDistributedCache cache,
            string key,
            CancellationToken cancellationToken = default)
        {
            var json = await cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) return default;

            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch
            {
                return default;
            }
        }

        public static async Task RemoveSafeAsync(
            this IDistributedCache cache,
            string key,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await cache.RemoveAsync(key, cancellationToken);
            }
            catch
            {
                // Bỏ qua lỗi nếu key không tồn tại
            }
        }
    }
}

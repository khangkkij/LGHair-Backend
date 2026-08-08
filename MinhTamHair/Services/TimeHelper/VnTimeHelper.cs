namespace MinhTamHair.Services.TimeHelper
{
    public static class VnTimeHelper
    {
        private static readonly TimeZoneInfo VnTimeZone = GetVnTimeZone();

        private static TimeZoneInfo GetVnTimeZone()
        {
            try
            {
                // Windows
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                // Linux / Docker
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
        }
        public static DateTime Now() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTimeZone);

        public static DateTime? ToVnTime(DateTime? dt)
        {
            if (!dt.HasValue) return null;
            if (dt.Value.Kind == DateTimeKind.Utc)
                return TimeZoneInfo.ConvertTimeFromUtc(dt.Value, VnTimeZone);
            if (dt.Value.Kind == DateTimeKind.Local)
                return TimeZoneInfo.ConvertTimeFromUtc(dt.Value.ToUniversalTime(), VnTimeZone);
            return dt.Value;
        }
    }
}

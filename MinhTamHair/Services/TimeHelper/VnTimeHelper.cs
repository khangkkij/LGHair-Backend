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
    }
}

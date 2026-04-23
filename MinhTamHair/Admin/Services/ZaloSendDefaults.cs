namespace MinhTamHair.Services;

/// <summary>
/// Giá trị mặc định khi appsettings không khai báo — ưu tiên gửi Zalo thật (bridge Personal), không dùng HttpTest/httpbin.
/// </summary>
public static class ZaloSendDefaults
{
    public const string ModeWhenUnset = "Personal";
}

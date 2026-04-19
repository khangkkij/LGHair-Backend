namespace MinhTamHair.Services;

/// <summary>
/// Gửi tin Zalo: <c>Zns</c> = API ZNS (OA); <c>Personal</c> = bridge Node zca-js; <c>HttpTest</c> = httpbin.
/// </summary>
public interface IZaloService
{
    /// <summary>
    /// true nếu Mode=Zns nhưng thiếu token/template và đã tự chuyển sang HttpTest (không tin trên app Zalo).
    /// </summary>
    bool LastSendWasZnsDegradedToHttpTest { get; }

    /// <param name="phone">Số điện thoại khách (SDT).</param>
    Task SendAsync(string phone, string? title, string content, CancellationToken cancellationToken = default);
}

using System.ComponentModel.DataAnnotations;

namespace MinhTamHair.Models.DTOs;

public class SendThongBaoRequest
{
    [Required]
    [MaxLength(10)]
    public string MaKh { get; set; } = null!;

    [MaxLength(100)]
    public string? TieuDe { get; set; }

    public string? NoiDung { get; set; }

    public bool SendViaZalo { get; set; }

    public bool SendViaGmail { get; set; }
}

public class SendThongBaoResponse
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public List<ChannelSendResultDto> Results { get; set; } = new();
}

public class ChannelSendResultDto
{
    public string KenhGui { get; set; } = null!;

    public int TrangThai { get; set; }

    public string? ChiTiet { get; set; }
}

public class KhachHangListItemDto
{
    public string MaKh { get; set; } = null!;

    public string? HoTen { get; set; }

    public string? Sdt { get; set; }

    public string? Email { get; set; }
}

/// <summary>Trạng thái Zalo cho màn Gửi thông báo (Personal bridge / ZNS / HttpTest).</summary>
public class ZaloGuiTrangThaiDto
{
    public string Mode { get; set; } = "";

    public bool IsPersonal { get; set; }

    public bool? PersonalBridgeReachable { get; set; }

    public bool? PersonalLoggedIn { get; set; }

    public bool? CredentialsFileExists { get; set; }

    public bool? CredentialsValidShape { get; set; }

    public bool? WarmingUp { get; set; }

    public string? PersonalLastError { get; set; }

    public string? Hint { get; set; }
}

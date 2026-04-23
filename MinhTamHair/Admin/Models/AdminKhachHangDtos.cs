using System.ComponentModel.DataAnnotations;

namespace MinhTamHair.Admin.Models;

public class AdminKhachHangListItemDto
{
    public string MaKh { get; set; } = null!;
    public string? HoTen { get; set; }
    public string? Sdt { get; set; }
    public string? Email { get; set; }
    public string? MaRank { get; set; }
    public string? TenRank { get; set; }
    public int? DiemTichLuy { get; set; }
    public int? TrangThai { get; set; }
    public DateTime? NgayTao { get; set; }
}

public class AdminKhachHangDetailDto : AdminKhachHangListItemDto
{
    public string? GioiTinh { get; set; }
    public string? HinhAnh { get; set; }

    /// <summary>Mật khẩu đăng nhập hiện tại (chỉ API admin; hệ thống đang lưu dạng plain text).</summary>
    public string? MatKhau { get; set; }
}

public class AdminKhachHangUpdateDto
{
    [MaxLength(100)]
    public string? HoTen { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(15)]
    public string? Sdt { get; set; }

    [MaxLength(10)]
    public string? GioiTinh { get; set; }

    public int? DiemTichLuy { get; set; }

    /// <summary>1: hoạt động, 0: khóa (theo logic đăng nhập hiện tại).</summary>
    public int? TrangThai { get; set; }

    [MaxLength(255)]
    public string? HinhAnh { get; set; }

    /// <summary>Chỉ cập nhật khi có giá trị (để trống = giữ mật khẩu cũ).</summary>
    [MaxLength(255)]
    public string? MatKhauMoi { get; set; }
}

public class AdminKhachHangCreateDto
{
    [Required]
    [MaxLength(100)]
    public string HoTen { get; set; } = null!;

    [Required]
    [MaxLength(15)]
    public string Sdt { get; set; } = null!;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(100)]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Vui lòng chọn giới tính.")]
    [MaxLength(10)]
    public string GioiTinh { get; set; } = null!;

    [MaxLength(255)]
    public string? MatKhau { get; set; }

    [Required(ErrorMessage = "Vui lòng tải ảnh đại diện.")]
    [MaxLength(255)]
    public string HinhAnh { get; set; } = null!;
}

public class AdminKhachHangListResponse
{
    public List<AdminKhachHangListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class HangRankOptionDto
{
    public string MaRank { get; set; } = null!;
    public string? TenRank { get; set; }
    public int? DiemToiThieu { get; set; }
}

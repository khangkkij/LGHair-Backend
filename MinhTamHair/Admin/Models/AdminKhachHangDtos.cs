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

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(10)]
    public string? GioiTinh { get; set; }

    [MaxLength(255)]
    public string? MatKhau { get; set; }

    [MaxLength(255)]
    public string? HinhAnh { get; set; }
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

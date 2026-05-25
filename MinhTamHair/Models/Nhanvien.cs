using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Nhanvien
{
    public string MaNv { get; set; } = null!;

    public string? MaRole { get; set; }

    public string? MaLoaiNv { get; set; }

    public string? HoTenNv { get; set; }

    public string? GioiTinh { get; set; }

    public string? Cccd { get; set; }

    public int? NamSinh { get; set; }

    public string? Email { get; set; }

    public string? Sdt { get; set; }

    public string? DiaChi { get; set; }

    public string? HinhAnh { get; set; }

    public string? TenDangNhap { get; set; }

    public string? MatKhau { get; set; }

    public DateOnly? NgayVaoLam { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietLichdat> ChitietLichdats { get; set; } = new List<ChitietLichdat>();

    public virtual ICollection<DanhgiaNhanvien> DanhgiaNhanviens { get; set; } = new List<DanhgiaNhanvien>();

    public virtual ICollection<HoaDon> HoaDons { get; set; } = new List<HoaDon>();

    public virtual ICollection<Lichlamviec> Lichlamviecs { get; set; } = new List<Lichlamviec>();

    public virtual Loainhanvien? MaLoaiNvNavigation { get; set; }

    public virtual Role? MaRoleNavigation { get; set; }
    public virtual ICollection<ChitietHoadonSp> ChitietHoadonSps { get; set; } = new List<ChitietHoadonSp>();
    public virtual ICollection<Phieunhap> Phieunhaps { get; set; } = new List<Phieunhap>();
}

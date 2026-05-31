using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class BienTheSanpham
{
    public string MaBienThe { get; set; } = null!;

    public string? MaSanPham { get; set; }

    public string? TenBienThe { get; set; }

    public decimal? GiaBan { get; set; }

    public int? SoLuongTon { get; set; }

    public string? HinhAnhDaiDien { get; set; }

    public decimal? PhanTramHoaHong { get; set; }

    public int? TrangThai { get; set; }

    public virtual SanPham? MaSpNavigation { get; set; }

    public virtual ICollection<ChitietGiohang> ChitietGiohangs { get; set; } = new List<ChitietGiohang>();

    public virtual ICollection<ChitietHoadonSp> ChitietHoadonSps { get; set; } = new List<ChitietHoadonSp>();

    public virtual ICollection<ChitietPhieunhap> ChitietPhieunhaps { get; set; } = new List<ChitietPhieunhap>();
}
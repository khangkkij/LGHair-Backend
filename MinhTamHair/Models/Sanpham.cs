using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Sanpham
{
    public string MaSp { get; set; } = null!;

    public string? MaLoaiSpcon { get; set; }

    public string? MaTh { get; set; }

    public string? TenSp { get; set; }

    public string? MoTa { get; set; }

    public string? ThanhPhan { get; set; }

    public string? HuongDanSuDung { get; set; }

    public decimal? GiaBan { get; set; }

    public int? SoLuongTon { get; set; }

    public string? HinhAnhDaiDien { get; set; }

    public decimal? PhanTramHoaHong { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietGiohang> ChitietGiohangs { get; set; } = new List<ChitietGiohang>();

    public virtual ICollection<ChitietHoadonSp> ChitietHoadonSps { get; set; } = new List<ChitietHoadonSp>();

    public virtual ICollection<ChitietPhieunhap> ChitietPhieunhaps { get; set; } = new List<ChitietPhieunhap>();

    public virtual ICollection<DanhgiaSanpham> DanhgiaSanphams { get; set; } = new List<DanhgiaSanpham>();

    public virtual ICollection<Hinhanhsp> Hinhanhsps { get; set; } = new List<Hinhanhsp>();

    public virtual Loaisanphamcon? MaLoaiSpconNavigation { get; set; }

    public virtual Thuonghieu? MaThNavigation { get; set; }
}


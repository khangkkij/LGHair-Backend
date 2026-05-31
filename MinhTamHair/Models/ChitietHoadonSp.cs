using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietHoadonSp
{
    public string MaHd { get; set; } = null!;

    public string MaBienThe { get; set; } = null!;

    public string? MaNv { get; set; }

    public int? SoLuong { get; set; }

    public decimal? DonGiaLucBan { get; set; }

    public decimal? TienHoaHongSp { get; set; }

    public decimal? ThanhTien { get; set; }

    public virtual BienTheSanpham MaBienTheNavigation { get; set; } = null!;
    public virtual HoaDon MaHdNavigation { get; set; } = null!;

    public virtual Nhanvien? MaNvNavigation { get; set; }
}
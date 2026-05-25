using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietHoadonSp
{
    public string MaHd { get; set; } = null!;

    public string MaSp { get; set; } = null!;

    public string? MaNv { get; set; }

    public int? SoLuong { get; set; }

    public decimal? DonGiaLucBan { get; set; }

    public decimal? TienHoaHongSp { get; set; }

    public decimal? ThanhTien { get; set; }

    public virtual HoaDon MaHdNavigation { get; set; } = null!;

    public virtual Nhanvien? MaNvNavigation { get; set; }

    public virtual Sanpham MaSpNavigation { get; set; } = null!;
}


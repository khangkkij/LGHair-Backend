using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ThongtinGiaohang
{
    public int MaGiaoHang { get; set; }

    public string? MaHd { get; set; }

    public string? TenNguoiNhan { get; set; }

    public string? SdtNhan { get; set; }

    public string? DiaChiNhan { get; set; }

    public string? DonViVanChuyen { get; set; }

    public string? MaVanDon { get; set; }

    public decimal? PhiShip { get; set; }

    public string? Email { get; set; }

    public int? TrangThaiGiaoHang { get; set; }

    public virtual HoaDon? MaHdNavigation { get; set; }
}


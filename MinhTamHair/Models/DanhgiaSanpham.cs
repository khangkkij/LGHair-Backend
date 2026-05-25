using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class DanhgiaSanpham
{
    public int MaDgsp { get; set; }

    public string? MaSp { get; set; }

    public string? MaHd { get; set; }

    public string? MaKh { get; set; }

    public int? SoSao { get; set; }

    public string? NoiDung { get; set; }

    public DateTime? NgayDanhGia { get; set; }

    public string? HinhAnh { get; set; }

    public string? PhanHoi { get; set; }

    public int? TrangThai { get; set; }

    public virtual HoaDon? MaHdNavigation { get; set; }

    public virtual Khachhang? MaKhNavigation { get; set; }

    public virtual Sanpham? MaSpNavigation { get; set; }
}


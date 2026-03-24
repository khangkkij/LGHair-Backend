using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class DanhgiaDichvu
{
    public int MaDgdv { get; set; }

    public string? MaHd { get; set; }

    public string? MaDv { get; set; }

    public string? MaCb { get; set; }

    public int? SoSao { get; set; }

    public DateTime? NgayDanhGia { get; set; }

    public string? NoiDung { get; set; }

    public string? PhanHoi { get; set; }

    public DateTime? NgayPhanHoi { get; set; }

    public string? HinhAnh { get; set; }

    public int? TrangThai { get; set; }

    public virtual Combo? MaCbNavigation { get; set; }

    public virtual Dichvu? MaDvNavigation { get; set; }

    public virtual HoaDon? MaHdNavigation { get; set; }
}

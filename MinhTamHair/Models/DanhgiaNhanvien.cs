using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class DanhgiaNhanvien
{
    public int MaDgnv { get; set; }

    public string? MaHd { get; set; }

    public string? MaNv { get; set; }

    public int? SoSao { get; set; }

    public DateTime? NgayDanhGia { get; set; }

    public string? NoiDung { get; set; }

    public int? TrangThai { get; set; }

    public virtual HoaDon? MaHdNavigation { get; set; }

    public virtual Nhanvien? MaNvNavigation { get; set; }
}

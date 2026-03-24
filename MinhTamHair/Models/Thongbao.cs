using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Thongbao
{
    public int MaTb { get; set; }

    public string? MaKh { get; set; }

    public string? TieuDe { get; set; }

    public string? NoiDung { get; set; }

    public string? KenhGui { get; set; }

    public string? LoaiTin { get; set; }

    public DateTime? NgayTao { get; set; }

    public DateTime? NgayCanGui { get; set; }

    public int? TrangThai { get; set; }

    public virtual Khachhang? MaKhNavigation { get; set; }
}

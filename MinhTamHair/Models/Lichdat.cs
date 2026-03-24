using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Lichdat
{
    public string MaLd { get; set; } = null!;

    public string? MaKh { get; set; }

    public DateOnly? NgayHen { get; set; }

    public TimeOnly? GioBatDau { get; set; }

    public TimeOnly? GioKetThuc { get; set; }

    public decimal? TongTienDuKien { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietLichdat> ChitietLichdats { get; set; } = new List<ChitietLichdat>();

    public virtual ICollection<HoaDon> HoaDons { get; set; } = new List<HoaDon>();

    public virtual Khachhang? MaKhNavigation { get; set; }
}

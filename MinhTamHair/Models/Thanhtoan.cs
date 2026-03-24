using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Thanhtoan
{
    public int MaTt { get; set; }

    public string? MaHd { get; set; }

    public string? PhuongThuc { get; set; }

    public decimal? SoTien { get; set; }

    public int? TrangThai { get; set; }

    public virtual HoaDon? MaHdNavigation { get; set; }
}

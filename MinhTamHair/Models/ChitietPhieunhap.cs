using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietPhieunhap
{
    public string MaPn { get; set; } = null!;

    public string MaBienThe { get; set; } = null!;

    public int? SoLuong { get; set; }

    public decimal? GiaNhap { get; set; }

    public virtual BienTheSanpham MaBienTheNavigation { get; set; } = null!;
    public virtual Phieunhap MaPnNavigation { get; set; } = null!;
}
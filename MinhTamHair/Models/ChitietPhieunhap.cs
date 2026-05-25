using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietPhieunhap
{
    public string MaPn { get; set; } = null!;

    public string MaSp { get; set; } = null!;

    public int? SoLuong { get; set; }

    public decimal? GiaNhap { get; set; }

    public virtual Phieunhap MaPnNavigation { get; set; } = null!;

    public virtual Sanpham MaSpNavigation { get; set; } = null!;
}


using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietGiohang
{
    public int MaGh { get; set; }

    public string MaSp { get; set; } = null!;

    public int? SoLuong { get; set; }

    public virtual Giohang MaGhNavigation { get; set; } = null!;

    public virtual Sanpham MaSpNavigation { get; set; } = null!;
}


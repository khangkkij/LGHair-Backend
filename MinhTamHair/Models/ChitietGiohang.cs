using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietGiohang
{
    public int MaGh { get; set; }
    public string MaBienThe { get; set; } = null!;

    public int? SoLuong { get; set; }

    public virtual BienTheSanpham MaBienTheNavigation { get; set; } = null!;

    public virtual Giohang MaGhNavigation { get; set; } = null!;
}
using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Hinhanhsp
{
    public int MaHa { get; set; }

    public string? MaSanPham { get; set; }

    public string? DuongDan { get; set; }

    public virtual SanPham? MaSpNavigation { get; set; }
}
using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Hinhanhsp
{
    public int MaHa { get; set; }

    public string? MaSp { get; set; }

    public string? DuongDan { get; set; }

    public virtual Sanpham? MaSpNavigation { get; set; }
}


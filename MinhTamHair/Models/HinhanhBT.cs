using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class HinhanhBT
{
    public int MaHa { get; set; }

    public string? MaBienThe { get; set; }

    public string? DuongDan { get; set; }

    public virtual BienTheSanpham? MaBienTheNavigation { get; set; }
}
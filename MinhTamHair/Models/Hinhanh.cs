using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Hinhanh
{
    public int MaHa { get; set; }

    public string? MaDv { get; set; }

    public string? DuongDan { get; set; }

    public virtual Dichvu? MaDvNavigation { get; set; }
}

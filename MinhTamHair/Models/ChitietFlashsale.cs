using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietFlashsale
{
    public string MaFl { get; set; } = null!;

    public string MaCb { get; set; } = null!;

    public string MaDv { get; set; } = null!;

    public int? PhanTramGiam { get; set; }

    public virtual Combo MaCbNavigation { get; set; } = null!;

    public virtual Dichvu MaDvNavigation { get; set; } = null!;

    public virtual Flashsale MaFlNavigation { get; set; } = null!;
}

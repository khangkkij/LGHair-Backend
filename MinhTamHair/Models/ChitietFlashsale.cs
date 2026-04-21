using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietFlashsale
{
    public int Id { get; set; }
    public string MaFl { get; set; } = null!;

    public string? MaCb { get; set; } 

    public string? MaDv { get; set; }

    public int? PhanTramGiam { get; set; }

    public virtual Combo? MaCbNavigation { get; set; } 

    public virtual Dichvu? MaDvNavigation { get; set; }

    public virtual Flashsale MaFlNavigation { get; set; } = null!;
}

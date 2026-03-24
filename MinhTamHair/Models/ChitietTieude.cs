using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietTieude
{
    public int MaCttd { get; set; }

    public string? MaTd { get; set; }

    public string? MaDv { get; set; }

    public string? TenTieuDe { get; set; }

    public string? MoTaTieuDe { get; set; }

    public string? HinhAnh { get; set; }

    public virtual Dichvu? MaDvNavigation { get; set; }

    public virtual TieudeDichvu? MaTdNavigation { get; set; }
}

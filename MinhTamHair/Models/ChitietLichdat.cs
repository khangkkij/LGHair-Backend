using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class ChitietLichdat
{
    public int MaCtld { get; set; }

    public string? MaLd { get; set; }

    public string? MaNv { get; set; }

    public string? MaDv { get; set; }

    public string? MaCb { get; set; }

    public decimal? GiaLucDat { get; set; }

    public decimal? TienHoaHongDv { get; set; }

    public string? GhiChu { get; set; }

    public virtual Combo? MaCbNavigation { get; set; }

    public virtual Dichvu? MaDvNavigation { get; set; }

    public virtual Lichdat? MaLdNavigation { get; set; }

    public virtual Nhanvien? MaNvNavigation { get; set; }
}

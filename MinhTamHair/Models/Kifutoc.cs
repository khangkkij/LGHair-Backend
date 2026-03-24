using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Kifutoc
{
    public string MaKt { get; set; } = null!;

    public string? MaDv { get; set; }

    public string? TenKieuToc { get; set; }

    public string? HinhAnh { get; set; }

    public string? MoTa { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Lichsuthutoc> Lichsuthutocs { get; set; } = new List<Lichsuthutoc>();

    public virtual Dichvu? MaDvNavigation { get; set; }
}

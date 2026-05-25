using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Loaisanphamcha
{
    public string MaLoaiSpcha { get; set; } = null!;

    public string? TenLoaiSp { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Loaisanphamcon> Loaisanphamcons { get; set; } = new List<Loaisanphamcon>();
}


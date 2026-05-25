using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Loaisanphamcon
{
    public string MaLoaiSpcon { get; set; } = null!;

    public string? MaLoaiSpcha { get; set; }

    public string? TenLoaiSp { get; set; }

    public int? TrangThai { get; set; }

    public virtual Loaisanphamcha? MaLoaiSpchaNavigation { get; set; }

    public virtual ICollection<Sanpham> Sanphams { get; set; } = new List<Sanpham>();
}


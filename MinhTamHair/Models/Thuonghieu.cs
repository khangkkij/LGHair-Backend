using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Thuonghieu
{
    public string MaTh { get; set; } = null!;

    public string? TenThuongHieu { get; set; }

    public string? MoTa { get; set; }

    public string? HinhAnh { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<SanPham> Sanphams { get; set; } = new List<SanPham>();
}


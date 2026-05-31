using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class DanhMucSP
{
    public string MaDMSP { get; set; } = null!;

    public string? TenDanhMuc { get; set; }

    public string? HinhAnh { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<SanPham> SanPhams { get; set; } = new List<SanPham>();
}
using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class DanhMuc
{
    public string MaDm { get; set; } = null!;

    public string? TenDanhMuc { get; set; }

    public string? HinhAnh { get; set; }

    public DateOnly? NgayTao { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Dichvu> Dichvus { get; set; } = new List<Dichvu>();
}

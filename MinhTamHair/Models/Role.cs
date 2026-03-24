using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Role
{
    public string MaRole { get; set; } = null!;

    public string? TenChucVu { get; set; }

    public bool? TrangThai { get; set; }

    public virtual ICollection<Nhanvien> Nhanviens { get; set; } = new List<Nhanvien>();
}

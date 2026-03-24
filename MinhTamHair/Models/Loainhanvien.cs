using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Loainhanvien
{
    public string MaLoaiNv { get; set; } = null!;

    public string? TenLoai { get; set; }

    public string? MoTa { get; set; }

    public decimal? Luong { get; set; }

    public bool? DuocDatLich { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Nhanvien> Nhanviens { get; set; } = new List<Nhanvien>();
}

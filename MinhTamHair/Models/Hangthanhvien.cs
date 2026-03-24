using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Hangthanhvien
{
    public string MaRank { get; set; } = null!;

    public string? TenRank { get; set; }

    public int? DiemToiThieu { get; set; }

    public int? PhanTramGiamGia { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Khachhang> Khachhangs { get; set; } = new List<Khachhang>();
}

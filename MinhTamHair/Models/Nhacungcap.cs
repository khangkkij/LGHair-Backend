using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Nhacungcap
{
    public string MaNcc { get; set; } = null!;

    public string? TenNcc { get; set; }

    public string? SoDienThoai { get; set; }

    public string? DiaChi { get; set; }

    public string? Email { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Phieunhap> Phieunhaps { get; set; } = new List<Phieunhap>();
}


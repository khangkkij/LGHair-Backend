using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Phieunhap
{
    public string MaPn { get; set; } = null!;

    public string? MaNv { get; set; }

    public string? MaNcc { get; set; }

    public DateTime? NgayNhap { get; set; }

    public decimal? TongTien { get; set; }

    public string? GhiChu { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietPhieunhap> ChitietPhieunhaps { get; set; } = new List<ChitietPhieunhap>();

    public virtual Nhacungcap? MaNccNavigation { get; set; }

    public virtual Nhanvien? MaNvNavigation { get; set; }
}


using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Khachhang
{
    public string MaKh { get; set; } = null!;

    public string? MaRank { get; set; }

    public string? HoTen { get; set; }

    public string? Email { get; set; }

    public string? Sdt { get; set; }

    public string? MatKhau { get; set; }

    public string? HinhAnh { get; set; }

    public string? GioiTinh { get; set; }

    public DateTime? NgayTao { get; set; }

    public int? DiemTichLuy { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<HoaDon> HoaDons { get; set; } = new List<HoaDon>();

    public virtual ICollection<Lichdat> Lichdats { get; set; } = new List<Lichdat>();

    public virtual ICollection<Lichsuthutoc> Lichsuthutocs { get; set; } = new List<Lichsuthutoc>();

    public virtual Hangthanhvien? MaRankNavigation { get; set; }

    public virtual ICollection<Thongbao> Thongbaos { get; set; } = new List<Thongbao>();

    public virtual ICollection<Voucher> MaCodes { get; set; } = new List<Voucher>();
}

using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class HoaDon
{
    public string MaHd { get; set; } = null!;

    public string? MaLd { get; set; }

    public string? MaNv { get; set; }

    public string? MaCode { get; set; }

    public string? MaKh { get; set; }

    public DateTime? NgayTao { get; set; }

    public decimal? TongTien { get; set; }

    public int? PhanTramGiamVip { get; set; }

    public decimal? ThanhTien { get; set; }

    public decimal? TienDaTra { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<DanhgiaDichvu> DanhgiaDichvus { get; set; } = new List<DanhgiaDichvu>();

    public virtual ICollection<DanhgiaNhanvien> DanhgiaNhanviens { get; set; } = new List<DanhgiaNhanvien>();

    public virtual Voucher? MaCodeNavigation { get; set; }

    public virtual Khachhang? MaKhNavigation { get; set; }

    public virtual Lichdat? MaLdNavigation { get; set; }

    public virtual Nhanvien? MaNvNavigation { get; set; }

    public virtual ICollection<Thanhtoan> Thanhtoans { get; set; } = new List<Thanhtoan>();
}

using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class SanPham
{
    public string MaSp { get; set; } = null!;

    public string? MaDanhMuc { get; set; }

    public string? MaTh { get; set; }

    public string? TenSp { get; set; }

    public string? MoTa { get; set; }

    public string? ThanhPhan { get; set; }

    public string? HuongDanSuDung { get; set; }

    public string? HinhAnhDaiDien { get; set; }

    public int? TrangThai { get; set; }
    public virtual DanhMucSP? MaDanhMucTheNavigation { get; set; }

    public virtual Thuonghieu? Thuonghieu { get; set; }

    public virtual ICollection<Hinhanhsp> Hinhanhsps { get; set; } = new List<Hinhanhsp>();

    public virtual ICollection<DanhgiaSanpham> DanhgiaSanphams { get; set; } = new List<DanhgiaSanpham>();

    public virtual ICollection<BienTheSanpham> BienTheSanphams { get; set; } = new List<BienTheSanpham>();
}
using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Dichvu
{
    public string MaDv { get; set; } = null!;

    public string? MaDm { get; set; }

    public string? TenDv { get; set; }

    public string? MoTa { get; set; }

    public decimal? Gia { get; set; }

    public decimal? PhanTramHoaHong { get; set; }

    public int? ThoiGianLam { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietFlashsale> ChitietFlashsales { get; set; } = new List<ChitietFlashsale>();

    public virtual ICollection<ChitietLichdat> ChitietLichdats { get; set; } = new List<ChitietLichdat>();

    public virtual ICollection<ChitietTieude> ChitietTieudes { get; set; } = new List<ChitietTieude>();

    public virtual ICollection<DanhgiaDichvu> DanhgiaDichvus { get; set; } = new List<DanhgiaDichvu>();

    public virtual ICollection<Hinhanh> Hinhanhs { get; set; } = new List<Hinhanh>();

    public virtual ICollection<Kifutoc> Kifutocs { get; set; } = new List<Kifutoc>();

    public virtual DanhMuc? MaDmNavigation { get; set; }

    public virtual ICollection<TieudeDichvu> TieudeDichvus { get; set; } = new List<TieudeDichvu>();

    public virtual ICollection<Combo> MaCbs { get; set; } = new List<Combo>();
    public ICollection<Combo_DichVu> Combo_DichVus { get; set; }
}

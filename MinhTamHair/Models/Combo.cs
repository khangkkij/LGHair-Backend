using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Combo
{
    public string MaCb { get; set; } = null!;

    public string? TenCombo { get; set; }

    public decimal? Gia { get; set; }

    public string? MoTa { get; set; }

    public string? HinhAnh { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietFlashsale> ChitietFlashsales { get; set; } = new List<ChitietFlashsale>();

    public virtual ICollection<ChitietLichdat> ChitietLichdats { get; set; } = new List<ChitietLichdat>();

    public virtual ICollection<DanhgiaDichvu> DanhgiaDichvus { get; set; } = new List<DanhgiaDichvu>();

    public virtual ICollection<Dichvu> MaDvs { get; set; } = new List<Dichvu>();
    public ICollection<Combo_DichVu> Combo_DichVus { get; set; }
}

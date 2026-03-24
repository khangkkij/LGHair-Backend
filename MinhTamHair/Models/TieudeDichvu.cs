using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class TieudeDichvu
{
    public string MaTd { get; set; } = null!;

    public string? MaDv { get; set; }

    public string? TenTieuDe { get; set; }

    public string? MoTaTieuDe { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietTieude> ChitietTieudes { get; set; } = new List<ChitietTieude>();

    public virtual Dichvu? MaDvNavigation { get; set; }
}

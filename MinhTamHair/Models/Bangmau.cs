using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Bangmau
{
    public string MaMau { get; set; } = null!;

    public string? TenMau { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<Lichsuthutoc> Lichsuthutocs { get; set; } = new List<Lichsuthutoc>();
}

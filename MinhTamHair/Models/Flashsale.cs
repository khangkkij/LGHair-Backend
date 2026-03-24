using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Flashsale
{
    public string MaFl { get; set; } = null!;

    public DateTime? NgayBatDau { get; set; }

    public DateTime? NgayKetThuc { get; set; }

    public int? TrangThai { get; set; }

    public virtual ICollection<ChitietFlashsale> ChitietFlashsales { get; set; } = new List<ChitietFlashsale>();
}

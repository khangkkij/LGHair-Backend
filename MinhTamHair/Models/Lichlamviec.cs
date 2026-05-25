using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Lichlamviec
{
    public int MaLich { get; set; }

    public string? MaNv { get; set; }

    public DateOnly? NgayLamViec { get; set; }
	
    public string? GhiChu { get; set; }


	public string? CaLam { get; set; }

    public int? TrangThai { get; set; }

    public virtual Nhanvien? MaNvNavigation { get; set; }
}

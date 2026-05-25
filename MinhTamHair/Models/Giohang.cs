using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Giohang
{
    public int MaGh { get; set; }

    public string? MaKh { get; set; }

    public virtual ICollection<ChitietGiohang> ChitietGiohangs { get; set; } = new List<ChitietGiohang>();

    public virtual Khachhang? MaKhNavigation { get; set; }
}


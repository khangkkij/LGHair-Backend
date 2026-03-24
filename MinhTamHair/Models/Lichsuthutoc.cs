using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Lichsuthutoc
{
    public int Id { get; set; }

    public string? MaKh { get; set; }

    public string? MaKt { get; set; }

    public string? MaMau { get; set; }

    public string? AnhGoc { get; set; }

    public string? AnhGhep { get; set; }

    public DateTime? NgayThu { get; set; }

    public virtual Khachhang? MaKhNavigation { get; set; }

    public virtual Kifutoc? MaKtNavigation { get; set; }

    public virtual Bangmau? MaMauNavigation { get; set; }
}

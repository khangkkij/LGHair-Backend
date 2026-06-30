using System;

namespace MinhTamHair.Models;

public partial class LichSuTonKho
{
    public long MaLich { get; set; }

    public string MaBienThe { get; set; } = null!;

    public string LoaiBienDong { get; set; } = null!;

    public int SoLuongThayDoi { get; set; }

    public int TonTruoc { get; set; }

    public int TonSau { get; set; }

    public string? MaChungTu { get; set; }

    public string? MaNv { get; set; }

    public string? GhiChu { get; set; }

    public DateTime ThoiGian { get; set; }

    public virtual BienTheSanpham? MaBienTheNavigation { get; set; }
}
using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

// Models/HoaDonDienTu.cs
public class HoaDonDienTu
{
    public string MaHddt { get; set; } = null!;   // PK, tự sinh
    public string MaHd { get; set; } = null!;      // FK -> HoaDon
    public byte LoaiKhach { get; set; }             // 0: cá nhân, 1: doanh nghiệp
    public string? TenNguoiMua { get; set; }
    public string? Mst { get; set; }
    public string? DiaChiXuatHd { get; set; }
    public string? EmailNhanHd { get; set; }
    public byte TrangThai { get; set; }             // 0: chưa xuất, 1: đã xuất, 2: lỗi
    public string? ReferenceCode { get; set; }       // tracking_code / reference_code từ SePay
    public string? SoHoaDon { get; set; }
    public string? KyHieu { get; set; }
    public string? LinkPdf { get; set; }
    public string? LinkXml { get; set; }
    public DateTime? NgayXuat { get; set; }
    public string? GhiChuLoi { get; set; }
}

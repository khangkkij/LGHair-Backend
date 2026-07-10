using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

// ════════════════════════════════════════════════════════════════
// ĐỐI SOÁT HÓA ĐƠN DỊCH VỤ (bán ra)
// Mỗi HOA_DON đã hoàn tất (TrangThai = hóa đơn đã chốt) phải có 1
// hóa đơn điện tử đã xuất thành công (HoaDonDienTu.TrangThai = 1)
// tương ứng qua HoaDon.MaHd -> HoaDonDienTu.MaHd
// ════════════════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class QuanLyDoiSoatDichVuController : ControllerBase
{
    private readonly MinhTamHairContext _db;
    public QuanLyDoiSoatDichVuController(MinhTamHairContext db) => _db = db;

    [HttpGet("doisoathoadon")]
    public async Task<IActionResult> GetDoiSoatHoaDon(
        [FromQuery] DateTime? tuNgay,
        [FromQuery] DateTime? denNgay,
        [FromQuery] string? trangThai, // "", "khop", "thieu"
        [FromQuery] string? search, // tìm theo mã hóa đơn, tên KH, mã dịch vụ, tên dịch vụ
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var q = _db.HoaDons
            .Include(h => h.MaKhNavigation)
            .Include(h => h.MaNvNavigation)
            .Include(h => h.MaLdNavigation)
                .ThenInclude(ld => ld!.ChitietLichdats)
                    .ThenInclude(ct => ct.MaDvNavigation)
            .AsQueryable();

        if (tuNgay.HasValue) q = q.Where(h => h.NgayTao >= tuNgay.Value);
        if (denNgay.HasValue) q = q.Where(h => h.NgayTao <= denNgay.Value.AddDays(1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim();
            q = q.Where(h =>
                h.MaHd.Contains(kw) ||
                (h.MaKhNavigation != null && h.MaKhNavigation.HoTen != null && h.MaKhNavigation.HoTen.Contains(kw)) ||
                (h.MaLdNavigation != null && h.MaLdNavigation.ChitietLichdats.Any(ct =>
                    (ct.MaDv != null && ct.MaDv.Contains(kw)) ||
                    (ct.MaDvNavigation != null && ct.MaDvNavigation.TenDv != null && ct.MaDvNavigation.TenDv.Contains(kw))
                ))
            );
        }

        // Gộp thông tin hóa đơn điện tử tương ứng (nếu có) qua HoaDon.MaHd = HoaDonDienTu.MaHd
        var joined = from h in q
                     join hddt in _db.HoaDonDienTus on h.MaHd equals hddt.MaHd into hddtGroup
                     from hddt in hddtGroup.DefaultIfEmpty()
                     select new
                     {
                         h.MaHd,
                         h.NgayTao,
                         h.TongTien,
                         h.TrangThai,
                         TenKh = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : null,
                         SdtKh = h.MaKhNavigation != null ? h.MaKhNavigation.Sdt : null,
                         TenNv = h.MaNvNavigation != null ? h.MaNvNavigation.HoTenNv : null,
                         DanhSachDichVu = h.MaLdNavigation != null
                             ? h.MaLdNavigation.ChitietLichdats
                                 .Select(ct => ct.MaDvNavigation != null ? ct.MaDvNavigation.TenDv : null)
                                 .Where(t => t != null)
                                 .ToList()
                             : new List<string?>(),
                         CoHoaDon = hddt != null,
                         TrangThaiHoaDon = hddt != null ? (int?)hddt.TrangThai : null,
                         SoHoaDon = hddt != null ? hddt.SoHoaDon : null,
                         LinkPdf = hddt != null ? hddt.LinkPdf : null,
                         GhiChuLoi = hddt != null ? hddt.GhiChuLoi : null
                     };

        // Trạng thái: "khop" = đã có hóa đơn điện tử xuất thành công (TrangThai=1);
        // "thieu" = chưa có hoặc lỗi/pending
        if (trangThai == "khop") joined = joined.Where(x => x.TrangThaiHoaDon == 1);
        else if (trangThai == "thieu") joined = joined.Where(x => x.TrangThaiHoaDon == null || x.TrangThaiHoaDon != 1);

        var total = await joined.CountAsync();

        // Thống kê tổng quan (tính trên toàn bộ tập đã lọc theo ngày, chưa phân trang)
        var statsQuery = from h in q
                         join hddt in _db.HoaDonDienTus on h.MaHd equals hddt.MaHd into hddtGroup2
                         from hddt in hddtGroup2.DefaultIfEmpty()
                         select hddt != null && hddt.TrangThai == 1 ? 1 : 0;
        var tongLuot = await q.CountAsync();
        var daKhop = await statsQuery.SumAsync();

        var list = await joined
            .OrderByDescending(x => x.NgayTao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            stats = new { tongLuot, daKhop, chuaKhop = tongLuot - daKhop },
            items = list
        });
    }
}
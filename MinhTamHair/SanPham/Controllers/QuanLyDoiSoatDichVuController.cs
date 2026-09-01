using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

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
        [FromQuery] string? trangThai,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var baseQuery = _db.HoaDons
            .AsNoTracking()
            .AsQueryable();

        if (tuNgay.HasValue) baseQuery = baseQuery.Where(h => h.NgayTao >= tuNgay.Value);
        if (denNgay.HasValue) baseQuery = baseQuery.Where(h => h.NgayTao <= denNgay.Value.AddDays(1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim();
            baseQuery = baseQuery.Where(h =>
                h.MaHd.Contains(kw) ||
                (h.MaKhNavigation != null && h.MaKhNavigation.HoTen != null && h.MaKhNavigation.HoTen.Contains(kw)) ||
                (h.MaLdNavigation != null && h.MaLdNavigation.ChitietLichdats.Any(ct =>
                    (ct.MaDv != null && ct.MaDv.Contains(kw)) ||
                    (ct.MaDvNavigation != null && ct.MaDvNavigation.TenDv != null && ct.MaDvNavigation.TenDv.Contains(kw))
                ))
            );
        }

        var q = baseQuery;
        if (trangThai == "khop")
        {
            q = q.Where(h => _db.HoaDonDienTus.Any(x => x.MaHd == h.MaHd) && _db.HoaDonDienTus.Where(x => x.MaHd == h.MaHd).All(x => x.TrangThai == 1));
        }
        else if (trangThai == "thieu")
        {
            q = q.Where(h => !_db.HoaDonDienTus.Any(x => x.MaHd == h.MaHd) || _db.HoaDonDienTus.Where(x => x.MaHd == h.MaHd).Any(x => x.TrangThai != 1));
        }

        var total = await q.CountAsync();
        var tongLuot = await baseQuery.CountAsync();
        var daKhop = await baseQuery
            .Where(h => _db.HoaDonDienTus.Any(x => x.MaHd == h.MaHd) && _db.HoaDonDienTus.Where(x => x.MaHd == h.MaHd).All(x => x.TrangThai == 1))
            .CountAsync();

        var pagedHoaDons = await q
            .OrderByDescending(h => h.NgayTao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new
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
                    : new List<string?>()
            })
            .ToListAsync();

        var maHdList = pagedHoaDons.Select(h => h.MaHd).ToList();

        var allHddt = await _db.HoaDonDienTus
            .AsNoTracking()
            .Where(x => maHdList.Contains(x.MaHd))
            .ToListAsync();
        var hddtByMaHd = allHddt.GroupBy(x => x.MaHd).ToDictionary(g => g.Key, g => g.ToList());

        var list = pagedHoaDons.Select(h =>
        {
            var hddts = hddtByMaHd.TryGetValue(h.MaHd, out var lst) ? lst : new List<HoaDonDienTu>();
            var hoaDonDienTus = hddts.Select(hddt => new
            {
                MaHddt = hddt.MaHddt,
                Loai = hddt.MaHddt != null && hddt.MaHddt.EndsWith("D") ? "Dịch vụ"
                     : hddt.MaHddt != null && hddt.MaHddt.EndsWith("S") ? "Sản phẩm"
                     : "Gộp",
                TrangThaiHoaDon = (int?)hddt.TrangThai,
                SoHoaDon = hddt.SoHoaDon,
                LinkPdf = hddt.LinkPdf,
                GhiChuLoi = hddt.GhiChuLoi
            }).ToList();
            var hddtGop = hoaDonDienTus.FirstOrDefault(x => x.Loai == "Gộp");
            var hddtDichVu = hoaDonDienTus.FirstOrDefault(x => x.Loai == "Dịch vụ") ?? hddtGop;
            var hddtSanPham = hoaDonDienTus.FirstOrDefault(x => x.Loai == "Sản phẩm") ?? hddtGop;
            bool daKhopHoanToan = hoaDonDienTus.Count > 0 && hoaDonDienTus.All(x => x.TrangThaiHoaDon == 1);

            return new
            {
                h.MaHd,
                h.NgayTao,
                h.TongTien,
                h.TrangThai,
                h.TenKh,
                h.SdtKh,
                h.TenNv,
                h.DanhSachDichVu,
                CoHoaDon = hoaDonDienTus.Count > 0,
                DaKhopHoanToan = daKhopHoanToan,
                HoaDonDienTus = hoaDonDienTus,
                TrangThaiHddtDichVu = hddtDichVu?.TrangThaiHoaDon,
                SoHoaDonDichVu = hddtDichVu?.SoHoaDon,
                LinkPdfDichVu = hddtDichVu?.LinkPdf,
                GhiChuLoiDichVu = hddtDichVu?.GhiChuLoi,

                TrangThaiHddtSanPham = hddtSanPham?.TrangThaiHoaDon,
                SoHoaDonSanPham = hddtSanPham?.SoHoaDon,
                LinkPdfSanPham = hddtSanPham?.LinkPdf,
                GhiChuLoiSanPham = hddtSanPham?.GhiChuLoi
            };
        }).ToList();

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
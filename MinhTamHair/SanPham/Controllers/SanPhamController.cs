using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SanPhamController : ControllerBase
{
    private readonly MinhTamHairContext _context;

    public SanPhamController(MinhTamHairContext context)
    {
        _context = context;
    }

    [HttpGet("init")]
    public async Task<IActionResult> GetInit()
    {
        var danhMuc = await _context.DanhMucSPs
            .Where(dm => dm.TrangThai == 1)
            .Select(dm => new { dm.MaDMSP, dm.TenDanhMuc })
            .OrderBy(dm => dm.TenDanhMuc)
            .ToListAsync();

        var thuongHieu = await _context.Thuonghieus
            .Where(th => th.TrangThai == 1)
            .Select(th => new { th.MaTh, th.TenThuongHieu })
            .OrderBy(th => th.TenThuongHieu)
            .ToListAsync();

        return Ok(new { danhMuc, thuongHieu });
    }

    [HttpGet("filter")]
    public async Task<IActionResult> Filter(
        [FromQuery] string? search,
        [FromQuery] string? danhMuc,
        [FromQuery] string? thuongHieu,
        [FromQuery] string? priceRange,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 9)
    {
        var query = _context.SanPhams
            .Where(sp => sp.TrangThai == 1)
            .Include(sp => sp.MaDanhMucTheNavigation)
            .Include(sp => sp.Thuonghieu)
            .Include(sp => sp.BienTheSanphams)
            .Include(sp => sp.DanhgiaSanphams)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(sp =>
                sp.TenSp!.Contains(search) ||
                (sp.MoTa != null && sp.MoTa.Contains(search)));

        if (!string.IsNullOrWhiteSpace(danhMuc) && danhMuc != "all")
            query = query.Where(sp => sp.MaDanhMuc == danhMuc);

        if (!string.IsNullOrWhiteSpace(thuongHieu) && thuongHieu != "all")
            query = query.Where(sp => sp.MaTh == thuongHieu);

        var allList = await query.ToListAsync();

        if (!string.IsNullOrWhiteSpace(priceRange) && priceRange != "all")
        {
            allList = priceRange switch
            {
                "low" => allList.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) < 200000).ToList(),
                "mid" => allList.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) >= 200000 &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) <= 500000).ToList(),
                "high" => allList.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) > 500000).ToList(),
                _ => allList
            };
        }

        var total = allList.Count;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var paged = allList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Top 3 sản phẩm bán nhiều nhất TRONG TỪNG DANH MỤC (mỗi danh mục có 3 hot riêng)
        var hotProductIds = (await _context.ChitietHoadonSps
            .Join(
                _context.BienTheSanphams,
                ct => ct.MaBienThe,
                bt => bt.MaBienThe,
                (ct, bt) => new { bt.MaSanPham, ct.SoLuong }
            )
            .Join(
                _context.SanPhams,
                x => x.MaSanPham,
                sp => sp.MaSp,
                (x, sp) => new { x.MaSanPham, x.SoLuong, sp.MaDanhMuc }
            )
            .GroupBy(x => new { x.MaSanPham, x.MaDanhMuc })
            .Select(g => new { g.Key.MaSanPham, g.Key.MaDanhMuc, TongBan = g.Sum(x => x.SoLuong) })
            .ToListAsync())
            .GroupBy(x => x.MaDanhMuc)
            .SelectMany(g => g.OrderByDescending(x => x.TongBan).Take(3))
            .Select(x => x.MaSanPham)
            .ToHashSet();

        var result = paged.Select(sp =>
        {
            var btHoatDong = sp.BienTheSanphams
                .Where(bt => bt.TrangThai == 1).ToList();

            // Giá thấp nhất & cao nhất từ biến thể đang hoạt động
            var giaMin = btHoatDong.Any() ? btHoatDong.Min(bt => bt.GiaBan) : (decimal?)null;
            var giaMax = btHoatDong.Any() ? btHoatDong.Max(bt => bt.GiaBan) : (decimal?)null;
            var tonKho = btHoatDong.Sum(bt => bt.SoLuongTon ?? 0);

            var danhGia = sp.DanhgiaSanphams.Where(dg => dg.TrangThai == 1).ToList();
            var soSao = danhGia.Any()
                ? Math.Round(danhGia.Average(dg => dg.SoSao ?? 0), 1)
                : (double?)null;

            return new
            {
                sp.MaSp,
                sp.TenSp,
                sp.MoTa,
                sp.HinhAnhDaiDien,
                GiaTu = giaMin,
                GiaDen = giaMax,
                SoLuongTon = tonKho,
                SoBienThe = btHoatDong.Count,
                SoSao = soSao,
                SoLuongDanhGia = danhGia.Count,
                IsHot = hotProductIds.Contains(sp.MaSp),
                DanhMuc = sp.MaDanhMucTheNavigation == null ? null : new
                {
                    sp.MaDanhMucTheNavigation.MaDMSP,
                    sp.MaDanhMucTheNavigation.TenDanhMuc,
                },
                ThuongHieu = sp.Thuonghieu == null ? null : new
                {
                    sp.Thuonghieu.MaTh,
                    sp.Thuonghieu.TenThuongHieu,
                },
            };
        });

        return Ok(new { data = result, totalPages, total });
    }

    // =========================================================
    // GET api/SanPham/{id}
    // =========================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var sp = await _context.SanPhams
            .Include(s => s.MaDanhMucTheNavigation)
            .Include(s => s.Thuonghieu)
            .Include(s => s.Hinhanhsps)
            .Include(s => s.BienTheSanphams)
                .ThenInclude(bt => bt.HinhanhBTs)
            .Include(s => s.DanhgiaSanphams)
            .FirstOrDefaultAsync(s => s.MaSp == id && s.TrangThai == 1);

        if (sp == null)
            return NotFound(new { message = "Không tìm thấy sản phẩm." });

        var btHoatDong = sp.BienTheSanphams
            .Where(bt => bt.TrangThai == 1)
            .OrderBy(bt => bt.GiaBan)
            .ToList();

        // Tất cả biến thể còn active (kể cả hết hàng) — để hiển thị trên trang chi tiết
        var btHienThi = sp.BienTheSanphams
            .Where(bt => bt.TrangThai == 1 || bt.TrangThai==0)
            .OrderBy(bt => bt.SoLuongTon == 0 ? 1 : 0) // biến thể còn hàng lên trước
            .ThenBy(bt => bt.GiaBan)
            .ToList();

        var danhGia = sp.DanhgiaSanphams
            .Where(dg => dg.TrangThai == 1).ToList();

        var goiY = await _context.SanPhams
            .Where(s => s.MaDanhMuc == sp.MaDanhMuc && s.MaSp != id && s.TrangThai == 1)
            .Include(s => s.Thuonghieu)
            .Include(s => s.BienTheSanphams)
            .Take(4)
            .Select(s => new
            {
                s.MaSp,
                s.TenSp,
                s.MoTa,
                s.HinhAnhDaiDien,
                GiaTu = s.BienTheSanphams
                    .Where(bt => bt.TrangThai == 1)
                    .Min(bt => (decimal?)bt.GiaBan),
                GiaDen = s.BienTheSanphams
                    .Where(bt => bt.TrangThai == 1)
                    .Max(bt => (decimal?)bt.GiaBan),
                ThuongHieu = s.Thuonghieu == null ? null : new
                {
                    s.Thuonghieu.MaTh,
                    s.Thuonghieu.TenThuongHieu,
                },
            })
            .ToListAsync();

        var result = new
        {
            sp.MaSp,
            sp.TenSp,
            sp.MoTa,
            sp.ThanhPhan,
            sp.HuongDanSuDung,
            sp.HinhAnhDaiDien,

            DanhMuc = sp.MaDanhMucTheNavigation == null ? null : new
            {
                sp.MaDanhMucTheNavigation.MaDMSP,
                sp.MaDanhMucTheNavigation.TenDanhMuc,
            },
            ThuongHieu = sp.Thuonghieu == null ? null : new
            {
                sp.Thuonghieu.MaTh,
                sp.Thuonghieu.TenThuongHieu,
                sp.Thuonghieu.HinhAnh,
            },

            // Gallery ảnh (bảng HinhAnhSP)
            HinhAnhs = sp.Hinhanhsps
                .Select(h => new { h.MaHa, h.DuongDan })
                .ToList(),

            // Biến thể — còn hàng lên trước, hết hàng xuống dưới
            BienThes = btHienThi.Select(bt => new
            {
                bt.MaBienThe,
                bt.TenBienThe,
                bt.GiaBan,
                bt.SoLuongTon,
                bt.HinhAnhDaiDien,
                bt.PhanTramHoaHong,
                // Gallery ảnh của từng biến thể (bảng HINHANH_BT)
                HinhAnhs = bt.HinhanhBTs
                    .Select(h => new { h.MaHa, h.DuongDan })
                    .ToList(),
            }).ToList(),

            // Dải giá tổng (cho header khi chưa chọn biến thể)
            GiaTu = btHoatDong.Any() ? btHoatDong.Min(bt => bt.GiaBan) : (decimal?)null,
            GiaDen = btHoatDong.Any() ? btHoatDong.Max(bt => bt.GiaBan) : (decimal?)null,

            SoSao = danhGia.Any()
                ? Math.Round(danhGia.Average(dg => dg.SoSao ?? 0), 1)
                : (double?)null,
            SoLuongDanhGia = danhGia.Count,

            SuggestedProducts = goiY,
        };

        return Ok(result);
    }
}
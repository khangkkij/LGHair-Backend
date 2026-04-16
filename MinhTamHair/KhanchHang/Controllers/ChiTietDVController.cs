using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MinhTamHair.Models;
using MinhTamHair.KhanchHang.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServicesController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;

        public ServicesController(MinhTamHairContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetServiceDetail(string id)
        {
            var cacheKey = $"DetailServiceUser_{id}";
            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (cachedData != null)
            {
                return Content(cachedData, "application/json");
            }
            // 🔹 Load service + ảnh + tiêu đề + chi tiết tiêu đề + kiểu tóc
            var service = await _context.Dichvus
                .Include(d => d.Hinhanhs)
                .Include(d => d.TieudeDichvus)
                    .ThenInclude(td => td.ChitietTieudes)
                .Include(d => d.Kifutocs)
                .FirstOrDefaultAsync(d => d.MaDv == id);

            if (service == null)
                return NotFound();

            // 🔹 Combos chứa service này
            var combos = await _context.Combos
                .Include(c => c.Combo_DichVus)
                    .ThenInclude(cd => cd.DichVu)
                .Where(c => c.Combo_DichVus.Any(cd => cd.MaDV == id))
                .ToListAsync();

            // 🔹 Suggested services (lấy cùng danh mục nếu có, fallback random)
            var suggested = await _context.Dichvus
                .Where(d => d.MaDv != id && d.TrangThai == 1)
                .OrderBy(d => d.MaDm == service.MaDm ? 0 : 1) // ưu tiên cùng danh mục
                .Take(3)
                .ToListAsync();

            var now = DateTime.Now;

            // 🔹 Flash sale đang active
            var flashSales = await _context.ChitietFlashsales
                .Where(ct => ct.MaFlNavigation.TrangThai == 1
                    && ct.MaFlNavigation.NgayBatDau <= now
                    && ct.MaFlNavigation.NgayKetThuc >= now)
                .Select(ct => new
                {
                    ct.MaDv,
                    ct.PhanTramGiam,
                    ct.MaFlNavigation.NgayKetThuc
                })
                .ToListAsync();

            // ================= MAIN SERVICE =================
            var fsMain = flashSales.FirstOrDefault(f => f.MaDv == service.MaDv);

            var giaGocMain = service.Gia ?? 0;
            var giaSauMain = giaGocMain;
            int? ptgMain = null;
            DateTime? ngayKetThucMain = null;

            if (fsMain != null && fsMain.PhanTramGiam.HasValue)
            {
                ptgMain = fsMain.PhanTramGiam;
                giaSauMain = giaGocMain * (100 - ptgMain.Value) / 100;
                ngayKetThucMain = fsMain.NgayKetThuc;
            }

            // ================= RESULT =================
            var result = new
            {
                MaDv = service.MaDv,
                TenDv = service.TenDv,
                Gia = giaSauMain,
                GiaGoc = giaGocMain,
                PhanTramGiam = ptgMain,
                NgayKetThuc = ngayKetThucMain,
                ThoiGianLam = service.ThoiGianLam ?? 0,
                MoTa = service.MoTa,

                Images = service.Hinhanhs
                    .Select(h => h.DuongDan)
                    .ToList(),

                // ================= TIÊU ĐỀ + CHI TIẾT TIÊU ĐỀ =================
                TieudeDichvus = service.TieudeDichvus
                    .Where(td => td.TrangThai == 1)
                    .OrderBy(td => td.SoThuTu)
                    .Select(td => new
                    {
                        MaTd = td.MaTd,
                        TenTieuDe = td.TenTieuDe,
                        MoTaTieuDe = td.MoTaTieuDe,
                        ChiTiet = td.ChitietTieudes
                            .OrderBy(ct => ct.SoThuTu)
                            .Select(ct => new
                            {
                                MaCttd = ct.MaCttd,
                                TenTieuDe = ct.TenTieuDe,
                                MoTaTieuDe = ct.MoTaTieuDe,
                                HinhAnh = ct.HinhAnh
                            })
                            .ToList()
                    })
                    .ToList(),

                // ================= KIỂU TÓC (CAROUSEL) =================
                KieuTocs = service.Kifutocs
                    .OrderBy(kt => kt.MaKt)
                    .Select(kt => new
                    {
                        MaKt = kt.MaKt,
                        TenKieuToc = kt.TenKieuToc,
                        HinhAnh = kt.HinhAnh,
                        MoTa = kt.MoTa
                    })
                    .ToList(),

                // ================= COMBOS =================
                RelatedCombos = combos.Select(c =>
                {
                    // Tính giá gốc = tổng giá các dịch vụ trong combo
                    var giaGocCombo = c.Combo_DichVus
                        .Sum(cd => cd.DichVu?.Gia ?? 0);

                    var giaCombo = c.Gia ?? 0;

                    // Tính % tiết kiệm so với mua lẻ
                    int? ptgCombo = null;
                    if (giaGocCombo > 0 && giaCombo < giaGocCombo)
                    {
                        ptgCombo = (int)Math.Round((1 - (double)giaCombo / (double)giaGocCombo) * 100);
                    }

                    return new
                    {
                        Id = c.MaCb,
                        TenCombo = c.TenCombo,
                        Gia = giaCombo,
                        GiaGoc = giaGocCombo > giaCombo ? (decimal?)giaGocCombo : null,
                        PhanTramGiam = ptgCombo,
                        MoTa = c.MoTa,
                        HinhAnh = c.HinhAnh,
                        Items = c.Combo_DichVus
                            .Select(cd => cd.DichVu != null ? cd.DichVu.TenDv : null)
                            .Where(t => t != null)
                            .ToList(),
                    };
                }).ToList(),

                // ================= SUGGESTED =================
                SuggestedServices = suggested.Select(s =>
                {
                    var fs = flashSales.FirstOrDefault(f => f.MaDv == s.MaDv);

                    var giaGoc = s.Gia ?? 0;
                    var giaSau = giaGoc;
                    int? ptg = null;
                    DateTime? ngayKetThuc = null;

                    if (fs != null && fs.PhanTramGiam.HasValue)
                    {
                        ptg = fs.PhanTramGiam;
                        giaSau = giaGoc * (100 - ptg.Value) / 100;
                        ngayKetThuc = fs.NgayKetThuc;
                    }

                    return new ServiceItemDto
                    {
                        Id = s.MaDv,
                        TenDv = s.TenDv,
                        Gia = giaSau,
                        GiaGoc = giaGoc,
                        PhanTramGiam = ptg,
                        NgayKetThuc = ngayKetThuc,
                        ThoiGianLam = s.ThoiGianLam ?? 0,
                        MoTa = s.MoTa,
                        HinhAnh = _context.Hinhanhs
                            .Where(h => h.MaDv == s.MaDv)
                            .Select(h => h.DuongDan)
                            .FirstOrDefault()
                    };
                }).ToList()
            };
            var json = JsonSerializer.Serialize(result,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            await _cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1)
                });
            return Ok(result);
        }
    }
}
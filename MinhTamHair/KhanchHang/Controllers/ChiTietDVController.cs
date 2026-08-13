using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MinhTamHair.Models;
using MinhTamHair.KhanchHang.Models;
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
                return Content(cachedData, "application/json");

            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();

            // ================= SERVICE =================
            var service = await _context.Dichvus
                .Include(d => d.Hinhanhs)
                .Include(d => d.TieudeDichvus)
                    .ThenInclude(td => td.ChitietTieudes)
                .Include(d => d.Kifutocs)
                .FirstOrDefaultAsync(d => d.MaDv == id);

            if (service == null)
                return NotFound();

            // ================= COMBO =================
            var combos = await _context.Combos
                .Include(c => c.Combo_DichVus)
                    .ThenInclude(cd => cd.DichVu)
                .Where(c => c.Combo_DichVus.Any(cd => cd.MaDV == id))
                .ToListAsync();

            // ================= SUGGESTED =================
            var suggested = await _context.Dichvus
                .Include(d => d.Hinhanhs)
                .Where(d => d.MaDv != id && d.TrangThai == 1)
                .OrderBy(d => d.MaDm == service.MaDm ? 0 : 1)
                .Take(3)
                .ToListAsync();

            // ================= FLASH SALES =================
            var flashSales = await _context.ChitietFlashsales
                .Where(ct =>
                    ct.MaFlNavigation.TrangThai == 1 &&
                    ct.MaFlNavigation.NgayBatDau <= now &&
                    ct.MaFlNavigation.NgayKetThuc >= now)
                .Select(ct => new
                {
                    ct.MaDv,
                    ct.MaCb,
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

                // ================= TITLE =================
                TieudeDichvus = service.TieudeDichvus
                    .Where(td => td.TrangThai == 1)
                    .OrderBy(td => td.SoThuTu)
                    .Select(td => new
                    {
                        td.MaTd,
                        td.TenTieuDe,
                        td.MoTaTieuDe,

                        ChiTiet = td.ChitietTieudes
                            .OrderBy(ct => ct.SoThuTu)
                            .Select(ct => new
                            {
                                ct.MaCttd,
                                ct.TenTieuDe,
                                ct.MoTaTieuDe,
                                ct.HinhAnh
                            })
                            .ToList()
                    })
                    .ToList(),

                // ================= HAIR STYLES =================
                KieuTocs = service.Kifutocs
                    .OrderBy(kt => kt.MaKt)
                    .Select(kt => new
                    {
                        kt.MaKt,
                        kt.TenKieuToc,
                        kt.HinhAnh,
                        kt.MoTa
                    })
                    .ToList(),

                // ================= RELATED COMBOS =================
                RelatedCombos = combos.Select(c =>
                {
                    var giaGocCombo = c.Combo_DichVus
                        .Sum(cd => cd.DichVu?.Gia ?? 0);

                    var basePrice = c.Gia ?? giaGocCombo;

                    var flash = flashSales
                        .Where(f => f.MaCb == c.MaCb)
                        .OrderByDescending(f => f.PhanTramGiam)
                        .FirstOrDefault();

                    decimal giaSau = basePrice;
                    int? ptg = null;
                    DateTime? ngayKetThuc = null;

                    if (flash != null && flash.PhanTramGiam.HasValue)
                    {
                        ptg = flash.PhanTramGiam;

                        giaSau = basePrice * (100 - ptg.Value) / 100;

                        ngayKetThuc = flash.NgayKetThuc;
                    }
                    else
                    {
                        if (giaGocCombo > 0 && basePrice < giaGocCombo)
                        {
                            ptg = (int)Math.Round(
                                (1 - (double)basePrice / (double)giaGocCombo) * 100
                            );
                        }
                    }

                    return new
                    {
                        Id = c.MaCb,
                        TenCombo = c.TenCombo,
                        Gia = giaSau,
                        GiaGoc = basePrice,
                        PhanTramGiam = ptg,
                        NgayKetThuc = ngayKetThuc,
                        MoTa = c.MoTa,
                        HinhAnh = c.HinhAnh,

                        Items = c.Combo_DichVus
                            .Select(cd => cd.DichVu != null ? cd.DichVu.TenDv : null)
                            .Where(x => x != null)
                            .ToList()
                    };
                }).ToList(),

                // ================= SUGGESTED SERVICES =================
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

                    // THÊM ĐƯỜNG DẪN CỤ THỂ VÀO TRƯỚC CHỮ ServiceItemDto ĐỂ ÉP C# DÙNG ĐÚNG FILE
                    return new MinhTamHair.KhanchHang.Models.ServiceItemDto
                    {
                        Id = s.MaDv,
                        TenDv = s.TenDv,
                        Gia = giaSau,
                        GiaGoc = giaGoc,
                        PhanTramGiam = ptg,
                        NgayKetThuc = ngayKetThuc,
                        ThoiGianLam = s.ThoiGianLam ?? 0,
                        MoTa = s.MoTa,
                        HinhAnh = s.Hinhanhs.FirstOrDefault()?.DuongDan
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
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
                });

            return Ok(result);
        }
    }
}
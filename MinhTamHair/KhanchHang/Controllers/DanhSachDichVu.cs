using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DanhSachDichVuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;

        public DanhSachDichVuController(MinhTamHairContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var cacheKey = "home_all";

            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (cachedData != null)
            {
                return Content(cachedData, "application/json");
            }

            // ================= CATEGORIES =================
            var categories = await _context.DanhMucs
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaDm,
                    x.TenDanhMuc
                })
                .ToListAsync();

            // ================= HOT SERVICES (đếm số lần đặt) =================
            // Top 3 dịch vụ được đặt nhiều nhất TRONG TỪNG DANH MỤC (mỗi danh mục có 3 hot riêng)
            var hotServiceIds = (await _context.ChitietLichdats
                .Where(x => x.MaDv != null)
                .Join(
                    _context.Dichvus,
                    ct => ct.MaDv,
                    dv => dv.MaDv,
                    (ct, dv) => new { ct.MaDv, dv.MaDm }
                )
                .GroupBy(x => new { x.MaDv, x.MaDm })
                .Select(g => new { g.Key.MaDv, g.Key.MaDm, SoLuong = g.Count() })
                .ToListAsync())
                .GroupBy(x => x.MaDm)
                .SelectMany(g => g.OrderByDescending(x => x.SoLuong).Take(3))
                .Select(x => x.MaDv)
                .ToHashSet();

            // ================= SERVICES (SAFE LOAD) =================
            var servicesRaw = await _context.Dichvus
                .Where(x => x.TrangThai == 1)
                .Include(x => x.Hinhanhs)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .ToListAsync();
            var now = DateTime.Now;
            var services = servicesRaw.Select(x =>
            {
                // KIỂM TRA DỊCH VỤ CÓ ĐANG TRONG THỜI GIAN SALE KHÔNG
                var flash = x.ChitietFlashsales
                    .FirstOrDefault(fs =>
                        fs.MaFlNavigation.TrangThai == 1 &&
                        fs.MaFlNavigation.NgayBatDau <= now &&
                        fs.MaFlNavigation.NgayKetThuc >= now);

                return new
                {
                    x.MaDv,
                    x.TenDv,
                    x.Gia,
                    x.ThoiGianLam,
                    x.MoTa,
                    MaDm = x.MaDm,

                    HinhAnh = x.Hinhanhs.FirstOrDefault()?.DuongDan,

                    // ĐÁNH DẤU HOT
                    IsHot = hotServiceIds.Contains(x.MaDv),

                    // BƠM DỮ LIỆU SALE RA CHUẨN NHƯ HÀM FILTER
                    FlashSale = flash == null ? null : new
                    {
                        flash.PhanTramGiam,
                        flash.MaFlNavigation.NgayKetThuc
                    }
                };
            }).ToList();

            // ================= COMBOS =================
            var combosRaw = await _context.Combos
                .Where(x => x.TrangThai == 1)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .ToListAsync();

            var combos = combosRaw.Select(x =>
            {
                var flash = x.ChitietFlashsales
                    .FirstOrDefault(fs =>
                        fs.MaFlNavigation.TrangThai == 1 &&
                        fs.MaFlNavigation.NgayBatDau <= now &&
                        fs.MaFlNavigation.NgayKetThuc >= now);

                return new
                {
                    x.MaCb,
                    x.TenCombo,

                    GiaGoc = x.Gia,

                    Gia = flash == null ? x.Gia : x.Gia - (x.Gia * flash.PhanTramGiam / 100),

                    x.MoTa,
                    x.HinhAnh,

                    FlashSale = flash == null ? null : new
                    {
                        flash.PhanTramGiam,
                        flash.MaFlNavigation.NgayKetThuc
                    }
                };
            }).ToList();

            // ================= RESULT =================
            var result = new
            {
                categories,
                services,
                combos
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

        // ================= FILTER (FIXED SAFE VERSION) =================
        [HttpGet("filter")]
        public async Task<IActionResult> Filter(
            string? search,
            string? category,
            string? priceRange,
            int page = 1,
            int pageSize = 6)
        {
            var cacheKey = $"filter_{search}_{category}_{priceRange}_{page}_{pageSize}";

            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (cachedData != null)
            {
                return Content(cachedData, "application/json");
            }

            var query = _context.Dichvus
                .Where(x => x.TrangThai == 1)
                .AsQueryable();

            // SEARCH
            if (!string.IsNullOrEmpty(search))
                query = query.Where(x => x.TenDv.Contains(search));

            // CATEGORY
            if (!string.IsNullOrEmpty(category) && category != "all")
                query = query.Where(x => x.MaDm == category);

            // PRICE
            if (!string.IsNullOrEmpty(priceRange))
            {
                if (priceRange == "low")
                    query = query.Where(x => x.Gia < 300000);
                else if (priceRange == "mid")
                    query = query.Where(x => x.Gia >= 300000 && x.Gia <= 700000);
                else if (priceRange == "high")
                    query = query.Where(x => x.Gia > 700000);
            }

            var totalItems = await query.CountAsync();

            // ================= HOT SERVICES =================
            // Top 3 dịch vụ được đặt nhiều nhất TRONG TỪNG DANH MỤC (mỗi danh mục có 3 hot riêng)
            var hotServiceIds = (await _context.ChitietLichdats
                .Where(x => x.MaDv != null)
                .Join(
                    _context.Dichvus,
                    ct => ct.MaDv,
                    dv => dv.MaDv,
                    (ct, dv) => new { ct.MaDv, dv.MaDm }
                )
                .GroupBy(x => new { x.MaDv, x.MaDm })
                .Select(g => new { g.Key.MaDv, g.Key.MaDm, SoLuong = g.Count() })
                .ToListAsync())
                .GroupBy(x => x.MaDm)
                .SelectMany(g => g.OrderByDescending(x => x.SoLuong).Take(3))
                .Select(x => x.MaDv)
                .ToHashSet();

            // ================= LOAD SAFE (NO PROJECTION IN SQL) =================
            var list = await query
                .Include(x => x.Hinhanhs)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .OrderBy(x => x.MaDv)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var now = DateTime.Now;

            var services = list.Select(x =>
            {
                var flash = x.ChitietFlashsales
                    .FirstOrDefault(fs =>
                        fs.MaFlNavigation.TrangThai == 1 &&
                        fs.MaFlNavigation.NgayBatDau <= now &&
                        fs.MaFlNavigation.NgayKetThuc >= now);

                return new
                {
                    x.MaDv,
                    x.TenDv,
                    x.Gia,
                    x.ThoiGianLam,
                    x.MoTa,

                    HinhAnh = x.Hinhanhs.FirstOrDefault()?.DuongDan,

                    // ĐÁNH DẤU HOT
                    IsHot = hotServiceIds.Contains(x.MaDv),

                    FlashSale = flash == null ? null : new
                    {
                        flash.PhanTramGiam,
                        flash.MaFlNavigation.NgayKetThuc
                    }
                };
            }).ToList();

            var result = new
            {
                data = services,
                totalItems,
                totalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                currentPage = page
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
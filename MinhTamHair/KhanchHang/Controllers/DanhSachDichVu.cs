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

            // ================= SERVICES (SAFE LOAD) =================
            var servicesRaw = await _context.Dichvus
                .Where(x => x.TrangThai == 1)
                .Include(x => x.Hinhanhs)
                .ToListAsync();

            var services = servicesRaw.Select(x => new
            {
                x.MaDv,
                x.TenDv,
                x.Gia,
                x.ThoiGianLam,
                x.MoTa,
                MaDm = x.MaDm,

                HinhAnh = x.Hinhanhs.FirstOrDefault()?.DuongDan
            }).ToList();

            // ================= COMBOS =================
            var combos = await _context.Combos
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaCb,
                    x.TenCombo,
                    x.Gia,
                    x.MoTa,
                    x.HinhAnh
                })
                .ToListAsync();

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
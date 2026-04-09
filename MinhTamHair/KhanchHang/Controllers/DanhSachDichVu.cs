using Microsoft.AspNetCore.Mvc;
using MinhTamHair.Models;
using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading.Tasks;

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

        // ==============================
        // GET ALL DATA
        // ==============================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var cacheKey = "home_all";

            var cachedData = await _cache.GetStringAsync(cacheKey);

            if (cachedData != null)
            {
                return Content(cachedData, "application/json");
            }
            var categories = _context.DanhMucs
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaDm,
                    x.TenDanhMuc,
                })
                .ToList();

            var services = _context.Dichvus
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaDv,
                    x.TenDv,
                    x.Gia,
                    x.MaDmNavigation,
                    x.ThoiGianLam,
                    x.MoTa,
                    HinhAnh = x.Hinhanhs
                        .Select(h => h.DuongDan)
                        .FirstOrDefault()
                })
                .ToList();

            var combos = _context.Combos
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaCb,
                    x.TenCombo,
                    x.Gia,
                    x.MoTa,
                    x.HinhAnh
                })
                .ToList();

            var result = new
            {
                categories,
                services,
                combos
            };

            var json = JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            await _cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                });

            return Ok(result);
        }
        // ==============================
        // FILTER SERVICES
        // ==============================
        [HttpGet("filter")]
        public async Task<IActionResult> Filter(
            string? search,
            string? category,
            string? priceRange,
            int page = 1,
            int pageSize = 6)
        {
            var cacheKey = $"filterServicesUser_{search}_{category}_{priceRange}_{page}_{pageSize}";
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
            {
                query = query.Where(x => x.TenDv.Contains(search));
            }

            // CATEGORY
            if (!string.IsNullOrEmpty(category) && category != "all")
            {
                query = query.Where(x => x.MaDm == category);
            }

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

            // 🔥 TOTAL COUNT
            var totalItems = query.Count();

            var now = DateTime.Now;

            var services = query
                .OrderBy(x => x.MaDv)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.MaDv,
                    x.TenDv,
                    x.Gia,
                    x.ThoiGianLam,
                    x.MoTa,

                    HinhAnh = x.Hinhanhs
                        .Select(h => h.DuongDan)
                        .FirstOrDefault(),

                    FlashSale = x.ChitietFlashsales
                        .Where(fs => fs.MaFlNavigation.TrangThai == 1
                            && fs.MaFlNavigation.NgayBatDau <= now
                            && fs.MaFlNavigation.NgayKetThuc >= now)
                        .Select(fs => new
                        {
                            fs.PhanTramGiam,
                            fs.MaFlNavigation.NgayKetThuc
                        })
                        .FirstOrDefault()
                })
                .ToList();
            var result = new
            {
                data = services,
                totalItems,
                totalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                currentPage = page
            };
            var json = JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            await _cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                }
                );
            return Ok(result);
        }
    }
}
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

        // Thời gian sống cache Redis cho toàn bộ endpoint dịch vụ: 20 phút.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);

        // Truy vấn "top dịch vụ hot theo từng danh mục" khá nặng (join + group by trên
        // ChitietLichdats). Nó không đổi trong ngắn hạn nên tách ra cache riêng (20 phút)
        // và dùng chung cho cả GetAll lẫn Filter, thay vì phải tính lại mỗi lần cache
        // tổng bị miss. Đây là phần giúp response nhanh gần như ngay lập tức (<300ms)
        // khi cache còn hạn, thay vì phải chờ query nặng chạy lại.
        private async Task<HashSet<string>> GetHotServiceIdsAsync()
        {
            const string cacheKey = "hot_service_ids";

            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached != null)
            {
                return JsonSerializer.Deserialize<HashSet<string>>(cached) ?? new HashSet<string>();
            }

            var hotServiceIds = (await _context.ChitietLichdats
                .AsNoTracking()
                .Where(x => x.MaDv != null)
                .Join(
                    _context.Dichvus.AsNoTracking(),
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

            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(hotServiceIds),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

            return hotServiceIds;
        }

        // 🔥 GetAll giờ phân trang cả dịch vụ lẫn combo (mỗi trang chỉ load + trả đúng phần
        // cần hiển thị), thay vì trước đây load TOÀN BỘ Dichvus/Combos trong 1 lần rồi cache
        // chung 1 key "home_all" (rất nặng khi bảng lớn, và cache miss thì phải tính lại hết).
        // Mặc định: 6 dịch vụ/trang, 3 combo/trang — trang sau chỉ được load khi FE
        // chủ động gọi lại với servicePage/comboPage tương ứng (giống cơ chế của /filter).
        [HttpGet]
        public async Task<IActionResult> GetAll(
            int servicePage = 1,
            int servicePageSize = 6,
            int comboPage = 1,
            int comboPageSize = 3)
        {
            var cacheKey = $"home_all_{servicePage}_{servicePageSize}_{comboPage}_{comboPageSize}";

            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (cachedData != null)
            {
                return Content(cachedData, "application/json");
            }

            var categories = await _context.DanhMucs
                .AsNoTracking()
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaDm,
                    x.TenDanhMuc
                })
                .ToListAsync();

            var hotServiceIds = await GetHotServiceIdsAsync();
            var now = DateTime.Now;

            // ---------- DỊCH VỤ (phân trang) ----------
            var serviceQuery = _context.Dichvus
                .AsNoTracking()
                .Where(x => x.TrangThai == 1);

            var totalServices = await serviceQuery.CountAsync();

            var servicesRaw = await serviceQuery
                .Include(x => x.Hinhanhs)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .OrderBy(x => x.MaDv)
                .Skip((servicePage - 1) * servicePageSize)
                .Take(servicePageSize)
                .ToListAsync();

            var services = servicesRaw.Select(x =>
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
                    MaDm = x.MaDm,

                    HinhAnh = x.Hinhanhs.FirstOrDefault()?.DuongDan,
                    IsHot = hotServiceIds.Contains(x.MaDv),
                    FlashSale = flash == null ? null : new
                    {
                        flash.PhanTramGiam,
                        flash.MaFlNavigation.NgayKetThuc
                    }
                };
            }).ToList();

            // ---------- COMBO (phân trang) ----------
            var comboQuery = _context.Combos
                .AsNoTracking()
                .Where(x => x.TrangThai == 1);

            var totalCombos = await comboQuery.CountAsync();

            var combosRaw = await comboQuery
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .OrderBy(x => x.MaCb)
                .Skip((comboPage - 1) * comboPageSize)
                .Take(comboPageSize)
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

            var result = new
            {
                categories,
                services,
                servicesTotalItems = totalServices,
                servicesTotalPages = (int)Math.Ceiling((double)totalServices / servicePageSize),
                servicesCurrentPage = servicePage,

                combos,
                combosTotalItems = totalCombos,
                combosTotalPages = (int)Math.Ceiling((double)totalCombos / comboPageSize),
                combosCurrentPage = comboPage
            };

            var json = JsonSerializer.Serialize(result,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            await _cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl
                });

            return Ok(result);
        }
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
                .AsNoTracking()
                .Where(x => x.TrangThai == 1)
                .Include(x => x.Hinhanhs)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(x => x.TenDv.Contains(search));

            if (!string.IsNullOrEmpty(category) && category != "all")
                query = query.Where(x => x.MaDm == category);

            var now = DateTime.Now;

            // Giá sau flash sale (nếu đang chạy) mới là giá khách thực trả,
            // nên phải tính trước rồi mới lọc theo priceRange thay vì lọc
            // thẳng trên x.Gia gốc.
            var hotServiceIds = await GetHotServiceIdsAsync();
            var dvRaw = await query
                .OrderBy(x => x.MaDv)
                .ToListAsync();

            var dvPriced = dvRaw.Select(x =>
            {
                var flash = x.ChitietFlashsales
                    .FirstOrDefault(fs =>
                        fs.MaFlNavigation.TrangThai == 1 &&
                        fs.MaFlNavigation.NgayBatDau <= now &&
                        fs.MaFlNavigation.NgayKetThuc >= now);

                var giaGoc = x.Gia ?? 0;
                var giaThucTe = flash == null
                    ? giaGoc
                    : giaGoc - (giaGoc * flash.PhanTramGiam / 100);

                return new
                {
                    Dv = x,
                    GiaGoc = giaGoc,
                    GiaThucTe = giaThucTe,
                    Flash = flash
                };
            });

            if (!string.IsNullOrEmpty(priceRange))
            {
                if (priceRange == "low")
                    dvPriced = dvPriced.Where(x => x.GiaThucTe < 300000);
                else if (priceRange == "mid")
                    dvPriced = dvPriced.Where(x => x.GiaThucTe >= 300000 && x.GiaThucTe <= 700000);
                else if (priceRange == "high")
                    dvPriced = dvPriced.Where(x => x.GiaThucTe > 700000);
            }

            var dvPricedList = dvPriced.ToList();
            var totalItems = dvPricedList.Count;

            var pagedList = dvPricedList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var services = pagedList.Select(x => new
            {
                x.Dv.MaDv,
                x.Dv.TenDv,
                x.Dv.Gia,
                x.Dv.ThoiGianLam,
                x.Dv.MoTa,

                HinhAnh = x.Dv.Hinhanhs.FirstOrDefault()?.DuongDan,
                IsHot = hotServiceIds.Contains(x.Dv.MaDv),

                FlashSale = x.Flash == null ? null : new
                {
                    x.Flash.PhanTramGiam,
                    x.Flash.MaFlNavigation.NgayKetThuc
                }
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
                    AbsoluteExpirationRelativeToNow = CacheTtl
                });

            return Ok(result);
        }

        // -------------------------------------------------------
        // GET /api/DanhSachDichVu/filter-combo
        // Lọc combo theo tên, dùng cho panel "Bộ Lọc" nổi ở khu vực
        // GÓI COMBO ĐẶC BIỆT (FE gọi lại API thay vì lọc tạm trên
        // dữ liệu client). Không lọc theo khoảng giá — bộ lọc giá
        // chỉ áp dụng cho dịch vụ (endpoint /filter ở trên), combo
        // luôn hiển thị đầy đủ theo tên tìm kiếm.
        // -------------------------------------------------------
        [HttpGet("filter-combo")]
        public async Task<IActionResult> FilterCombo(
            string? search,
            int page = 1,
            int pageSize = 3)
        {
            var cacheKey = $"filter_combo_{search}_{page}_{pageSize}";

            var cachedData = await _cache.GetStringAsync(cacheKey);
            if (cachedData != null)
            {
                return Content(cachedData, "application/json");
            }

            var now = DateTime.Now;

            var query = _context.Combos
                .AsNoTracking()
                .Where(x => x.TrangThai == 1)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(x => x.TenCombo.Contains(search));

            var comboRaw = await query
                .OrderBy(x => x.MaCb)
                .ToListAsync();

            var comboPriced = comboRaw.Select(x =>
            {
                var flash = x.ChitietFlashsales
                    .FirstOrDefault(fs =>
                        fs.MaFlNavigation.TrangThai == 1 &&
                        fs.MaFlNavigation.NgayBatDau <= now &&
                        fs.MaFlNavigation.NgayKetThuc >= now);

                var giaGoc = x.Gia ?? 0;
                var giaThucTe = flash == null
                    ? giaGoc
                    : giaGoc - (giaGoc * flash.PhanTramGiam / 100);

                return new
                {
                    Combo = x,
                    GiaGoc = giaGoc,
                    GiaThucTe = giaThucTe,
                    Flash = flash
                };
            });

            var comboPricedList = comboPriced.ToList();
            var totalItems = comboPricedList.Count;

            var pagedList = comboPricedList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var combos = pagedList.Select(x => new
            {
                x.Combo.MaCb,
                x.Combo.TenCombo,

                GiaGoc = x.GiaGoc,
                Gia = x.GiaThucTe,

                x.Combo.MoTa,
                x.Combo.HinhAnh,

                FlashSale = x.Flash == null ? null : new
                {
                    x.Flash.PhanTramGiam,
                    x.Flash.MaFlNavigation.NgayKetThuc
                }
            }).ToList();

            var result = new
            {
                data = combos,
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
                    AbsoluteExpirationRelativeToNow = CacheTtl
                });

            return Ok(result);
        }
    }
}
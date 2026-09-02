using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MinhTamHair.Models;
using MinhTamHair.Services;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DanhSachDichVuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IServiceCacheService _cache;
        private readonly IMemoryCache _memoryCache;

        // TTL 10 phút cho toàn bộ cache đọc dịch vụ — đổi ở đây thay vì rải khắp file.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        // "Top dịch vụ hot" không cần realtime (chỉ đổi khi có đơn đặt lịch mới), nên
        // cache riêng lâu hơn hẳn (1 giờ) bằng IMemoryCache in-process — tránh phải
        // join + group by toàn bộ ChitietLichdats lại mỗi lần GetAll/Filter cache-miss.
        private const string HotServiceIdsCacheKey = "sv:hot-service-ids";
        private static readonly TimeSpan HotServiceIdsCacheTtl = TimeSpan.FromHours(1);

        public DanhSachDichVuController(MinhTamHairContext context, IServiceCacheService cache, IMemoryCache memoryCache)
        {
            _context = context;
            _cache = cache;
            _memoryCache = memoryCache;
        }

        // Truy vấn "top dịch vụ hot theo từng danh mục" khá nặng (join + group by trên
        // ChitietLichdats), dùng chung cho cả GetAll lẫn Filter. Cache riêng 1 giờ vì
        // dữ liệu này thay đổi chậm.
        private async Task<HashSet<string>> GetHotServiceIdsAsync()
        {
            if (_memoryCache.TryGetValue(HotServiceIdsCacheKey, out HashSet<string>? cached) && cached != null)
                return cached;

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

            _memoryCache.Set(HotServiceIdsCacheKey, hotServiceIds, HotServiceIdsCacheTtl);
            return hotServiceIds;
        }

        // GetAll phân trang cả dịch vụ lẫn combo (mỗi trang chỉ load + trả đúng phần
        // cần hiển thị). Mặc định: 6 dịch vụ/trang, 3 combo/trang — trang sau chỉ
        // được load khi FE chủ động gọi lại với servicePage/comboPage tương ứng.
        [HttpGet]
        public async Task<IActionResult> GetAll(
            int servicePage = 1,
            int servicePageSize = 6,
            int comboPage = 1,
            int comboPageSize = 3)
        {
            var ver = await _cache.GetCurrentVersionAsync();
            var cacheKey = $"sv:v{ver}:getall:{servicePage}:{servicePageSize}:{comboPage}:{comboPageSize}";

            var json = await _cache.GetOrCreateJsonAsync(cacheKey, async () =>
                await BuildGetAllResultAsync(servicePage, servicePageSize, comboPage, comboPageSize),
                CacheTtl);

            return Content(json, "application/json");
        }

        private async Task<object?> BuildGetAllResultAsync(
            int servicePage, int servicePageSize, int comboPage, int comboPageSize)
        {
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
            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            var viCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

            // ---------- DỊCH VỤ (phân trang) ----------
            var serviceQuery = _context.Dichvus
                .AsNoTracking()
                .Where(x => x.TrangThai == 1);

            var allServicesRaw = await serviceQuery
                .Include(x => x.Hinhanhs)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .ToListAsync();

            // Sắp xếp: Dịch vụ hot ưu tiên xếp lên đầu, các dịch vụ sau sắp xếp theo bảng chữ cái ABC (A-Z)
            var servicesSorted = allServicesRaw
                .OrderByDescending(x => hotServiceIds.Contains(x.MaDv))
                .ThenBy(x => x.TenDv, StringComparer.Create(viCulture, true))
                .ToList();

            var totalServices = servicesSorted.Count;

            var servicesRaw = servicesSorted
                .Skip((servicePage - 1) * servicePageSize)
                .Take(servicePageSize)
                .ToList();

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
                // Combo tạo SAU hiển thị TRƯỚC (mới nhất lên đầu) — cùng lý do với dịch vụ.
                .OrderByDescending(x => x.MaCb)
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

            return result;
        }
        [HttpGet("filter")]
        public async Task<IActionResult> Filter(
            string? search,
            string? category,
            string? priceRange,
            int page = 1,
            int pageSize = 6)
        {
            var ver = await _cache.GetCurrentVersionAsync();
            var cacheKey = $"sv:v{ver}:filter:{search}:{category}:{priceRange}:{page}:{pageSize}";

            var json = await _cache.GetOrCreateJsonAsync(cacheKey, async () =>
                await BuildFilterResultAsync(search, category, priceRange, page, pageSize), CacheTtl);

            return Content(json, "application/json");
        }

        private async Task<object?> BuildFilterResultAsync(
            string? search, string? category, string? priceRange, int page, int pageSize)
        {
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

            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();

            // Giá sau flash sale (nếu đang chạy) mới là giá khách thực trả,
            // nên phải tính trước rồi mới lọc theo priceRange thay vì lọc
            // thẳng trên x.Gia gốc.
            var hotServiceIds = await GetHotServiceIdsAsync();
            var viCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
            var dvRaw = await query.ToListAsync();

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

            // Sắp xếp: Dịch vụ hot ưu tiên xếp lên đầu, các dịch vụ sau sắp xếp theo bảng chữ cái ABC (A-Z)
            var dvSorted = dvPriced
                .OrderByDescending(x => hotServiceIds.Contains(x.Dv.MaDv))
                .ThenBy(x => x.Dv.TenDv, StringComparer.Create(viCulture, true))
                .ToList();

            var totalItems = dvSorted.Count;

            var pagedList = dvSorted
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

            return result;
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
            var ver = await _cache.GetCurrentVersionAsync();
            var cacheKey = $"sv:v{ver}:filtercombo:{search}:{page}:{pageSize}";

            var json = await _cache.GetOrCreateJsonAsync(cacheKey, async () =>
                await BuildFilterComboResultAsync(search, page, pageSize), CacheTtl);

            return Content(json, "application/json");
        }

        private async Task<object?> BuildFilterComboResultAsync(string? search, int page, int pageSize)
        {
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
                // Combo tạo SAU hiển thị TRƯỚC (mới nhất lên đầu) — đồng bộ với GetAll.
                .OrderByDescending(x => x.MaCb)
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

            return result;
        }
    }
}
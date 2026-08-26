using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MinhTamHair.Models;
using MinhTamHair.SanPham.Controllers;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SanPhamController : ControllerBase
{
    private readonly MinhTamHairContext _context;
    private readonly IProductCacheService _cache;
    private readonly IMemoryCache _memoryCache;

    // TTL 10 phút cho toàn bộ cache đọc sản phẩm — đổi ở đây thay vì rải khắp file.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // "Sản phẩm bán chạy" tính từ lịch sử toàn bộ đơn hàng, không cần realtime (chỉ đổi
    // khi có đơn mới), nên cache riêng lâu hơn hẳn (1 giờ) bằng IMemoryCache in-process —
    // tránh phải join + group by toàn bộ ChitietHoadonSps lại mỗi lần Filter/theo-danh-muc
    // cache-miss (đây là truy vấn nặng nhất trong controller vì quét cả lịch sử đơn hàng).
    private const string HotProductIdsCacheKey = "sp:hot-product-ids";
    private static readonly TimeSpan HotProductIdsCacheTtl = TimeSpan.FromHours(1);

    public SanPhamController(MinhTamHairContext context, IProductCacheService cache, IMemoryCache memoryCache)
    {
        _context = context;
        _cache = cache;
        _memoryCache = memoryCache;
    }

    // =========================================================
    // Top 3 sản phẩm bán nhiều nhất TRONG TỪNG DANH MỤC
    // =========================================================
    private async Task<HashSet<string>> GetHotProductIdsAsync()
    {
        if (_memoryCache.TryGetValue(HotProductIdsCacheKey, out HashSet<string>? cached) && cached != null)
            return cached;

        var hotProductIds = (await _context.ChitietHoadonSps
            .AsNoTracking()
            .Join(
                _context.BienTheSanphams.AsNoTracking(),
                ct => ct.MaBienThe,
                bt => bt.MaBienThe,
                (ct, bt) => new { bt.MaSanPham, ct.SoLuong }
            )
            .Join(
                _context.SanPhams.AsNoTracking(),
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

        _memoryCache.Set(HotProductIdsCacheKey, hotProductIds, HotProductIdsCacheTtl);
        return hotProductIds;
    }

    [HttpGet("init")]
    public async Task<IActionResult> GetInit()
    {
        var ver = await _cache.GetCurrentVersionAsync();
        var json = await _cache.GetOrCreateJsonAsync($"sp:v{ver}:init", async () =>
        {
            var danhMuc = await _context.DanhMucSPs
                .AsNoTracking()
                .Where(dm => dm.TrangThai == 1)
                .Select(dm => new { dm.MaDMSP, dm.TenDanhMuc })
                .OrderBy(dm => dm.TenDanhMuc)
                .ToListAsync();

            var thuongHieu = await _context.Thuonghieus
                .AsNoTracking()
                .Where(th => th.TrangThai == 1)
                .Select(th => new
                {
                    th.MaTh,
                    th.TenThuongHieu,
                    th.HinhAnh,
                    SoLuongSP = th.Sanphams.Count(sp => sp.TrangThai == 1)
                })
                .OrderBy(th => th.TenThuongHieu)
                .ToListAsync();

            return new { danhMuc, thuongHieu };
        }, CacheTtl);

        return Content(json, "application/json");
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
        // Key gộp mọi tham số ảnh hưởng tới kết quả — mỗi tổ hợp search/lọc/trang là 1 cache riêng.
        // Nhúng version vào key: sau mỗi lần CRUD sản phẩm, version tăng lên -> mọi tổ hợp filter
        // cũ (dù chưa hết TTL) đều bị bỏ qua, request tiếp theo sẽ build lại dữ liệu mới.
        var ver = await _cache.GetCurrentVersionAsync();
        var cacheKey = $"sp:v{ver}:filter:{search}:{danhMuc}:{thuongHieu}:{priceRange}:{page}:{pageSize}";

        var json = await _cache.GetOrCreateJsonAsync(cacheKey, async () =>
            await BuildFilterResultAsync(search, danhMuc, thuongHieu, priceRange, page, pageSize), CacheTtl);

        return Content(json, "application/json");
    }

    private async Task<object?> BuildFilterResultAsync(
        string? search, string? danhMuc, string? thuongHieu, string? priceRange, int page, int pageSize)
    {
        // Query "gọn" (chưa Include) dùng để đếm tổng + lọc theo giá ngay trong SQL.
        // Không gọi ToListAsync() ở bước này — mọi điều kiện lọc vẫn còn là IQueryable
        // nên EF Core dịch hết ra SQL (kể cả lọc theo giá qua subquery Min trên biến thể),
        // tránh phải kéo toàn bộ bảng sản phẩm về app rồi mới lọc/cắt trang trong bộ nhớ.
        var query = _context.SanPhams
            .AsNoTracking()
            .Where(sp => sp.TrangThai == 1)
            // Ẩn luôn sản phẩm thuộc danh mục đang tạm ẩn (TrangThai == 0)
            .Where(sp => sp.MaDanhMucTheNavigation == null || sp.MaDanhMucTheNavigation.TrangThai == 1)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(sp =>
                sp.TenSp!.Contains(search) ||
                (sp.MoTa != null && sp.MoTa.Contains(search)));

        if (!string.IsNullOrWhiteSpace(danhMuc) && danhMuc != "all")
            query = query.Where(sp => sp.MaDanhMuc == danhMuc);

        if (!string.IsNullOrWhiteSpace(thuongHieu) && thuongHieu != "all")
            query = query.Where(sp => sp.MaTh == thuongHieu);

        if (!string.IsNullOrWhiteSpace(priceRange) && priceRange != "all")
        {
            query = priceRange switch
            {
                "low" => query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) < 200000),
                "mid" => query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) >= 200000 &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) <= 500000),
                "high" => query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) > 500000),
                _ => query
            };
        }

        // Đếm tổng để tính totalPages — không Include nên nhẹ, không kéo dữ liệu chi tiết
        // của các trang khác về.
        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        // Cần ORDER BY tường minh thì Skip/Take mới cho kết quả ổn định giữa các lần gọi.
        // CHỈ TỪ ĐÂY mới Include + Skip/Take + ToListAsync — nghĩa là chỉ đúng những sản
        // phẩm thuộc TRANG ĐANG XEM mới được kéo đầy đủ biến thể/đánh giá về, các trang
        // chưa bấm tới hoàn toàn không được truy vấn.
        var paged = await query
            // Sản phẩm tạo SAU hiển thị TRƯỚC (mới nhất lên đầu) — mã sản phẩm tăng dần
            // theo thứ tự tạo (SP43 tạo sau SP42), nên sắp giảm dần theo mã là đúng.
            .OrderByDescending(sp => sp.MaSp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(sp => sp.MaDanhMucTheNavigation)
            .Include(sp => sp.Thuonghieu)
            .Include(sp => sp.BienTheSanphams)
            .Include(sp => sp.DanhgiaSanphams)
            .Include(sp => sp.Hinhanhsps)
            .AsSplitQuery()
            .ToListAsync();

        // Top 3 sản phẩm bán nhiều nhất TRONG TỪNG DANH MỤC
        var hotProductIds = await GetHotProductIdsAsync();

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
                // Vài ảnh phụ đầu tiên (ngoài ảnh đại diện) để hiển thị lướt trái/phải
                // ngay trên card danh sách — không lấy hết gallery cho nhẹ payload.
                HinhAnhs = sp.Hinhanhsps
                    .OrderBy(h => h.MaHa)
                    .Take(5)
                    .Select(h => h.DuongDan)
                    .ToList(),
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
        }).ToList();

        var response = new { data = result, totalPages, total };

        return response;
    }

    // =========================================================
    // FEED TRANG SHOP: danh mục kèm banner + sản phẩm phân trang riêng từng danh mục
    // Dùng cho khối "Banner A -> Sản phẩm A -> Banner B -> Sản phẩm B -> ..." ở đầu
    // trang Shop.vue, tách biệt hoàn toàn với khu vực Filter/Grid/Phân trang cũ.
    // =========================================================

    private const int TRANG_THAI_BANNER = 2;

    /// <summary>
    /// Danh sách danh mục (đang hiện) kèm toàn bộ banner của từng danh mục (nếu có).
    /// Không kèm sản phẩm — FE sẽ gọi riêng "san-pham-theo-danh-muc" cho từng danh mục
    /// khi cuộn tới, để tránh tải nặng ngay lần đầu.
    /// </summary>
    [HttpGet("danh-muc-feed")]
    public async Task<IActionResult> GetDanhMucFeed()
    {
        var ver = await _cache.GetCurrentVersionAsync();
        var json = await _cache.GetOrCreateJsonAsync($"sp:v{ver}:danhmucfeed", async () =>
        {
            var data = await BuildDanhMucFeedAsync();
            return new { data };
        }, CacheTtl);

        return Content(json, "application/json");
    }

    // Đọc số thứ tự hiển thị từ tên banner đầu tiên (hậu tố "-<số>" ở cuối tên).
    // Không có hậu tố hợp lệ -> null (chưa set số thứ tự).
    private static int? ParseSttFromTenBanner(string? tenBanner)
    {
        if (string.IsNullOrWhiteSpace(tenBanner)) return null;
        var lastDash = tenBanner.LastIndexOf('-');
        if (lastDash > 0 && lastDash < tenBanner.Length - 1
            && int.TryParse(tenBanner[(lastDash + 1)..], out var stt))
        {
            return stt;
        }
        return null;
    }

    private async Task<object?> BuildDanhMucFeedAsync()
    {
        // Danh mục thật (loại bỏ các row banner TrangThai == 2)
        var danhMucs = await _context.DanhMucSPs
            .AsNoTracking()
            .Where(dm => dm.TrangThai == 1)
            .OrderBy(dm => dm.TenDanhMuc)
            .Select(dm => new { dm.MaDMSP, dm.TenDanhMuc })
            .ToListAsync();

        // Toàn bộ banner (TrangThai == 2), mã dạng "<MaDanhMucCha>-<STT>"
        var allBanners = await _context.DanhMucSPs
            .AsNoTracking()
            .Where(dm => dm.TrangThai == TRANG_THAI_BANNER)
            .OrderBy(dm => dm.MaDMSP)
            .Select(dm => new { dm.MaDMSP, dm.TenDanhMuc, dm.HinhAnh })
            .ToListAsync();

        var danhMucVoiBanner = danhMucs.Select(dm =>
        {
            var banners = allBanners
                .Where(b => b.MaDMSP.StartsWith(dm.MaDMSP + "-", StringComparison.OrdinalIgnoreCase))
                .Select(b => new
                {
                    maBanner = b.MaDMSP,
                    tieuDe = b.TenDanhMuc,
                    hinhAnh = b.HinhAnh,
                })
                .ToList();

            // Số thứ tự hiển thị của danh mục được mã hóa trong TÊN của banner — MỌI banner
            // cùng 1 danh mục đều mang CÙNG MỘT TÊN (xem UpdateThuTu/AddBanner bên
            // QuanLyDanhMucSPController) — nên lấy banner BẤT KỲ (FirstOrDefault trong danh
            // sách "banners" đã lọc theo đúng danh mục này ở trên) là đủ để đọc đúng STT; xóa
            // banner nào cũng không làm mất STT miễn danh mục còn ít nhất 1 banner.
            var bannerBatKy = banners.FirstOrDefault();

            return new
            {
                dm.MaDMSP,
                dm.TenDanhMuc,
                Banners = banners,
                ThuTu = bannerBatKy != null ? ParseSttFromTenBanner(bannerBatKy.tieuDe) : null,
            };
        }).ToList();

        // 🔥 GHÉP DANH MỤC KHÔNG-BANNER VÀO DANH MỤC-BANNER GẦN NHẤT
        // Danh mục nào không có banner riêng thì sẽ "ở ké" sản phẩm chung khối với 1 danh
        // mục có banner, thay vì đứng thành 1 khối trơ trọi không banner giữa trang.
        // Ưu tiên ghép vào danh mục-banner gần nhất đứng NGAY TRƯỚC nó (theo thứ tự alphabet
        // đang OrderBy ở trên); nếu nó nằm trước mọi danh mục-banner (chưa có ai phía trước)
        // thì tạm ghép vào danh mục-banner đầu tiên tìm thấy.
        var coBanner = danhMucVoiBanner.Where(dm => dm.Banners.Count > 0).ToList();
        var nhomGhep = coBanner.ToDictionary(dm => dm.MaDMSP, dm => new List<string> { dm.MaDMSP });

        if (coBanner.Count > 0)
        {
            string maBannerGanNhatPhiaTruoc = null;
            foreach (var dm in danhMucVoiBanner)
            {
                if (dm.Banners.Count > 0)
                {
                    maBannerGanNhatPhiaTruoc = dm.MaDMSP;
                    continue;
                }
                var target = maBannerGanNhatPhiaTruoc ?? coBanner[0].MaDMSP;
                nhomGhep[target].Add(dm.MaDMSP);
            }
        }

        // 🔥 SẮP XẾP THEO SỐ THỨ TỰ HIỂN THỊ
        // Danh mục có banner: sắp theo ThuTu đọc từ tên banner đầu tiên (chưa đặt số thì
        // coi như "chưa set", xếp sau các danh mục đã có số, giữ thứ tự alphabet cũ giữa
        // các danh mục cùng chưa-set hoặc cùng số). Danh mục không-banner luôn xếp sau
        // toàn bộ danh mục có banner (chúng chỉ "ở ké" hiển thị, không đứng khối riêng).
        var coBannerDaSapXep = coBanner
            .OrderBy(dm => dm.ThuTu.HasValue ? 0 : 1)
            .ThenBy(dm => dm.ThuTu ?? int.MaxValue)
            .ThenBy(dm => dm.TenDanhMuc)
            .ToList();

        // Nếu không có danh mục nào có banner (hiếm khi xảy ra) thì đành giữ nguyên,
        // mỗi danh mục vẫn là 1 khối riêng như trước (không có gì để ghép vào cả).
        var result = coBanner.Count > 0
            ? coBannerDaSapXep.Select(dm => new
            {
                maDanhMuc = dm.MaDMSP,
                tenDanhMuc = dm.TenDanhMuc,
                banners = dm.Banners,
                // Toàn bộ mã danh mục đang "ở ké" chung khối này (luôn gồm chính nó) — FE
                // dùng để gộp sản phẩm của các danh mục không-banner vào chung khối này.
                maDanhMucGop = nhomGhep[dm.MaDMSP],
            }).ToList()
            : danhMucVoiBanner.Select(dm => new
            {
                maDanhMuc = dm.MaDMSP,
                tenDanhMuc = dm.TenDanhMuc,
                banners = dm.Banners,
                maDanhMucGop = new List<string> { dm.MaDMSP },
            }).ToList();

        return result;
    }

    /// <summary>
    /// Sản phẩm của 1 danh mục cụ thể, phân trang riêng — dùng cho nút "Xem thêm"
    /// bên dưới mỗi khối banner trong feed. pageSize mặc định 8 (~2 hàng x 4 cột).
    /// </summary>
    [HttpGet("san-pham-theo-danh-muc")]
    public async Task<IActionResult> GetSanPhamTheoDanhMuc(
        [FromQuery] string maDanhMuc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 8)
    {
        if (string.IsNullOrWhiteSpace(maDanhMuc))
            return BadRequest(new { message = "Thiếu mã danh mục." });

        // 🔥 Hỗ trợ ghép sản phẩm nhiều danh mục vào chung 1 khối feed (danh mục không có
        // banner "ở ké" sản phẩm vào khối của danh mục có banner) — maDanhMuc có thể là
        // 1 mã đơn hoặc nhiều mã cách nhau dấu phẩy, ví dụ "DM01,DM02,DM03".
        var maDanhMucList = maDanhMuc
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        if (maDanhMucList.Count == 0)
            return BadRequest(new { message = "Thiếu mã danh mục." });

        var ver = await _cache.GetCurrentVersionAsync();
        var cacheKey = $"sp:v{ver}:theodanhmuc:{string.Join(',', maDanhMucList.OrderBy(x => x))}:{page}:{pageSize}";

        var json = await _cache.GetOrCreateJsonAsync(cacheKey, async () =>
            await BuildSanPhamTheoDanhMucAsync(maDanhMucList, page, pageSize), CacheTtl);

        return Content(json, "application/json");
    }

    private async Task<object?> BuildSanPhamTheoDanhMucAsync(List<string> maDanhMucList, int page, int pageSize)
    {
        var query = _context.SanPhams
            .AsNoTracking()
            .Where(sp => sp.TrangThai == 1 && maDanhMucList.Contains(sp.MaDanhMuc))
            .Where(sp => sp.MaDanhMucTheNavigation == null || sp.MaDanhMucTheNavigation.TrangThai == 1);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var paged = await query
            // Sản phẩm tạo SAU hiển thị TRƯỚC (mới nhất lên đầu) — đồng bộ với BuildFilterResultAsync.
            .OrderByDescending(sp => sp.MaSp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(sp => sp.MaDanhMucTheNavigation)
            .Include(sp => sp.Thuonghieu)
            .Include(sp => sp.BienTheSanphams)
            .Include(sp => sp.DanhgiaSanphams)
            .Include(sp => sp.Hinhanhsps)
            .AsSplitQuery()
            .ToListAsync();

        var hotProductIds = await GetHotProductIdsAsync();

        var result = paged.Select(sp =>
        {
            var btHoatDong = sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).ToList();
            var giaMin = btHoatDong.Any() ? btHoatDong.Min(bt => bt.GiaBan) : (decimal?)null;
            var giaMax = btHoatDong.Any() ? btHoatDong.Max(bt => bt.GiaBan) : (decimal?)null;
            var tonKho = btHoatDong.Sum(bt => bt.SoLuongTon ?? 0);

            var danhGia = sp.DanhgiaSanphams.Where(dg => dg.TrangThai == 1).ToList();
            var soSao = danhGia.Any() ? Math.Round(danhGia.Average(dg => dg.SoSao ?? 0), 1) : (double?)null;

            return new
            {
                sp.MaSp,
                sp.TenSp,
                sp.MoTa,
                sp.HinhAnhDaiDien,
                HinhAnhs = sp.Hinhanhsps.OrderBy(h => h.MaHa).Take(5).Select(h => h.DuongDan).ToList(),
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
        }).ToList();

        var response = new { data = result, totalPages, total, page };

        return response;
    }

    // =========================================================
    // GET api/SanPham/{id}
    // =========================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        // "Không tìm thấy" KHÔNG được cache — nếu cache lỗi 404 sẽ chặn sản phẩm mới tạo
        // trong 10 phút kế tiếp dù DB đã có, nên IProductCacheService tự bỏ qua việc cache null
        // (xem NullMarker trong ProductCacheService) và trả "__NULL__" cho trường hợp này.
        var ver = await _cache.GetCurrentVersionAsync();
        var cacheKey = $"sp:v{ver}:detail:{id}";
        var json = await _cache.GetOrCreateJsonAsync(cacheKey, async () =>
            await BuildSanPhamDetailAsync(id), CacheTtl);

        if (json == ProductCacheConstants.NullMarker)
            return NotFound(new { message = "Không tìm thấy sản phẩm." });

        return Content(json, "application/json");
    }

    private async Task<object?> BuildSanPhamDetailAsync(string id)
    {
        var sp = await _context.SanPhams
            .AsNoTracking()
            .Include(s => s.MaDanhMucTheNavigation)
            .Include(s => s.Thuonghieu)
            .Include(s => s.Hinhanhsps)
            .Include(s => s.BienTheSanphams)
                .ThenInclude(bt => bt.HinhanhBTs)
            .Include(s => s.DanhgiaSanphams)
            .FirstOrDefaultAsync(s => s.MaSp == id && s.TrangThai == 1);

        // Danh mục cha đang tạm ẩn thì coi như sản phẩm cũng không tồn tại với khách hàng
        if (sp != null && sp.MaDanhMucTheNavigation != null && sp.MaDanhMucTheNavigation.TrangThai != 1)
            sp = null;

        if (sp == null)
            return null;

        var btHoatDong = sp.BienTheSanphams
            .Where(bt => bt.TrangThai == 1)
            .OrderBy(bt => bt.GiaBan)
            .ToList();

        // Tất cả biến thể còn active (kể cả hết hàng) — để hiển thị trên trang chi tiết
        var btHienThi = sp.BienTheSanphams
            .Where(bt => bt.TrangThai == 1 || bt.TrangThai == 0)
            .OrderBy(bt => bt.SoLuongTon == 0 ? 1 : 0) // biến thể còn hàng lên trước
            .ThenBy(bt => bt.GiaBan)
            .ToList();

        var danhGia = sp.DanhgiaSanphams
            .Where(dg => dg.TrangThai == 1).ToList();

        var goiY = await _context.SanPhams
            .AsNoTracking()
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

        return result;
    }
}
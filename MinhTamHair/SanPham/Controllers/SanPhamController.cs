using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MinhTamHair.Models;
using System.Text.Json;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SanPhamController : ControllerBase
{
    private readonly MinhTamHairContext _context;
    private readonly IDistributedCache _cache;

    public SanPhamController(MinhTamHairContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // Options JSON dùng chung cho mọi response cache — đồng bộ camelCase với bên DichVu
    // để FE (Shop.vue) không phải sửa gì cả khi đọc dữ liệu từ cache lẫn từ Ok(...).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Cache "init" (danh mục + thương hiệu) và "filter" gần như không đổi trong ngắn hạn
    // → 20 phút, giống bên dịch vụ.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);

    // Chi tiết sản phẩm có thể được admin cập nhật (giá, tồn kho...) thường xuyên hơn
    // nên để TTL ngắn hơn một chút, tránh khách thấy giá/tồn kho cũ quá lâu.
    private static readonly TimeSpan DetailCacheTtl = TimeSpan.FromMinutes(10);

    // =========================================================
    // Top 3 sản phẩm bán nhiều nhất TRONG TỪNG DANH MỤC
    // Query này join + group by trên ChitietHoadonSp khá nặng nhưng không đổi trong
    // ngắn hạn → tách cache riêng (20 phút), dùng chung cho Filter (và có thể tái sử
    // dụng ở nơi khác nếu cần), y hệt GetHotServiceIdsAsync bên DichVu.
    // =========================================================
    private async Task<HashSet<string>> GetHotProductIdsAsync()
    {
        const string cacheKey = "hot_product_ids";

        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached != null)
        {
            return JsonSerializer.Deserialize<HashSet<string>>(cached) ?? new HashSet<string>();
        }

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

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(hotProductIds),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return hotProductIds;
    }

    [HttpGet("init")]
    public async Task<IActionResult> GetInit()
    {
        const string cacheKey = "product_init";

        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (cachedData != null)
        {
            return Content(cachedData, "application/json");
        }

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

        var result = new { danhMuc, thuongHieu };

        var json = JsonSerializer.Serialize(result, JsonOpts);
        await _cache.SetStringAsync(cacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return Ok(result);
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
        // Cache theo đúng tổ hợp tham số lọc — giống hệt cách Filter bên DichVu đang làm.
        var cacheKey = $"product_filter_{search}_{danhMuc}_{thuongHieu}_{priceRange}_{page}_{pageSize}";

        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (cachedData != null)
        {
            return Content(cachedData, "application/json");
        }

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
            .OrderBy(sp => sp.MaSp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(sp => sp.MaDanhMucTheNavigation)
            .Include(sp => sp.Thuonghieu)
            .Include(sp => sp.BienTheSanphams)
            .Include(sp => sp.DanhgiaSanphams)
            .Include(sp => sp.Hinhanhsps)
            .AsSplitQuery()
            .ToListAsync();

        // Top 3 sản phẩm bán nhiều nhất TRONG TỪNG DANH MỤC — lấy từ cache riêng (20 phút)
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

        var json = JsonSerializer.Serialize(response, JsonOpts);
        await _cache.SetStringAsync(cacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return Ok(response);
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
        // 🔥 "_v2": đổi version khi đổi FORMAT dữ liệu trả về (vd thêm field maDanhMucGop) để
        // cache cũ (set trước lúc deploy, còn hạn TTL 20 phút) không bị phục vụ nhầm — tránh
        // trường hợp deploy xong nhưng vẫn thấy hành vi cũ do cache chưa hết hạn.
        const string cacheKey = "product_danhmuc_feed_v2";

        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (cachedData != null)
        {
            return Content(cachedData, "application/json");
        }

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

        var danhMucVoiBanner = danhMucs.Select(dm => new
        {
            dm.MaDMSP,
            dm.TenDanhMuc,
            Banners = allBanners
                .Where(b => b.MaDMSP.StartsWith(dm.MaDMSP + "-", StringComparison.OrdinalIgnoreCase))
                .Select(b => new
                {
                    maBanner = b.MaDMSP,
                    tieuDe = b.TenDanhMuc,
                    hinhAnh = b.HinhAnh,
                })
                .ToList(),
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

        // Nếu không có danh mục nào có banner (hiếm khi xảy ra) thì đành giữ nguyên,
        // mỗi danh mục vẫn là 1 khối riêng như trước (không có gì để ghép vào cả).
        var result = coBanner.Count > 0
            ? coBanner.Select(dm => new
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

        var json = JsonSerializer.Serialize(new { data = result }, JsonOpts);
        await _cache.SetStringAsync(cacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return Ok(new { data = result });
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

        var cacheKey = $"product_by_danhmuc_{string.Join("_", maDanhMucList)}_{page}_{pageSize}";

        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (cachedData != null)
        {
            return Content(cachedData, "application/json");
        }

        var query = _context.SanPhams
            .AsNoTracking()
            .Where(sp => sp.TrangThai == 1 && maDanhMucList.Contains(sp.MaDanhMuc))
            .Where(sp => sp.MaDanhMucTheNavigation == null || sp.MaDanhMucTheNavigation.TrangThai == 1);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var paged = await query
            .OrderBy(sp => sp.MaSp)
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

        var json = JsonSerializer.Serialize(response, JsonOpts);
        await _cache.SetStringAsync(cacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return Ok(response);
    }

    // =========================================================
    // GET api/SanPham/{id}
    // =========================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var cacheKey = $"product_detail_{id}";

        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (cachedData != null)
        {
            return Content(cachedData, "application/json");
        }

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
            return NotFound(new { message = "Không tìm thấy sản phẩm." });

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

        var json = JsonSerializer.Serialize(result, JsonOpts);
        await _cache.SetStringAsync(cacheKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DetailCacheTtl });

        return Ok(result);
    }
}
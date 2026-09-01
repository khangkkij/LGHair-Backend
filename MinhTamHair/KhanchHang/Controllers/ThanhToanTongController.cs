using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MinhTamHair.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System.Security.Cryptography;
using System.Text;
using MinhTamHair.Services.TimeHelper;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThanhToanTongController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IQrEmailService _qrEmailService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDistributedCache _cache;

        public ThanhToanTongController(
            MinhTamHairContext context,
            IConfiguration configuration,
            IQrEmailService qrEmailService,
            IServiceScopeFactory scopeFactory,
            IDistributedCache cache)
        {
            _context = context;
            _configuration = configuration;
            _qrEmailService = qrEmailService;
            _scopeFactory = scopeFactory;
            _cache = cache;
        }

        private static string PendingInvoiceCacheKey(string maHd) => $"pending_invoice_{maHd}";

        public class PendingInvoiceInfo
        {
            public byte LoaiKhach { get; set; }
            public string? TenNguoiMua { get; set; }
            public string? Mst { get; set; }
            public string? DiaChiXuatHd { get; set; }
            public string? EmailNhanHd { get; set; }
        }

        [HttpGet("get-services")]
        public async Task<IActionResult> GetServices()
        {
            try
            {
                var now = VnTimeHelper.Now();
                var viCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

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

                var rawServices = await _context.Dichvus
                    .AsNoTracking()
                    .Where(s => s.TrangThai == 1)
                    .Select(s => new
                    {
                        id = s.MaDv,
                        name = s.TenDv,
                        originalPrice = s.Gia,
                        duration = s.ThoiGianLam ?? 30,
                        hinhAnh = s.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault(),
                        phanTramGiam = s.ChitietFlashsales
                            .Where(fs => fs.MaFlNavigation.TrangThai == 1 &&
                                         fs.MaFlNavigation.NgayBatDau <= now &&
                                         fs.MaFlNavigation.NgayKetThuc >= now)
                            .Select(fs => fs.PhanTramGiam)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                var services = rawServices.Select(s =>
                {
                    decimal finalPrice = (decimal)s.originalPrice;
                    if (s.phanTramGiam.HasValue && s.phanTramGiam.Value > 0)
                    {
                        finalPrice = (decimal)s.originalPrice - ((decimal)s.originalPrice * (decimal)s.phanTramGiam.Value / 100m);
                    }

                    return new
                    {
                        id = s.id,
                        name = s.name,
                        price = finalPrice,
                        originalPrice = s.originalPrice,
                        duration = s.duration,
                        hinhAnh = s.hinhAnh,
                        icon = s.hinhAnh,
                        isHot = hotServiceIds.Contains(s.id)
                    };
                })
                .OrderByDescending(s => hotServiceIds.Contains(s.id))
                .ThenBy(s => s.name, StringComparer.Create(viCulture, true))
                .ToList();

                var rawCombos = await _context.Combos
                    .AsNoTracking()
                    .Where(c => c.TrangThai == 1)
                    .Select(c => new
                    {
                        id = c.MaCb,
                        name = c.TenCombo,
                        price = c.Gia,
                        originalPrice = c.Gia,
                        duration = c.Combo_DichVus.Sum(cd => cd.DichVu.ThoiGianLam ?? 30),
                        hinhAnh = c.HinhAnh,
                        icon = "fas fa-layer-group",
                    })
                    .ToListAsync();

                var combos = rawCombos
                    .OrderBy(c => c.name, StringComparer.Create(viCulture, true))
                    .ToList();

                return Ok(new { success = true, services = services, combos = combos });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi lấy dịch vụ: " + ex.Message });
            }
        }

        [HttpGet("get-products")]
        public async Task<IActionResult> GetProducts([FromQuery] string? keyword = "", [FromQuery] string? categoryId = "")
        {
            try
            {
                var query = _context.SanPhams.AsNoTracking().Where(sp => sp.TrangThai == 1);

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    query = query.Where(sp => sp.TenSp.Contains(keyword));
                }

                if (!string.IsNullOrWhiteSpace(categoryId))
                {
                    query = query.Where(sp => sp.MaDanhMuc == categoryId);
                }

                query = query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1));

                var result = await query.SelectMany(sp => sp.BienTheSanphams
                    .Where(bt => bt.TrangThai == 1)
                    .Select(bt => new
                    {
                        maSp = sp.MaSp,
                        maBienThe = bt.MaBienThe,
                        tenSp = sp.TenSp,
                        tenBienThe = bt.TenBienThe,
                        giaBan = bt.GiaBan,
                        soLuongTon = bt.SoLuongTon,
                        hinhAnhDaiDien = !string.IsNullOrEmpty(bt.HinhAnhDaiDien) ? bt.HinhAnhDaiDien : sp.HinhAnhDaiDien
                    }))
                    .Take(30)
                    .ToListAsync();

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi lấy sản phẩm: " + ex.Message });
            }
        }

        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal, [FromQuery] string? itemIds)
        {
            var kh = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);

            var now = VnTimeHelper.Now();
            var cartIds = string.IsNullOrWhiteSpace(itemIds) ? new List<string>() : itemIds.Split(',').ToList();

            string? maRank = kh?.MaRank;
            int? rankCap = null;
            decimal tienGiamThanhVien = 0m;
            int phanTramGiamRank = 0;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                if (rank != null)
                {
                    rankCap = rank.PhanTramGiamGia;
                    phanTramGiamRank = rank.PhanTramGiamGia ?? 0;
                    if (phanTramGiamRank > 0 && subTotal > 0)
                    {
                        tienGiamThanhVien = Math.Round(subTotal * ((decimal)phanTramGiamRank / 100m), 0);
                    }
                }
            }

            decimal subTotalSauKhiGiamRank = subTotal - tienGiamThanhVien;

            var myCodes = kh != null ? kh.MaCodes : new List<Voucher>();
            var allVouchers = myCodes.ToList();

            var usageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in allVouchers.Select(v => v.MaCode))
            {
                usageMap[c] = await _context.HoaDons.AsNoTracking().CountAsync(h => h.MaKh == maKH && h.MaCode == c);
            }

            var result = new List<object>();
            foreach (var v in allVouchers)
            {
                if (v.TrangThai != null && v.TrangThai != 1) continue;
                if (v.NgayBatDau.HasValue && now < v.NgayBatDau.Value) continue;
                if (v.NgayKetThuc.HasValue && now > v.NgayKetThuc.Value) continue;
                if (v.SoLuong.HasValue && (v.SoLuongDaDung ?? 0) >= v.SoLuong.Value) continue;

                var uses = usageMap.GetValueOrDefault(v.MaCode, 0);
                if (uses >= 1) continue;
                var err = VoucherCartLogic.ValidateAndCalculate(v, uses, subTotalSauKhiGiamRank, cartIds, now, now, maRank, rankCap, out var tienGiam, out _);

                decimal displayDiscount = tienGiam;
                if (displayDiscount == 0)
                {
                    if (v.SoTienGiam.HasValue && v.SoTienGiam > 0)
                        displayDiscount = v.SoTienGiam.Value;
                    else if (v.PhanTramGiam.HasValue && v.PhanTramGiam > 0 && subTotalSauKhiGiamRank > 0)
                        displayDiscount = Math.Round(subTotalSauKhiGiamRank * v.PhanTramGiam.Value / 100m, 0);
                }

                string desc = v.MoTa ?? "";
                if (v.GiaTriToiThieu.HasValue && v.GiaTriToiThieu > 0 && subTotalSauKhiGiamRank < v.GiaTriToiThieu.Value)
                {
                    desc += $" (Đơn tối thiểu {v.GiaTriToiThieu.Value:N0}đ)";
                }

                result.Add(new
                {
                    id = v.MaCode,
                    code = v.MaCode,
                    discount = displayDiscount,
                    desc = desc,
                    expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : "Vĩnh viễn",
                    isAvailable = (err == null),
                    errorMessage = err
                });
            }
            return Ok(result);
        }

        [HttpPost("get-available-stylists")]
        public async Task<IActionResult> GetAvailableStylists([FromBody] ThanhToanCheckStylistRequest request)
        {
            try
            {
                if (!DateOnly.TryParse(request.NgayHen, out DateOnly ngayHen))
                {
                    return BadRequest(new { message = "Ngày hẹn không hợp lệ." });
                }
                if (!TimeOnly.TryParse(request.GioBatDau, out TimeOnly start))
                {
                    return BadRequest(new { message = "Giờ bắt đầu không hợp lệ." });
                }

                TimeOnly end = start.AddMinutes(request.TongThoiGian);

                string reqMaKh = (request.MaKh ?? "").Trim();
                string reqPhone = (request.Phone ?? "").Trim();

                var myCustomerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(reqMaKh) && reqMaKh != "GUEST")
                {
                    myCustomerIds.Add(reqMaKh);
                }
                if (!string.IsNullOrEmpty(reqPhone))
                {
                    var khs = await _context.Khachhangs.AsNoTracking()
                        .Where(k => k.Sdt == reqPhone)
                        .Select(k => k.MaKh.Trim())
                        .ToListAsync();
                    foreach (var kId in khs) myCustomerIds.Add(kId);
                }

                var myUnpaidBookingIds = await _context.HoaDons
                    .Where(h => myCustomerIds.Contains(h.MaKh) && h.TrangThai == 0)
                    .Select(h => h.MaLd)
                    .ToListAsync();

                var favoriteStylistIds = new List<string>();
                if (!string.IsNullOrEmpty(reqMaKh) && reqMaKh != "GUEST")
                {
                    favoriteStylistIds = await _context.Set<YeuThichNhanVien>()
                        .Where(y => y.MaKH.Trim() == reqMaKh)
                        .Select(y => y.MaNV.Trim())
                        .ToListAsync();
                }

                var lichLamViecs = await _context.Lichlamviecs
                    .AsNoTracking()
                    .Include(l => l.MaNvNavigation)
                    .ThenInclude(nv => nv.MaLoaiNvNavigation)
                    .Where(l => l.NgayLamViec == ngayHen &&
                                l.TrangThai != 0 &&
                                l.TrangThai != 4 &&
                                l.TrangThai != 5 &&
                                l.TrangThai != 7 &&
                                l.CaLam != "Chưa xếp" &&
                                l.CaLam != "Nghỉ phép" &&
                                l.CaLam != "Nghỉ lễ" &&
                                l.MaNvNavigation.TrangThai == 1)
                    .ToListAsync();

                // DÙNG CHUẨN ĐIỀU KIỆN != 0
                var cacLichBan = await _context.ChitietLichdats
                    .AsNoTracking()
                    .Where(ct => ct.MaLdNavigation.NgayHen == ngayHen
                              && ct.MaLdNavigation.TrangThai != 0)
                    .Select(ct => new
                    {
                        MaNv = (ct.MaNv ?? "").Trim(),
                        GioBatDau = ct.MaLdNavigation.GioBatDau,
                        GioKetThuc = ct.MaLdNavigation.GioKetThuc,
                        TrangThai = ct.MaLdNavigation.TrangThai,
                        MaKh = (ct.MaLdNavigation.MaKh ?? "").Trim(),
                        MaLd = ct.MaLd
                    })
                    .Distinct()
                    .ToListAsync();

                var result = new List<object>();

                foreach (var lich in lichLamViecs)
                {
                    var nv = lich.MaNvNavigation;
                    string nvId = (nv.MaNv ?? "").Trim();
                    bool isAvailable = true;
                    string reason = "";
                    var lichTrinhChiTiet = new List<string>();

                    bool dungCaLam = false;
                    string ca = (lich.CaLam ?? "").Trim();
                    string gioLamThucTe = ca;
                    TimeOnly shiftStart = new TimeOnly(7, 0);
                    TimeOnly shiftEnd = new TimeOnly(20, 0);

                    if (ca.Contains("Ca sáng (7h-17h)") || ca.Contains("Ca ngày") || ca == "Ca sáng") { gioLamThucTe = "07:00 - 17:00"; shiftStart = new TimeOnly(7, 0); shiftEnd = new TimeOnly(17, 0); }
                    else if (ca.Contains("Ca tối (13h-20h)") || ca == "Ca tối") { gioLamThucTe = "13:00 - 20:00"; shiftStart = new TimeOnly(13, 0); shiftEnd = new TimeOnly(20, 0); }
                    else if (ca.Contains("Cả ngày") || ca.Contains("Full time")) { gioLamThucTe = "07:00 - 20:00"; shiftStart = new TimeOnly(7, 0); shiftEnd = new TimeOnly(20, 0); }
                    else if (ca.Contains("Ca sáng (Part-time)")) { gioLamThucTe = "07:00 - 11:00"; shiftStart = new TimeOnly(7, 0); shiftEnd = new TimeOnly(11, 0); }
                    else if (ca.Contains("Ca chiều (Part-time)")) { gioLamThucTe = "13:00 - 17:00"; shiftStart = new TimeOnly(13, 0); shiftEnd = new TimeOnly(17, 0); }
                    else if (ca.Contains("Ca tối (Part-time)")) { gioLamThucTe = "16:00 - 20:00"; shiftStart = new TimeOnly(16, 0); shiftEnd = new TimeOnly(20, 0); }

                    if (start >= shiftStart && end <= shiftEnd) dungCaLam = true;

                    var cacLichCuaTho = cacLichBan
                        .Where(b => b.MaNv.Equals(nvId, StringComparison.OrdinalIgnoreCase)
                                 && !((b.TrangThai != 0) && myUnpaidBookingIds.Contains(b.MaLd))) // Đã sửa theo TrangThai != 0
                        .OrderBy(b => b.GioBatDau)
                        .ToList();

                    if (!dungCaLam)
                    {
                        isAvailable = false;
                        reason = $"Trái ca (Làm {gioLamThucTe})";
                        lichTrinhChiTiet.Add(reason);
                    }
                    else
                    {
                        TimeOnly currentTime = shiftStart;

                        foreach (var b in cacLichCuaTho)
                        {
                            if (b.GioBatDau.HasValue && b.GioKetThuc.HasValue)
                            {
                                if (currentTime < b.GioBatDau.Value)
                                {
                                    lichTrinhChiTiet.Add($"Lịch trống: {currentTime.ToString(@"HH\:mm")} - {b.GioBatDau.Value.ToString(@"HH\:mm")}");
                                }

                                lichTrinhChiTiet.Add($"Có hẹn: {b.GioBatDau.Value.ToString(@"HH\:mm")} - {b.GioKetThuc.Value.ToString(@"HH\:mm")}");

                                if (currentTime < b.GioKetThuc.Value)
                                {
                                    currentTime = b.GioKetThuc.Value;
                                }
                            }
                        }

                        if (currentTime < shiftEnd)
                        {
                            lichTrinhChiTiet.Add($"Lịch trống: {currentTime.ToString(@"HH\:mm")} - {shiftEnd.ToString(@"HH\:mm")}");
                        }

                        const int MAX_CAPACITY = 2;
                        int maxActiveInInterval = 0;
                        bool isOverCapacity = false;

                        for (TimeOnly t = start; t < end; t = t.AddMinutes(15))
                        {
                            int activeAtT = cacLichCuaTho.Count(b => b.GioBatDau.HasValue && b.GioKetThuc.HasValue && b.GioBatDau.Value <= t && t < b.GioKetThuc.Value);
                            if (activeAtT > maxActiveInInterval)
                            {
                                maxActiveInInterval = activeAtT;
                            }
                            if (activeAtT >= MAX_CAPACITY)
                            {
                                isOverCapacity = true;
                                break;
                            }
                        }

                        if (isOverCapacity)
                        {
                            isAvailable = false;
                            reason = $"Đã kín chỗ ({maxActiveInInterval}/{MAX_CAPACITY} khách)";
                        }
                        else if (maxActiveInInterval > 0)
                        {
                            reason = $"Đang nhận ({maxActiveInInterval}/{MAX_CAPACITY} khách)";
                        }
                    }

                    var ratingVal = _context.DanhgiaDichvus.Where(dg => _context.HoaDons.Any(hd => dg.MaHd == hd.MaHd && _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))).Average(dg => (double?)dg.SoSao);
                    bool isFav = favoriteStylistIds.Contains(nvId);

                    result.Add(new
                    {
                        id = nvId,
                        name = nv.HoTenNv,
                        role = nv.MaLoaiNvNavigation?.TenLoai ?? "Stylist",
                        img = nv.HinhAnh ?? "/images/no-image.png",
                        rating = ratingVal != null ? Math.Round(ratingVal.Value, 1) : 5.0,
                        isAvailable = isAvailable,
                        unAvailableReason = reason,
                        caLam = lich.CaLam,
                        lichTrinh = lichTrinhChiTiet,
                        isFavorite = isFav
                    });
                }

                var finalResult = result
                    .OrderByDescending(r => (bool)((dynamic)r).isAvailable)
                    .ThenByDescending(r => (bool)((dynamic)r).isFavorite)
                    .ToList();

                return Ok(finalResult);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi Backend: " + ex.Message });
            }
        }

        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(string maKH, [FromQuery] string? codes, [FromQuery] decimal subTotal, [FromQuery] string? itemIds)
        {
            var parts = string.IsNullOrWhiteSpace(codes) ? new List<string>() : codes.Split(',').Select(VoucherCartLogic.NormalizeMaCode).Take(2).ToList();

            var kh = await _context.Khachhangs.Include(k => k.MaCodes).FirstOrDefaultAsync(k => k.MaKh == maKH);

            string? maRank = kh?.MaRank;
            int? rankCap = null;
            decimal tienGiamThanhVien = 0m;
            int phanTramGiamRank = 0;

            if (kh != null && !string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank);
                if (rank != null)
                {
                    rankCap = rank.PhanTramGiamGia;
                    phanTramGiamRank = rank.PhanTramGiamGia ?? 0;
                    if (phanTramGiamRank > 0 && subTotal > 0)
                    {
                        tienGiamThanhVien = Math.Round(subTotal * ((decimal)phanTramGiamRank / 100m), 0);
                    }
                }
            }

            decimal subTotalSauKhiGiamRank = subTotal - tienGiamThanhVien;

            if (parts.Count == 0)
            {
                return Ok(new
                {
                    totalDiscount = tienGiamThanhVien,
                    finalAmount = subTotalSauKhiGiamRank,
                    memberDiscount = tienGiamThanhVien,
                    voucherAmount = 0m,
                    memberRankPercent = phanTramGiamRank,
                    codes = parts
                });
            }

            var cartIds = string.IsNullOrWhiteSpace(itemIds) ? new List<string>() : itemIds.Split(',').ToList();

            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(_context, parts, maKH, subTotalSauKhiGiamRank, cartIds, VnTimeHelper.Now(), VnTimeHelper.Now(), maRank, rankCap, maxVouchersAllowed: 2);

            if (err != null) return BadRequest(new { message = err });

            return Ok(new
            {
                totalDiscount = tienGiamThanhVien + totalGiam,
                finalAmount = sau,
                memberDiscount = tienGiamThanhVien,
                voucherAmount = totalGiam,
                memberRankPercent = phanTramGiamRank,
                codes = parts
            });
        }

        [HttpPost("submit-checkout")]
        public async Task<IActionResult> SubmitCheckout([FromBody] CheckoutSummaryRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ!" });

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var dbNow = VnTimeHelper.Now();

                    // CHUẨN 10 KÝ TỰ MÃ HÓA ĐƠN VÀ MÃ LỊCH ĐẶT
                    string maHd = "TH" + dbNow.ToString("HHmmssff");

                    if (req.Products != null && req.Products.Any())
                    {
                        foreach (var p in req.Products)
                        {
                            if (string.IsNullOrEmpty(p.Id)) continue;

                            var bientheCheck = await _context.BienTheSanphams
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x => x.MaBienThe == p.Id);

                            if (bientheCheck == null)
                            {
                                return BadRequest(new { success = false, errorCode = "PRODUCT_OUT_OF_STOCK", outOfStockProductId = p.Id, message = $"Không tìm thấy sản phẩm có mã {p.Id}." });
                            }

                            int tonKho = bientheCheck.SoLuongTon ?? 0;
                            if (p.SoLuong > tonKho)
                            {
                                return BadRequest(new { success = false, errorCode = "PRODUCT_OUT_OF_STOCK", outOfStockProductId = p.Id, message = $"Sản phẩm '{bientheCheck.TenBienThe}' vượt quá số lượng tồn kho. Hiện chỉ còn {tonKho} sản phẩm, vui lòng cập nhật lại giỏ hàng." });
                            }
                        }
                    }

                    string safeKhachHang = req.MaKh;
                    if (string.IsNullOrWhiteSpace(safeKhachHang) || safeKhachHang == "GUEST")
                    {
                        if (!string.IsNullOrEmpty(req.Phone))
                        {
                            var existingKhach = await _context.Khachhangs.FirstOrDefaultAsync(x => x.Sdt == req.Phone);
                            if (existingKhach != null)
                            {
                                safeKhachHang = existingKhach.MaKh;
                                if (!string.IsNullOrWhiteSpace(req.Email) && existingKhach.Email != req.Email)
                                {
                                    existingKhach.Email = req.Email;
                                    await _context.SaveChangesAsync();
                                }
                            }
                            else
                            {
                                string prefixSdt = req.Phone.Length >= 4 ? req.Phone.Substring(0, 4) : "0000";
                                // CHUẨN 10 KÝ TỰ MÃ KHÁCH HÀNG (KH + 4 số + 4 random = 10 chars)
                                safeKhachHang = "KH" + prefixSdt + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
                                _context.Khachhangs.Add(new Khachhang
                                {
                                    MaKh = safeKhachHang,
                                    HoTen = string.IsNullOrWhiteSpace(req.CustomerName) ? "Khách vãng lai" : req.CustomerName,
                                    Sdt = req.Phone,
                                    Email = req.Email ?? "",
                                    TrangThai = 1,
                                    NgayTao = dbNow,
                                    DiemTichLuy = 0,
                                    MaRank = "RANK01"
                                });
                                await _context.SaveChangesAsync();
                            }
                        }
                        else { safeKhachHang = null; }
                    }

                    var validServices = req.Services?.Where(s => !string.IsNullOrWhiteSpace(s.Id)).ToList() ?? new List<CheckoutServiceItemDto>();
                    var validProducts = req.Products?.Where(p => !string.IsNullOrWhiteSpace(p.Id) && p.SoLuong > 0).ToList() ?? new List<ProductItemDto2>();

                    bool coDichVu = validServices.Any();
                    bool coSanPham = validProducts.Any();

                    if (!coDichVu && !coSanPham)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { success = false, message = "Giỏ hàng rỗng hoặc dữ liệu không hợp lệ!" });
                    }

                    string phuongThucDv = coDichVu ? (req.ServicePaymentMethod ?? "COD") : "COD";
                    string phuongThucSp = coSanPham ? (req.ProductPaymentMethod ?? "COD") : "COD";

                    bool dvLaOnline = coDichVu && (string.Equals(phuongThucDv, "vnpay", StringComparison.OrdinalIgnoreCase) || string.Equals(phuongThucDv, "momo", StringComparison.OrdinalIgnoreCase));
                    bool spLaOnline = coSanPham && string.Equals(req.ReceiveType, "home", StringComparison.OrdinalIgnoreCase) && (string.Equals(phuongThucSp, "vnpay", StringComparison.OrdinalIgnoreCase) || string.Equals(phuongThucSp, "momo", StringComparison.OrdinalIgnoreCase));
                    bool isOnlinePayment = dvLaOnline || spLaOnline;

                    string phuongThuc = dvLaOnline ? phuongThucDv : (spLaOnline ? phuongThucSp : (coDichVu ? phuongThucDv : phuongThucSp));

                    decimal serviceSubTotal = 0;
                    int tongThoiGianPhut = 0;

                    if (coDichVu)
                    {
                        foreach (var sv in validServices)
                        {
                            if (sv.Id.StartsWith("DV"))
                            {
                                var dbDv = await _context.Dichvus.Include(d => d.ChitietFlashsales).ThenInclude(f => f.MaFlNavigation)
                                    .FirstOrDefaultAsync(d => d.MaDv == sv.Id && d.TrangThai == 1);
                                if (dbDv == null) { await transaction.RollbackAsync(); return BadRequest(new { success = false, message = $"Dịch vụ {sv.Id} không tồn tại hoặc đã ngưng hoạt động." }); }

                                decimal price = (decimal)(dbDv.Gia ?? 0);
                                var validFlashSale = dbDv.ChitietFlashsales.FirstOrDefault(fs => fs.MaFlNavigation.TrangThai == 1 && fs.MaFlNavigation.NgayBatDau <= dbNow && fs.MaFlNavigation.NgayKetThuc >= dbNow);
                                if (validFlashSale != null && validFlashSale.PhanTramGiam > 0)
                                {
                                    price = price - (price * (decimal)validFlashSale.PhanTramGiam / 100m);
                                }
                                sv.Price = price;
                                sv.Duration = dbDv.ThoiGianLam ?? 30;
                                serviceSubTotal += price;
                                tongThoiGianPhut += sv.Duration;
                            }
                            else if (sv.Id.StartsWith("CB"))
                            {
                                var dbCb = await _context.Combos.Include(c => c.Combo_DichVus).ThenInclude(cd => cd.DichVu)
                                    .FirstOrDefaultAsync(c => c.MaCb == sv.Id && c.TrangThai == 1);
                                if (dbCb == null) { await transaction.RollbackAsync(); return BadRequest(new { success = false, message = $"Combo {sv.Id} không tồn tại hoặc đã ngưng hoạt động." }); }

                                decimal price = (decimal)(dbCb.Gia ?? 0);
                                sv.Price = price;
                                sv.Duration = dbCb.Combo_DichVus.Sum(cd => cd.DichVu.ThoiGianLam ?? 30);
                                serviceSubTotal += price;
                                tongThoiGianPhut += sv.Duration;
                            }
                        }
                    }

                    decimal productSubTotal = 0;
                    if (coSanPham)
                    {
                        foreach (var p in validProducts)
                        {
                            var dbBt = await _context.BienTheSanphams.FirstOrDefaultAsync(b => b.MaBienThe == p.Id && b.TrangThai == 1);
                            if (dbBt == null) { await transaction.RollbackAsync(); return BadRequest(new { success = false, message = $"Sản phẩm {p.Id} không tồn tại hoặc đã ngưng hoạt động." }); }
                            if (dbBt.SoLuongTon < p.SoLuong) { await transaction.RollbackAsync(); return BadRequest(new { success = false, message = $"Sản phẩm {dbBt.TenBienThe} không đủ số lượng trong kho." }); }

                            p.Gia = (decimal)(dbBt.GiaBan ?? 0);
                            productSubTotal += p.Gia * p.SoLuong;
                        }
                    }

                    decimal shippingFee = 0;
                    if (coSanPham && string.Equals(req.ReceiveType, "home", StringComparison.OrdinalIgnoreCase))
                    {
                        shippingFee = req.ShippingFee;
                    }

                    decimal totalBeforeDiscount = serviceSubTotal + productSubTotal + shippingFee;

                    string maLd = null;
                    Lichdat? lichDatMoi = null;
                    if (coDichVu)
                    {
                        // CHUẨN 10 KÝ TỰ MÃ LỊCH ĐẶT
                        maLd = dbNow.ToString("yyHHmmssff");

                        var reqBookingDate = req.BookingDate ?? dbNow;
                        var localTime = reqBookingDate.Kind == DateTimeKind.Utc ? reqBookingDate.ToLocalTime() : reqBookingDate;

                        var requestedDate = DateOnly.FromDateTime(localTime);
                        var requestedStart = TimeOnly.FromDateTime(localTime);
                        var requestedEnd = requestedStart.AddMinutes(tongThoiGianPhut);

                        string lockResource = string.IsNullOrEmpty(req.StylistId)
                            ? $"Booking_Salon_{requestedDate.ToString("yyyyMMdd")}"
                            : $"Booking_Stylist_{req.StylistId}_{requestedDate.ToString("yyyyMMdd")}";

                        await _context.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource = {lockResource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000");

                        if (!string.IsNullOrEmpty(req.StylistId))
                        {
                            var conflictingBookings = await _context.Lichdats
                                .Where(ld => ld.NgayHen == requestedDate
                                          && ld.TrangThai != 0 // ĐÃ SỬA THEO CHUẨN != 0
                                          && ld.GioBatDau < requestedEnd
                                          && ld.GioKetThuc > requestedStart
                                          && _context.ChitietLichdats.Any(ct => ct.MaLd == ld.MaLd && ct.MaNv == req.StylistId))
                                .ToListAsync();

                            if (conflictingBookings.Any())
                            {
                                var myStuckBooking = conflictingBookings.FirstOrDefault(b => b.MaKh == safeKhachHang && b.TrangThai != 0);

                                if (myStuckBooking != null)
                                {
                                    bool isUnpaid = await _context.HoaDons.AnyAsync(h => h.MaLd == myStuckBooking.MaLd && h.TrangThai == 0);
                                    if (!isUnpaid) myStuckBooking = null;
                                }

                                if (myStuckBooking != null)
                                {
                                    myStuckBooking.TrangThai = 0;
                                    var oldInvoice = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == myStuckBooking.MaLd);
                                    if (oldInvoice != null)
                                    {
                                        oldInvoice.TrangThai = 0;
                                        var oldPayment = await _context.Thanhtoans.FirstOrDefaultAsync(t => t.MaHd == oldInvoice.MaHd);
                                        if (oldPayment != null) oldPayment.TrangThai = 0;
                                    }
                                    await _context.SaveChangesAsync();
                                    conflictingBookings.Remove(myStuckBooking);
                                }

                                const int MAX_CAPACITY = 2;
                                bool isOverCapacity = false;
                                for (TimeOnly t = requestedStart; t < requestedEnd; t = t.AddMinutes(15))
                                {
                                    int countAtT = conflictingBookings.Count(b => b.GioBatDau.HasValue && b.GioKetThuc.HasValue && b.GioBatDau.Value <= t && t < b.GioKetThuc.Value);
                                    if (countAtT >= MAX_CAPACITY)
                                    {
                                        isOverCapacity = true;
                                        break;
                                    }
                                }

                                if (isOverCapacity)
                                {
                                    await transaction.RollbackAsync();
                                    return BadRequest(new { success = false, message = "Rất tiếc, thợ bạn chọn đã kín chỗ (tối đa 2 khách) vào khung giờ này. Vui lòng chọn thợ hoặc giờ khác!" });
                                }
                            }
                        }

                        lichDatMoi = new Lichdat
                        {
                            MaLd = maLd,
                            MaKh = safeKhachHang,
                            NgayHen = requestedDate,
                            GioBatDau = requestedStart,
                            GioKetThuc = requestedEnd,
                            TongTienDuKien = serviceSubTotal,
                            TrangThai = 0 // LUÔN KHỞI TẠO BẰNG 0
                        };
                        _context.Lichdats.Add(lichDatMoi);
                        await _context.SaveChangesAsync();

                        foreach (var sv in validServices)
                        {
                            var chiTiet = new ChitietLichdat
                            {
                                MaLd = maLd,
                                MaNv = string.IsNullOrEmpty(req.StylistId) ? null : req.StylistId,
                                GiaLucDat = sv.Price
                            };

                            if (sv.Id.StartsWith("DV")) { chiTiet.MaDv = sv.Id; }
                            else if (sv.Id.StartsWith("CB")) { chiTiet.MaCb = sv.Id; }

                            _context.ChitietLichdats.Add(chiTiet);
                        }
                        await _context.SaveChangesAsync();
                    }

                    int? phanTramGiamRank = 0;
                    decimal tienGiamThanhVien = 0;

                    if (!string.IsNullOrWhiteSpace(safeKhachHang) && safeKhachHang != "GUEST")
                    {
                        var khDb = await _context.Khachhangs.AsNoTracking().FirstOrDefaultAsync(k => k.MaKh == safeKhachHang);
                        if (khDb != null && !string.IsNullOrWhiteSpace(khDb.MaRank))
                        {
                            var rankDb = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == khDb.MaRank);
                            if (rankDb != null)
                            {
                                phanTramGiamRank = rankDb.PhanTramGiamGia;
                                if (phanTramGiamRank > 0 && (serviceSubTotal + productSubTotal) > 0)
                                {
                                    tienGiamThanhVien = Math.Round((serviceSubTotal + productSubTotal) * ((decimal)phanTramGiamRank / 100m), 0);
                                }
                            }
                        }
                    }

                    decimal subTotalSauKhiGiamRank = serviceSubTotal + productSubTotal - tienGiamThanhVien;
                    decimal totalVoucherDiscount = 0;
                    string? maCodeChinh = null;

                    if (req.VoucherIds != null && req.VoucherIds.Any() && safeKhachHang != null)
                    {
                        var allCartIds = new List<string>();
                        if (coDichVu) allCartIds.AddRange(validServices.Select(s => s.Id));
                        if (coSanPham) allCartIds.AddRange(validProducts.Select(p => p.Id));

                        var (vErr, tGiam, _) = await VoucherCartLogic.ValidateStackedAsync(
                            _context, req.VoucherIds, safeKhachHang, subTotalSauKhiGiamRank, allCartIds, dbNow, dbNow, null, null, maxVouchersAllowed: 2);
                        if (vErr != null)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { success = false, message = vErr });
                        }
                        totalVoucherDiscount = tGiam;
                        maCodeChinh = req.VoucherIds.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    }

                    decimal tongGiamGia = tienGiamThanhVien + totalVoucherDiscount;
                    decimal backendGrandTotal = serviceSubTotal + productSubTotal + shippingFee - tongGiamGia;
                    if (backendGrandTotal < 0) backendGrandTotal = 0;

                    decimal serviceDiscount = Math.Min(tongGiamGia, serviceSubTotal);
                    decimal serviceAfterVoucher = Math.Max(0, serviceSubTotal - serviceDiscount);

                    decimal remainingDiscount = Math.Max(0, tongGiamGia - serviceSubTotal);
                    decimal productAfterVoucher = Math.Max(0, productSubTotal - remainingDiscount);

                    if (lichDatMoi != null)
                    {
                        lichDatMoi.TongTienDuKien = serviceAfterVoucher;
                    }

                    decimal backendPaidAmount = 0;
                    if (coDichVu && dvLaOnline)
                    {
                        if (req.PaymentMode == "10") backendPaidAmount += Math.Round(serviceSubTotal * 0.1m, 0);
                        else backendPaidAmount += serviceAfterVoucher;
                    }
                    if (coSanPham && spLaOnline)
                    {
                        backendPaidAmount += productAfterVoucher + shippingFee;
                    }
                    backendPaidAmount = Math.Min(backendGrandTotal, backendPaidAmount);

                    var hoaDon = new HoaDon
                    {
                        MaHd = maHd,
                        MaKh = safeKhachHang,
                        MaLd = maLd,
                        NgayTao = dbNow,
                        TongTien = serviceSubTotal + productSubTotal,
                        ThanhTien = backendGrandTotal,
                        TienDaTra = backendPaidAmount,
                        TrangThai = isOnlinePayment ? 0 : (backendPaidAmount >= backendGrandTotal ? 2 : 1),
                        MaCode = maCodeChinh,
                        PhanTramGiamVip = phanTramGiamRank
                    };
                    _context.HoaDons.Add(hoaDon);

                    // THÀNH CÔNG (THANH TOÁN COD/TIỀN MẶT LÀ XONG LUÔN) -> CẬP NHẬT LÊN 1 HOẶC 2
                    if (!isOnlinePayment && lichDatMoi != null)
                    {
                        lichDatMoi.TrangThai = hoaDon.TrangThai;
                    }

                    await _context.SaveChangesAsync();

                    string maHddtSpPhatHanh = null;
                    if (coSanPham)
                    {
                        foreach (var p in validProducts)
                        {
                            _context.ChitietHoadonSps.Add(new ChitietHoadonSp
                            {
                                MaHd = maHd,
                                MaBienThe = p.Id,
                                SoLuong = p.SoLuong,
                                DonGiaLucBan = p.Gia,
                                ThanhTien = p.Gia * p.SoLuong
                            });

                            int rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE BIENTHE_SANPHAM SET SoLuongTon = SoLuongTon - {p.SoLuong} WHERE MaBienThe = {p.Id} AND SoLuongTon >= {p.SoLuong}");
                            if (rowsAffected <= 0)
                            {
                                await transaction.RollbackAsync();
                                var bt = await _context.BienTheSanphams.FindAsync(p.Id);
                                return BadRequest(new { success = false, errorCode = "PRODUCT_OUT_OF_STOCK", outOfStockProductId = p.Id, message = $"Sản phẩm '{bt?.TenBienThe ?? p.Id}' vừa bị khách khác mua hết hoặc không đủ số lượng trong kho!" });
                            }

                            var updatedBt = await _context.BienTheSanphams.AsNoTracking().FirstOrDefaultAsync(b => b.MaBienThe == p.Id);
                            if (updatedBt != null)
                            {
                                int tonSau = updatedBt.SoLuongTon ?? 0;
                                int tonTruoc = tonSau + p.SoLuong;

                                _context.LichSuTonKhos.Add(new LichSuTonKho
                                {
                                    MaBienThe = updatedBt.MaBienThe,
                                    LoaiBienDong = "BAN",
                                    SoLuongThayDoi = -p.SoLuong,
                                    TonTruoc = tonTruoc,
                                    TonSau = tonSau,
                                    MaChungTu = maHd,
                                    MaNv = null,
                                    GhiChu = $"Bán hàng theo đơn {maHd}",
                                    ThoiGian = dbNow
                                });
                            }
                        }

                        if (string.Equals(req.ReceiveType, "home", StringComparison.OrdinalIgnoreCase) && req.DeliveryInfo != null)
                        {
                            _context.ThongtinGiaohangs.Add(new ThongtinGiaohang
                            {
                                MaHd = maHd,
                                TenNguoiNhan = req.DeliveryInfo.HoTen ?? "Khách hàng",
                                SdtNhan = req.DeliveryInfo.SoDienThoai ?? "",
                                DiaChiNhan = req.DeliveryInfo.DiaChi ?? "",
                                Email = req.DeliveryInfo.Email ?? "",
                                PhiShip = shippingFee,
                                TrangThaiGiaoHang = 0
                            });
                        }
                    }

                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = maHd,
                        PhuongThuc = (phuongThuc ?? "COD").ToUpper(),
                        SoTien = backendPaidAmount,
                        TrangThai = isOnlinePayment ? 0 : 1
                    });

                    if (req.VoucherIds != null)
                    {
                        foreach (var code in req.VoucherIds)
                        {
                            await VoucherCartLogic.TryIncrementUsageAsync(_context, code, safeKhachHang);
                        }
                    }

                    {
                        bool laDoanhNghiep = req.InvoiceInfo?.LaDoanhNghiep ?? false;
                        string tenNguoiMua = laDoanhNghiep ? req.InvoiceInfo?.TenCongTy : (req.CustomerName ?? "Khach hang");
                        string mst = laDoanhNghiep ? req.InvoiceInfo?.Mst : null;
                        string diaChiXuat = req.InvoiceInfo?.DiaChiXuatHD ?? req.DeliveryInfo?.DiaChi;
                        string emailNhan = req.InvoiceInfo?.EmailNhanHD ?? req.Email;
                        byte loaiKhach = laDoanhNghiep ? (byte)1 : (byte)0;

                        if (isOnlinePayment)
                        {
                            await _cache.SetObjectAsync(PendingInvoiceCacheKey(maHd), new PendingInvoiceInfo
                            {
                                LoaiKhach = loaiKhach,
                                TenNguoiMua = tenNguoiMua,
                                Mst = mst,
                                DiaChiXuatHd = diaChiXuat,
                                EmailNhanHd = emailNhan
                            }, TimeSpan.FromMinutes(60));
                        }
                        else if (coDichVu)
                        {
                            var maHddtGop = "HD" + dbNow.ToString("yyMMddHHmmss");
                            _context.HoaDonDienTus.Add(new HoaDonDienTu
                            {
                                MaHddt = maHddtGop,
                                MaHd = maHd,
                                LoaiKhach = loaiKhach,
                                TenNguoiMua = tenNguoiMua,
                                Mst = mst,
                                DiaChiXuatHd = diaChiXuat,
                                EmailNhanHd = emailNhan,
                                TrangThai = 3
                            });
                        }
                        else if (coSanPham)
                        {
                            var maHddtSp = "HD" + dbNow.ToString("yyMMddHHmmss") + "S";
                            _context.HoaDonDienTus.Add(new HoaDonDienTu
                            {
                                MaHddt = maHddtSp,
                                MaHd = maHd,
                                LoaiKhach = loaiKhach,
                                TenNguoiMua = tenNguoiMua,
                                Mst = mst,
                                DiaChiXuatHd = diaChiXuat,
                                EmailNhanHd = emailNhan,
                                TrangThai = 0
                            });

                            bool spLaCod = string.Equals(req.ProductPaymentMethod, "cod", StringComparison.OrdinalIgnoreCase);
                            if (!spLaCod)
                            {
                                maHddtSpPhatHanh = maHddtSp;
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    if (!string.IsNullOrEmpty(maHddtSpPhatHanh))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var scope = _scopeFactory.CreateScope();
                                var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                                await eInvoiceService.PhatHanhHoaDonAsync(maHddtSpPhatHanh);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[EINVOICE ERROR] Lỗi phát hành hóa đơn {maHddtSpPhatHanh}: {ex.Message}");
                            }
                        });
                    }

                    string paymentUrl = null;
                    if (backendPaidAmount > 0 && isOnlinePayment)
                    {
                        if (string.Equals(phuongThuc, "vnpay", StringComparison.OrdinalIgnoreCase))
                        {
                            var vnpay = new VnPayLibrary();
                            vnpay.AddRequestData("vnp_Version", "2.1.0");
                            vnpay.AddRequestData("vnp_Command", "pay");
                            vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"] ?? "");
                            vnpay.AddRequestData("vnp_Amount", ((long)(backendPaidAmount * 100)).ToString());

                            var vnpayNow = dbNow;
                            vnpay.AddRequestData("vnp_CreateDate", vnpayNow.ToString("yyyyMMddHHmmss"));
                            vnpay.AddRequestData("vnp_ExpireDate", vnpayNow.AddMinutes(15).ToString("yyyyMMddHHmmss"));
                            vnpay.AddRequestData("vnp_CurrCode", "VND");
                            vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                            vnpay.AddRequestData("vnp_Locale", "vn");
                            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan hoa don " + maHd);
                            vnpay.AddRequestData("vnp_OrderType", "other");
                            vnpay.AddRequestData("vnp_ReturnUrl", _configuration["Vnpay:ReturnUrlTong"]);
                            vnpay.AddRequestData("vnp_TxnRef", maHd + "_" + vnpayNow.ToString("HHmmss"));

                            paymentUrl = vnpay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"] ?? "", _configuration["Vnpay:HashSecret"] ?? "");
                        }
                        else if (string.Equals(phuongThuc, "momo", StringComparison.OrdinalIgnoreCase))
                        {
                            var partnerCode = _configuration["Momo:PartnerCode"];
                            var accessKey = _configuration["Momo:AccessKey"];
                            var secretKey = _configuration["Momo:SecretKey"];
                            var endpoint = _configuration["Momo:ApiEndpoint"];
                            var redirectUrl = _configuration["Momo:ReturnUrlTong"];
                            var ipnUrl = _configuration["Momo:NotifyUrl"];

                            long amount = (long)backendPaidAmount;
                            string orderInfo = "Thanh toan hoa don tong " + maHd;
                            string orderId = maHd + "_" + dbNow.ToString("HHmmss");
                            string requestId = Guid.NewGuid().ToString();

                            string rawHash =
                                $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType=captureWallet";

                            string signature = SignHmacSHA256(rawHash, secretKey);

                            var requestData = new
                            {
                                partnerCode,
                                requestId,
                                amount,
                                orderId,
                                orderInfo,
                                redirectUrl,
                                ipnUrl,
                                lang = "vi",
                                extraData = "",
                                requestType = "captureWallet",
                                signature
                            };

                            using var client = new HttpClient();
                            var response = await client.PostAsJsonAsync(endpoint, requestData);
                            var result = await response.Content.ReadFromJsonAsync<MomoResponse>();

                            if (result != null && result.resultCode == 0)
                            {
                                paymentUrl = result.payUrl;
                            }
                            else
                            {
                                return BadRequest(new { success = false, message = "Lỗi tạo link MoMo: " + (result?.message ?? "Không xác định") });
                            }
                        }
                    }

                    return Ok(new { success = true, isOnlinePayment = !string.IsNullOrEmpty(paymentUrl), url = paymentUrl, maHd = maHd });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine("====== LỖI LƯU HÓA ĐƠN ======\n" + ex.Message);
                    if (ex.InnerException != null) Console.WriteLine("CHI TIẾT: " + ex.InnerException.Message);
                    return BadRequest(new { success = false, message = "Lỗi khi lưu DB: " + (ex.InnerException?.Message ?? ex.Message) });
                }
            });
        }

        [HttpGet("get-receipt")]
        public async Task<IActionResult> GetReceipt(
            [FromQuery] string maHd,
            [FromQuery] string? vnp_ResponseCode = null,
            [FromQuery] string? resultCode = null,
            [FromQuery] string? vnp_TxnRef = null)
        {
            string rawKey = !string.IsNullOrEmpty(vnp_TxnRef) ? vnp_TxnRef : maHd;
            string realMaHd = rawKey?.Split('_')[0] ?? "";

            if (string.IsNullOrEmpty(realMaHd))
                return BadRequest(new { success = false, message = "Thiếu mã hóa đơn." });

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(h => h.ThongtinGiaohangs)
                    .Include(h => h.Thanhtoans)
                    .FirstOrDefaultAsync(h => h.MaHd == realMaHd);

                if (hoaDon == null)
                    return NotFound(new { success = false, message = "Không tìm thấy hóa đơn" });

                bool hasPaymentStatus = !string.IsNullOrEmpty(vnp_ResponseCode) || !string.IsNullOrEmpty(resultCode);
                bool isPaymentSuccess = (vnp_ResponseCode == "00") || (resultCode == "0");

                if (hasPaymentStatus && hoaDon.TrangThai == 0)
                {
                    if (isPaymentSuccess)
                    {
                        hoaDon.TrangThai = (hoaDon.TienDaTra ?? 0) >= (hoaDon.ThanhTien ?? 0) ? 2 : 1;
                        var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                        if (thanhToan != null) thanhToan.TrangThai = 1;
                        await _context.SaveChangesAsync();

                        bool daCoHddt = await _context.HoaDonDienTus.AnyAsync(h => h.MaHd == realMaHd);
                        if (!daCoHddt)
                        {
                            bool coDichVuDb = !string.IsNullOrEmpty(hoaDon.MaLd);
                            bool coSanPhamDb = await _context.ChitietHoadonSps.AnyAsync(c => c.MaHd == realMaHd);
                            var pending = await _cache.GetObjectAsync<PendingInvoiceInfo>(PendingInvoiceCacheKey(realMaHd));

                            if (pending == null)
                            {
                                var khDuPhong = await _context.Khachhangs.FindAsync(hoaDon.MaKh);
                                var deliveryDuPhong = hoaDon.ThongtinGiaohangs.FirstOrDefault();
                                pending = new PendingInvoiceInfo
                                {
                                    LoaiKhach = 0,
                                    TenNguoiMua = khDuPhong?.HoTen ?? deliveryDuPhong?.TenNguoiNhan ?? "Khach hang",
                                    Mst = null,
                                    DiaChiXuatHd = deliveryDuPhong?.DiaChiNhan,
                                    EmailNhanHd = khDuPhong?.Email ?? deliveryDuPhong?.Email
                                };
                            }

                            var thoiDiemTao = VnTimeHelper.Now();

                            if (coDichVuDb)
                            {
                                var maHddtGop = "HD" + thoiDiemTao.ToString("yyMMddHHmmss");
                                _context.HoaDonDienTus.Add(new HoaDonDienTu
                                {
                                    MaHddt = maHddtGop,
                                    MaHd = realMaHd,
                                    LoaiKhach = pending.LoaiKhach,
                                    TenNguoiMua = pending.TenNguoiMua,
                                    Mst = pending.Mst,
                                    DiaChiXuatHd = pending.DiaChiXuatHd,
                                    EmailNhanHd = pending.EmailNhanHd,
                                    TrangThai = 3
                                });
                                await _context.SaveChangesAsync();
                            }
                            else if (coSanPhamDb)
                            {
                                var maHddtSp = "HD" + thoiDiemTao.ToString("yyMMddHHmmss") + "S";
                                _context.HoaDonDienTus.Add(new HoaDonDienTu
                                {
                                    MaHddt = maHddtSp,
                                    MaHd = realMaHd,
                                    LoaiKhach = pending.LoaiKhach,
                                    TenNguoiMua = pending.TenNguoiMua,
                                    Mst = pending.Mst,
                                    DiaChiXuatHd = pending.DiaChiXuatHd,
                                    EmailNhanHd = pending.EmailNhanHd,
                                    TrangThai = 0
                                });
                                await _context.SaveChangesAsync();

                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        using var scope = _scopeFactory.CreateScope();
                                        var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                                        await eInvoiceService.PhatHanhHoaDonAsync(maHddtSp);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[EINVOICE ERROR] Lỗi phát hành hóa đơn {maHddtSp}: {ex.Message}");
                                    }
                                });
                            }

                            await _cache.RemoveSafeAsync(PendingInvoiceCacheKey(realMaHd));
                        }
                    }
                    else
                    {
                        hoaDon.TrangThai = 0;
                        var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                        if (thanhToan != null) thanhToan.TrangThai = 0;

                        if (!string.IsNullOrEmpty(hoaDon.MaLd))
                        {
                            var lichDat = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                            if (lichDat != null) lichDat.TrangThai = 0;
                        }
                        await _context.SaveChangesAsync();
                    }
                }

                bool shouldSendEmail = false;
                if ((hoaDon.TrangThai == 1 || hoaDon.TrangThai == 2) && !string.IsNullOrEmpty(hoaDon.MaLd))
                {
                    var lichDat = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                    if (lichDat != null && lichDat.TrangThai == 0)
                    {
                        lichDat.TrangThai = hoaDon.TrangThai; // Thành công lên 1 hoặc 2
                        await _context.SaveChangesAsync();
                        shouldSendEmail = true;
                    }
                }

                var delivery = hoaDon.ThongtinGiaohangs.FirstOrDefault();
                if (shouldSendEmail)
                {
                    try
                    {
                        var khachHang = await _context.Khachhangs.FindAsync(hoaDon.MaKh);
                        string tenKhach = khachHang?.HoTen ?? "Khách hàng";
                        string emailNhan = khachHang?.Email;

                        if (string.IsNullOrWhiteSpace(emailNhan) && delivery != null)
                        {
                            emailNhan = delivery.Email;
                            tenKhach = delivery.TenNguoiNhan;
                        }

                        if (!string.IsNullOrWhiteSpace(emailNhan))
                        {
                            var ld = await _context.Lichdats.AsNoTracking().FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                            DateTime ngayHenDateTime = VnTimeHelper.Now();

                            if (ld != null && ld.NgayHen.HasValue && ld.GioBatDau.HasValue)
                            {
                                ngayHenDateTime = ld.NgayHen.Value.ToDateTime(ld.GioBatDau.Value);
                            }

                            string maLdGui = hoaDon.MaLd;
                            decimal thanhTienGui = hoaDon.ThanhTien ?? 0m;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using var scope = _scopeFactory.CreateScope();
                                    var qrService = scope.ServiceProvider.GetRequiredService<IQrEmailService>();
                                    await qrService.SendBookingEmailAsync(emailNhan, tenKhach, maLdGui, ngayHenDateTime, thanhTienGui);
                                    Console.WriteLine($"[EMAIL] Đã gửi mã QR thành công tới email: {emailNhan}");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[EMAIL ERROR] Lỗi gửi email QR cho đơn {maLdGui}: {ex.Message}");
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("====== LỖI CHUẨN BỊ GỬI EMAIL QR TRONG GET-RECEIPT ======\n" + ex.Message);
                    }
                }

                var khachHangInfo = await _context.Khachhangs.AsNoTracking().FirstOrDefaultAsync(k => k.MaKh == hoaDon.MaKh);

                object bookingInfo = null;
                if (!string.IsNullOrEmpty(hoaDon.MaLd))
                {
                    var ld = await _context.Lichdats.AsNoTracking().FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                    var chiTietLd = await _context.ChitietLichdats.AsNoTracking().Where(c => c.MaLd == hoaDon.MaLd).ToListAsync();

                    var servicesList = new List<object>();
                    foreach (var ct in chiTietLd)
                    {
                        string name = "";
                        if (!string.IsNullOrEmpty(ct.MaDv)) { var dv = await _context.Dichvus.FindAsync(ct.MaDv); name = dv?.TenDv; }
                        else if (!string.IsNullOrEmpty(ct.MaCb)) { var cb = await _context.Combos.FindAsync(ct.MaCb); name = cb?.TenCombo; }

                        string stylistName = "Chưa chọn thợ";
                        if (!string.IsNullOrEmpty(ct.MaNv)) { var nv = await _context.Nhanviens.FindAsync(ct.MaNv); stylistName = nv?.HoTenNv; }

                        servicesList.Add(new { name = name ?? "Dịch vụ", price = ct.GiaLucDat, stylist = stylistName });
                    }

                    if (ld != null) bookingInfo = new
                    {
                        ngayHen = ld.NgayHen?.ToString("dd/MM/yyyy"),
                        gioBatDau = ld.GioBatDau?.ToString(@"HH\:mm"),
                        services = servicesList
                    };
                }

                var chiTietSp = await _context.ChitietHoadonSps.AsNoTracking().Where(c => c.MaHd == realMaHd).ToListAsync();
                var productsList = new List<object>();
                foreach (var ct in chiTietSp)
                {
                    var bt = await _context.BienTheSanphams.FindAsync(ct.MaBienThe);
                    var sp = bt != null ? await _context.SanPhams.FindAsync(bt.MaSanPham) : null;
                    productsList.Add(new { 
                        name = sp?.TenSp ?? "Sản phẩm", 
                        variant = bt?.TenBienThe ?? "Mặc định",
                        qty = ct.SoLuong, 
                        price = ct.DonGiaLucBan, 
                        total = ct.ThanhTien,
                        img = sp?.HinhAnhDaiDien
                    });
                }

                var receipt = new
                {
                    maHd = hoaDon.MaHd,
                    ngayTao = hoaDon.NgayTao?.ToString("dd/MM/yyyy HH:mm"),
                    khachHang = khachHangInfo?.HoTen ?? "Khách vãng lai",
                    sdt = khachHangInfo?.Sdt ?? "",
                    tongTien = hoaDon.TongTien,
                    thanhTien = hoaDon.ThanhTien,
                    tienDaTra = hoaDon.TienDaTra,
                    trangThai = (hoaDon.TrangThai == 1 || hoaDon.TrangThai == 2) ? "Đã thanh toán" : "Chưa thanh toán",
                    booking = bookingInfo,
                    products = productsList,
                    warehouse = new {
                        name = "Tổng Kho Salon LGHair",
                        address = "Số 123 Nguyễn Trãi, Phường Bến Thành, Quận 1, TP. Hồ Chí Minh",
                        phone = "0987.654.321"
                    },
                    delivery = delivery != null ? new { 
                        nguoiNhan = delivery.TenNguoiNhan, 
                        sdt = delivery.SdtNhan, 
                        diaChi = delivery.DiaChiNhan, 
                        phiShip = delivery.PhiShip,
                        
                    } : null
                };

                return Ok(new { success = true, data = receipt });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        private string SignHmacSHA256(string message, string key)
        {
            byte[] keyByte = Encoding.UTF8.GetBytes(key);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmacsha256 = new HMACSHA256(keyByte);
            byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);

            return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class CheckoutSummaryRequest
    {
        public string? CheckoutType { get; set; }
        public string? MaKh { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Note { get; set; }
        public DateTime? BookingDate { get; set; }
        public string? StylistId { get; set; }

        public List<CheckoutServiceItemDto>? Services { get; set; }
        public List<ProductItemDto2>? Products { get; set; }
        public string? ReceiveType { get; set; }
        public DeliveryInfoDto? DeliveryInfo { get; set; }
        public List<string>? VoucherIds { get; set; }

        public decimal GrandTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ShippingFee { get; set; }

        public string? PaymentMode { get; set; }
        public string? ServicePaymentMethod { get; set; }
        public string? ProductPaymentMethod { get; set; }

        public InvoiceInfoDto? InvoiceInfo { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class CheckoutServiceItemDto
    {
        public string? Id { get; set; }
        public decimal Price { get; set; }
        public int Duration { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class ProductItemDto2
    {
        public string? Id { get; set; }
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class DeliveryInfoDto
    {
        public string? HoTen { get; set; }
        public string? SoDienThoai { get; set; }
        public string? DiaChi { get; set; }
        public string? Email { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class ThanhToanCheckStylistRequest
    {
        public string NgayHen { get; set; }
        public string GioBatDau { get; set; }
        public int TongThoiGian { get; set; }
        public string MaKh { get; set; }
        public string? Phone { get; set;
    }
}
}

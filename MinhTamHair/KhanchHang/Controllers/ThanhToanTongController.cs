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

        public ThanhToanTongController(MinhTamHairContext context, IConfiguration configuration, IQrEmailService qrEmailService, IServiceScopeFactory scopeFactory, IDistributedCache cache)
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
                        icon = s.hinhAnh
                    };
                }).ToList();

                var combos = await _context.Combos
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

        // ĐÃ VÁ: Cập nhật hàm GetAvailableStylists giống bản chuẩn DatDichVu
        [HttpPost("get-available-stylists")]
        public async Task<IActionResult> GetAvailableStylists([FromBody] ThanhToanCheckStylistRequest request)
        {
            try
            {
                DateOnly ngayHen = DateOnly.Parse(request.NgayHen);
                TimeOnly start = TimeOnly.Parse(request.GioBatDau);
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

                var cacLichBan = await _context.ChitietLichdats
                    .AsNoTracking()
                    .Where(ct => ct.MaLdNavigation.NgayHen == ngayHen
                              && (ct.MaLdNavigation.TrangThai == 1 || ct.MaLdNavigation.TrangThai == 2 || ct.MaLdNavigation.TrangThai == 3))
                    .Select(ct => new
                    {
                        MaNv = (ct.MaNv ?? "").Trim(),
                        GioBatDau = ct.MaLdNavigation.GioBatDau,
                        GioKetThuc = ct.MaLdNavigation.GioKetThuc,
                        TrangThai = ct.MaLdNavigation.TrangThai,
                        MaKh = (ct.MaLdNavigation.MaKh ?? "").Trim()
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
                                 && !(b.TrangThai == 3 && myCustomerIds.Contains(b.MaKh)))
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

                        var lichKhet = cacLichCuaTho.FirstOrDefault(b => b.GioBatDau < end && b.GioKetThuc > start);
                        if (lichKhet != null)
                        {
                            isAvailable = false;
                            reason = $"Bận khách ({lichKhet.GioBatDau?.ToString(@"HH\:mm")} - {lichKhet.GioKetThuc?.ToString(@"HH\:mm")})";
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

        // ĐÃ VÁ: Lắp Mutex chống đụng độ và tự dọn đơn rác "chính chủ"
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
                    string maHd = "TH" + VnTimeHelper.Now().ToString("HHmmssff");

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
                                
                                // ĐÃ VÁ: Cập nhật email nếu khách cũ nhập email mới để có thể gửi mã QR
                                if (!string.IsNullOrWhiteSpace(req.Email) && existingKhach.Email != req.Email)
                                {
                                    existingKhach.Email = req.Email;
                                    await _context.SaveChangesAsync();
                                }
                            }
                            else
                            {
                                string prefixSdt = req.Phone.Length >= 4 ? req.Phone.Substring(0, 4) : "0000";
                                safeKhachHang = "KH" + prefixSdt + new Random().Next(100, 999).ToString();
                                _context.Khachhangs.Add(new Khachhang
                                {
                                    MaKh = safeKhachHang,
                                    HoTen = string.IsNullOrWhiteSpace(req.CustomerName) ? "Khách vãng lai" : req.CustomerName,
                                    Sdt = req.Phone,
                                    Email = req.Email ?? "",
                                    TrangThai = 1,
                                    NgayTao = VnTimeHelper.Now(),
                                    DiemTichLuy = 0,
                                    MaRank = "RANK01"
                                });
                                await _context.SaveChangesAsync();
                            }
                        }
                        else { safeKhachHang = null; }
                    }

                    // TÍNH TOÁN CÁC CỜ PHƯƠNG THỨC THANH TOÁN TRƯỚC ĐỂ LƯU LỊCH ĐẶT CHUẨN XÁC
                    bool coDichVu = req.Services != null && req.Services.Any();
                    bool coSanPham = req.Products != null && req.Products.Any();
                    string phuongThucDv = coDichVu ? (req.ServicePaymentMethod ?? "COD") : "COD";
                    bool isOnlineService = coDichVu && (phuongThucDv.ToLower() == "vnpay" || phuongThucDv.ToLower() == "momo");
                    int trangThaiLichDat = isOnlineService ? 3 : (req.PaymentMode == "10" ? 1 : 2);

                    string maLd = null;
                    Lichdat? lichDatMoi = null;
                    if (coDichVu)
                    {
                        maLd = VnTimeHelper.Now().ToString("yyHHmmssff");
                        int tongThoiGianPhut = req.Services.Sum(s => s.Duration);
                        decimal serviceSubTotalBanDau = req.Services.Sum(s => s.Price);

                        var localTime = req.BookingDate.HasValue ? req.BookingDate.Value.ToUniversalTime().AddHours(7) : VnTimeHelper.Now();
                        var requestedDate = DateOnly.FromDateTime(localTime);
                        var requestedStart = TimeOnly.FromDateTime(localTime);
                        var requestedEnd = requestedStart.AddMinutes(tongThoiGianPhut);

                        // ---> XIN KHÓA MUTEX <---
                        string lockResource = string.IsNullOrEmpty(req.StylistId)
                            ? $"Booking_Salon_{requestedDate.ToString("yyyyMMdd")}"
                            : $"Booking_Stylist_{req.StylistId}_{requestedDate.ToString("yyyyMMdd")}";
                        await _context.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource = {lockResource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000");

                        // ---> KHÓA SLOT & DỌN RÁC CHÍNH CHỦ <---
                        if (!string.IsNullOrEmpty(req.StylistId))
                        {
                            var conflictingBookings = await _context.Lichdats
                                .Where(ld => ld.NgayHen == requestedDate
                                          && (ld.TrangThai == 3 || ld.TrangThai == 1 || ld.TrangThai == 2)
                                          && ld.GioBatDau < requestedEnd
                                          && ld.GioKetThuc > requestedStart
                                          && _context.ChitietLichdats.Any(ct => ct.MaLd == ld.MaLd && ct.MaNv == req.StylistId))
                                .ToListAsync();

                            if (conflictingBookings.Any())
                            {
                                var myStuckBooking = conflictingBookings.FirstOrDefault(b => b.MaKh == safeKhachHang && b.TrangThai == 3);

                                if (myStuckBooking != null)
                                {
                                    myStuckBooking.TrangThai = 0;
                                    var oldInvoice = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == myStuckBooking.MaLd);
                                    if (oldInvoice != null) oldInvoice.TrangThai = 0;
                                    var oldPayment = await _context.Thanhtoans.FirstOrDefaultAsync(t => t.MaHd == myStuckBooking.MaLd);
                                    if (oldPayment != null) oldPayment.TrangThai = 0;
                                    await _context.SaveChangesAsync();
                                }
                                else
                                {
                                    await transaction.RollbackAsync();
                                    return BadRequest(new { success = false, message = "Rất tiếc, thợ bạn chọn đang có khách giao dịch. Vui lòng chọn thợ hoặc giờ khác!" });
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
                            TongTienDuKien = serviceSubTotalBanDau,
                            TrangThai = trangThaiLichDat // VÁ LỖI CỨNG MÃ 0 Ở ĐÂY
                        };
                        _context.Lichdats.Add(lichDatMoi);
                        await _context.SaveChangesAsync();

                        foreach (var sv in req.Services)
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

                    decimal serviceSubTotal = req.Services?.Sum(s => s.Price) ?? 0;
                    decimal productSubTotal = req.Products?.Sum(p => p.Gia * p.SoLuong) ?? 0;
                    decimal totalBeforeDiscount = serviceSubTotal + productSubTotal + req.ShippingFee;

                    if (req.VoucherIds != null && req.VoucherIds.Any() && safeKhachHang != null)
                    {
                        var allCartIds = new List<string>();
                        if (req.Services != null) allCartIds.AddRange(req.Services.Select(s => s.Id));
                        if (req.Products != null) allCartIds.AddRange(req.Products.Select(p => p.Id));
                        var (vErr, _, _) = await VoucherCartLogic.ValidateStackedAsync(
                            _context, req.VoucherIds, safeKhachHang, serviceSubTotal + productSubTotal, allCartIds, VnTimeHelper.Now(), VnTimeHelper.Now(), null, null, maxVouchersAllowed: 2);
                        if (vErr != null)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { success = false, message = vErr });
                        }
                    }

                    decimal tongGiamGia = Math.Max(0, totalBeforeDiscount - req.GrandTotal);
                    decimal serviceDiscount = Math.Min(tongGiamGia, serviceSubTotal);
                    decimal serviceAfterVoucher = Math.Max(0, serviceSubTotal - serviceDiscount);

                    decimal remainingDiscount = Math.Max(0, tongGiamGia - serviceSubTotal);
                    decimal productAfterVoucher = Math.Max(0, productSubTotal - remainingDiscount);
                    if (lichDatMoi != null)
                    {
                        lichDatMoi.TongTienDuKien = serviceAfterVoucher;
                    }

                    decimal servicePayment = 0;
                    if (coDichVu)
                    {
                        servicePayment = (req.PaymentMode == "10") ? serviceAfterVoucher * 0.1m : serviceAfterVoucher;
                    }

                    bool spNhanTaiTiem = string.Equals(req.ReceiveType, "store", StringComparison.OrdinalIgnoreCase);
                    bool spLaCod = string.Equals(req.ProductPaymentMethod, "cod", StringComparison.OrdinalIgnoreCase);
                    decimal productPayment = 0;
                    if (coSanPham && !spLaCod && !spNhanTaiTiem)
                    {
                        productPayment = productAfterVoucher + req.ShippingFee;
                    }

                    string phuongThucSp = coSanPham ? (req.ProductPaymentMethod ?? "COD") : "COD";

                    bool spLaOnline = coSanPham && productPayment > 0 &&
                        (phuongThucSp.ToLower() == "vnpay" || phuongThucSp.ToLower() == "momo");

                    bool isOnlinePayment = isOnlineService || spLaOnline;
                    string phuongThuc = isOnlineService ? phuongThucDv : (spLaOnline ? phuongThucSp : (coDichVu ? phuongThucDv : phuongThucSp));

                    int? phanTramGiamRank = 0;
                    if (!string.IsNullOrWhiteSpace(safeKhachHang) && safeKhachHang != "GUEST")
                    {
                        var khDb = await _context.Khachhangs.AsNoTracking().FirstOrDefaultAsync(k => k.MaKh == safeKhachHang);
                        if (khDb != null && !string.IsNullOrWhiteSpace(khDb.MaRank))
                        {
                            var rankDb = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == khDb.MaRank);
                            if (rankDb != null)
                            {
                                phanTramGiamRank = rankDb.PhanTramGiamGia;
                            }
                        }
                    }

                    string? maCodeChinh = req.VoucherIds != null && req.VoucherIds.Any(v => !string.IsNullOrWhiteSpace(v))
                        ? req.VoucherIds.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                        : null;
                    decimal totalOriginal = serviceSubTotal + productSubTotal;
                    var hoaDon = new HoaDon
                    {
                        MaHd = maHd,
                        MaKh = safeKhachHang,
                        MaLd = maLd,
                        NgayTao = VnTimeHelper.Now(),
                        TongTien = totalOriginal,
                        ThanhTien = req.GrandTotal,
                        TienDaTra = req.PaidAmount,
                        TrangThai = isOnlinePayment ? 0 : (req.PaidAmount >= req.GrandTotal ? 2 : 1),
                        MaCode = maCodeChinh,
                        PhanTramGiamVip = phanTramGiamRank
                    };
                    _context.HoaDons.Add(hoaDon);
                    await _context.SaveChangesAsync();

                    string maHddtSpPhatHanh = null;
                    if (req.Products != null && req.Products.Any())
                    {
                        foreach (var p in req.Products)
                        {
                            _context.ChitietHoadonSps.Add(new ChitietHoadonSp
                            {
                                MaHd = maHd,
                                MaBienThe = p.Id,
                                SoLuong = p.SoLuong,
                                DonGiaLucBan = p.Gia,
                                ThanhTien = p.Gia * p.SoLuong
                            });

                            var bienthe = await _context.BienTheSanphams.FindAsync(p.Id);
                            if (bienthe != null)
                            {
                                int rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE BIENTHE_SANPHAM SET SoLuongTon = SoLuongTon - {p.SoLuong} WHERE MaBienThe = {p.Id} AND SoLuongTon >= {p.SoLuong}");
                                if (rowsAffected <= 0)
                                {
                                    await transaction.RollbackAsync();
                                    return BadRequest(new { success = false, errorCode = "PRODUCT_OUT_OF_STOCK", outOfStockProductId = p.Id, message = $"Sản phẩm '{bienthe.TenBienThe}' vừa bị khách khác mua hết hoặc không đủ số lượng trong kho!" });
                                }

                                int tonTruoc = bienthe.SoLuongTon ?? 0;
                                int tonSau = tonTruoc - p.SoLuong;

                                _context.LichSuTonKhos.Add(new LichSuTonKho
                                {
                                    MaBienThe = bienthe.MaBienThe,
                                    LoaiBienDong = "BAN",
                                    SoLuongThayDoi = -p.SoLuong,
                                    TonTruoc = tonTruoc,
                                    TonSau = tonSau,
                                    MaChungTu = maHd,
                                    MaNv = null,
                                    GhiChu = $"Bán hàng theo đơn {maHd}",
                                    ThoiGian = VnTimeHelper.Now()
                                });
                            }
                        }

                        if (req.ReceiveType == "home" && req.DeliveryInfo != null)
                        {
                            _context.ThongtinGiaohangs.Add(new ThongtinGiaohang
                            {
                                MaHd = maHd,
                                TenNguoiNhan = req.DeliveryInfo.HoTen ?? "Khách hàng",
                                SdtNhan = req.DeliveryInfo.SoDienThoai ?? "",
                                DiaChiNhan = req.DeliveryInfo.DiaChi ?? "",
                                Email = req.DeliveryInfo.Email ?? "",
                                PhiShip = req.ShippingFee,
                                TrangThaiGiaoHang = 0
                            });
                        }
                    }

                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = maHd,
                        PhuongThuc = (phuongThuc ?? "COD").ToUpper(),
                        SoTien = req.PaidAmount,
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
                        var thoiDiemTao = VnTimeHelper.Now();

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
                            var maHddtGop = "HD" + thoiDiemTao.ToString("yyMMddHHmmss");
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
                            var maHddtSp = "HD" + thoiDiemTao.ToString("yyMMddHHmmss") + "S";
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
                    if (req.PaidAmount > 0)
                    {
                        if (phuongThuc.ToLower() == "vnpay")
                        {
                            var vnpay = new VnPayLibrary();
                            vnpay.AddRequestData("vnp_Version", "2.1.0");
                            vnpay.AddRequestData("vnp_Command", "pay");
                            vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"] ?? "");
                            vnpay.AddRequestData("vnp_Amount", ((long)(req.PaidAmount * 100)).ToString());

                            var vnpayNow = VnTimeHelper.Now();
                            vnpay.AddRequestData("vnp_CreateDate", vnpayNow.ToString("yyyyMMddHHmmss"));
                            vnpay.AddRequestData("vnp_ExpireDate", vnpayNow.AddMinutes(15).ToString("yyyyMMddHHmmss"));
                            vnpay.AddRequestData("vnp_CurrCode", "VND");
                            vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                            vnpay.AddRequestData("vnp_Locale", "vn");
                            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan hoa don " + maHd);
                            vnpay.AddRequestData("vnp_OrderType", "other");
                            vnpay.AddRequestData("vnp_ReturnUrl", _configuration["Vnpay:ReturnUrlTong"]);
                            vnpay.AddRequestData("vnp_TxnRef", maHd);

                            paymentUrl = vnpay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"] ?? "", _configuration["Vnpay:HashSecret"] ?? "");
                        }
                        else if (phuongThuc.ToLower() == "momo")
                        {
                            var partnerCode = _configuration["Momo:PartnerCode"];
                            var accessKey = _configuration["Momo:AccessKey"];
                            var secretKey = _configuration["Momo:SecretKey"];
                            var endpoint = _configuration["Momo:ApiEndpoint"];
                            var redirectUrl = _configuration["Momo:ReturnUrlTong"];
                            var ipnUrl = _configuration["Momo:NotifyUrl"];

                            long amount = (long)req.PaidAmount;
                            string orderInfo = "Thanh toan hoa don tong " + maHd;

                            string rawHash =
                                $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={maHd}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={maHd}&requestType=captureWallet";

                            string signature = SignHmacSHA256(rawHash, secretKey);

                            var requestData = new
                            {
                                partnerCode,
                                requestId = maHd,
                                amount,
                                orderId = maHd,
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

        // ĐÃ VÁ: Bắt tín hiệu khách hủy để nhả slot ngay lập tức
        [HttpGet("get-receipt")]
        public async Task<IActionResult> GetReceipt(
            [FromQuery] string maHd,
            [FromQuery] string? vnp_ResponseCode = null,
            [FromQuery] string? resultCode = null)
        {
            if (string.IsNullOrEmpty(maHd)) return BadRequest(new { success = false, message = "Thiếu mã hóa đơn." });

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(h => h.ThongtinGiaohangs)
                    .Include(h => h.Thanhtoans)
                    .FirstOrDefaultAsync(h => h.MaHd == maHd);

                if (hoaDon == null) return NotFound(new { success = false, message = "Không tìm thấy hóa đơn" });

                bool isPaymentSuccess = (vnp_ResponseCode == "00") || (resultCode == "0");
                bool vuaXacNhanThanhCong = isPaymentSuccess && hoaDon.TrangThai == 0;

                if (vuaXacNhanThanhCong)
                {
                    hoaDon.TrangThai = (hoaDon.TienDaTra ?? 0) >= (hoaDon.ThanhTien ?? 0) ? 2 : 1;
                    var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                    if (thanhToan != null) thanhToan.TrangThai = 1;
                    await _context.SaveChangesAsync();

                    bool daCoHddt = await _context.HoaDonDienTus.AnyAsync(h => h.MaHd == maHd);
                    if (!daCoHddt)
                    {
                        bool coDichVuDb = !string.IsNullOrEmpty(hoaDon.MaLd);
                        bool coSanPhamDb = await _context.ChitietHoadonSps.AnyAsync(c => c.MaHd == maHd);
                        var pending = await _cache.GetObjectAsync<PendingInvoiceInfo>(PendingInvoiceCacheKey(maHd));
                        if (pending != null)
                        {
                            await _cache.RemoveSafeAsync(PendingInvoiceCacheKey(maHd));
                        }
                        else
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
                                MaHd = maHd,
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
                                MaHd = maHd,
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
                    }
                }
                else if (!isPaymentSuccess && hoaDon.TrangThai == 0) // LƯỚI BẢO VỆ DỌN RÁC KHÁCH HỦY
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

                bool shouldSendEmail = false;
                if ((hoaDon.TrangThai == 1 || hoaDon.TrangThai == 2) && !string.IsNullOrEmpty(hoaDon.MaLd))
                {
                    var lichDat = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                    if (lichDat != null && lichDat.TrangThai == 0)
                    {
                        lichDat.TrangThai = 1;
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

                var chiTietSp = await _context.ChitietHoadonSps.AsNoTracking().Where(c => c.MaHd == maHd).ToListAsync();
                var productsList = new List<object>();
                foreach (var ct in chiTietSp)
                {
                    var bt = await _context.BienTheSanphams.FindAsync(ct.MaBienThe);
                    var sp = bt != null ? await _context.SanPhams.FindAsync(bt.MaSanPham) : null;
                    productsList.Add(new { name = $"{sp?.TenSp} - {bt?.TenBienThe}", qty = ct.SoLuong, price = ct.DonGiaLucBan, total = ct.ThanhTien });
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
                    delivery = delivery != null ? new { nguoiNhan = delivery.TenNguoiNhan, sdt = delivery.SdtNhan, diaChi = delivery.DiaChiNhan, phiShip = delivery.PhiShip } : null
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

    // ĐÃ VÁ: Bổ sung trường Phone để đồng bộ logic quét "Chính chủ"
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class ThanhToanCheckStylistRequest
    {
        public string NgayHen { get; set; }
        public string GioBatDau { get; set; }
        public int TongThoiGian { get; set; }
        public string MaKh { get; set; }
        public string? Phone { get; set; }
    }
}
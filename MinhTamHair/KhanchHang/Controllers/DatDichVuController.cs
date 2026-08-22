using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System.Text;
using System.Security.Cryptography;
using System.Net.Http.Json;
using MinhTamHair.Services;
using MinhTamHair.Services.TimeHelper;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DatDichVuController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IQrEmailService _qrEmailService;
        private readonly IServiceScopeFactory _scopeFactory;

        public DatDichVuController(MinhTamHairContext context, IConfiguration configuration, IQrEmailService qrEmailService, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _configuration = configuration;
            _qrEmailService = qrEmailService;
            _scopeFactory = scopeFactory;
        }

        // =========================================================================
        // 1. LẤY DỮ LIỆU DỊCH VỤ & COMBO (ĐÃ TỐI ƯU HÓA TRUY VẤN SQL VÀ FLASHSALE)
        // =========================================================================
        [HttpGet("data-booking")]
        public async Task<IActionResult> GetAll()
        {
            var now = VnTimeHelper.Now();

            // Truy vấn trực tiếp trên SQL Server với AsNoTracking, chỉ lấy các trường cần thiết
            var rawServices = await _context.Dichvus
                .AsNoTracking()
                .Where(s => s.TrangThai == 1)
                .Select(s => new
                {
                    id = s.MaDv,
                    name = s.TenDv,
                    originalPrice = s.Gia,
                    duration = s.ThoiGianLam ?? 30,
                    icon = "fas fa-cut",
                    phanTramHoaHong = s.PhanTramHoaHong ?? 0,
                    hinhAnh = s.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault(),
                    // Lọc FlashSale còn hạn trực tiếp trên SQL
                    phanTramGiam = s.ChitietFlashsales
                        .Where(fs => fs.MaFlNavigation.TrangThai == 1 &&
                                     fs.MaFlNavigation.NgayBatDau <= now &&
                                     fs.MaFlNavigation.NgayKetThuc >= now)
                        .Select(fs => fs.PhanTramGiam)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Tính toán giá sau giảm
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
                    icon = s.icon,
                    phanTramHoaHong = s.phanTramHoaHong,
                    hinhAnh = s.hinhAnh
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
                    duration = c.Combo_DichVus.Sum(ct => ct.DichVu.ThoiGianLam ?? 30),
                    icon = "fas fa-layer-group",
                    phanTramHoaHong = 0,
                    hinhAnh = c.HinhAnh
                })
                .ToListAsync();

            return Ok(new { services, combos });
        }

        [HttpGet("get-stylists")]
        public async Task<IActionResult> GetStylists()
        {
            var stylists = await _context.Nhanviens
                .AsNoTracking()
                .Include(nv => nv.MaLoaiNvNavigation)
                .Where(nv => nv.TrangThai == 1 && nv.MaLoaiNv == "LNV01")
                .Select(nv => new
                {
                    id = nv.MaNv,
                    name = nv.HoTenNv,
                    role = nv.MaLoaiNvNavigation.TenLoai,
                    img = nv.HinhAnh ?? "https://i.pravatar.cc/150",
                    rating = Math.Round(_context.DanhgiaDichvus
                        .Where(dg => _context.HoaDons
                            .Where(hd => _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))
                            .Select(hd => hd.MaHd)
                            .Contains(dg.MaHd))
                        .Average(dg => (double?)dg.SoSao) ?? 5.0, 1)
                })
                .ToListAsync();

            return Ok(stylists);
        }

        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal,
            [FromQuery] DateTime? ngayGioHen, [FromQuery] string? serviceIds)
        {

            var kh = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);

            var now = VnTimeHelper.Now();
            var ngayDichVu = ngayGioHen ?? now;
            var cartIds = string.IsNullOrWhiteSpace(serviceIds)
                ? new List<string>()
                : serviceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
                    expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : "Vĩnh viễn"
                });
            }

            return Ok(result);
        }

        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(
            string maKH,
            [FromQuery] string? codes,
            [FromQuery] decimal subTotal,
            [FromQuery] DateTime? ngayGioHen,
            [FromQuery] string? serviceIds)
        {
            if (string.IsNullOrWhiteSpace(maKH))
                return BadRequest(new { message = "Thiếu mã khách hàng." });

            var parts = string.IsNullOrWhiteSpace(codes)
                ? new List<string>()
                : codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(VoucherCartLogic.NormalizeMaCode)
                    .Where(s => s.Length > 0)
                    .Take(1)
                    .ToList();

            var kh = await _context.Khachhangs.AsNoTracking()
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);

            if (kh == null)
                return Ok(new { totalDiscount = 0m, memberDiscount = 0m, finalAmount = subTotal, codes = parts });

            string? maRank = kh.MaRank;
            int? rankCap = null;
            decimal memberDiscount = 0m;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                if (rank != null)
                {
                    rankCap = rank.PhanTramGiamGia;
                    if (rank.PhanTramGiamGia > 0 && subTotal > 0)
                    {
                        memberDiscount = Math.Round(subTotal * ((decimal)rank.PhanTramGiamGia.Value / 100m), 0);
                    }
                }
            }

            var now = VnTimeHelper.Now();
            var owned = new HashSet<string>(kh.MaCodes.Select(x => x.MaCode), StringComparer.OrdinalIgnoreCase);

            foreach (var c in parts)
            {
                if (!owned.Contains(c))
                {
                    return Ok(new
                    {
                        totalDiscount = 0m,
                        memberDiscount = memberDiscount,
                        finalAmount = Math.Max(0m, subTotal - memberDiscount),
                        codes = parts,
                        errorMessage = $"Mã voucher không tồn tại hoặc đã hết hạn: {c}"
                    });
                }
            }

            var ngayDichVu = ngayGioHen ?? now;
            var cartIds = string.IsNullOrWhiteSpace(serviceIds)
                ? new List<string>()
                : serviceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            decimal totalGiam = 0m;
            if (parts.Count > 0)
            {
                var (err, voucherTotalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(
                    _context,
                    parts,
                    maKH,
                    subTotal,
                    cartIds,
                    ngayDichVu,
                    now,
                    maRank,
                    rankCap,
                    maxVouchersAllowed: 1);

                if (err != null)
                {
                    return Ok(new
                    {
                        totalDiscount = 0m,
                        memberDiscount = memberDiscount,
                        finalAmount = Math.Max(0m, subTotal - memberDiscount),
                        codes = parts,
                        errorMessage = err
                    });
                }

                totalGiam = voucherTotalGiam;
            }

            decimal finalAmount = Math.Max(0m, subTotal - memberDiscount - totalGiam);

            return Ok(new { totalDiscount = totalGiam, memberDiscount = memberDiscount, finalAmount = finalAmount, codes = parts });
        }

        [HttpPost("create-payment")]
        public async Task<IActionResult> CreatePayment([FromBody] PaymentInformationRequest model)
        {
            if (model.OrderDescription.ToLower().Contains("vnpay"))
            {
                var vnpay = new VnPayLibrary();
                var vnp_TmnCode = _configuration["Vnpay:TmnCode"];
                var vnp_HashSecret = _configuration["Vnpay:HashSecret"];
                var vnp_Url = _configuration["Vnpay:BaseUrl"];
                var vnp_ReturnUrl = _configuration["Vnpay:ReturnUrlDichVu"];

                var vnpayNow = VnTimeHelper.Now();

                vnpay.AddRequestData("vnp_Version", "2.1.0");
                vnpay.AddRequestData("vnp_Command", "pay");
                vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
                vnpay.AddRequestData("vnp_Amount", ((long)(model.Amount * 100)).ToString());
                vnpay.AddRequestData("vnp_CreateDate", vnpayNow.ToString("yyyyMMddHHmmss"));
                vnpay.AddRequestData("vnp_ExpireDate", vnpayNow.AddMinutes(15).ToString("yyyyMMddHHmmss"));
                vnpay.AddRequestData("vnp_CurrCode", "VND");
                vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                vnpay.AddRequestData("vnp_Locale", "vn");
                vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan lich hen " + vnpayNow.Ticks);
                vnpay.AddRequestData("vnp_OrderType", "other");
                vnpay.AddRequestData("vnp_ReturnUrl", vnp_ReturnUrl);
                vnpay.AddRequestData("vnp_TxnRef", vnpayNow.ToString("yyyyMMddHHmmss"));

                var paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
                return Ok(new { url = paymentUrl });
            }

            var partnerCode = _configuration["Momo:PartnerCode"];
            var accessKey = _configuration["Momo:AccessKey"];
            var secretKey = _configuration["Momo:SecretKey"];
            var endpoint = _configuration["Momo:ApiEndpoint"];
            var redirectUrl = _configuration["Momo:ReturnUrlDichVu"];
            var ipnUrl = _configuration["Momo:NotifyUrl"];

            string orderId = VnTimeHelper.Now().ToString("yyyyMMddHHmmss");
            string requestId = Guid.NewGuid().ToString();
            long amount = (long)model.Amount;
            string orderInfo = "Thanh toan lich hen Minh Tam Hair";

            string rawHash = $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType=captureWallet";
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

            if (result != null && result.resultCode == 0) return Ok(new { url = result.payUrl });
            return BadRequest(result?.message ?? "Lỗi MoMo");
        }

        // =========================================================================
        // 2. LƯU LỊCH ĐẶT VÀ THANH TOÁN (ĐÃ TỐI ƯU TRANSACTION & GỬI EMAIL NGẦM)
        // =========================================================================
        [HttpPost("save-booking-final")]
        public async Task<IActionResult> SaveBooking([FromBody] BookingRequest model, [FromQuery] string paymentMode, [FromQuery] string? maStylist)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                string shortMaLd = model.BookingId.Length > 10
                    ? model.BookingId.Substring(model.BookingId.Length - 10)
                    : model.BookingId;

                string currentMaKh = model.MaKh;
                decimal total = 0m;
                string emailGuiMail = model.Email;
                string tenKhachGuiMail = model.HoTen ?? "Khách hàng";

                // =========================================================
                // BƯỚC 1: XỬ LÝ TOÀN BỘ DATABASE VÀ COMMIT NGAY LẬP TỨC
                // =========================================================
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        // 1. XỬ LÝ KHÁCH HÀNG VÃNG LAI
                        if (currentMaKh == "GUEST" && !string.IsNullOrEmpty(model.Phone))
                        {
                            var existingKhach = await _context.Khachhangs.FirstOrDefaultAsync(x => x.Sdt == model.Phone);

                            if (existingKhach != null)
                            {
                                currentMaKh = existingKhach.MaKh;
                                emailGuiMail = !string.IsNullOrEmpty(emailGuiMail) ? emailGuiMail : existingKhach.Email;
                                tenKhachGuiMail = !string.IsNullOrEmpty(tenKhachGuiMail) ? tenKhachGuiMail : existingKhach.HoTen;
                            }
                            else
                            {
                                string prefixSdt = model.Phone.Length >= 4 ? model.Phone.Substring(0, 4) : "0000";
                                string newMaKh = "KH" + prefixSdt + new Random().Next(100, 999).ToString();

                                var newKhachHang = new Khachhang
                                {
                                    MaKh = newMaKh,
                                    HoTen = model.HoTen,
                                    Sdt = model.Phone,
                                    Email = model.Email,
                                    MatKhau = null,
                                    TrangThai = 1,
                                    NgayTao = VnTimeHelper.Now(),
                                    DiemTichLuy = 0,
                                    MaRank = "RANK01"
                                };

                                _context.Khachhangs.Add(newKhachHang);
                                await _context.SaveChangesAsync();
                                currentMaKh = newMaKh;
                            }
                        }

                        decimal subtotal = model.Services.Sum(s => s.Price);

                        // 2. XỬ LÝ GIẢM GIÁ THÀNH VIÊN & VOUCHER
                        decimal tongSauGiam = subtotal;
                        decimal tienGiamThanhVien = 0m;

                        var khachHangDb = await _context.Khachhangs.AsNoTracking()
                            .Include(k => k.MaCodes)
                            .FirstOrDefaultAsync(k => k.MaKh == currentMaKh);

                        if (khachHangDb != null)
                        {
                            emailGuiMail = !string.IsNullOrEmpty(emailGuiMail) ? emailGuiMail : khachHangDb.Email;
                            tenKhachGuiMail = !string.IsNullOrEmpty(tenKhachGuiMail) ? tenKhachGuiMail : khachHangDb.HoTen;
                        }

                        string? maRank = khachHangDb?.MaRank;
                        int? phanTramGiamRank = 0;
                        int? rankCap = null;

                        if (!string.IsNullOrWhiteSpace(maRank))
                        {
                            var rank = await _context.Hangthanhviens.AsNoTracking()
                                .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));

                            if (rank != null)
                            {
                                phanTramGiamRank = rank.PhanTramGiamGia ?? 0;
                                rankCap = rank.PhanTramGiamGia;
                            }
                        }

                        if (phanTramGiamRank > 0 && subtotal > 0)
                        {
                            tienGiamThanhVien = Math.Round(subtotal * ((decimal)phanTramGiamRank.Value / 100m), 0);
                            tongSauGiam -= tienGiamThanhVien;
                        }

                        var codesList = new List<string>();
                        if (model.MaCodes != null && model.MaCodes.Count > 0)
                        {
                            foreach (var c in model.MaCodes.Take(1))
                            {
                                if (!string.IsNullOrWhiteSpace(c))
                                    codesList.Add(VoucherCartLogic.NormalizeMaCode(c));
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(model.MaCode))
                        {
                            codesList.Add(VoucherCartLogic.NormalizeMaCode(model.MaCode));
                        }

                        string? maCodeNorm = codesList.Count > 0 ? codesList[0] : null;

                        if (codesList.Count > 0)
                        {
                            if (codesList.Count != codesList.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                            {
                                await transaction.RollbackAsync();
                                return BadRequest(new { message = "Không được dùng trùng một mã voucher." });
                            }

                            if (khachHangDb == null)
                            {
                                await transaction.RollbackAsync();
                                return BadRequest(new { message = "Không tìm thấy khách hàng." });
                            }

                            var ownedCodes = new HashSet<string>(khachHangDb.MaCodes.Select(x => x.MaCode), StringComparer.OrdinalIgnoreCase);

                            foreach (var c in codesList)
                            {
                                if (!ownedCodes.Contains(c))
                                {
                                    await transaction.RollbackAsync();
                                    return BadRequest(new { message = $"Mã voucher không hợp lệ hoặc đã hết hạn: {c}" });
                                }
                            }

                            var cartIds = model.Services.Select(s => s.Id).ToList();

                            var (vErr, totalGiamVoucher, sauVoucher) = await VoucherCartLogic.ValidateStackedAsync(
                                _context, codesList, currentMaKh, subtotal, cartIds,
                                model.NgayHen, VnTimeHelper.Now(), maRank, rankCap, maxVouchersAllowed: 1);

                            if (vErr != null)
                            {
                                await transaction.RollbackAsync();
                                return BadRequest(new { message = vErr });
                            }

                            tongSauGiam = Math.Max(0m, subtotal - tienGiamThanhVien - totalGiamVoucher);
                        }

                        tongSauGiam = Math.Max(0m, tongSauGiam);

                        // 3. TÍNH TOÁN TIỀN THANH TOÁN
                        total = tongSauGiam;
                        decimal daThanhToan = (paymentMode == "10") ? (total * 0.1m) : total;
                        int trangThaiLichDat = (paymentMode == "10") ? 1 : 2;

                        // 4. LƯU BẢNG LỊCH ĐẶT
                        int tongPhut = 0;
                        foreach (var item in model.Services)
                        {
                            if (item.Id.StartsWith("DV"))
                            {
                                var dv = await _context.Dichvus.FindAsync(item.Id);
                                tongPhut += dv?.ThoiGianLam ?? 30;
                            }
                            else if (item.Id.StartsWith("CB"))
                            {
                                var cb = await _context.Combos
                                    .Include(c => c.Combo_DichVus)
                                    .ThenInclude(cd => cd.DichVu)
                                    .FirstOrDefaultAsync(c => c.MaCb == item.Id);

                                if (cb != null)
                                {
                                    int comboDuration = cb.Combo_DichVus.Sum(ct => ct.DichVu.ThoiGianLam ?? 30);
                                    tongPhut += comboDuration > 0 ? comboDuration : 60;
                                }
                            }
                        }

                        var localTime = model.NgayHen.ToUniversalTime().AddHours(7);

                        var lichDat = new Lichdat
                        {
                            MaLd = shortMaLd,
                            MaKh = currentMaKh,
                            NgayHen = DateOnly.FromDateTime(localTime),
                            GioBatDau = TimeOnly.FromDateTime(localTime),
                            GioKetThuc = TimeOnly.FromDateTime(localTime).AddMinutes(tongPhut),
                            TongTienDuKien = total,
                            TrangThai = trangThaiLichDat
                        };
                        _context.Lichdats.Add(lichDat);

                        // 5. LƯU BẢNG HÓA ĐƠN
                        string maHdTạm = shortMaLd;
                        var hoaDon = new HoaDon
                        {
                            MaHd = shortMaLd,
                            MaLd = shortMaLd,
                            MaKh = currentMaKh,
                            NgayTao = VnTimeHelper.Now(),
                            TongTien = subtotal,
                            ThanhTien = tongSauGiam,
                            TienDaTra = daThanhToan,
                            PhanTramGiamVip = phanTramGiamRank,
                            TrangThai = daThanhToan >= tongSauGiam ? 2 : 1,
                            MaCode = maCodeNorm
                        };
                        _context.HoaDons.Add(hoaDon);

                        // 6. LƯU CHI TIẾT LỊCH ĐẶT
                        foreach (var item in model.Services)
                        {
                            var chiTiet = new ChitietLichdat
                            {
                                MaLd = shortMaLd,
                                MaNv = maStylist,
                                GiaLucDat = item.Price
                            };

                            if (item.Id.StartsWith("DV"))
                            {
                                chiTiet.MaDv = item.Id;
                                chiTiet.MaCb = null;
                            }
                            else if (item.Id.StartsWith("CB"))
                            {
                                chiTiet.MaCb = item.Id;
                                chiTiet.MaDv = null;
                            }
                            _context.ChitietLichdats.Add(chiTiet);
                        }

                        // 7. LƯU BẢNG THANH TOÁN
                        var tt = new Thanhtoan
                        {
                            MaHd = maHdTạm,
                            PhuongThuc = (paymentMode == "10") ? "Coc 10%" : "Full 100%",
                            SoTien = daThanhToan,
                            TrangThai = 1
                        };
                        _context.Thanhtoans.Add(tt);

                        // 8. TRỪ LƯỢT DÙNG VOUCHER
                        foreach (var code in codesList)
                        {
                            var (incOk, incErr) = await VoucherCartLogic.TryIncrementUsageAsync(_context, code, currentMaKh);
                            if (!incOk)
                            {
                                await transaction.RollbackAsync();
                                return BadRequest(new { message = incErr ?? "Không thể ghi nhận lượt dùng voucher." });
                            }
                        }

                        // 9. XUẤT HÓA ĐƠN ĐIỆN TỬ
                        bool laDoanhNghiep = model.InvoiceInfo?.LaDoanhNghiep ?? false;
                        var maHddt = "HD" + VnTimeHelper.Now().ToString("yyMMddHHmmss");
                        _context.HoaDonDienTus.Add(new HoaDonDienTu
                        {
                            MaHddt = maHddt,
                            MaHd = maHdTạm,
                            LoaiKhach = laDoanhNghiep ? (byte)1 : (byte)0,
                            TenNguoiMua = laDoanhNghiep ? model.InvoiceInfo?.TenCongTy : (!string.IsNullOrWhiteSpace(model.HoTen) ? model.HoTen : "Khach hang"),
                            Mst = laDoanhNghiep ? model.InvoiceInfo?.Mst : null,
                            DiaChiXuatHd = model.InvoiceInfo?.DiaChiXuatHD,
                            EmailNhanHd = model.InvoiceInfo?.EmailNhanHD ?? model.Email,
                            TrangThai = 3
                        });

                        await _context.SaveChangesAsync();

                        // 10. COMMIT TRANSACTION - KẾT THÚC VÀ GIẢI PHÓNG CONNECTION DB NGAY LẬP TỨC
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest("Lỗi khi xử lý đặt lịch: " + (ex.InnerException?.Message ?? ex.Message));
                    }
                } // Kết thúc using Transaction

                // =========================================================
                // BƯỚC 2: GỬI EMAIL QR CHẠY NGẦM (BÊN NGOÀI TRANSACTION)
                // =========================================================
                if (!string.IsNullOrEmpty(emailGuiMail))
                {
                    var ngayHenGui = model.NgayHen;
                    var tongTienGui = total;
                    var maLdGui = shortMaLd;
                    var emailGui = emailGuiMail;
                    var tenGui = tenKhachGuiMail;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var qrService = scope.ServiceProvider.GetRequiredService<IQrEmailService>();
                            await qrService.SendBookingEmailAsync(emailGui, tenGui, maLdGui, ngayHenGui, tongTienGui);
                            Console.WriteLine($"[EMAIL] Đã gửi mã QR thành công tới email: {emailGui}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[EMAIL ERROR] Lỗi gửi email cho lịch {maLdGui}: {ex.Message}");
                        }
                    });
                }

                // Trả về phản hồi ngay lập tức cho client
                return Ok(new { message = "Thành công", maLd = shortMaLd });
            });
        }

        private string SignHmacSHA256(string message, string key)
        {
            byte[] keyByte = Encoding.UTF8.GetBytes(key);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
                return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
            }
        }

        public class CheckStylistRequest
        {
            public string NgayHen { get; set; }
            public string GioBatDau { get; set; }
            public int TongThoiGian { get; set; }
            public string MaKh { get; set; }
        }

        [HttpPost("get-available-stylists")]
        public async Task<IActionResult> GetAvailableStylists([FromBody] CheckStylistRequest request)
        {
            try
            {
                DateOnly ngayHen = DateOnly.Parse(request.NgayHen);
                TimeOnly start = TimeOnly.Parse(request.GioBatDau);
                TimeOnly end = start.AddMinutes(request.TongThoiGian);

                var favoriteStylistIds = new List<string>();
                if (!string.IsNullOrEmpty(request.MaKh) && request.MaKh != "GUEST")
                {
                    favoriteStylistIds = await _context.Set<YeuThichNhanVien>()
                        .Where(y => y.MaKH == request.MaKh)
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
                    .Where(ct => ct.MaLdNavigation.NgayHen == ngayHen && ct.MaLdNavigation.TrangThai != 0)
                    .Select(ct => new
                    {
                        MaNv = ct.MaNv,
                        GioBatDau = ct.MaLdNavigation.GioBatDau,
                        GioKetThuc = ct.MaLdNavigation.GioKetThuc
                    })
                    .Distinct()
                    .ToListAsync();

                var result = new List<object>();

                foreach (var lich in lichLamViecs)
                {
                    var nv = lich.MaNvNavigation;
                    bool isAvailable = true;
                    var lichTrinhChiTiet = new List<string>();

                    bool dungCaLam = false;
                    string ca = lich.CaLam ?? "";
                    string gioLamThucTe = ca;
                    TimeOnly shiftStart = new TimeOnly(7, 0);
                    TimeOnly shiftEnd = new TimeOnly(20, 0);

                    if (ca == "Ca sáng (7h-17h)" || ca == "Ca ngày" || ca == "Ca sáng") { gioLamThucTe = "07:00 - 17:00"; shiftStart = new TimeOnly(7, 0); shiftEnd = new TimeOnly(17, 0); }
                    else if (ca == "Ca tối (13h-20h)" || ca == "Ca tối") { gioLamThucTe = "13:00 - 20:00"; shiftStart = new TimeOnly(13, 0); shiftEnd = new TimeOnly(20, 0); }
                    else if (ca == "Cả ngày" || ca == "Full time") { gioLamThucTe = "07:00 - 20:00"; shiftStart = new TimeOnly(7, 0); shiftEnd = new TimeOnly(20, 0); }
                    else if (ca == "Ca sáng (Part-time)") { gioLamThucTe = "07:00 - 11:00"; shiftStart = new TimeOnly(7, 0); shiftEnd = new TimeOnly(11, 0); }
                    else if (ca == "Ca chiều (Part-time)") { gioLamThucTe = "13:00 - 17:00"; shiftStart = new TimeOnly(13, 0); shiftEnd = new TimeOnly(17, 0); }
                    else if (ca == "Ca tối (Part-time)") { gioLamThucTe = "16:00 - 20:00"; shiftStart = new TimeOnly(16, 0); shiftEnd = new TimeOnly(20, 0); }

                    if ((gioLamThucTe == "07:00 - 17:00") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(17, 0)) dungCaLam = true;
                    else if ((gioLamThucTe == "13:00 - 20:00") && start >= new TimeOnly(13, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;
                    else if ((gioLamThucTe == "07:00 - 20:00") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;
                    else if ((gioLamThucTe == "07:00 - 11:00") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(11, 0)) dungCaLam = true;
                    else if ((gioLamThucTe == "13:00 - 17:00") && start >= new TimeOnly(13, 0) && end <= new TimeOnly(17, 0)) dungCaLam = true;
                    else if ((gioLamThucTe == "16:00 - 20:00") && start >= new TimeOnly(16, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;

                    var cacLichCuaTho = cacLichBan
                        .Where(b => b.MaNv == nv.MaNv)
                        .OrderBy(b => b.GioBatDau)
                        .ToList();

                    if (!dungCaLam)
                    {
                        isAvailable = false;
                        lichTrinhChiTiet.Add($"Trái ca (Làm {gioLamThucTe})");
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
                                    lichTrinhChiTiet.Add($"Lịch trống: {currentTime.ToString(@"hh\:mm")} - {b.GioBatDau.Value.ToString(@"hh\:mm")}");
                                }

                                lichTrinhChiTiet.Add($"Có hẹn: {b.GioBatDau.Value.ToString(@"hh\:mm")} - {b.GioKetThuc.Value.ToString(@"hh\:mm")}");

                                if (currentTime < b.GioKetThuc.Value)
                                {
                                    currentTime = b.GioKetThuc.Value;
                                }
                            }
                        }

                        if (currentTime < shiftEnd)
                        {
                            lichTrinhChiTiet.Add($"Lịch trống: {currentTime.ToString(@"hh\:mm")} - {shiftEnd.ToString(@"hh\:mm")}");
                        }

                        bool isConflict = cacLichCuaTho.Any(b => b.GioBatDau < end && b.GioKetThuc > start);
                        if (isConflict)
                        {
                            isAvailable = false;
                        }
                    }

                    var ratingVal = _context.DanhgiaDichvus.Where(dg => _context.HoaDons.Any(hd => dg.MaHd == hd.MaHd && _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))).Average(dg => (double?)dg.SoSao);
                    bool isFav = favoriteStylistIds.Contains(nv.MaNv.Trim());

                    result.Add(new
                    {
                        id = nv.MaNv.Trim(),
                        name = nv.HoTenNv,
                        role = nv.MaLoaiNvNavigation?.TenLoai ?? "Stylist",
                        img = nv.HinhAnh ?? "/images/no-image.png",
                        rating = ratingVal != null ? Math.Round(ratingVal.Value, 1) : 5.0,
                        isAvailable = isAvailable,
                        lichTrinh = lichTrinhChiTiet,
                        caLam = lich.CaLam,
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
    }
}

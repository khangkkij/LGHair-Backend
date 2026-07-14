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

        [HttpGet("data-booking")]
        public IActionResult GetAll()
        {
            var now = VnTimeHelper.Now();
            var services = _context.Dichvus
                .Where(s => s.TrangThai == 1)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                    .Include(x => x.Hinhanhs)
                .ToList() // Load vào RAM để xử lý
                .Select(s => {
                    var flash = s.ChitietFlashsales
                        .FirstOrDefault(fs =>
                            fs.MaFlNavigation.TrangThai == 1 &&
                            fs.MaFlNavigation.NgayBatDau <= now &&
                            fs.MaFlNavigation.NgayKetThuc >= now);

                    decimal finalPrice = (decimal)s.Gia;
                    if (flash != null && flash.PhanTramGiam.HasValue)
                    {
                        // Thêm chữ 'm' sau số 100 để C# tự động hiểu đó là số decimal
                        finalPrice = (decimal)s.Gia - ((decimal)s.Gia * (decimal)flash.PhanTramGiam.Value / 100m);
                    }

                    return new
                    {
                        id = s.MaDv,
                        name = s.TenDv,
                        price = finalPrice,       // Giá đã giảm chuẩn decimal
                        originalPrice = s.Gia,    // Giá gốc
                        duration = s.ThoiGianLam ?? 30,
                        icon = "fas fa-cut",
                        hinhAnh = s.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault()
                    };
                }).ToList();

            var combos = _context.Combos
            .Where(c => c.TrangThai == 1)
            .Select(c => new {
                id = c.MaCb,
                name = c.TenCombo,

                price = c.Gia,
                originalPrice = c.Gia, // THÊM DÒNG NÀY: Để đồng bộ cấu trúc với Frontend

                // Dùng Combo_DichVus theo đúng class C# của bạn
                duration = c.Combo_DichVus.Sum(ct => ct.DichVu.ThoiGianLam ?? 30),
                icon = "fas fa-layer-group",
                hinhAnh = c.HinhAnh
            }).ToList();

            return Ok(new { services, combos });
        }

        [HttpGet("get-stylists")]
        public IActionResult GetStylists()
        {
            var stylists = _context.Nhanviens
                .Include(nv => nv.MaLoaiNvNavigation)
                .Where(nv => nv.TrangThai == 1 && nv.MaLoaiNv == "LNV01")
                .Select(nv => new {
                    id = nv.MaNv,
                    name = nv.HoTenNv,
                    role = nv.MaLoaiNvNavigation.TenLoai,
                    img = nv.HinhAnh ?? "https://i.pravatar.cc/150",
                    // Lọc số sao của thợ này từ bảng DanhGia -> HoaDon -> ChiTiet
                    rating = Math.Round(_context.DanhgiaDichvus
                        .Where(dg => _context.HoaDons
                            .Where(hd => _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))
                            .Select(hd => hd.MaHd)
                            .Contains(dg.MaHd))
                        .Average(dg => (double?)dg.SoSao) ?? 5.0, 1) // Mặc định 5 sao nếu chưa có đánh giá
                }).ToList();
            return Ok(stylists);
        }

        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal,
            [FromQuery] DateTime? ngayGioHen, [FromQuery] string? serviceIds)
        {
            await VoucherCartLogic.AutoDisableExpiredVouchersAsync(_context, VnTimeHelper.Now());
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
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                rankCap = rank?.PhanTramGiamGia;
            }

            var publicVouchers = await _context.Vouchers.AsNoTracking()
                .Where(v => (v.TrangThai == null || v.TrangThai == 1)
                         && (!v.NgayBatDau.HasValue || v.NgayBatDau <= now)
                         && (!v.NgayKetThuc.HasValue || v.NgayKetThuc >= now)
                         && (!v.SoLuong.HasValue || (v.SoLuongDaDung ?? 0) < v.SoLuong))
                .ToListAsync();

            var myCodes = kh != null ? kh.MaCodes : new List<Voucher>();
            var allVouchers = myCodes.Concat(publicVouchers)
                .GroupBy(v => v.MaCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

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
                var err = VoucherCartLogic.ValidateAndCalculate(v, uses, subTotal, cartIds, ngayDichVu, now, maRank, rankCap, out var tienGiam, out _);

                decimal displayDiscount = tienGiam;
                if (displayDiscount == 0)
                {
                    if (v.SoTienGiam.HasValue && v.SoTienGiam > 0)
                        displayDiscount = v.SoTienGiam.Value;
                    else if (v.PhanTramGiam.HasValue && v.PhanTramGiam > 0 && subTotal > 0)
                        displayDiscount = Math.Round(subTotal * v.PhanTramGiam.Value / 100m, 0);
                }

                string desc = v.MoTa ?? "";
                if (v.GiaTriToiThieu.HasValue && v.GiaTriToiThieu > 0 && subTotal < v.GiaTriToiThieu.Value)
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

        /// <summary>Xem trước tổng giảm khi xếp chồng tối đa 2 mã (thứ tự trong <paramref name="codes"/>).</summary>
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

            if (parts.Count == 0)
                return Ok(new { totalDiscount = 0m, finalAmount = subTotal, codes = Array.Empty<string>() });

            var kh = await _context.Khachhangs.AsNoTracking()
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);
            if (kh == null)
                return Ok(new { totalDiscount = 0m, finalAmount = subTotal, codes = parts });

            var owned = new HashSet<string>(kh.MaCodes.Select(x => x.MaCode), StringComparer.OrdinalIgnoreCase);
            foreach (var c in parts)
            {
                if (!owned.Contains(c))
                    return BadRequest(new { message = $"Bạn không sở hữu mã: {c}" });
            }

            var now = VnTimeHelper.Now();
            var ngayDichVu = ngayGioHen ?? now;
            var cartIds = string.IsNullOrWhiteSpace(serviceIds)
                ? new List<string>()
                : serviceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            string? maRank = kh.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                rankCap = rank?.PhanTramGiamGia;
            }

            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(
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
                return BadRequest(new { message = err });

            return Ok(new { totalDiscount = totalGiam, finalAmount = sau, codes = parts });
        }

        [HttpPost("create-payment")]
        public async Task<IActionResult> CreatePayment([FromBody] PaymentInformationRequest model)
        {
            // 1. NẾU LÀ THANH TOÁN VNPAY
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
                vnpay.AddRequestData("vnp_ExpireDate", vnpayNow.AddMinutes(15).ToString("yyyyMMddHHmmss")); // Đã bổ sung ExpireDate
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

            // 2. NẾU LÀ THANH TOÁN MOMO
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

            // Tạo chữ ký MoMo
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
            // LỖI CS4032 ĐƯỢC SỬA TẠI ĐÂY (Sử dụng await trong async Task)
            var response = await client.PostAsJsonAsync(endpoint, requestData);
            var result = await response.Content.ReadFromJsonAsync<MomoResponse>();

            if (result != null && result.resultCode == 0) return Ok(new { url = result.payUrl });
            return BadRequest(result?.message ?? "Lỗi MoMo");
        }

        [HttpPost("save-booking-final")]
        public async Task<IActionResult> SaveBooking([FromBody] BookingRequest model, [FromQuery] string paymentMode, [FromQuery] string? maStylist)
        {
            // Tạo chiến lược thực thi để chống lỗi mất kết nối DB
            var strategy = _context.Database.CreateExecutionStrategy();

            // BẮT BUỘC PHẢI CÓ <IActionResult> Ở ĐÂY ĐỂ C# KHÔNG BÁO LỖI CS8031
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                // Khởi tạo Transaction bằng hàm Async bên trong ExecutionStrategy
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    string currentMaKh = model.MaKh;

                    // 1. XỬ LÝ KHÁCH HÀNG VÃNG LAI
                    if (currentMaKh == "GUEST" && !string.IsNullOrEmpty(model.Phone))
                    {
                        var existingKhach = await _context.Khachhangs.FirstOrDefaultAsync(x => x.Sdt == model.Phone);

                        if (existingKhach != null)
                        {
                            currentMaKh = existingKhach.MaKh;
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

                    string shortMaLd = model.BookingId.Length > 10
                        ? model.BookingId.Substring(model.BookingId.Length - 10)
                        : model.BookingId;

                    decimal subtotal = model.Services.Sum(s => s.Price);

                    // 2. XỬ LÝ VOUCHER
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
                    decimal tongSauGiam = subtotal;

                    if (codesList.Count > 0)
                    {
                        if (codesList.Count != codesList.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = "Không được dùng trùng một mã voucher." });
                        }

                        var khForRank = await _context.Khachhangs.AsNoTracking()
                            .Include(k => k.MaCodes)
                            .FirstOrDefaultAsync(k => k.MaKh == currentMaKh);

                        if (khForRank == null)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = "Không tìm thấy khách hàng." });
                        }

                        var ownedCodes = new HashSet<string>(khForRank.MaCodes.Select(x => x.MaCode), StringComparer.OrdinalIgnoreCase);
                        foreach (var c in codesList)
                        {
                            if (!ownedCodes.Contains(c))
                            {
                                await transaction.RollbackAsync();
                                return BadRequest(new { message = $"Bạn không sở hữu mã voucher: {c}" });
                            }
                        }

                        string? maRank = khForRank.MaRank;
                        int? rankCap = null;
                        if (!string.IsNullOrWhiteSpace(maRank))
                        {
                            var rank = await _context.Hangthanhviens.AsNoTracking()
                                .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                            rankCap = rank?.PhanTramGiamGia;
                        }

                        var cartIds = model.Services.Select(s => s.Id).ToList();
                        var (vErr, _, sau) = await VoucherCartLogic.ValidateStackedAsync(
                            _context, codesList, currentMaKh, subtotal, cartIds,
                            model.NgayHen, VnTimeHelper.Now(), maRank, rankCap, maxVouchersAllowed: 1);

                        if (vErr != null)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = vErr });
                        }
                        tongSauGiam = sau;
                    }

                    // 3. TÍNH TOÁN THANH TOÁN
                    decimal total = tongSauGiam;
                    decimal daThanhToan = (paymentMode == "10") ? (total * 0.1m) : total;
                    int trangThaiLichDat = (paymentMode == "10") ? 1 : 2;


                    // 4. LƯU BẢNG LỊCH ĐẶT
                    int tongPhut = 0;
                    foreach (var item in model.Services)
                    {
                        if (item.Id.StartsWith("DV"))
                        {
                            var dv = await _context.Dichvus.FindAsync(item.Id);
                            tongPhut += dv?.ThoiGianLam ?? 30; // Mặc định 30p nếu không có dữ liệu
                        }
                        else if (item.Id.StartsWith("CB"))
                        {
                            // ĐÃ SỬA LỖI TẠI ĐÂY: Tính thời gian bằng cách cộng dồn các dịch vụ con trong Combo
                            var cb = await _context.Combos
                                .Include(c => c.Combo_DichVus)
                                .ThenInclude(cd => cd.DichVu)
                                .FirstOrDefaultAsync(c => c.MaCb == item.Id);

                            if (cb != null)
                            {
                                int comboDuration = cb.Combo_DichVus.Sum(ct => ct.DichVu.ThoiGianLam ?? 30);
                                tongPhut += comboDuration > 0 ? comboDuration : 60; // Mặc định 60p nếu Combo rỗng
                            }
                        }
                    }

                    // SỬA LỖI UTC DATE TẠI ĐÂY (Ép về giờ VN để tránh lỗi trên server Azure)
                    var localTime = model.NgayHen.ToUniversalTime().AddHours(7);

                    var lichDat = new Lichdat
                    {
                        MaLd = shortMaLd,
                        MaKh = currentMaKh,
                        NgayHen = DateOnly.FromDateTime(localTime),
                        GioBatDau = TimeOnly.FromDateTime(localTime),

                        // Tính giờ kết thúc (Dựa trên tổng thời gian làm đã tính ở bước trước)
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
                        // Tiền đã thu tại lúc tạo (daThanhToan) >= tổng phải trả (tongSauGiam) -> đã thanh toán đủ (2),
                        // ngược lại chỉ mới đặt cọc (1). Trước đây hardcode = 0 khiến hiển thị "Đã Hủy" dù đã thu tiền.
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

                    await _context.SaveChangesAsync();

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

                    // 9. LƯU VÀO DATABASE CHÍNH THỨC
                    await transaction.CommitAsync();

                    // Xuất hóa đơn điện tử
                    {
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
                            // LUÔN = 3 (chờ hoàn tất dịch vụ), bất kể cọc hay thanh toán full lúc đặt.
                            TrangThai = 3
                        });
                        await _context.SaveChangesAsync();
                    }

                    // 10. GỬI EMAIL QR SAU KHI LƯU THÀNH CÔNG
                    try
                    {
                        var khachHang = await _context.Khachhangs.FindAsync(currentMaKh);
                        string tenKhach = khachHang?.HoTen ?? "Khách hàng";
                        string emailNhan = !string.IsNullOrEmpty(model.Email) ? model.Email : khachHang?.Email;

                        if (!string.IsNullOrEmpty(emailNhan))
                        {
                            await _qrEmailService.SendBookingEmailAsync(emailNhan, tenKhach, shortMaLd, model.NgayHen, total);
                            Console.WriteLine("Đã gửi mã QR thành công tới email: " + emailNhan);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("LỖI GỬI EMAIL: " + ex.Message);
                    }

                    return Ok(new { message = "Thành công", maLd = shortMaLd });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return BadRequest("Lỗi: " + (ex.InnerException?.Message ?? ex.Message));
                }
            }); // Kết thúc ExecuteAsync<IActionResult>
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

        // Class hứng dữ liệu gửi lên từ Vue
        public class CheckStylistRequest
        {
            public string NgayHen { get; set; }
            public string GioBatDau { get; set; }
            public int TongThoiGian { get; set; } // Tổng số phút
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
                        .Select(y => y.MaNV.Trim()) // Trim luôn ở đây cho chắc chắn
                        .ToListAsync();
                }

                // 1. LẤY TẤT CẢ THỢ ĐI LÀM TRONG NGÀY
                var lichLamViecs = await _context.Lichlamviecs
                    .Include(l => l.MaNvNavigation)
                    .ThenInclude(nv => nv.MaLoaiNvNavigation)
                    .Where(l => l.NgayLamViec == ngayHen && l.TrangThai != 0 && l.TrangThai != 5 && l.MaNvNavigation.TrangThai == 1)
                    .ToListAsync();

                // 2. LẤY CHI TIẾT CÁC LỊCH ĐANG BẬN (Lấy cả giờ để báo cho khách)
                var cacLichBan = await _context.ChitietLichdats
                    .Where(ct => ct.MaLdNavigation.NgayHen == ngayHen
                                 && ct.MaLdNavigation.TrangThai != 0
                                 && ct.MaLdNavigation.GioBatDau < end
                                 && ct.MaLdNavigation.GioKetThuc > start)
                    .Select(ct => new {
                        MaNv = ct.MaNv,
                        GioBatDau = ct.MaLdNavigation.GioBatDau,
                        GioKetThuc = ct.MaLdNavigation.GioKetThuc
                    })
                    .ToListAsync();

                var result = new List<object>();

                // 3. XỬ LÝ TỪNG THỢ
                foreach (var lich in lichLamViecs)
                {
                    var nv = lich.MaNvNavigation;
                    bool isAvailable = true;
                    string reason = "";

                    bool dungCaLam = false;
                    string ca = lich.CaLam ?? "";
                    string gioLamThucTe = ca;

                    // Kiểm tra khớp ca
                    if ((ca == "Ca sáng (7h-17h)" || ca == "Ca ngày" || ca == "Ca sáng") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(17, 0)) dungCaLam = true;
                    else if ((ca == "Ca tối (13h-20h)" || ca == "Ca tối") && start >= new TimeOnly(13, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;
                    else if ((ca == "Cả ngày" || ca == "Full time") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;
                    else if (ca == "Ca sáng (Part-time)" && start >= new TimeOnly(7, 0) && end <= new TimeOnly(11, 0)) dungCaLam = true;
                    else if (ca == "Ca chiều (Part-time)" && start >= new TimeOnly(13, 0) && end <= new TimeOnly(17, 0)) dungCaLam = true;
                    else if (ca == "Ca tối (Part-time)" && start >= new TimeOnly(16, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;

                    // Đổi tên ca thành Giờ cụ thể để hiển thị cho khách dễ hiểu
                    if (ca == "Ca sáng (7h-17h)" || ca == "Ca ngày" || ca == "Ca sáng") gioLamThucTe = "07:00 - 17:00";
                    else if (ca == "Ca tối (13h-20h)" || ca == "Ca tối") gioLamThucTe = "13:00 - 20:00";
                    else if (ca == "Cả ngày" || ca == "Full time") gioLamThucTe = "07:00 - 20:00";
                    else if (ca == "Ca sáng (Part-time)") gioLamThucTe = "07:00 - 11:00";
                    else if (ca == "Ca chiều (Part-time)") gioLamThucTe = "13:00 - 17:00";
                    else if (ca == "Ca tối (Part-time)") gioLamThucTe = "16:00 - 20:00";

                    // Lấy lịch khách khác đang chặn thợ này (nếu có)
                    var lichKhet = cacLichBan.Where(b => b.MaNv == nv.MaNv).OrderByDescending(b => b.GioKetThuc).FirstOrDefault();

                    if (!dungCaLam)
                    {
                        isAvailable = false;
                        reason = $"Trái ca (Làm {gioLamThucTe})"; // Khách chọn 7h, thợ làm ca tối -> Báo "Trái ca (Làm 13:00 - 20:00)"
                    }
                    else if (lichKhet != null)
                    {
                        isAvailable = false;
                        reason = $"Bận khách ({lichKhet.GioBatDau?.ToString(@"hh\:mm")} - {lichKhet.GioKetThuc?.ToString(@"hh\:mm")})"; // Báo rõ thợ bận từ mấy giờ đến mấy giờ
                    }

                    var ratingVal = _context.DanhgiaDichvus.Where(dg => _context.HoaDons.Any(hd => dg.MaHd == hd.MaHd && _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))).Average(dg => (double?)dg.SoSao);

                    bool isFav = favoriteStylistIds.Contains(nv.MaNv.Trim());
                    result.Add(new
                    {
                        id = nv.MaNv.Trim(), // Trim luôn ID trả về
                        name = nv.HoTenNv,
                        role = nv.MaLoaiNvNavigation?.TenLoai ?? "Stylist",
                        img = nv.HinhAnh ?? "/images/no-image.png",
                        rating = ratingVal != null ? Math.Round(ratingVal.Value, 1) : 5.0,
                        isAvailable = isAvailable,
                        unAvailableReason = reason,
                        caLam = lich.CaLam,
                        isFavorite = isFav // TRẢ VỀ CỜ NÀY CHO VUE
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System.Security.Cryptography;
using System.Text;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThanhToanTongController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IQrEmailService _qrEmailService;

        public ThanhToanTongController(MinhTamHairContext context, IConfiguration configuration, IQrEmailService qrEmailService)
        {
            _context = context;
            _configuration = configuration;
            _qrEmailService = qrEmailService;
        }

        // TẢI DANH SÁCH DỊCH VỤ 
        [HttpGet("get-services")]
        public async Task<IActionResult> GetServices()
        {
            try
            {
                var services = await _context.Dichvus
                    .Where(s => s.TrangThai == 1)
                    .Select(s => new
                    {
                        id = s.MaDv,
                        name = s.TenDv,
                        price = s.Gia,
                        originalPrice = s.Gia,
                        duration = s.ThoiGianLam ?? 30,
                        icon = s.Hinhanhs
                    })
                    .ToListAsync();

                return Ok(new { success = true, services = services });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi lấy dịch vụ: " + ex.Message });
            }
        }


        //TẢI DANH SÁCH SẢN PHẨM & BIẾN THỂ (CÓ LỌC)
        [HttpGet("get-products")]
        public async Task<IActionResult> GetProducts([FromQuery] string? keyword = "", [FromQuery] string? categoryId = "")
        {
            try
            {
                var query = _context.SanPhams.Where(sp => sp.TrangThai == 1);

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

        // TẢI DANH SÁCH VOUCHER 
        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal, [FromQuery] string? itemIds)
        {
            var kh = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);

            if (kh == null) return Ok(Array.Empty<object>());

            var now = DateTime.Now;
            var cartIds = string.IsNullOrWhiteSpace(itemIds) ? new List<string>() : itemIds.Split(',').ToList();

            string? maRank = kh.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank);
                rankCap = rank?.PhanTramGiamGia;
            }

            var usageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in kh.MaCodes.Select(v => v.MaCode))
            {
                usageMap[c] = await _context.HoaDons.CountAsync(h => h.MaKh == maKH && h.MaCode == c);
            }

            var result = new List<object>();
            foreach (var v in kh.MaCodes)
            {
                var uses = usageMap.GetValueOrDefault(v.MaCode, 0);

                var err = VoucherCartLogic.ValidateAndCalculate(v, uses, subTotal, cartIds, now, now, maRank, rankCap, out var tienGiam, out _);
                if (err == null)
                {
                    result.Add(new
                    {
                        id = v.MaCode,
                        code = v.MaCode,
                        discount = tienGiam,
                        desc = v.MoTa,
                        expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : ""
                    });
                }
            }
            return Ok(result);
        }

        // TÍNH TOÁN TRƯỚC SỐ TIỀN ĐƯỢC GIẢM
        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(string maKH, [FromQuery] string? codes, [FromQuery] decimal subTotal, [FromQuery] string? itemIds)
        {
            var parts = string.IsNullOrWhiteSpace(codes) ? new List<string>() : codes.Split(',').Select(VoucherCartLogic.NormalizeMaCode).Take(2).ToList();
            if (parts.Count == 0) return Ok(new { totalDiscount = 0m, finalAmount = subTotal });

            var kh = await _context.Khachhangs.Include(k => k.MaCodes).FirstOrDefaultAsync(k => k.MaKh == maKH);
            if (kh == null) return Ok(new { totalDiscount = 0m, finalAmount = subTotal });

            string? maRank = kh.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank);
                rankCap = rank?.PhanTramGiamGia;
            }

            var cartIds = string.IsNullOrWhiteSpace(itemIds) ? new List<string>() : itemIds.Split(',').ToList();

            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(_context, parts, maKH, subTotal, cartIds, DateTime.Now, DateTime.Now, maRank, rankCap);

            if (err != null) return BadRequest(new { message = err });
            return Ok(new { totalDiscount = totalGiam, finalAmount = sau, codes = parts });
        }

        // LƯU TẤT CẢ VÀO CƠ SỞ DỮ LIỆU
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
                    string maHd = "TH" + DateTime.Now.ToString("HHmmssff");

                    // BƯỚC 1: TẠO KHÁCH HÀNG (Nếu chưa có)
                    string safeKhachHang = req.MaKh;
                    if (string.IsNullOrWhiteSpace(safeKhachHang) || safeKhachHang == "GUEST")
                    {
                        if (!string.IsNullOrEmpty(req.Phone))
                        {
                            var existingKhach = await _context.Khachhangs.FirstOrDefaultAsync(x => x.Sdt == req.Phone);
                            if (existingKhach != null) { safeKhachHang = existingKhach.MaKh; }
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
                                    NgayTao = DateTime.Now,
                                    DiemTichLuy = 0,
                                    MaRank = "RANK01"
                                });
                                await _context.SaveChangesAsync();
                            }
                        }
                        else { safeKhachHang = null; }
                    }

                    // BƯỚC 2: TẠO LỊCH ĐẶT & CHI TIẾT
                    string maLd = null;
                    if (req.Services != null && req.Services.Any())
                    {
                        // Sinh 10 số random liên tục từ thời gian + mili-giây
                        maLd = DateTime.Now.ToString("yyHHmmssff");

                        int tongThoiGianPhut = req.Services.Sum(s => s.Duration);
                        var localTime = (req.BookingDate ?? DateTime.Now).ToLocalTime();
                        var gioBatDau = TimeOnly.FromDateTime(localTime);
                        var gioKetThuc = gioBatDau.AddMinutes(tongThoiGianPhut);

                        _context.Lichdats.Add(new Lichdat
                        {
                            MaLd = maLd,
                            MaKh = safeKhachHang,
                            NgayHen = DateOnly.FromDateTime(localTime),
                            GioBatDau = gioBatDau,
                            GioKetThuc = gioKetThuc,
                            TrangThai = 0,
                        });
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

                    // BƯỚC 3: TẠO HÓA ĐƠN 
                    string? maCodeChinh = req.VoucherIds != null && req.VoucherIds.Any(v => !string.IsNullOrWhiteSpace(v))
                    ? req.VoucherIds.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                    : null;
                    var hoaDon = new HoaDon
                    {
                        MaHd = maHd,
                        MaKh = safeKhachHang,
                        MaLd = maLd,
                        NgayTao = DateTime.Now,
                        TongTien = req.GrandTotal,
                        ThanhTien = req.GrandTotal,
                        TienDaTra = req.PaidAmount,
                        TrangThai = 1,
                        MaCode = maCodeChinh
                    };
                    _context.HoaDons.Add(hoaDon);
                    await _context.SaveChangesAsync();

                    // BƯỚC 4: TẠO CHI TIẾT SẢN PHẨM & GIAO HÀNG
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
                            if (bienthe != null && bienthe.SoLuongTon >= p.SoLuong)
                            {
                                bienthe.SoLuongTon -= p.SoLuong;
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

                    // BƯỚC 5: TẠO THANH TOÁN & TRỪ VOUCHER
                    string phuongThuc = "COD";
                    if (req.PaidAmount > 0)
                    {
                        phuongThuc = (req.CheckoutType == "CHI_MUA_SAN_PHAM") ? req.ProductPaymentMethod : req.ServicePaymentMethod;
                    }

                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = maHd,
                        PhuongThuc = (phuongThuc ?? "COD").ToUpper(),
                        SoTien = req.PaidAmount,
                        TrangThai = req.PaidAmount > 0 ? 1 : 0
                    });

                    if (req.VoucherIds != null)
                    {
                        foreach (var code in req.VoucherIds)
                        {
                            await VoucherCartLogic.TryIncrementUsageAsync(_context, code);
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    if (!string.IsNullOrEmpty(maLd)) // Chỉ gửi nếu đơn hàng này có sinh ra Mã Lịch Đặt
                    {
                        try
                        {
                            var khachHang = await _context.Khachhangs.FindAsync(safeKhachHang);

                            // Ưu tiên lấy tên và email khách vừa nhập trên form, nếu không có thì lấy trong DB
                            string tenKhach = !string.IsNullOrWhiteSpace(req.CustomerName) ? req.CustomerName : (khachHang?.HoTen ?? "Khách hàng");
                            string emailNhan = !string.IsNullOrWhiteSpace(req.Email) ? req.Email : khachHang?.Email;

                            if (!string.IsNullOrWhiteSpace(emailNhan) && req.BookingDate.HasValue)
                            {
                                // Gọi service gửi Email
                                await _qrEmailService.SendBookingEmailAsync(
                                    emailNhan,
                                    tenKhach,
                                    maLd,
                                    req.BookingDate.Value,
                                    req.GrandTotal
                                );
                                Console.WriteLine("Đã gửi mã QR thành công tới email: " + emailNhan);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("====== LỖI GỬI EMAIL QR ======\n" + ex.Message);
                        }
                    }

                    // BƯỚC 6: TẠO LINK VNPAY HOẶC MOMO
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
                            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
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
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "Lỗi tạo link MoMo: " + (result?.message ?? "Không xác định")
                                });
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
        // ===============================================
        // API 6: CẬP NHẬT TRẠNG THÁI & LẤY CHI TIẾT BIÊN LAI
        // ===============================================
        [HttpGet("get-receipt")]
        public async Task<IActionResult> GetReceipt([FromQuery] string maHd, [FromQuery] string? vnp_ResponseCode = null)
        {
            if (string.IsNullOrEmpty(maHd)) return BadRequest(new { success = false, message = "Thiếu mã hóa đơn." });

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(h => h.ThongtinGiaohangs)
                    .Include(h => h.Thanhtoans)
                    .FirstOrDefaultAsync(h => h.MaHd == maHd);

                if (hoaDon == null) return NotFound(new { success = false, message = "Không tìm thấy hóa đơn" });

                // 1. CẬP NHẬT TRẠNG THÁI NẾU TỪ VNPAY VỀ THÀNH CÔNG (Mã 00)
                if (vnp_ResponseCode == "00" && hoaDon.TrangThai == 0)
                {
                    hoaDon.TrangThai = 1;
                    var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                    if (thanhToan != null) thanhToan.TrangThai = 1;

                    if (!string.IsNullOrEmpty(hoaDon.MaLd))
                    {
                        var lichDat = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                        if (lichDat != null && lichDat.TrangThai == 0) lichDat.TrangThai = 1;
                    }
                    await _context.SaveChangesAsync();
                }

                // 2. GOM DỮ LIỆU ĐỂ TRẢ VỀ FRONTEND
                var khachHang = await _context.Khachhangs.FindAsync(hoaDon.MaKh);

                object bookingInfo = null;
                if (!string.IsNullOrEmpty(hoaDon.MaLd))
                {
                    var ld = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                    var chiTietLd = await _context.ChitietLichdats.Where(c => c.MaLd == hoaDon.MaLd).ToListAsync();

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
                        gioBatDau = ld.GioBatDau?.ToString(@"hh\:mm"),
                        services = servicesList
                    };
                }

                var chiTietSp = await _context.ChitietHoadonSps.Where(c => c.MaHd == maHd).ToListAsync();
                var productsList = new List<object>();
                foreach (var ct in chiTietSp)
                {
                    var bt = await _context.BienTheSanphams.FindAsync(ct.MaBienThe);
                    var sp = bt != null ? await _context.SanPhams.FindAsync(bt.MaSanPham) : null;
                    productsList.Add(new { name = $"{sp?.TenSp} - {bt?.TenBienThe}", qty = ct.SoLuong, price = ct.DonGiaLucBan, total = ct.ThanhTien });
                }

                var delivery = hoaDon.ThongtinGiaohangs.FirstOrDefault();

                var receipt = new
                {
                    maHd = hoaDon.MaHd,
                    ngayTao = hoaDon.NgayTao?.ToString("dd/MM/yyyy HH:mm"),
                    khachHang = khachHang?.HoTen ?? "Khách vãng lai",
                    sdt = khachHang?.Sdt ?? "",
                    tongTien = hoaDon.TongTien,
                    thanhTien = hoaDon.ThanhTien,
                    tienDaTra = hoaDon.TienDaTra,
                    trangThai = hoaDon.TrangThai == 1 ? "Đã thanh toán" : "Chưa thanh toán",
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
        public DeliveryInfoDto? DeliveryInfo { get; set; } // Cho phép null nếu không giao hàng
        public List<string>? VoucherIds { get; set; }

        public decimal GrandTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ShippingFee { get; set; }

        public string? PaymentMode { get; set; }
        public string? ServicePaymentMethod { get; set; }
        public string? ProductPaymentMethod { get; set; }
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
}
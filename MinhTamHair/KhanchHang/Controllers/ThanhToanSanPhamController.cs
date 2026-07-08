using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System.Security.Cryptography;
using System.Text;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThanhToanSanPhamController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        public ThanhToanSanPhamController(MinhTamHairContext context, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _configuration = configuration;
            _scopeFactory = scopeFactory;
        }
        [HttpGet("search-products-variants")]
        public async Task<IActionResult> SearchProductsWithVariants([FromQuery] string? keyword = "")
        {
            try
            {
                var query = _context.SanPhams.Where(sp => sp.TrangThai == 1);

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    query = query.Where(sp => sp.TenSp.Contains(keyword));
                }
                query = query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1));
                var result = await query.Select(sp => new
                {
                    MaSp = sp.MaSp,
                    TenSp = sp.TenSp,
                    ThuongHieu = sp.Thuonghieu,

                    Bienthes = sp.BienTheSanphams
                                 .Where(bt => bt.TrangThai == 1)
                                 .Select(bt => new
                                 {
                                     MaBienThe = bt.MaBienThe,
                                     TenBienThe = bt.TenBienThe,
                                     GiaBan = bt.GiaBan,
                                     SoLuongTon = bt.SoLuongTon,
                                     HinhAnh = !string.IsNullOrEmpty(bt.HinhAnhDaiDien) ? bt.HinhAnhDaiDien : sp.HinhAnhDaiDien
                                 })
                })
                .Take(20)
                .ToListAsync();

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi tải sản phẩm: " + ex.Message });
            }
        }
        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal, [FromQuery] string? productIds)
        {
            await VoucherCartLogic.AutoDisableExpiredVouchersAsync(_context, DateTime.Now);
            var kh = await _context.Khachhangs.Include(k => k.MaCodes).FirstOrDefaultAsync(k => k.MaKh == maKH);

            var now = DateTime.Now;
            var cartIds = string.IsNullOrWhiteSpace(productIds) ? new List<string>() : productIds.Split(',').ToList();

            string? maRank = kh?.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
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
                var err = VoucherCartLogic.ValidateAndCalculate(v, uses, subTotal, cartIds, now, now, maRank, rankCap, out var tienGiam, out _);

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
        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(string maKH, [FromQuery] string? codes, [FromQuery] decimal subTotal, [FromQuery] string? productIds)
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

            var cartIds = string.IsNullOrWhiteSpace(productIds) ? new List<string>() : productIds.Split(',').ToList();
            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(_context, parts, maKH, subTotal, cartIds, DateTime.Now, DateTime.Now, maRank, rankCap);

            if (err != null) return BadRequest(new { message = err });
            return Ok(new { totalDiscount = totalGiam, finalAmount = sau, codes = parts });
        }

        // 4. API LƯU ĐƠN HÀNG SẢN PHẨM MỚI TẠO 
        [HttpPost("submit-product-order")]
        public async Task<IActionResult> SubmitProductOrder([FromBody] ProductCheckoutRequest req)
        {
            if (req == null || req.Products == null || !req.Products.Any())
            {
                return BadRequest(new { success = false, message = "Dữ liệu đơn hàng trống hoặc không hợp lệ!" });
            }
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    string maHd = "SP" + DateTime.Now.ToString("yyMMddHHmmss");
                    decimal subTotal = req.Products.Sum(p => p.GiaBan * p.SoLuong);
                    decimal tongSauGiam = subTotal;
                    string? maCodeChinh = req.VoucherIds?.FirstOrDefault();

                    string safeKhachHang = (string.IsNullOrWhiteSpace(req.MaKh) || req.MaKh == "GUEST") ? null : req.MaKh;
                    string safePayment = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "cod" : req.PaymentMethod.ToLower();

                    if (req.VoucherIds != null && req.VoucherIds.Any() && safeKhachHang != null)
                    {
                        var cartIds = req.Products.Select(p => p.MaBienThe).ToList();
                        var (vErr, _, sau) = await VoucherCartLogic.ValidateStackedAsync(
                            _context, req.VoucherIds, safeKhachHang, subTotal, cartIds, DateTime.Now, DateTime.Now, null, null);

                        if (vErr == null) tongSauGiam = sau;
                    }

                    decimal finalTotal = tongSauGiam + req.ShippingFee;

                    // Lưu hóa đơn
                    var hoaDon = new HoaDon
                    {
                        MaHd = maHd,
                        MaKh = safeKhachHang,
                        NgayTao = DateTime.Now,
                        TongTien = subTotal + req.ShippingFee,
                        ThanhTien = finalTotal,
                        TienDaTra = safePayment == "cod" ? 0 : finalTotal,
                        TrangThai = 1,
                        MaCode = maCodeChinh
                    };
                    _context.HoaDons.Add(hoaDon);

                    // Lưu thông tin giao hàng
                    var thongTinGH = new ThongtinGiaohang
                    {
                        MaHd = maHd,
                        TenNguoiNhan = req.HoTen ?? "Khách hàng",
                        SdtNhan = req.Phone ?? "",
                        DiaChiNhan = req.Address ?? "",
                        Email = req.Email ?? "",
                        PhiShip = req.ShippingFee,
                        TrangThaiGiaoHang = 1
                    };
                    _context.ThongtinGiaohangs.Add(thongTinGH);

                    // Lưu chi tiết và trừ tồn kho, 
                    foreach (var p in req.Products)
                    {
                        if (string.IsNullOrEmpty(p.MaBienThe)) continue;

                        _context.ChitietHoadonSps.Add(new ChitietHoadonSp
                        {
                            MaHd = maHd,
                            MaBienThe = p.MaBienThe,
                            SoLuong = p.SoLuong,
                            DonGiaLucBan = p.GiaBan,
                            ThanhTien = p.GiaBan * p.SoLuong
                        });

                        var bienthe = await _context.BienTheSanphams.FindAsync(p.MaBienThe);
                        if (bienthe != null && bienthe.SoLuongTon >= p.SoLuong)
                        {
                            int tonTruoc = bienthe.SoLuongTon ?? 0;
                            bienthe.SoLuongTon -= p.SoLuong;
                            int tonSau = bienthe.SoLuongTon ?? 0;

                            _context.LichSuTonKhos.Add(new LichSuTonKho
                            {
                                MaBienThe = p.MaBienThe,
                                LoaiBienDong = "BAN",
                                SoLuongThayDoi = -p.SoLuong, // âm vì là xuất kho bán ra
                                TonTruoc = tonTruoc,
                                TonSau = tonSau,
                                MaChungTu = maHd, // liên kết với HOA_DON để đối soát hóa đơn điện tử sau này
                                MaNv = null, // đơn hàng khách tự đặt online, không qua nhân viên
                                GhiChu = "Ban hang qua website - Don " + maHd,
                                ThoiGian = DateTime.Now
                            });
                        }
                    }

                    // Lưu thanh toán
                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = maHd,
                        PhuongThuc = safePayment.ToUpper(),
                        SoTien = finalTotal,
                        TrangThai = safePayment == "cod" ? 0 : 1
                    });

                    await _context.SaveChangesAsync();

                    if (req.VoucherIds != null)
                    {
                        foreach (var code in req.VoucherIds)
                        {
                            await VoucherCartLogic.TryIncrementUsageAsync(_context, code);
                        }
                    }

                    await transaction.CommitAsync();

                    // PHẦN XUẤT HÓA ĐƠN ĐIỆN TỬ
                    // Luật yêu cầu MỌI giao dịch bán hàng đều phải xuất hóa đơn, không phụ thuộc khách có yêu cầu hay không.
                    // req.InvoiceInfo chỉ dùng để xác định đây là hóa đơn cá nhân hay doanh nghiệp (có MST) và thông tin xuất hóa đơn riêng (nếu khác thông tin giao hàng).
                    {
                        bool laDoanhNghiep = req.InvoiceInfo?.LaDoanhNghiep ?? false;
                        var maHddt = "HD" + DateTime.Now.ToString("yyMMddHHmmss");
                        _context.HoaDonDienTus.Add(new HoaDonDienTu
                        {
                            MaHddt = maHddt,
                            MaHd = maHd,
                            LoaiKhach = laDoanhNghiep ? (byte)1 : (byte)0,
                            TenNguoiMua = laDoanhNghiep ? req.InvoiceInfo?.TenCongTy : (req.HoTen ?? "Khach hang"),
                            Mst = laDoanhNghiep ? req.InvoiceInfo?.Mst : null,
                            DiaChiXuatHd = req.InvoiceInfo?.DiaChiXuatHD ?? req.Address,
                            EmailNhanHd = req.InvoiceInfo?.EmailNhanHD ?? req.Email,
                            TrangThai = 0
                        });
                        await _context.SaveChangesAsync();

                        _ = Task.Run(async () =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                            await eInvoiceService.PhatHanhHoaDonAsync(maHddt);
                        });
                    }


                    string paymentUrl = null;

                    if (safePayment == "vnpay")
                    {
                        var vnpay = new VnPayLibrary();
                        vnpay.AddRequestData("vnp_Version", "2.1.0");
                        vnpay.AddRequestData("vnp_Command", "pay");
                        vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"] ?? throw new Exception("Thiếu TmnCode"));
                        vnpay.AddRequestData("vnp_Amount", ((long)(finalTotal * 100)).ToString());
                        vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                        vnpay.AddRequestData("vnp_CurrCode", "VND");
                        vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                        vnpay.AddRequestData("vnp_Locale", "vn");
                        vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan don hang san pham " + maHd);
                        vnpay.AddRequestData("vnp_OrderType", "other");
                        vnpay.AddRequestData("vnp_ReturnUrl", _configuration["Vnpay:ReturnUrlSanPham"]);
                        vnpay.AddRequestData("vnp_TxnRef", maHd);

                        paymentUrl = vnpay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"], _configuration["Vnpay:HashSecret"]);
                    }
                    else if (safePayment == "momo")
                    {
                        var partnerCode = _configuration["Momo:PartnerCode"];
                        var accessKey = _configuration["Momo:AccessKey"];
                        var secretKey = _configuration["Momo:SecretKey"] ?? throw new Exception("Thiếu MoMo SecretKey");
                        var endpoint = _configuration["Momo:ApiEndpoint"];
                        var redirectUrl = _configuration["Momo:ReturnUrlSanPham"];
                        var ipnUrl = _configuration["Momo:NotifyUrl"];

                        long amount = (long)finalTotal;
                        string orderInfo = "Thanh toan don hang SP Minh Tam Hair";
                        string rawHash = $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={maHd}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={maHd}&requestType=captureWallet";
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
                            return BadRequest(new { success = false, message = "Lỗi tạo link MoMo: " + (result?.message ?? "Chưa xác định") });
                        }
                    }

                    return Ok(new { success = true, isOnlinePayment = !string.IsNullOrEmpty(paymentUrl), url = paymentUrl });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { success = false, message = "Lỗi khi lưu đơn hàng: " + ex.Message });
                }
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

    }
}
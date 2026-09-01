using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using MinhTamHair.Services.TimeHelper;
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

        [HttpGet("confirm-payment")]
        public async Task<IActionResult> ConfirmPayment(
            [FromQuery] string orderId,
            [FromQuery] string? vnp_ResponseCode = null,
            [FromQuery] string? resultCode = null)
        {
            if (string.IsNullOrEmpty(orderId))
                return BadRequest(new { success = false, message = "Thiếu mã đơn hàng." });

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(h => h.Thanhtoans)
                    .FirstOrDefaultAsync(h => h.MaHd == orderId);

                if (hoaDon == null)
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng." });

                bool isPaymentSuccess = (vnp_ResponseCode == "00") || (resultCode == "0");
                var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                if (isPaymentSuccess && thanhToan != null && thanhToan.TrangThai != 1)
                {
                    thanhToan.TrangThai = 1;
                    await _context.SaveChangesAsync();

                    // ĐÃ ĐỔI NGHIỆP VỤ: không còn phát hành hóa đơn điện tử ngay khi thanh toán online
                    // thành công nữa. Dù thanh toán 100% (kể cả online) hay COD, hóa đơn điện tử luôn
                    // chờ nhân viên xác nhận đơn "Đã hoàn thành/Đã giao hàng" (TrangThai = 3) tại
                    // HoaDonController.UpdateStatus mới được phát hành. Bản ghi HoaDonDienTu vẫn giữ
                    // nguyên TrangThai = 0 (nháp) chờ tới lúc đó.

                    return Ok(new { success = true, message = "Đã xác nhận thanh toán." });
                }

                // Nếu callback báo thất bại/hủy, hoặc đơn đã được xác nhận từ trước, không làm gì thêm.
                return Ok(new { success = true, message = "Không có thay đổi (đã xử lý trước đó hoặc thanh toán không thành công)." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("search-products-variants")]
        public async Task<IActionResult> SearchProductsWithVariants([FromQuery] string? keyword = "")
        {
            try
            {
                var query = _context.SanPhams
                    .AsNoTracking()
                    .Where(sp => sp.TrangThai == 1);

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    query = query.Where(sp => sp.TenSp != null && sp.TenSp.Contains(keyword));
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

            var kh = await _context.Khachhangs.AsNoTracking().Include(k => k.MaCodes).FirstOrDefaultAsync(k => k.MaKh == maKH);

            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            var cartIds = string.IsNullOrWhiteSpace(productIds) ? new List<string>() : productIds.Split(',').ToList();

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

            var voucherCodes = allVouchers.Select(v => v.MaCode).ToList();
            var usageCounts = await _context.HoaDons
                .AsNoTracking()
                .Where(h => h.MaKh == maKH && h.MaCode != null && voucherCodes.Contains(h.MaCode))
                .GroupBy(h => h.MaCode!)
                .Select(g => new { MaCode = g.Key, Count = g.Count() })
                .ToListAsync();

            var usageMap = usageCounts.ToDictionary(x => x.MaCode, x => x.Count, StringComparer.OrdinalIgnoreCase);

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
                    expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : "Vĩnh viễn"
                });
            }
            return Ok(result);
        }
        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(string maKH, [FromQuery] string? codes, [FromQuery] decimal subTotal, [FromQuery] string? productIds)
        {
            var parts = string.IsNullOrWhiteSpace(codes) ? new List<string>() : codes.Split(',').Select(VoucherCartLogic.NormalizeMaCode).Take(1).ToList();

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

            decimal memberDiscount = subTotal * ((rankCap ?? 0) / 100m);
            decimal afterMemberDiscount = subTotal - memberDiscount;

            var cartIds = string.IsNullOrWhiteSpace(productIds) ? new List<string>() : productIds.Split(',').ToList();
            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(_context, parts, maKH, subTotalSauKhiGiamRank, cartIds, MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(), MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(), maRank, rankCap, maxVouchersAllowed: 1);

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

        // 4. API LƯU ĐƠN HÀNG SẢN PHẨM MỚI TẠO 
        [HttpPost("submit-product-order")]
        public async Task<IActionResult> SubmitProductOrder([FromBody] ProductCheckoutRequest req)
        {
            var vnNow = VnTimeHelper.Now();
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
                    foreach (var p in req.Products)
                    {
                        if (string.IsNullOrEmpty(p.MaBienThe)) continue;

                        var bientheCheck = await _context.BienTheSanphams
                                                         .AsNoTracking()
                                                         .FirstOrDefaultAsync(x => x.MaBienThe == p.MaBienThe);

                        if (bientheCheck == null)
                        {
                            return BadRequest(new { success = false, message = $"Không tìm thấy sản phẩm có mã {p.MaBienThe}." });
                        }

                        int tonKho = bientheCheck.SoLuongTon ?? 0;

                        // Nếu khách mua nhiều hơn số lượng trong kho -> Báo lỗi và hoàn tác giao dịch
                        if (p.SoLuong > tonKho)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                errorCode = "PRODUCT_OUT_OF_STOCK",
                                outOfStockProductId = p.MaBienThe,
                                message = $"Sản phẩm '{bientheCheck.TenBienThe}' vượt quá số lượng tồn kho. Hiện chỉ còn {tonKho} sản phẩm, vui lòng cập nhật lại giỏ hàng."
                            });
                        }
                    }

                    string maHd = "SP" + vnNow.ToString("ddHHmmss");
                    decimal subTotal = req.Products.Sum(p => p.GiaBan * p.SoLuong);
                    decimal tongSauGiam = subTotal;
                    string? maCodeChinh = req.VoucherIds?.FirstOrDefault();

                    string safeKhachHang = (string.IsNullOrWhiteSpace(req.MaKh) || req.MaKh == "GUEST") ? null : req.MaKh;
                    string safePayment = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "cod" : req.PaymentMethod.ToLower();

                    // Xác định sớm COD/online để dùng chung cho HoaDon.TrangThai và HoaDonDienTu bên dưới.
                    bool laCod = string.Equals(safePayment, "cod", StringComparison.OrdinalIgnoreCase);
                    bool laOnline = string.Equals(safePayment, "vnpay", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(safePayment, "momo", StringComparison.OrdinalIgnoreCase);

                    // TÍNH LẠI PHẦN TRĂM GIẢM HẠNG THÀNH VIÊN
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

                    decimal tienGiamThanhVien = 0m;
                    if (phanTramGiamRank > 0 && subTotal > 0)
                    {
                        tienGiamThanhVien = Math.Round(subTotal * ((decimal)phanTramGiamRank.Value / 100m), 0);
                    }

                    decimal subTotalSauKhiGiamRank = subTotal - tienGiamThanhVien;
                    tongSauGiam = subTotalSauKhiGiamRank;

                    string? maRank = null;
                    int? rankCap = null;
                    if (safeKhachHang != null)
                    {
                        var khDb = await _context.Khachhangs.AsNoTracking().FirstOrDefaultAsync(k => k.MaKh == safeKhachHang);
                        if (khDb != null && !string.IsNullOrWhiteSpace(khDb.MaRank))
                        {
                            maRank = khDb.MaRank;
                            var rankObj = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                            rankCap = rankObj?.PhanTramGiamGia;
                        }
                    }

                    decimal memberDiscount = subTotal * ((rankCap ?? 0) / 100m);
                    decimal afterMemberDiscount = subTotal - memberDiscount;
                    tongSauGiam = afterMemberDiscount;

                    if (req.VoucherIds != null && req.VoucherIds.Any() && safeKhachHang != null)
                    {
                        var cartIds = req.Products.Select(p => p.MaBienThe).ToList();
                        var (vErr, _, sau) = await VoucherCartLogic.ValidateStackedAsync(
                            _context, req.VoucherIds.Take(1).ToList(), safeKhachHang, subTotalSauKhiGiamRank, cartIds, vnNow, vnNow, null, phanTramGiamRank, maxVouchersAllowed: 1);

                        if (vErr == null) tongSauGiam = sau;
                    }

                    decimal finalTotal = tongSauGiam + req.ShippingFee;

                    // Lưu hóa đơn
                    var hoaDon = new HoaDon
                    {
                        MaHd = maHd,
                        MaKh = safeKhachHang,
                        NgayTao = vnNow,
                        TongTien = subTotal + req.ShippingFee,
                        ThanhTien = finalTotal,
                        TienDaTra = safePayment == "cod" ? 0 : finalTotal,
                        TrangThai = 1,
                        MaCode = maCodeChinh,
                        PhanTramGiamVip = phanTramGiamRank
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

                    // Lưu chi tiết và trừ tồn kho
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
                        if (bienthe != null)
                        {
                            // 1. Dùng lệnh SQL ép Database tự trừ kho an toàn
                            int rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE BIENTHE_SANPHAM SET SoLuongTon = SoLuongTon - {p.SoLuong} WHERE MaBienThe = {p.MaBienThe} AND SoLuongTon >= {p.SoLuong}");

                            // 2. Nếu Database từ chối trừ (vì kho không đủ)
                            if (rowsAffected <= 0)
                            {
                                await transaction.RollbackAsync(); // Hủy toàn bộ giao dịch
                                return BadRequest(new { success = false, message = $"Sản phẩm '{bienthe.TenBienThe}' vừa bị khách khác mua hết hoặc không đủ số lượng!" });
                            }

                            // 3. Nếu thành công, ghi lại lịch sử tồn kho như cũ
                            int tonTruoc = bienthe.SoLuongTon ?? 0;
                            int tonSau = tonTruoc - p.SoLuong;

                            // Cập nhật biến trong C# để lưu lịch sử
                            // bienthe.SoLuongTon = tonSau; // B? qua d? ng?n EF Core ghi d ln Raw SQL 

                            _context.LichSuTonKhos.Add(new LichSuTonKho
                            {
                                MaBienThe = p.MaBienThe,
                                LoaiBienDong = "BAN",
                                SoLuongThayDoi = -p.SoLuong,
                                TonTruoc = tonTruoc,
                                TonSau = tonSau,
                                MaChungTu = maHd,
                                MaNv = null,
                                GhiChu = "Ban hang qua website - Don " + maHd,
                                ThoiGian = vnNow
                            });
                        }
                    }

                    // Lưu thanh toán
                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = maHd,
                        PhuongThuc = safePayment.ToUpper(),
                        SoTien = finalTotal,
                        TrangThai = (laCod || laOnline) ? 0 : 1
                    });

                    await _context.SaveChangesAsync();

                    if (req.VoucherIds != null)
                    {
                        foreach (var code in req.VoucherIds.Take(1))
                        {
                            await VoucherCartLogic.TryIncrementUsageAsync(_context, code, safeKhachHang);
                        }
                    }

                    await transaction.CommitAsync();
                    {
                        bool laDoanhNghiep = req.InvoiceInfo?.LaDoanhNghiep ?? false;
                        var maHddt = "HD" + vnNow.ToString("yyMMddHHmmss");
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
                        // Không còn gọi PhatHanhHoaDonAsync ở đây trong bất kỳ trường hợp nào.
                    }

                    string paymentUrl = null;

                    if (safePayment == "vnpay")
                    {
                        var vnpayNow = VnTimeHelper.Now();
                        var vnpay = new VnPayLibrary();
                        vnpay.AddRequestData("vnp_Version", "2.1.0");
                        vnpay.AddRequestData("vnp_Command", "pay");
                        vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"] ?? throw new Exception("Thiếu TmnCode"));
                        vnpay.AddRequestData("vnp_Amount", ((long)(finalTotal * 100)).ToString());
                        vnpay.AddRequestData("vnp_CreateDate", vnpayNow.ToString("yyyyMMddHHmmss"));
                        vnpay.AddRequestData("vnp_ExpireDate", vnpayNow.AddMinutes(15).ToString("yyyyMMddHHmmss"));
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

                    return Ok(new { success = true, isOnlinePayment = !string.IsNullOrEmpty(paymentUrl), url = paymentUrl, maHd = maHd });
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

        [HttpGet("get-receipt")]
        public async Task<IActionResult> GetReceipt(
            [FromQuery] string orderId,
            [FromQuery] string? vnp_ResponseCode = null,
            [FromQuery] string? resultCode = null,
            [FromQuery] string? vnp_TxnRef = null)
        {
            string rawKey = !string.IsNullOrEmpty(vnp_TxnRef) ? vnp_TxnRef : orderId;
            string realMaHd = rawKey?.Split('_')[0] ?? "";

            if (string.IsNullOrEmpty(realMaHd))
                return BadRequest(new { success = false, message = "Thiếu mã đơn hàng." });

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(h => h.ThongtinGiaohangs)
                    .Include(h => h.Thanhtoans)
                    .FirstOrDefaultAsync(h => h.MaHd == realMaHd);

                if (hoaDon == null)
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng." });

                bool isPaymentSuccess = (vnp_ResponseCode == "00") || (resultCode == "0");
                var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                if (isPaymentSuccess && thanhToan != null && thanhToan.TrangThai != 1)
                {
                    thanhToan.TrangThai = 1;
                    hoaDon.TrangThai = (hoaDon.TienDaTra ?? 0) >= (hoaDon.ThanhTien ?? 0) ? 2 : 1;
                    await _context.SaveChangesAsync();
                }

                var khachHangInfo = await _context.Khachhangs.FindAsync(hoaDon.MaKh);
                var delivery = hoaDon.ThongtinGiaohangs.FirstOrDefault();

                var chitietSps = await _context.ChitietHoadonSps
                    .Where(c => c.MaHd == realMaHd)
                    .ToListAsync();

                var productsList = new List<object>();
                foreach (var ct in chitietSps)
                {
                    var bt = await _context.BienTheSanphams.FindAsync(ct.MaBienThe);
                    var spItem = bt != null ? await _context.SanPhams.FindAsync(bt.MaSanPham) : null;
                    productsList.Add(new
                    {
                        id = ct.MaBienThe,
                        name = spItem?.TenSp ?? "Sản phẩm",
                        variant = bt?.TenBienThe ?? "Mặc định",
                        qty = ct.SoLuong,
                        price = ct.DonGiaLucBan,
                        total = ct.ThanhTien,
                        img = spItem?.HinhAnhDaiDien
                    });
                }

                var receipt = new
                {
                    maHd = hoaDon.MaHd,
                    ngayTao = hoaDon.NgayTao?.ToString("dd/MM/yyyy HH:mm"),
                    khachHang = khachHangInfo?.HoTen ?? delivery?.TenNguoiNhan ?? "Khách hàng",
                    sdt = khachHangInfo?.Sdt ?? delivery?.SdtNhan ?? "",
                    tongTien = hoaDon.TongTien,
                    thanhTien = hoaDon.ThanhTien,
                    tienDaTra = hoaDon.TienDaTra,
                    phuongThucThanhToan = thanhToan?.PhuongThuc ?? (hoaDon.TienDaTra > 0 ? "VNPay" : "COD"),
                    trangThai = (hoaDon.TrangThai == 1 || hoaDon.TrangThai == 2) ? "Đã thanh toán" : "Chờ thanh toán",
                    warehouse = new
                    {
                        name = "Tổng Kho Salon LGHair",
                        address = "Số 123 Nguyễn Trãi, Phường Bến Thành, Quận 1, TP. Hồ Chí Minh",
                        phone = "0987.654.321"
                    },
                    products = productsList,
                    delivery = delivery != null ? new
                    {
                        nguoiNhan = delivery.TenNguoiNhan,
                        sdt = delivery.SdtNhan,
                        diaChi = delivery.DiaChiNhan,
                        phiShip = delivery.PhiShip
                    } : null
                };

                return Ok(new { success = true, data = receipt });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}

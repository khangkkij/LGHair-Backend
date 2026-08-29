using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckOutController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;

        public CheckOutController(MinhTamHairContext context, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _configuration = configuration;
            _scopeFactory = scopeFactory;
        }

        // 1. Lấy danh sách khách đang phục vụ (Trạng thái 3) - CÓ LỌC THEO NGÀY
        [HttpGet("get-checkout-list")]
        public async Task<IActionResult> GetCheckoutList(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;
            var targetDateOnly = DateOnly.FromDateTime(targetDate);

            var data = await _context.Lichdats
                .AsNoTracking()
                .Include(ld => ld.MaKhNavigation)
                .Where(ld => ld.NgayHen == targetDateOnly && ld.TrangThai == 3)
                .OrderByDescending(ld => ld.GioBatDau)
                .Select(ld => new
                {
                    maLd = ld.MaLd,
                    tenKhach = ld.MaKhNavigation.HoTen,
                    sdt = ld.MaKhNavigation.Sdt,
                    email = ld.MaKhNavigation.Email,
                    ngayHen = $"{ld.NgayHen:yyyy-MM-dd}T{(ld.GioBatDau.HasValue ? ld.GioBatDau.Value.ToString("HH:mm") : "00:00")}",

                    tongTien = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Sum(hd => hd.TongTien ?? 0),
                    daThanhToan = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Sum(hd => hd.TienDaTra ?? 0),
                    tienGiam = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Sum(hd => (hd.TongTien ?? 0) - (hd.ThanhTien ?? 0))
                })
                .ToListAsync();

            return Ok(data);
        }

        // 2. Lấy chi tiết đơn hàng để Check-out tại quầy
        [HttpGet("get-checkout-detail/{maLd}")]
        public async Task<IActionResult> GetCheckoutDetail(string maLd)
        {
            var cleanId = maLd.Trim();
            var ld = await _context.Lichdats
                .AsNoTracking()
                .Include(x => x.MaKhNavigation)
                .FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanId && x.TrangThai == 3);

            if (ld == null) return NotFound("Không tìm thấy đơn hàng đang phục vụ.");

            var hoaDons = await _context.HoaDons.AsNoTracking().Where(h => h.MaLd == ld.MaLd).ToListAsync();
            var mainHoaDon = hoaDons.FirstOrDefault(h => !h.MaHd.StartsWith("SP")) ?? hoaDons.FirstOrDefault();
            var maHds = hoaDons.Select(h => h.MaHd).ToList();

            var result = new
            {
                maLd = ld.MaLd,
                tenKhach = ld.MaKhNavigation.HoTen,
                sdt = ld.MaKhNavigation.Sdt,
                email = ld.MaKhNavigation.Email,

                tongTien = hoaDons.Sum(h => h.TongTien ?? 0),
                daThanhToan = hoaDons.Sum(h => h.TienDaTra ?? 0),
                voucher = mainHoaDon?.MaCode,
                phanTramGiamVip = mainHoaDon?.PhanTramGiamVip ?? 0,
                tienGiam = hoaDons.Sum(h => (h.TongTien ?? 0) - (h.ThanhTien ?? 0)),

                maNv = _context.ChitietLichdats.Where(ct => ct.MaLd == ld.MaLd).Select(ct => ct.MaNv).FirstOrDefault(),

                dichVus = _context.ChitietLichdats
                    .Where(ct => ct.MaLd == ld.MaLd)
                    .Select(ct => new
                    {
                        id = ct.MaDv ?? ct.MaCb,
                        ten = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                        gia = ct.GiaLucDat ?? 0,
                        phanTramHoaHong = ct.MaDvNavigation != null ? (ct.MaDvNavigation.PhanTramHoaHong ?? 0) : 0,
                    }).ToList(),

                sanPhams = _context.ChitietHoadonSps
                    .Where(ct => maHds.Contains(ct.MaHd))
                    .Join(_context.BienTheSanphams, ct => ct.MaBienThe, bt => bt.MaBienThe, (ct, bt) => new { ct, bt })
                    .Join(_context.SanPhams, temp => temp.bt.MaSanPham, sp => sp.MaSp, (temp, sp) => new {
                        id = temp.ct.MaBienThe,
                        ten = sp.TenSp + " - " + temp.bt.TenBienThe,
                        soLuong = temp.ct.SoLuong,
                        gia = temp.ct.DonGiaLucBan,
                        phanTramHoaHong = temp.bt.PhanTramHoaHong ?? 0
                    }).ToList()
            };

            return Ok(result);
        }

        // 3. Cập nhật Giỏ hàng (Mua thêm sản phẩm / Bớt sản phẩm hoàn kho / Thêm bớt dịch vụ)
        [HttpPost("update-order")]
        public async Task<IActionResult> UpdateOrder([FromBody] UpdateCheckoutRequest request)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var ld = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd == request.MaLd);
                    var hoaDons = await _context.HoaDons.Where(h => h.MaLd == request.MaLd).ToListAsync();
                    var hoaDon = hoaDons.FirstOrDefault(h => !h.MaHd.StartsWith("SP")) ?? hoaDons.FirstOrDefault();
                    // SỬA #1: KHÔNG dùng/tạo hóa đơn "SP..." tách riêng nữa. EInvoiceService.PhatHanhHoaDonAsync
                    // đọc ChitietHoadonSp theo đúng MaHd của CHÍNH hóa đơn đang xuất (không theo MaLd), nên nếu
                    // sản phẩm nằm ở 1 HoaDon "SP..." khác thì hóa đơn điện tử gộp (tied vào hoaDon dịch vụ)
                    // sẽ không bao giờ thấy được sản phẩm đó -> mất trắng khỏi hóa đơn điện tử.

                    if (ld == null || hoaDon == null) return NotFound("Lỗi dữ liệu đơn hàng.");

                    // ============ A. XỬ LÝ DỊCH VỤ ============
                    var oldServices = await _context.ChitietLichdats.Where(ct => ct.MaLd == request.MaLd).ToListAsync();
                    _context.ChitietLichdats.RemoveRange(oldServices);

                    decimal tongDichVu = 0;
                    if (request.Services != null)
                    {
                        foreach (var item in request.Services)
                        {
                            tongDichVu += item.Gia;
                            _context.ChitietLichdats.Add(new ChitietLichdat
                            {
                                MaLd = request.MaLd,
                                MaNv = !string.IsNullOrEmpty(request.MaStylist) ? request.MaStylist : null,
                                GiaLucDat = item.Gia,
                                MaDv = item.Id.StartsWith("DV") ? item.Id : null,
                                MaCb = item.Id.StartsWith("CB") ? item.Id : null
                            });
                        }
                    }

                    // ============ B. XỬ LÝ SẢN PHẨM & ĐỒNG BỘ TỒN KHO ============
                    var maHds = hoaDons.Select(h => h.MaHd).ToList();
                    var oldProducts = await _context.ChitietHoadonSps.Where(ct => maHds.Contains(ct.MaHd)).ToListAsync();

                    // 1. Hoàn trả tồn kho cho các sản phẩm cũ trước
                    foreach (var oldP in oldProducts)
                    {
                        var bt = await _context.BienTheSanphams.FindAsync(oldP.MaBienThe);
                        if (bt != null && oldP.SoLuong.HasValue)
                        {
                            var tonTruoc = bt.SoLuongTon ?? 0;
                            bt.SoLuongTon += oldP.SoLuong.Value;

                            _context.LichSuTonKhos.Add(new LichSuTonKho
                            {
                                MaBienThe = bt.MaBienThe,
                                LoaiBienDong = "HOAN_TRA",
                                SoLuongThayDoi = oldP.SoLuong.Value,
                                TonTruoc = tonTruoc,
                                TonSau = bt.SoLuongTon.Value,
                                MaChungTu = hoaDon.MaHd,
                                GhiChu = "Cập nhật số lượng tại quầy",
                                ThoiGian = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()
                            });
                        }
                    }
                    _context.ChitietHoadonSps.RemoveRange(oldProducts);
                    await _context.SaveChangesAsync(); // Cập nhật kho tạm thời

                    // 2. Trừ tồn kho theo số lượng thực tế mới (Khách lấy bao nhiêu trừ bấy nhiêu)
                    // SỬA #2: bỏ toàn bộ đoạn tạo/dùng "hoaDonSp" - ghi ChitietHoadonSp thẳng vào
                    // hoaDon (hóa đơn dịch vụ chính) để đồng bộ với MaHd mà HoaDonDienTu gộp đang trỏ tới.
                    decimal tongSanPham = 0;
                    if (request.Products != null && request.Products.Any(p => p.SoLuong > 0))
                    {
                        foreach (var p in request.Products.Where(p => p.SoLuong > 0))
                        {
                            var bt = await _context.BienTheSanphams.FindAsync(p.Id);
                            if (bt == null || (bt.SoLuongTon ?? 0) < p.SoLuong)
                                throw new Exception($"Sản phẩm {p.Id} không đủ tồn kho.");

                            tongSanPham += p.Gia * p.SoLuong;
                            var tonTruoc = bt.SoLuongTon ?? 0;
                            bt.SoLuongTon -= p.SoLuong;

                            _context.ChitietHoadonSps.Add(new ChitietHoadonSp
                            {
                                MaHd = hoaDon.MaHd, // SỬA: trước đây là hoaDonSp.MaHd (hóa đơn "SP..." tách riêng)
                                MaBienThe = p.Id,
                                SoLuong = p.SoLuong,
                                DonGiaLucBan = p.Gia,
                                ThanhTien = p.Gia * p.SoLuong
                            });

                            _context.LichSuTonKhos.Add(new LichSuTonKho
                            {
                                MaBienThe = bt.MaBienThe,
                                LoaiBienDong = "BAN",
                                SoLuongThayDoi = -p.SoLuong,
                                TonTruoc = tonTruoc,
                                TonSau = bt.SoLuongTon.Value,
                                MaChungTu = hoaDon.MaHd, // SỬA: trước đây là hoaDonSp.MaHd
                                GhiChu = "Bán sản phẩm tại quầy",
                                ThoiGian = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()
                            });
                        }
                    }

                    // ============ C. TÍNH LẠI TỔNG TIỀN VÀ VOUCHER ============
                    // SỬA #3: TongTien phải gồm CẢ dịch vụ lẫn sản phẩm (trước đây chỉ mỗi tongDichVu,
                    // vì sản phẩm đã bị tách sang hoaDonSp riêng - giờ gộp lại chung 1 hóa đơn nên phải cộng đủ).
                    decimal tongTienMoi = tongDichVu + tongSanPham;
                    hoaDon.TongTien = tongTienMoi;
                    ld.TongTienDuKien = tongDichVu + tongSanPham;

                    decimal thanhTienMoi = tongTienMoi;
                    if (!string.IsNullOrEmpty(hoaDon.MaCode))
                    {
                        var vc = await _context.Vouchers.FirstOrDefaultAsync(v => v.MaCode == hoaDon.MaCode);
                        if (vc != null)
                        {
                            if (vc.SoTienGiam.HasValue && vc.SoTienGiam > 0)
                                thanhTienMoi = Math.Max(0, tongTienMoi - vc.SoTienGiam.Value);
                            else if (vc.PhanTramGiam.HasValue && vc.PhanTramGiam > 0)
                                thanhTienMoi = Math.Max(0, tongTienMoi - (tongTienMoi * vc.PhanTramGiam.Value / 100m));
                        }
                    }

                    decimal phiShipHienTai = await _context.ThongtinGiaohangs
                        .Where(t => t.MaHd == hoaDon.MaHd)
                        .Select(t => t.PhiShip ?? 0m)
                        .FirstOrDefaultAsync();
                    hoaDon.ThanhTien = thanhTienMoi + phiShipHienTai;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { message = "Đã cập nhật đơn hàng thành công!" });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = ex.Message });
                }
            });
        }

        // 4. Xác nhận Thanh toán tại quầy & Đóng đơn
        [HttpPost("confirm-payment")]
        public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmCheckoutPaymentRequest req)
        {
            var ld = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd == req.MaLd);
            var hoaDons = await _context.HoaDons.Where(hd => hd.MaLd == req.MaLd).ToListAsync();

            if (ld == null || !hoaDons.Any()) return NotFound("Không tìm thấy dữ liệu hóa đơn.");

            var mainHoaDon = hoaDons.FirstOrDefault(h => !h.MaHd.StartsWith("SP")) ?? hoaDons.First();

            foreach (var h in hoaDons)
            {
                if (!string.IsNullOrEmpty(req.MaStylist))
                {
                    h.MaNv = req.MaStylist;
                }
            }

            // Thu số tiền còn lại tại quầy (nếu có)
            if (req.AmountToPay > 0)
            {
                if (req.PaymentMethod.ToLower() == "tienmat")
                {
                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = mainHoaDon.MaHd,
                        PhuongThuc = "Tiền mặt",
                        SoTien = req.AmountToPay,
                        TrangThai = 1
                    });
                    mainHoaDon.TienDaTra = (mainHoaDon.TienDaTra ?? 0) + req.AmountToPay;
                }
                else // Online Payment (VNPay / MoMo)
                {
                    await _context.SaveChangesAsync();
                    if (req.PaymentMethod.ToLower() == "vnpay")
                        return Ok(new { payUrl = CreateVNPayLink(mainHoaDon.MaHd, (double)req.AmountToPay) });

                    return Ok(new { payUrl = await CreateMoMoLink(mainHoaDon.MaHd, (long)req.AmountToPay) });
                }
            }

            // Đóng lượt phục vụ và hóa đơn
            ld.TrangThai = 4; // 4: Đã hoàn thành phục vụ
            // SỬA #4: sau khi gộp #1-#3 ở trên, danh sách hoaDons giờ thường chỉ còn đúng 1 hóa đơn dịch vụ
            // (không còn hóa đơn "SP..." tách riêng được tạo mới nữa). Set TrangThai = 3 ("Hoàn thành") rồi
            // BẮN HÓA ĐƠN ĐIỆN TỬ NGAY TẠI ĐÂY luôn - checkout là điểm kết thúc thật sự của giao dịch tại
            // quầy (không như đơn ship COD còn phải chờ xác nhận giao hàng), và nhân viên đã xác nhận số
            // tiền qua hộp thoại Swal trước khi gọi API này rồi nên xuất luôn là an toàn.
            var maHdCanXuatHddt = new List<string>();
            foreach (var h in hoaDons)
            {
                h.TrangThai = 3; // 3: Hóa đơn đã hoàn tất
                maHdCanXuatHddt.Add(h.MaHd);
            }

            await _context.SaveChangesAsync();

            foreach (var maHd in maHdCanXuatHddt)
            {
                var hddtChoXuat = await _context.HoaDonDienTus
                    .Where(x => x.MaHd == maHd && x.TrangThai == 3)
                    .OrderByDescending(x => x.MaHddt)
                    .FirstOrDefaultAsync();

                if (hddtChoXuat != null)
                {
                    hddtChoXuat.TrangThai = 0; // Sẵn sàng xuất
                    await _context.SaveChangesAsync();
                    var maHddt = hddtChoXuat.MaHddt;
                    _ = Task.Run(async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                        await eInvoiceService.PhatHanhHoaDonAsync(maHddt);
                    });
                }
            }

            return Ok(new { message = "Thanh toán thành công, đơn hàng đã hoàn tất!" });
        }

        // 5. Lấy danh sách toàn bộ sản phẩm khả dụng trong kho để bán tại quầy
        [HttpGet("get-all-products")]
        public async Task<IActionResult> GetAllProducts()
        {
            try
            {
                var result = await (from bt in _context.BienTheSanphams
                                    join sp in _context.SanPhams on bt.MaSanPham equals sp.MaSp
                                    where bt.TrangThai == 1 && sp.TrangThai == 1 && bt.SoLuongTon > 0
                                    select new
                                    {
                                        id = bt.MaBienThe,
                                        tenSanPham = sp.TenSp + " - " + bt.TenBienThe,
                                        gia = bt.GiaBan ?? 0,
                                        soLuongTon = bt.SoLuongTon,
                                        phanTramHoaHong = bt.PhanTramHoaHong ?? 0,
                                        hinhAnh = string.IsNullOrEmpty(bt.HinhAnhDaiDien) ? sp.HinhAnhDaiDien : bt.HinhAnhDaiDien
                                    }).ToListAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ================= CÁC HÀM HỖ TRỢ THANH TOÁN =================
        private string CreateVNPayLink(string maHd, double amount)
        {
            var vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", "2.1.0");
            vnpay.AddRequestData("vnp_Command", "pay");
            vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"]);
            vnpay.AddRequestData("vnp_Amount", (amount * 100).ToString());
            vnpay.AddRequestData("vnp_CreateDate", MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", "VND");
            vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
            vnpay.AddRequestData("vnp_Locale", "vn");
            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan CheckOut: " + maHd);
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", _configuration["Vnpay:ReturnUrlCheckIn"]);
            vnpay.AddRequestData("vnp_TxnRef", maHd + "_" + MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Ticks);

            return vnpay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"], _configuration["Vnpay:HashSecret"]);
        }

        private async Task<string> CreateMoMoLink(string maHd, long amount)
        {
            var partnerCode = _configuration["Momo:PartnerCode"];
            var accessKey = _configuration["Momo:AccessKey"];
            var secretKey = _configuration["Momo:SecretKey"];
            var endpoint = _configuration["Momo:ApiEndpoint"];
            var redirectUrl = _configuration["Momo:ReturnUrlCheckIn"];
            var ipnUrl = _configuration["Momo:NotifyUrl"];

            string orderId = maHd + "_" + MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Ticks;
            string requestId = Guid.NewGuid().ToString();
            string orderInfo = "Thanh toan CheckOut";

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
            return result?.payUrl;
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

    // ================= DTOs =================
    public class UpdateCheckoutRequest
    {
        public string MaLd { get; set; }
        public string MaStylist { get; set; }
        public List<CheckoutServiceDTO> Services { get; set; }
        public List<CheckoutProductDTO> Products { get; set; }
    }

    public class ConfirmCheckoutPaymentRequest
    {
        public string MaLd { get; set; }
        public string MaStylist { get; set; }
        public string PaymentMethod { get; set; }
        public decimal AmountToPay { get; set; }
    }

    public class CheckoutServiceDTO
    {
        public string Id { get; set; }
        public decimal Gia { get; set; }
    }

    public class CheckoutProductDTO
    {
        public string Id { get; set; }
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
    }
}
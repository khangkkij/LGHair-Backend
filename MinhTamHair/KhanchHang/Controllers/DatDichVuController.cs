using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System.Text; // Để dùng Encoding
using System.Security.Cryptography; // Để dùng HMACSHA256
using System.Net.Http.Json; // Để dùng PostAsJsonAsync cho MoMo

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DatDichVuController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        public DatDichVuController(MinhTamHairContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }
        [HttpGet("data-booking")]
        public IActionResult GetAll()
        {
            var services = _context.Dichvus
                .Where(s => s.TrangThai == 1)
                .Select(s => new {
                    id = s.MaDv,
                    name = s.TenDv,
                    price = s.Gia,
                    icon = "fas fa-cut" // Icon mặc định
                }).ToList();

            // Lấy danh sách Combo đang hoạt động
            var combos = _context.Combos
                .Where(c => c.TrangThai == 1)
                .Select(c => new {
                    id = c.MaCb,
                    name = c.TenCombo,
                    price = c.Gia,
                    icon = "fas fa-box-open"
                }).ToList();

            return Ok(new { services, combos });
        }
        [HttpGet("get-stylists")]
        public IActionResult GetStylists()
        {
            var stylists = _context.Nhanviens
                .Include(nv => nv.MaLoaiNvNavigation) // Thêm Include này
                .Where(nv => nv.TrangThai == 1 && nv.MaLoaiNv == "LNV01")
                .Select(nv => new {
                    id = nv.MaNv,
                    name = nv.HoTenNv,
                    role = nv.MaLoaiNvNavigation.TenLoai, // Lấy tên thật: Stylist chính, Thợ cả...
                    img = nv.HinhAnh ?? "https://i.pravatar.cc/150"
                }).ToList();
            return Ok(stylists);
        }

        [HttpGet("get-my-vouchers/{maKH}")]
        public IActionResult GetMyVouchers(string maKH, [FromQuery] decimal subTotal)
        {
            var now = DateTime.Now;

            // Truy vấn thông qua thuộc tính điều hướng MaCodes của Khachhang
            var vouchers = _context.Khachhangs
                .Where(kh => kh.MaKh == maKH)
                .SelectMany(kh => kh.MaCodes) // Đây là cách lấy danh sách Voucher của khách mà không cần gọi bảng trung gian
                .Where(v => v.TrangThai == 1
                            && v.NgayBatDau <= now
                            && v.NgayKetThuc >= now
                            && v.SoLuong > v.SoLuongDaDung
                            && subTotal >= v.GiaTriToiThieu)
                .Select(v => new {
                    id = v.MaCode,
                    code = v.MaCode,
                    discount = v.SoTienGiam,
                    desc = v.MoTa,
                    expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : ""
                })
                .ToList();

            return Ok(vouchers);
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
                var vnp_ReturnUrl = _configuration["Vnpay:ReturnUrl"];

                vnpay.AddRequestData("vnp_Version", "2.1.0");
                vnpay.AddRequestData("vnp_Command", "pay");
                vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
                vnpay.AddRequestData("vnp_Amount", ((long)(model.Amount * 100)).ToString());
                vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                vnpay.AddRequestData("vnp_CurrCode", "VND");
                vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                vnpay.AddRequestData("vnp_Locale", "vn");
                vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan lich hen " + DateTime.Now.Ticks);
                vnpay.AddRequestData("vnp_OrderType", "other");
                vnpay.AddRequestData("vnp_ReturnUrl", vnp_ReturnUrl);
                vnpay.AddRequestData("vnp_TxnRef", DateTime.Now.ToString("yyyyMMddHHmmss"));

                var paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
                return Ok(new { url = paymentUrl });
            }

            // 2. NẾU LÀ THANH TOÁN MOMO
            var partnerCode = _configuration["Momo:PartnerCode"];
            var accessKey = _configuration["Momo:AccessKey"];
            var secretKey = _configuration["Momo:SecretKey"];
            var endpoint = _configuration["Momo:ApiEndpoint"];
            var redirectUrl = _configuration["Momo:ReturnUrl"];
            var ipnUrl = _configuration["Momo:NotifyUrl"];

            string orderId = DateTime.Now.ToString("yyyyMMddHHmmss");
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
        public async Task<IActionResult> SaveBooking([FromBody] BookingRequest model, [FromQuery] string paymentMode, [FromQuery] string maStylist) // Nhận thêm mã Stylist
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                string shortMaLd = model.BookingId.Length > 10
                    ? model.BookingId.Substring(model.BookingId.Length - 10)
                    : model.BookingId;

                decimal total = model.TotalAmount;
                decimal daThanhToan = (paymentMode == "10") ? (total * 0.1m) : total;
                int trangThaiLichDat = (paymentMode == "10") ? 1 : 2;

                // 1. LƯU BẢNG LICHDAT
                var lichDat = new Lichdat
                {
                    MaLd = shortMaLd,
                    MaKh = model.MaKh,
                    NgayHen = DateOnly.FromDateTime(model.NgayHen),
                    GioBatDau = TimeOnly.FromDateTime(model.NgayHen), // Lấy giờ từ NgayHen
                    TongTienDuKien = total,
                    TrangThai = trangThaiLichDat
                };
                _context.Lichdats.Add(lichDat);

                // 2. TẠO HÓA ĐƠN TẠM (Để tránh lỗi khóa ngoại bảng THANHTOAN)
                string maHdTạm = shortMaLd;
                var hoaDon = new HoaDon
                {
                    MaHd = shortMaLd,
                    MaLd = shortMaLd,
                    MaKh = model.MaKh,
                    NgayTao = DateTime.Now,
                    TongTien = total,
                    TienDaTra = (paymentMode == "10") ? (total * 0.1m) : total,
                    TrangThai = 0 // 0: Hóa đơn chưa hoàn tất (đang thực hiện)
                };
                _context.HoaDons.Add(hoaDon);

                // 3. LƯU CHI TIẾT DỊCH VỤ
                foreach (var item in model.Services)
                {
                    var chiTiet = new ChitietLichdat
                    {
                        MaLd = shortMaLd,
                        MaNv = maStylist, // Gán mã thợ đã chọn
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

                // 4. LƯU BẢNG THANHTOAN
                var tt = new Thanhtoan
                {
                    MaHd = maHdTạm, // Gán mã hóa đơn tạm vừa tạo
                    PhuongThuc = (paymentMode == "10") ? "Coc 10%" : "Full 100%",
                    SoTien = daThanhToan,
                    TrangThai = 1
                };
                _context.Thanhtoans.Add(tt);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "Thành công", maLd = shortMaLd });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest("Lỗi: " + ex.InnerException?.Message ?? ex.Message);
            }
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

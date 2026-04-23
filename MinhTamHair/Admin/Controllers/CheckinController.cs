using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Admin.Models;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System.Text;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckInController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        // Thêm khai báo này ở đầu class
        private readonly IConfiguration _configuration;


        // Sửa lại Constructor để nhận IConfiguration
        public CheckInController(MinhTamHairContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration; // Gán giá trị vào biến dùng chung
        }

        // 1. Lấy danh sách khách hàng chờ check-in theo ngày
        // Thay thế hàm GetAppointments cũ trong CheckInController.cs
        [HttpGet("get-appointments")]
        public async Task<IActionResult> GetAppointments(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;
            var targetDateOnly = DateOnly.FromDateTime(targetDate);

            var data = await _context.Lichdats
                .Include(ld => ld.MaKhNavigation)
                // QUY ƯỚC: Chỉ lấy trạng thái 1 (Cọc 10%) và 2 (Cọc 100%)
                .Where(ld => ld.NgayHen == targetDateOnly && (ld.TrangThai == 1 || ld.TrangThai == 2))
                .Select(ld => new CheckInViewDTO
                {
                    MaLd = ld.MaLd,
                    TenKhach = ld.MaKhNavigation.HoTen,
                    Sdt = ld.MaKhNavigation.Sdt,
                    NgayHen = ld.NgayHen.ToString() + "T" + ld.GioBatDau.ToString() + " - " + ld.GioKetThuc.ToString(),
                    TongTien = ld.TongTienDuKien ?? 0,
                    DaThanhToan = _context.Thanhtoans
                        .Where(tt => tt.MaHdNavigation.MaLd == ld.MaLd)
                        .Sum(tt => tt.SoTien) ?? 0,
                    MaNv = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => ct.MaNv)
                        .FirstOrDefault(),
                    DichVus = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => new ServiceItemDTO
                        {
                            Id = ct.MaDv ?? ct.MaCb,
                            Ten = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                            Gia = ct.GiaLucDat ?? 0
                        }).ToList()
                })
                .ToListAsync();

            return Ok(data);
        }

        // 2. API xác nhận Check-in và gán Stylist vào CHI TIẾT lịch đặt
        [HttpPost("confirm-checkin")]
        public async Task<IActionResult> ConfirmCheckIn([FromBody] CheckInConfirmRequest request)
        {
            // Tìm lịch đặt chính
            var lichDat = await _context.Lichdats.FindAsync(request.MaLd);
            if (lichDat == null) return NotFound("Không tìm thấy lịch đặt");

            // Cập nhật trạng thái lịch đặt thành 1 (Đang thực hiện)
            lichDat.TrangThai = 1;

            // VÌ BẢNG LICHDAT KHÔNG CÓ MaNV, CHÚNG TA CẬP NHẬT VÀO CHITIET_LICHDAT
            var chiTietLichDats = await _context.ChitietLichdats
                .Where(ct => ct.MaLd == request.MaLd)
                .ToListAsync();

            foreach (var item in chiTietLichDats)
            {
                item.MaNv = request.MaStylist; // Gán thợ vào từng dịch vụ trong đơn
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Check-in thành công và đã gán Stylist!" });
        }
        // Trong file CheckinController.cs
        [HttpGet("get-by-qrcode/{maLd}")]
        public async Task<IActionResult> GetByQrCode(string maLd)
        {
            // Loại bỏ khoảng trắng thừa
            var cleanId = maLd.Trim();

            var ld = await _context.Lichdats
                .Include(x => x.MaKhNavigation)
                // Dùng .Trim() trong SQL để so sánh chính xác tuyệt đối
                .FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanId);

            if (ld == null)
            {
                return NotFound($"Mã ID '{cleanId}' không tồn tại.");
            }

            // 3. Trình bày dữ liệu chuẩn DTO cho Vue
            var result = new CheckInViewDTO
            {
                MaLd = ld.MaLd,
                TenKhach = ld.MaKhNavigation.HoTen,
                Sdt = ld.MaKhNavigation.Sdt,
                // Sử dụng nội suy chuỗi để tránh lỗi TimeSpan.ToString()
                NgayHen = $"{ld.NgayHen:yyyy-MM-dd}T{ld.GioBatDau:HH\\:mm} - {ld.GioKetThuc:HH\\:mm}",
                TongTien = ld.TongTienDuKien ?? 0,
                DaThanhToan = _context.Thanhtoans
                    .Where(tt => tt.MaHdNavigation.MaLd == ld.MaLd)
                    .Sum(tt => tt.SoTien) ?? 0,
                MaNv = _context.ChitietLichdats
                    .Where(ct => ct.MaLd == ld.MaLd)
                    .Select(ct => ct.MaNv)
                    .FirstOrDefault(),
                DichVus = _context.ChitietLichdats
                    .Where(ct => ct.MaLd == ld.MaLd)
                    .Select(ct => new ServiceItemDTO
                    {
                        Id = ct.MaDv ?? ct.MaCb,
                        Ten = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                        Gia = ct.GiaLucDat ?? 0
                    }).ToList()
            };

            return Ok(result);
        }
        // 4. API lấy tất cả Dịch vụ và Combo đang hoạt động để Frontend dễ tìm kiếm khi thêm dịch vụ vào lịch đặt
        [HttpGet("all-services-combos")]
        public async Task<IActionResult> GetAllServicesAndCombos()
        {
            // Lấy danh sách Dịch vụ
            var services = await _context.Dichvus
                .Where(s => s.TrangThai == 1)
                .Select(s => new {
                    Id = s.MaDv,
                    Name = s.TenDv,
                    Price = s.Gia,
                    Type = "DV"
                }).ToListAsync();

            // Lấy danh sách Combo
            var combos = await _context.Combos
                .Where(c => c.TrangThai == 1)
                .Select(c => new {
                    Id = c.MaCb,
                    Name = c.TenCombo,
                    Price = c.Gia,
                    Type = "CB"
                }).ToListAsync();

            // Gộp chung lại để Frontend dễ tìm kiếm
            var result = services.Cast<object>().Concat(combos.Cast<object>()).ToList();
            return Ok(result);
        }
        // Thay thế hàm CreatePaymentCheckIn cũ trong CheckInController.cs
        [HttpPost("create-payment-checkin")]
        public async Task<IActionResult> CreatePaymentCheckIn([FromBody] PaymentCheckInRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var ld = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd == request.MaLd);
                if (ld == null) return NotFound("Không tìm thấy lịch đặt");

                // BƯỚC 1: Chốt trạng thái Hoàn thành (3)
                ld.TrangThai = 3;

                // BƯỚC 2: Cập nhật lại danh sách dịch vụ thực tế làm tại quầy
                var oldDetails = await _context.ChitietLichdats.Where(ct => ct.MaLd == request.MaLd).ToListAsync();
                _context.ChitietLichdats.RemoveRange(oldDetails);

                foreach (var item in request.UpdatedServices)
                {
                    var newDetail = new ChitietLichdat
                    {
                        MaLd = request.MaLd,
                        MaNv = request.MaStylist, // Gán thợ làm thực tế
                        GiaLucDat = item.Gia
                    };
                    if (item.Id.StartsWith("DV")) newDetail.MaDv = item.Id;
                    else newDetail.MaCb = item.Id;
                    _context.ChitietLichdats.Add(newDetail);
                }

                // BƯỚC 3: Cập nhật tổng tiền mới vào Hóa đơn
                decimal tongTienMoi = request.UpdatedServices.Sum(s => s.Gia);
                ld.TongTienDuKien = tongTienMoi;

                var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(hd => hd.MaLd == request.MaLd);
                if (hoaDon != null)
                {
                    hoaDon.TongTien = tongTienMoi;
                    hoaDon.TienDaTra = tongTienMoi;
                    hoaDon.TrangThai = 1; // Đánh dấu hóa đơn đã xong
                }

                await _context.SaveChangesAsync();

                // BƯỚC 4: Tính toán khoản tiền cần thu thêm
                decimal daTra = await _context.Thanhtoans
                    .Where(tt => tt.MaHdNavigation.MaLd == request.MaLd)
                    .SumAsync(tt => tt.SoTien) ?? 0;

                decimal soTienThuThem = tongTienMoi - daTra;

                await transaction.CommitAsync();

                // BƯỚC 5: Trả về link thanh toán cho khoản phát sinh
                if (soTienThuThem > 0)
                {
                    if (request.PaymentMethod == "vnpay")
                        return Ok(new { payUrl = CreateVNPayLink(request.MaLd, (double)soTienThuThem) });

                    var momoUrl = await CreateMoMoLink(request.MaLd, (long)soTienThuThem);
                    return Ok(new { payUrl = momoUrl });
                }

                return Ok(new { message = "Hoàn tất phục vụ không phát sinh thêm phí" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest("Lỗi xử lý: " + ex.Message);
            }
        }

        // Hàm tạo link VNPay (Tái sử dụng logic từ VnPayLibrary)
        private string CreateVNPayLink(string maLd, double amount)
        {
            var vnpay = new VnPayLibrary();
            // Các thông số cấu hình lấy từ appsettings.json tương tự bên DatDichVu
            vnpay.AddRequestData("vnp_Version", "2.1.0");
            vnpay.AddRequestData("vnp_Command", "pay");
            vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"]);
            vnpay.AddRequestData("vnp_Amount", (amount * 100).ToString());
            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", "VND");
            vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
            vnpay.AddRequestData("vnp_Locale", "vn");
            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan phat sinh Checkin: " + maLd);
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", _configuration["Vnpay:ReturnUrlCheckIn"]);
            vnpay.AddRequestData("vnp_TxnRef", maLd + "_" + DateTime.Now.Ticks);

            return vnpay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"], _configuration["Vnpay:HashSecret"]);
        }
        private async Task<string> CreateMoMoLink(string maLd, long amount)
        {
            var partnerCode = _configuration["Momo:PartnerCode"];
            var accessKey = _configuration["Momo:AccessKey"];
            var secretKey = _configuration["Momo:SecretKey"];
            var endpoint = _configuration["Momo:ApiEndpoint"];
            var redirectUrl = _configuration["Momo:ReturnUrlCheckIn"]; // Lưu ý tạo URL riêng cho Checkin
            var ipnUrl = _configuration["Momo:NotifyUrl"];

            string orderId = maLd + "_" + DateTime.Now.Ticks;
            string requestId = Guid.NewGuid().ToString();
            string orderInfo = "Thanh toan phat sinh Checkin";

            string rawHash = $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType=captureWallet";

            // Bạn cần copy hàm SignHmacSHA256 từ DatDichVuController sang đây nữa
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

        // Đừng quên copy luôn hàm này
        private string SignHmacSHA256(string message, string key)
        {
            byte[] keyByte = Encoding.UTF8.GetBytes(key);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            using (var hmacsha256 = new System.Security.Cryptography.HMACSHA256(keyByte))
            {
                byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
                return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
            }
        }
        // API tạo lịch cho khách vãng lai (Walk-in) - LOGIC LỌC ĐỘNG TỪ DATABASE RỒI RANDOM
        [HttpPost("create-walkin")]
        public async Task<IActionResult> CreateWalkIn([FromBody] WalkInRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                string maKh = "";

                // 1. VÀO DATABASE TÌM TẤT CẢ TÀI KHOẢN CÓ TÊN LÀ "Khách vãng lai"
                // (Dùng Contains để quét luôn cả trường hợp gõ "Khách vãn lai" bị thiếu dấu)
                var walkInAccounts = await _context.Khachhangs
                    .Where(kh => kh.HoTen.Contains("Khách vãng lai") || kh.HoTen.Contains("Khách vãn lai"))
                    .Select(kh => kh.MaKh)
                    .ToListAsync();

                // 2. KIỂM TRA SỐ LƯỢNG VÀ RANDOM
                if (walkInAccounts.Count == 0)
                {
                    // Đề phòng trường hợp Database chưa có tài khoản nào: Tự tạo 1 cái để dùng tạm tránh lỗi
                    maKh = "KVL" + DateTime.Now.ToString("ddHHmm");
                    var newKh = new Khachhang
                    {
                        MaKh = maKh,
                        HoTen = "Khách vãng lai",
                        Sdt = "0000000000",
                        Email = "khachvanglai@gmail.com",
                        TrangThai = 1,
                        NgayTao = DateTime.Now,
                        MaRank = "RANK01",
                        DiemTichLuy = 0
                    };
                    _context.Khachhangs.Add(newKh);
                    await _context.SaveChangesAsync(); // Lưu ngay để Lịch đặt bên dưới có cái để nối vào
                }
                else
                {
                    // Nếu đã có (VD tìm thấy 4 tài khoản) -> QUAY RANDOM lấy 1 mã
                    Random rnd = new Random();
                    int index = rnd.Next(walkInAccounts.Count);
                    maKh = walkInAccounts[index]; // Bốc trúng mã nào dùng mã đó
                }

                // 3. Tạo mã Lịch đặt (MaLD)
                string maLd = "WLK" + DateTime.Now.ToString("HHmmss");

                // 4. Lưu Lịch đặt (Trạng thái 1: Đang chờ phục vụ)
                decimal tongTien = request.Services.Sum(s => s.Gia);
                var ld = new Lichdat
                {
                    MaLd = maLd,
                    MaKh = maKh, // <--- Gán cái mã ngẫu nhiên vừa bốc được vào đây
                    NgayHen = DateOnly.FromDateTime(DateTime.Today),
                    GioBatDau = TimeOnly.FromDateTime(DateTime.Now),
                    TongTienDuKien = tongTien,
                    TrangThai = 1 // 1: Đang chờ/Đang làm
                };
                _context.Lichdats.Add(ld);

                // Mẹo: Vẫn lấy thông tin thật mà Lễ tân nhập vào (Tên/SĐT thực) nhét vào Ghi chú để dễ tìm kiếm sau này
                string thongTinKhachThat = $"Tên thật: {request.TenKhach} - SĐT: {request.Sdt}";

                // 5. Lưu Chi tiết dịch vụ & Gán thợ
                foreach (var s in request.Services)
                {
                    var ct = new ChitietLichdat
                    {
                        MaLd = maLd,
                        MaNv = request.MaStylist,
                        GiaLucDat = s.Gia,
                        GhiChu = thongTinKhachThat
                    };
                    if (s.Id.StartsWith("DV")) ct.MaDv = s.Id;
                    else ct.MaCb = s.Id;
                    _context.ChitietLichdats.Add(ct);
                }

                // 6. Tạo Hóa đơn tạm
                var hd = new HoaDon
                {
                    MaHd = maLd,
                    MaLd = maLd,
                    MaKh = maKh, // <--- Gán cái mã ngẫu nhiên vào Hóa đơn
                    NgayTao = DateTime.Now,
                    TongTien = tongTien,
                    TienDaTra = 0,
                    TrangThai = 0
                };
                _context.HoaDons.Add(hd);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = $"Đã thêm khách vãng lai thành công! (Lưu vào mã: {maKh})", maLd });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest("Lỗi: " + ex.InnerException?.Message ?? ex.Message);
            }
        }
    }

    public class CheckInConfirmRequest
    {
        public string MaLd { get; set; }
        public string MaStylist { get; set; }
    }
    public class PaymentCheckInRequest
    {
        public string MaLd { get; set; }
        public string MaStylist { get; set; }
        public string PaymentMethod { get; set; }
        public List<ServiceItemDTO> UpdatedServices { get; set; }
    }
    // DTO hứng dữ liệu cho khách vãng lai
    public class WalkInRequest
    {
        public string TenKhach { get; set; }
        public string Sdt { get; set; }
        public string MaStylist { get; set; }
        public List<ServiceItemDTO> Services { get; set; }
    }
}
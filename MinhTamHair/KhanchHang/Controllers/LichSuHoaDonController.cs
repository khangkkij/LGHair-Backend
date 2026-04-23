using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MinhTamHair.Models;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LichSuHoaDonController : Controller
    {
        private readonly MinhTamHairContext _context;
        public LichSuHoaDonController(MinhTamHairContext context)
        {
            _context = context;

        }
        [HttpGet("booking-history/{maKh}")]
        public async Task<IActionResult> GetBookingHistory(string maKh)
        {
            // Kiểm tra nếu maKh trống
            if (string.IsNullOrEmpty(maKh)) return BadRequest("Mã khách hàng không hợp lệ");

            var history = await _context.Lichdats
                .Where(ld => ld.MaKh == maKh)
                .OrderByDescending(ld => ld.NgayHen)
                .Select(ld => new {
                    id = ld.MaLd,
                    date = ld.NgayHen.HasValue ? ld.NgayHen.Value.ToString("yyyy-MM-dd") : "",
                    service = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo)
                        .FirstOrDefault() ?? "Dịch vụ",
                    amount = ld.TongTienDuKien ?? 0,
                    // Quy ước trạng thái như đã bàn
                    status = ld.TrangThai == 0 ? "cancelled" :
                             ld.TrangThai == 1 ? "pending" :
                             ld.TrangThai == 2 ? "paid-full" : "completed"
                })
                .ToListAsync();

            return Ok(history);
        }
        [HttpPut("cancel-booking/{id}")]
        public async Task<IActionResult> CancelBooking(string id)
        {
            var booking = await _context.Lichdats.FindAsync(id);
            if (booking == null) return NotFound();

            // Chỉ cho phép hủy nếu đơn đang ở trạng thái chờ (1 hoặc 2)
            if (booking.TrangThai == 1 || booking.TrangThai == 2)
            {
                booking.TrangThai = 0; // Chuyển về trạng thái Hủy
                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã hủy đơn thành công" });
            }

            return BadRequest("Không thể hủy đơn hàng này.");
        }
        [HttpGet("booking-detail/{id}")]
        public async Task<IActionResult> GetBookingDetail(string id)
        {
            var detail = await _context.Lichdats
                .Where(ld => ld.MaLd == id)
                .Select(ld => new
                {
                    maLd = ld.MaLd,
                    ngayHen = ld.NgayHen,
                    gioHen = ld.GioBatDau,

                    // 1. Lấy danh sách dịch vụ + Giá gốc (OriginalPrice)
                    items = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => new {
                            name = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                            price = ct.GiaLucDat ?? 0m, // Giá thực thu lúc đặt (đã giảm sale)
                            originalPrice = ct.MaDv != null ? (ct.MaDvNavigation.Gia ?? 0m) : (ct.MaCbNavigation.Gia ?? 0m)
                        }).ToList(),

                    // 2. Tổng tiền thực tế của các món (sau Sale, nhưng chưa trừ Voucher)
                    subTotal = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Sum(ct => ct.GiaLucDat) ?? 0m,

                    // 3. Lấy thông tin Voucher đã dùng
                    voucherGiam = _context.HoaDons
                        .Where(hd => hd.MaLd == ld.MaLd && !string.IsNullOrEmpty(hd.MaCode))
                        .Select(hd => new {
                            code = hd.MaCode,
                            // Số tiền Voucher giảm = Tổng tiền hàng - Số tiền phải trả trên hóa đơn
                            amount = (hd.TongTien - hd.ThanhTien) > 0 ? (hd.TongTien - hd.ThanhTien) : 0m
                        }).FirstOrDefault(),

                    trangThai = ld.TrangThai
                })
                .FirstOrDefaultAsync();

            if (detail == null) return NotFound();

            // FIX LỖI CASTING: Ép kiểu sang decimal và xử lý null bằng ?? 0m
            decimal totalOriginal = detail.items.Sum(i => (decimal)i.originalPrice);

            // Tiết kiệm từ Flash Sale = Tổng giá gốc ban đầu - Tổng giá sau sale
            decimal flashSaleDiscount = totalOriginal - detail.subTotal;

            // Voucher giảm giá
            decimal voucherAmount = detail.voucherGiam?.amount ?? 0m;

            // Tổng thanh toán cuối cùng = Giá sau sale - Tiền Voucher
            decimal finalAmount = detail.subTotal - voucherAmount;

            return Ok(new
            {
                detail.maLd,
                detail.ngayHen,
                detail.gioHen,
                detail.items,
                totalOriginal,         // Tổng giá gốc (chưa sale, chưa voucher)
                flashSaleDiscount,     // Số tiền hời từ Flash Sale
                voucherGiam = detail.voucherGiam,
                finalAmount,           // Con số khách đã trả cuối cùng
                detail.trangThai
            });
        }
        // 1. Tạo Model hứng dữ liệu từ Form Vue gửi lên
        public class RefundRequest
        {
            public string MaLd { get; set; }
            public string Stk { get; set; }
            public string NganHang { get; set; }
            public string ChuTk { get; set; }
        }

        // 2. API Xử lý Lưu ra file Excel (CSV)
        [HttpPost("submit-refund")]
        public async Task<IActionResult> SubmitRefund([FromBody] RefundRequest req)
        {
            try
            {
                // A. Đổi trạng thái lịch đặt thành Hủy (0)
                var booking = await _context.Lichdats.FindAsync(req.MaLd);
                if (booking != null)
                {
                    booking.TrangThai = 0;
                    var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == req.MaLd);
                    if (hoaDon != null) hoaDon.TrangThai = 0;

                    await _context.SaveChangesAsync();
                }

                // B. Ghi thông tin chuyển khoản ra file Excel (CSV)
                // File này sẽ được lưu ngay trong thư mục gốc của Backend C#
                string filePath = "DanhSachHoanTien.csv";

                // C. Nếu file chưa tồn tại, tạo file và ghi dòng Tiêu đề cột
                if (!System.IO.File.Exists(filePath))
                {
                    // Thêm BOM UTF-8 để Excel đọc không bị lỗi font tiếng Việt
                    string header = "Mã Lịch Đặt,Ngân Hàng,Số Tài Khoản,Tên Chủ Tài Khoản,Ngày Yêu Cầu\n";
                    await System.IO.File.WriteAllTextAsync(filePath, "\uFEFF" + header, System.Text.Encoding.UTF8);
                }

                // D. Ghi dữ liệu khách hàng vào dòng mới
                // Dùng dấu phẩy (,) để Excel tự động chia cột. Dấu nháy đơn (') trước STK để Excel không làm mất số 0 ở đầu.
                string newLine = $"{req.MaLd},\"{req.NganHang}\",'{req.Stk},\"{req.ChuTk}\",{DateTime.Now:dd/MM/yyyy HH:mm}\n";
                await System.IO.File.AppendAllTextAsync(filePath, newLine, System.Text.Encoding.UTF8);

                return Ok(new { message = "Hủy lịch thành công! Yêu cầu hoàn tiền đã được ghi nhận." });
            }
            catch (Exception ex)
            {
                return BadRequest("Lỗi khi gửi yêu cầu: " + ex.Message);
            }
        }
    }
}

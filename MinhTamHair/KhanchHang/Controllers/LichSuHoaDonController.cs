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

                    // 1. Lấy danh sách dịch vụ cụ thể
                    items = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => new {
                            name = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                            price = ct.GiaLucDat
                        }).ToList(),

                    // 2. Tính tổng tiền dịch vụ (chưa giảm)
                    totalOriginal = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Sum(ct => ct.GiaLucDat),

                    // 3. Lấy CHÍNH XÁC số tiền giảm từ bảng VOUCHER trong Database
                    voucherGiam = _context.HoaDons
                        .Where(hd => hd.MaLd == ld.MaLd && hd.MaCode != null && hd.MaCode != "")
                        .Select(hd => new {
                            code = hd.MaCode,
                            amount = _context.Vouchers
                                .Where(v => v.MaCode == hd.MaCode)
                                .Select(v => v.SoTienGiam > 0 ? v.SoTienGiam :
                                            (v.PhanTramGiam > 0 ? (_context.ChitietLichdats.Where(ct => ct.MaLd == ld.MaLd).Sum(ct => ct.GiaLucDat) * v.PhanTramGiam / 100) : 0))
                                .FirstOrDefault()
                        }).FirstOrDefault(),

                    trangThai = ld.TrangThai
                })
                .FirstOrDefaultAsync();

            if (detail == null) return NotFound();

            decimal discount = detail.voucherGiam != null ? (detail.voucherGiam.amount ?? 0) : 0;
            decimal finalAmount = (detail.totalOriginal ?? 0) - discount;

            // Trả về dữ liệu đã được tính toán kỹ lưỡng
            return Ok(new
            {
                maLd = detail.maLd,
                ngayHen = detail.ngayHen,
                gioHen = detail.gioHen,
                items = detail.items,
                totalOriginal = detail.totalOriginal,
                voucherGiam = detail.voucherGiam,
                finalAmount = finalAmount,
                trangThai = detail.trangThai
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

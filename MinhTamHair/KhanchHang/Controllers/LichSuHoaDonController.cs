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
            await AutoCancelOverdueBookings(maKh);
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

                    // 1. THÊM LẠI GIỜ KẾT THÚC Ở ĐÂY
                    gioKetThuc = ld.GioKetThuc,

                    items = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => new {
                            name = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                            price = ct.GiaLucDat ?? 0m,
                            originalPrice = ct.MaDv != null ? (ct.MaDvNavigation.Gia ?? 0m) : (ct.MaCbNavigation.Gia ?? 0m),

                            // 2. THÊM LẠI TÊN THỢ VÀO BÊN TRONG HÀM SELECT NÀY
                            stylistName = ct.MaNvNavigation != null ? ct.MaNvNavigation.HoTenNv : "Chưa chọn thợ"
                        }).ToList(),

                    subTotal = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Sum(ct => ct.GiaLucDat) ?? 0m,

                    hoaDonInfo = _context.HoaDons
                        .Where(hd => hd.MaLd == ld.MaLd)
                        .Select(hd => new {
                            phanTramGiamVip = hd.PhanTramGiamVip,
                            code = hd.MaCode,
                            tongTien = hd.TongTien ?? 0m,
                            thanhTien = hd.ThanhTien ?? 0m
                        }).FirstOrDefault(),

                    trangThai = ld.TrangThai
                })
                .FirstOrDefaultAsync();

            if (detail == null) return NotFound();

            decimal totalOriginal = detail.items.Sum(i => (decimal)i.originalPrice);
            decimal flashSaleDiscount = totalOriginal - detail.subTotal;

            decimal memberDiscount = 0m;
            decimal voucherAmount = 0m;
            object? voucherGiam = null;

            if (detail.hoaDonInfo != null)
            {
                decimal totalDiscount = detail.hoaDonInfo.tongTien - detail.hoaDonInfo.thanhTien;
                if (totalDiscount > 0)
                {
                    if (detail.hoaDonInfo.phanTramGiamVip > 0 && detail.subTotal > 0)
                    {
                        memberDiscount = Math.Round(detail.subTotal * ((decimal)detail.hoaDonInfo.phanTramGiamVip.Value / 100m), 0);
                    }
                    else if (string.IsNullOrEmpty(detail.hoaDonInfo.code))
                    {
                        // Fallback for old bookings without PhanTramGiamVip and no voucher
                        memberDiscount = totalDiscount;
                    }
                    
                    voucherAmount = totalDiscount - memberDiscount;
                    if (voucherAmount < 0) voucherAmount = 0m;
                }

                if (!string.IsNullOrEmpty(detail.hoaDonInfo.code) && voucherAmount > 0)
                {
                    voucherGiam = new { code = detail.hoaDonInfo.code, amount = voucherAmount };
                }
            }

            decimal finalAmount = detail.subTotal - memberDiscount - voucherAmount;

            return Ok(new
            {
                detail.maLd,
                detail.ngayHen,
                detail.gioHen,
                detail.gioKetThuc, // 3. NHỚ TRẢ VỀ FRONTEND Ở ĐÂY NỮA
                detail.items,
                totalOriginal,
                flashSaleDiscount,
                memberDiscount,
                voucherGiam,
                finalAmount,
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
                // Khởi tạo cờ kiểm tra xem đã tìm thấy và xử lý hủy đơn nào chưa
                bool isProcessed = false;

                // TRƯỜNG HỢP 1: Kiểm tra xem ID gửi lên có phải là Lịch đặt dịch vụ (MaLd) không
                var booking = await _context.Lichdats.FindAsync(req.MaLd);
                if (booking != null)
                {
                    // Chỉ cho phép hủy nếu đơn đang ở trạng thái chờ (1 hoặc 2)
                    if (booking.TrangThai == 1 || booking.TrangThai == 2)
                    {
                        booking.TrangThai = 0; // Chuyển về trạng thái Hủy

                        // Đồng bộ hủy luôn hóa đơn dịch vụ đi kèm nếu có
                        var hoaDonDichVu = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == req.MaLd);
                        if (hoaDonDichVu != null) hoaDonDichVu.TrangThai = 0;

                        await _context.SaveChangesAsync();
                        isProcessed = true;
                    }
                    else
                    {
                        return BadRequest(new { message = "Lịch đặt dịch vụ này đã hoàn thành hoặc đã bị hủy trước đó." });
                    }
                }
                // TRƯỜNG HỢP 2: Nếu không phải lịch đặt, kiểm tra xem có phải Đơn hàng sản phẩm (MaHd) không
                else
                {
                    var hoaDonSanPham = await _context.HoaDons.FindAsync(req.MaLd);
                    if (hoaDonSanPham != null)
                    {
                        // Chỉ cho phép hủy khi trạng thái là 1 (Chờ xử lý) giống bên DonHangSanPhamController
                        if (hoaDonSanPham.TrangThai != 1)
                        {
                            return BadRequest(new { message = "Đơn hàng sản phẩm đã được xử lý hoặc đang giao, không thể hủy." });
                        }

                        hoaDonSanPham.TrangThai = 0; // Cập nhật trạng thái sang 0 (Đã hủy)

                        // HOÀN TỒN KHO: Tự động cộng trả lại số lượng tồn cho từng biến thể sản phẩm khi hủy đơn
                        var details = await _context.ChitietHoadonSps.Where(ct => ct.MaHd == req.MaLd).ToListAsync();
                        foreach (var item in details)
                        {
                            var variant = await _context.BienTheSanphams.FindAsync(item.MaBienThe);
                            if (variant != null && item.SoLuong.HasValue)
                            {
                                // Đè/Cập nhật trường số lượng tồn thực tế của bạn tại đây (nếu có quản lý tồn kho)
                                // variant.SoLuongTon += item.SoLuong.Value; 
                            }
                        }

                        await _context.SaveChangesAsync();
                        isProcessed = true;
                    }
                }

                // Nếu đi qua cả 2 trường hợp mà không tìm thấy dữ liệu khớp
                if (!isProcessed)
                {
                    return NotFound(new { message = "Không tìm thấy đơn hàng hoặc lịch đặt hợp lệ để thực hiện hủy." });
                }

                // B. Ghi thông tin chuyển khoản ra file Excel (CSV) cho kế toán kiểm tra hoàn tiền
                string filePath = "DanhSachHoanTien.csv";

                // Nếu file chưa tồn tại, tạo file và ghi dòng Tiêu đề cột
                if (!System.IO.File.Exists(filePath))
                {
                    // Thêm BOM UTF-8 để Excel đọc không bị lỗi font tiếng Việt
                    string header = "Mã Đơn/Lịch,Ngân Hàng,Số Tài Khoản,Tên Chủ Tài Khoản,Ngày Yêu Cầu\n";
                    await System.IO.File.WriteAllTextAsync(filePath, "\uFEFF" + header, System.Text.Encoding.UTF8);
                }

                // Ghi dữ liệu khách hàng vào dòng mới
                string newLine = $"{req.MaLd},\"{req.NganHang}\",'{req.Stk},\"{req.ChuTk}\",{MinhTamHair.Services.TimeHelper.VnTimeHelper.Now():dd/MM/yyyy HH:mm}\n";
                await System.IO.File.AppendAllTextAsync(filePath, newLine, System.Text.Encoding.UTF8);

                return Ok(new { message = "Hủy đơn hàng thành công! Thông tin hoàn tiền đã được hệ thống ghi nhận." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi hệ thống khi thực hiện hủy đơn: " + ex.Message });
            }
        }
        [HttpPut("reschedule-booking")]
        public async Task<IActionResult> RescheduleBooking([FromBody] RescheduleRequest req)
        {
            try
            {
                // 1. Tìm lịch đặt trong cơ sở dữ liệu
                var booking = await _context.Lichdats.FindAsync(req.MaLd);
                if (booking == null)
                {
                    return NotFound(new { message = "Không tìm thấy lịch đặt." });
                }

                // 2. Kiểm tra trạng thái: Chỉ cho phép đổi nếu đang chờ xử lý (1 = Cọc, 2 = Đã trả full)
                if (booking.TrangThai != 1 && booking.TrangThai != 2)
                {
                    return BadRequest(new { message = "Lịch đặt này đã hoàn thành hoặc bị hủy, không thể thay đổi." });
                }

                // 3. (Tùy chọn) Kiểm tra thời gian hẹn mới không được nhỏ hơn thời gian hiện tại
                // Lưu ý: Nếu bạn có VnTimeHelper, hãy thay MinhTamHair.Services.TimeHelper.VnTimeHelper.Now() bằng VnTimeHelper.Now()
                var newDateTime = req.NgayHenMoi.ToDateTime(req.GioBatDauMoi);
                if (newDateTime <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
                {
                    return BadRequest(new { message = "Thời gian hẹn mới phải lớn hơn thời gian hiện tại." });
                }

                // 4. Tính toán lại Giờ Kết Thúc
                // Bản chất là lấy thời lượng cũ (GioKetThuc - GioBatDau) cộng vào GioBatDau mới
                if (booking.GioBatDau.HasValue && booking.GioKetThuc.HasValue)
                {
                    TimeSpan duration = booking.GioKetThuc.Value - booking.GioBatDau.Value;
                    booking.GioKetThuc = req.GioBatDauMoi.Add(duration);
                }

                // 5. Cập nhật ngày và giờ mới
                booking.NgayHen = req.NgayHenMoi;
                booking.GioBatDau = req.GioBatDauMoi;

                await _context.SaveChangesAsync();

                return Ok(new { message = "Thay đổi lịch hẹn thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống khi dời lịch: " + ex.Message });
            }
        }
        private async Task AutoCancelOverdueBookings(string maKh)
        {
            // Lấy ngày hiện tại (nếu project của bạn dùng VnTimeHelper thì thay MinhTamHair.Services.TimeHelper.VnTimeHelper.Now() bằng VnTimeHelper.Now())
            var currentDate = DateOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());

            // Tìm các lịch hẹn của khách hàng này có trạng thái đang chờ (1 hoặc 2) nhưng ngày hẹn bé hơn ngày hiện tại
            var overdueBookings = await _context.Lichdats
                .Where(ld => ld.MaKh == maKh
                          && (ld.TrangThai == 1 || ld.TrangThai == 2)
                          && ld.NgayHen < currentDate)
                .ToListAsync();

            if (overdueBookings.Any())
            {
                foreach (var booking in overdueBookings)
                {
                    // 1. Hủy lịch đặt
                    booking.TrangThai = 0;

                    // 2. Đồng bộ hủy luôn hóa đơn dịch vụ đi kèm
                    var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == booking.MaLd);
                    if (hoaDon != null)
                    {
                        hoaDon.TrangThai = 0;
                    }
                }

                // Lưu các thay đổi vào CSDL
                await _context.SaveChangesAsync();
            }
        }
    }
    public class RescheduleRequest
    {
        public string MaLd { get; set; }
        public DateOnly NgayHenMoi { get; set; }
        public TimeOnly GioBatDauMoi { get; set; }
    }
}

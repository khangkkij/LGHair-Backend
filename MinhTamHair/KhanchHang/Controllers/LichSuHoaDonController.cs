using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MinhTamHair.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

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
                        .FirstOrDefault() ?? "Dịch vụ Salon",
                    amount = (ld.TongTienDuKien.HasValue && ld.TongTienDuKien.Value > 0)
                        ? ld.TongTienDuKien.Value
                        : (_context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.ThanhTien ?? hd.TongTien ?? 0).FirstOrDefault() > 0
                            ? _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.ThanhTien ?? hd.TongTien ?? 0).FirstOrDefault()
                            : (_context.ChitietLichdats.Where(ct => ct.MaLd == ld.MaLd).Sum(ct => ct.GiaLucDat) ?? 0)),
                    // Quy ước trạng thái chuẩn
                    status = ld.TrangThai == 0 ? "cancelled" :
                               ld.TrangThai == 1 ? "pending" :
                               ld.TrangThai == 2 ? "paid-full" :
                               ld.TrangThai == 4 ? "temp-cancelled" :
                               ld.TrangThai == 5 ? "waiting" :
                               ld.TrangThai == 6 ? "refunded" :
                               ld.TrangThai == 9 ? "rescheduled" : "completed",
                    trangThai = ld.TrangThai
                })
                .ToListAsync();

            return Ok(history);
        }

        [HttpPut("cancel-booking/{id}")]
        public async Task<IActionResult> CancelBooking(string id)
        {
            var booking = await _context.Lichdats.FindAsync(id);
            if (booking == null) return NotFound();

            // Chỉ cho phép hủy nếu đơn đang ở trạng thái chờ (1 hoặc 2) hoặc tạm hủy (4)
            if (booking.TrangThai == 1 || booking.TrangThai == 2 || booking.TrangThai == 4)
            {
                booking.TrangThai = 0; // Chuyển về trạng thái Hủy
                var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == id);
                if (hoaDon != null) hoaDon.TrangThai = 0;

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
                    gioKetThuc = ld.GioKetThuc,

                    items = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => new {
                            name = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                            price = ct.GiaLucDat ?? 0m,
                            originalPrice = ct.MaDv != null ? (ct.MaDvNavigation.Gia ?? 0m) : (ct.MaCbNavigation.Gia ?? 0m),
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
                            thanhTien = hd.ThanhTien ?? 0m,
                            tienDaTra = hd.TienDaTra ?? 0m
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

            decimal calculatedFinal = detail.subTotal - memberDiscount - voucherAmount;
            decimal finalAmount = (detail.hoaDonInfo != null && detail.hoaDonInfo.thanhTien > 0) ? detail.hoaDonInfo.thanhTien : (calculatedFinal > 0 ? calculatedFinal : (detail.subTotal > 0 ? detail.subTotal : totalOriginal));

            decimal tienDaTra = 0m;
            if (detail.hoaDonInfo != null && detail.hoaDonInfo.tienDaTra > 0)
            {
                tienDaTra = detail.hoaDonInfo.tienDaTra;
            }
            else if (detail.trangThai == 1 || detail.trangThai == 4 || detail.trangThai == 5)
            {
                tienDaTra = Math.Round(finalAmount * 0.1m);
            }
            else if (detail.trangThai == 2 || detail.trangThai == 3)
            {
                tienDaTra = finalAmount;
            }
            else
            {
                tienDaTra = Math.Round(finalAmount * 0.1m);
            }

            var ycHh = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == detail.maLd);
            decimal soTienHoan = ycHh != null && ycHh.SoTienHoan > 0 ? ycHh.SoTienHoan : tienDaTra;

            return Ok(new
            {
                detail.maLd,
                detail.ngayHen,
                detail.gioHen,
                detail.gioKetThuc,
                detail.items,
                totalOriginal,
                flashSaleDiscount,
                memberDiscount,
                voucherGiam,
                finalAmount,
                depositAmount = Math.Round(finalAmount * 0.1m),
                tienDaTra,
                soTienHoan,
                detail.trangThai
            });
        }

        // 1. Tạo Model hứng dữ liệu từ Form Vue gửi lên
        public class RefundRequest
        {
            public string MaLd { get; set; } = null!;
            public string Stk { get; set; } = null!;
            public string NganHang { get; set; } = null!;
            public string ChuTk { get; set; } = null!;
        }

        // 2. API Xử lý Lưu ra file Excel (CSV)
                [HttpPost("submit-refund")]
        public async Task<IActionResult> SubmitRefund([FromBody] RefundRequest req)
        {
            try
            {
                bool isProcessed = false;
                var cleanId = req.MaLd?.Trim();

                var booking = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanId);
                if (booking != null)
                {
                    if (booking.TrangThai == 1 || booking.TrangThai == 2 || booking.TrangThai == 4 || booking.TrangThai == 5)
                    {
                        booking.TrangThai = 5; // Trạng thái 5: Chờ hoàn tiền

                        var hoaDonDichVu = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd.Trim() == cleanId || h.MaHd.Trim() == cleanId);
                        if (hoaDonDichVu != null)
                        {
                            hoaDonDichVu.TrangThai = 5;
                        }

                        var maHdKey = hoaDonDichVu?.MaHd ?? booking.MaLd;
                        var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == maHdKey || x.MaHd == booking.MaLd);
                        if (yeuCau == null)
                        {
                            yeuCau = new YeuCauHoanHang
                            {
                                MaHd = maHdKey,
                                NgayYeuCau = DateTime.Now
                            };
                            _context.YeuCauHoanHangs.Add(yeuCau);
                        }

                        decimal tienHoan = 0;
                        if (hoaDonDichVu != null && hoaDonDichVu.TienDaTra.HasValue && hoaDonDichVu.TienDaTra.Value > 0)
                        {
                            tienHoan = hoaDonDichVu.TienDaTra.Value;
                        }
                        else if (booking.TrangThai == 1 || (hoaDonDichVu != null && hoaDonDichVu.TrangThai == 1))
                        {
                            // Đã cọc 10% -> Chỉ hoàn đúng 10% số tiền cọc
                            tienHoan = Math.Round((hoaDonDichVu?.ThanhTien ?? booking.TongTienDuKien ?? 0) * 0.1m);
                        }
                        else if (booking.TrangThai == 2 || (hoaDonDichVu != null && hoaDonDichVu.TrangThai == 2))
                        {
                            // Đã thanh toán 100% -> Hoàn toàn bộ 100%
                            tienHoan = hoaDonDichVu?.ThanhTien ?? booking.TongTienDuKien ?? 0;
                        }
                        else
                        {
                            // Mặc định tính theo cọc 10%
                            var tong = hoaDonDichVu?.ThanhTien ?? booking.TongTienDuKien ?? 0;
                            tienHoan = Math.Round(tong * 0.1m);
                        }

                        yeuCau.LyDoHoan = "Khách yêu cầu hoàn tiền cọc dịch vụ Salon";
                        yeuCau.GhiChuKhach = "Yêu cầu hoàn tiền cọc lịch hẹn #" + booking.MaLd;
                        yeuCau.SoTienHoan = tienHoan;
                        yeuCau.MaNganHang = req.NganHang;
                        yeuCau.TenNganHang = req.NganHang;
                        yeuCau.SoTaiKhoan = req.Stk;
                        yeuCau.TenChuTaiKhoan = req.ChuTk?.Trim().ToUpper();
                        yeuCau.TrangThai = 4; // Trạng thái 4: Sẵn sàng quét VietQR để hoàn tiền ngay

                        await _context.SaveChangesAsync();
                        isProcessed = true;
                    }
                    else
                    {
                        return BadRequest(new { message = "Lịch đặt dịch vụ này đã hoàn thành hoặc không ở trạng thái hợp lệ để hoàn tiền." });
                    }
                }
                else
                {
                    var hoaDonSanPham = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaHd.Trim() == cleanId);
                    if (hoaDonSanPham != null)
                    {
                        hoaDonSanPham.TrangThai = 5; // Chờ hoàn tiền
                        var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == hoaDonSanPham.MaHd);
                        if (yeuCau == null)
                        {
                            yeuCau = new YeuCauHoanHang
                            {
                                MaHd = hoaDonSanPham.MaHd,
                                NgayYeuCau = DateTime.Now
                            };
                            _context.YeuCauHoanHangs.Add(yeuCau);
                        }

                        yeuCau.SoTienHoan = hoaDonSanPham.ThanhTien ?? hoaDonSanPham.TongTien ?? 0;
                        yeuCau.MaNganHang = req.NganHang;
                        yeuCau.TenNganHang = req.NganHang;
                        yeuCau.SoTaiKhoan = req.Stk;
                        yeuCau.TenChuTaiKhoan = req.ChuTk?.Trim().ToUpper();
                        yeuCau.TrangThai = 4;

                        await _context.SaveChangesAsync();
                        isProcessed = true;
                    }
                }

                if (!isProcessed)
                {
                    return NotFound(new { message = "Không tìm thấy thông tin đơn hàng hoặc lịch hẹn để xử lý." });
                }

                return Ok(new { message = "Gửi yêu cầu hoàn tiền thành công! Salon sẽ xử lý chuyển khoản qua VietQR cho Quý khách." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi xử lý hoàn tiền: " + ex.Message });
            }
        }

        [HttpPut("reschedule-booking")]
        public async Task<IActionResult> RescheduleBooking([FromBody] RescheduleRequest req)
        {
            try
            {
                var booking = await _context.Lichdats.FindAsync(req.MaLd);
                if (booking == null)
                {
                    return NotFound(new { message = "Không tìm thấy lịch đặt." });
                }

                // Cho phép đổi nếu là 1 (Cọc), 2 (Đã trả full), hoặc 4 (Tạm hủy do trễ hẹn)
                if (booking.TrangThai != 1 && booking.TrangThai != 2 && booking.TrangThai != 4)
                {
                    return BadRequest(new { message = "Lịch đặt này đã hoàn thành hoặc bị hủy, không thể thay đổi." });
                }

                var newDateTime = req.NgayHenMoi.ToDateTime(req.GioBatDauMoi);
                if (newDateTime <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
                {
                    return BadRequest(new { message = "Thời gian hẹn mới phải lớn hơn thời gian hiện tại." });
                }

                if (booking.GioBatDau.HasValue && booking.GioKetThuc.HasValue)
                {
                    TimeSpan duration = booking.GioKetThuc.Value - booking.GioBatDau.Value;
                    booking.GioKetThuc = req.GioBatDauMoi.Add(duration);
                }

                booking.NgayHen = req.NgayHenMoi;
                booking.GioBatDau = req.GioBatDauMoi;

                // Nếu là đơn tạm hủy (4) đổi sang ngày mới -> khôi phục lại trạng thái 1 hoặc 2
                if (booking.TrangThai == 4)
                {
                    var hd = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == booking.MaLd);
                    if (hd != null && hd.TienDaTra.HasValue && hd.ThanhTien.HasValue && hd.TienDaTra >= hd.ThanhTien)
                    {
                        booking.TrangThai = 2; // Đã trả 100%
                        hd.TrangThai = 2;
                    }
                    else
                    {
                        booking.TrangThai = 1; // Đã cọc 10%
                        if (hd != null) hd.TrangThai = 1;
                    }
                }

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
            var currentDate = DateOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());

            var overdueBookings = await _context.Lichdats
                .Where(ld => ld.MaKh == maKh
                          && (ld.TrangThai == 1 || ld.TrangThai == 2)
                          && ld.NgayHen < currentDate)
                .ToListAsync();

            if (overdueBookings.Any())
            {
                foreach (var booking in overdueBookings)
                {
                    booking.TrangThai = 0;
                    var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == booking.MaLd);
                    if (hoaDon != null)
                    {
                        hoaDon.TrangThai = 0;
                    }
                }

                await _context.SaveChangesAsync();
            }
        }
    }

    public class RescheduleRequest
    {
        public string MaLd { get; set; } = null!;
        public DateOnly NgayHenMoi { get; set; }
        public TimeOnly GioBatDauMoi { get; set; }
    }
}
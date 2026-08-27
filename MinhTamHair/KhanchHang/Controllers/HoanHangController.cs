using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/hoan-hang")]
    [ApiController]
    public class HoanHangController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public HoanHangController(MinhTamHairContext context)
        {
            _context = context;
        }

        // =========================================================================
        // API 1: KHÁCH HÀNG GỬI YÊU CẦU HOÀN HÀNG (Kèm thông tin STK VietQR)
        // POST: api/hoan-hang/yeu-cau
        // =========================================================================
        [HttpPost("yeu-cau")]
        [AllowAnonymous]
        public async Task<IActionResult> GuiYeuCauHoanHang([FromBody] YeuCauHoanHangDto req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.MaHd))
                    return BadRequest(new { success = false, message = "Mã hóa đơn không được để trống." });

                var hd = await _context.HoaDons.FirstOrDefaultAsync(x => x.MaHd == req.MaHd);
                if (hd == null)
                    return NotFound(new { success = false, message = "Không tìm thấy hóa đơn trên hệ thống." });

                // 1. Kiểm tra trạng thái: Đơn phải ở trạng thái đã hoàn thành/đã giao (Trạng thái 3) hoặc đang chờ hoàn (Trạng thái 4)
                if (hd.TrangThai != 3 && hd.TrangThai != 4)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Chỉ có thể yêu cầu trả hàng / hoàn tiền đối với đơn hàng đã giao thành công."
                    });
                }

                // 2. Kiểm tra thời hạn đổi trả trong 3 ngày (72 giờ)
                if (hd.NgayTao.HasValue)
                {
                    var diffHours = (DateTime.Now - hd.NgayTao.Value).TotalHours;
                    if (diffHours > 72)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Đơn hàng đã quá thời hạn 3 ngày (72 giờ) đổi trả theo quy định của Salon."
                        });
                    }
                }

                // 3. Tạo hoặc cập nhật bản ghi YeuCauHoanHang
                var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == req.MaHd);
                if (yeuCau == null)
                {
                    yeuCau = new YeuCauHoanHang
                    {
                        MaHd = req.MaHd,
                        NgayYeuCau = DateTime.Now
                    };
                    _context.YeuCauHoanHangs.Add(yeuCau);
                }

                yeuCau.LyDoHoan = req.LyDoHoan;
                yeuCau.GhiChuKhach = req.GhiChuKhach;
                yeuCau.SoTienHoan = hd.ThanhTien ?? hd.TongTien ?? 0;
                yeuCau.MaNganHang = req.MaNganHang;
                yeuCau.TenNganHang = req.TenNganHang;
                yeuCau.SoTaiKhoan = req.SoTaiKhoan;
                yeuCau.TenChuTaiKhoan = req.TenChuTaiKhoan?.Trim().ToUpper();
                yeuCau.TrangThai = 1; // 1: Chờ Salon duyệt

                // 4. Chuyển trạng thái đơn sang 4 (Đang hoàn hàng)
                hd.TrangThai = 4;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Gửi yêu cầu hoàn hàng thành công! Salon sẽ phản hồi trong vòng 24h.",
                    soTienHoan = yeuCau.SoTienHoan
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xử lý yêu cầu hoàn hàng: " + ex.Message });
            }
        }

        // =========================================================================
        // API 2: KHÁCH HÀNG CẬP NHẬT MÃ VẬN ĐƠN (GHN, GHTK, Viettel Post...)
        // POST: api/hoan-hang/cap-nhat-van-don
        // =========================================================================
        [HttpPost("cap-nhat-van-don")]
        [AllowAnonymous]
        public async Task<IActionResult> CapNhatVanDon([FromBody] CapNhatVanDonDto req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.MaHd) || string.IsNullOrWhiteSpace(req.MaVanDon))
                {
                    return BadRequest(new { success = false, message = "Vui lòng nhập đầy đủ mã đơn hàng và mã vận đơn." });
                }

                var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == req.MaHd);
                if (yeuCau == null)
                {
                    var hd = await _context.HoaDons.FirstOrDefaultAsync(x => x.MaHd == req.MaHd);
                    if (hd == null)
                        return NotFound(new { success = false, message = "Không tìm thấy thông tin đơn hàng." });

                    yeuCau = new YeuCauHoanHang
                    {
                        MaHd = req.MaHd,
                        NgayYeuCau = DateTime.Now,
                        TrangThai = 3,
                        SoTienHoan = hd.ThanhTien ?? hd.TongTien ?? 0
                    };
                    _context.YeuCauHoanHangs.Add(yeuCau);
                }

                yeuCau.DonViVanChuyen = req.DonViVanChuyen;
                yeuCau.MaVanDon = req.MaVanDon.Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(req.HinhAnhBienLai))
                {
                    yeuCau.HinhAnhBienLai = req.HinhAnhBienLai;
                }
                yeuCau.TrangThai = 3; // 3: Khách đã gửi bưu phẩm và có mã vận đơn thực tế

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật mã vận đơn thành công! Salon sẽ theo dõi kiện hàng và hoàn tiền sớm nhất.",
                    data = yeuCau
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật vận đơn: " + ex.Message });
            }
        }

        // =========================================================================
        // API 3: XEM CHI TIẾT YÊU CẦU HOÀN HÀNG + TẠO URL VIETQR ĐỘNG
        // GET: api/hoan-hang/chi-tiet/{maHd}
        // =========================================================================
        [HttpGet("chi-tiet/{maHd}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetChiTietHoanHang(string maHd)
        {
            try
            {
                var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == maHd);
                if (yeuCau == null)
                {
                    return Ok(new { success = true, hasData = false, message = "Chưa có yêu cầu hoàn tiền cho đơn này." });
                }

                string qrUrl = "";
                if (!string.IsNullOrWhiteSpace(yeuCau.MaNganHang) && !string.IsNullOrWhiteSpace(yeuCau.SoTaiKhoan))
                {
                    var amount = Math.Round(yeuCau.SoTienHoan);
                    var memo = WebUtility.UrlEncode($"HOAN TIEN DON {maHd}");
                    var holder = WebUtility.UrlEncode(yeuCau.TenChuTaiKhoan ?? "");
                    qrUrl = $"https://img.vietqr.io/image/{yeuCau.MaNganHang}-{yeuCau.SoTaiKhoan}-compact2.png?amount={amount}&addInfo={memo}&accountName={holder}";
                }

                return Ok(new
                {
                    success = true,
                    hasData = true,
                    data = new
                    {
                        yeuCau.Id,
                        yeuCau.MaHd,
                        yeuCau.LyDoHoan,
                        yeuCau.GhiChuKhach,
                        yeuCau.SoTienHoan,
                        yeuCau.MaNganHang,
                        yeuCau.TenNganHang,
                        yeuCau.SoTaiKhoan,
                        yeuCau.TenChuTaiKhoan,
                        yeuCau.DonViVanChuyen,
                        yeuCau.MaVanDon,
                        yeuCau.HinhAnhBienLai,
                        yeuCau.TrangThai,
                        yeuCau.NgayYeuCau,
                        yeuCau.NgayDuyet,
                        yeuCau.LyDoTuChoi,
                        VietQrUrl = qrUrl
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy chi tiết: " + ex.Message });
            }
        }

        // =========================================================================
        // API 4: ADMIN DUYỆT HOẶC TỪ CHỐI YÊU CẦU HOÀN
        // PATCH: api/hoan-hang/admin/duyet-yeu-cau/{maHd}
        // =========================================================================
        [HttpPatch("admin/duyet-yeu-cau/{maHd}")]
        [AllowAnonymous]
        public async Task<IActionResult> AdminDuyetYeuCau(string maHd, [FromBody] DuyetHoanHangDto req)
        {
            try
            {
                var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == maHd);
                if (yeuCau == null)
                    return NotFound(new { success = false, message = "Không tìm thấy yêu cầu hoàn hàng." });

                var hd = await _context.HoaDons.FirstOrDefaultAsync(x => x.MaHd == maHd);

                bool isApproved = req.Duyet || req.DongY;

                if (isApproved)
                {
                    yeuCau.TrangThai = 2; // 2: Đã duyệt, chờ khách gửi hàng và cung cấp mã vận đơn
                    yeuCau.NgayDuyet = DateTime.Now;
                    if (hd != null) hd.TrangThai = 4;
                }
                else
                {
                    yeuCau.TrangThai = 0; // 0: Từ chối
                    yeuCau.LyDoTuChoi = req.LyDoTuChoi ?? "Salon từ chối yêu cầu hoàn hàng.";
                    yeuCau.NgayDuyet = DateTime.Now;
                    if (hd != null) hd.TrangThai = 3; // Quay lại trạng thái hoàn thành ban đầu
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = isApproved ? "Đã duyệt yêu cầu hoàn hàng! Chờ khách cung cấp mã vận đơn gửi hàng." : "Đã từ chối yêu cầu hoàn hàng.",
                    trangThai = yeuCau.TrangThai
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi duyệt yêu cầu: " + ex.Message });
            }
        }

        // =========================================================================
        // API 5: ADMIN XÁC NHẬN ĐÃ NHẬN ĐƯỢC HÀNG & TỰ ĐỘNG CỘNG LẠI TỒN KHO
        // CHỈ ĐƯỢC NHẬN KHI KHÁCH ĐÃ CUNG CẤP MÃ VẬN ĐƠN (CHỐNG KHAI KHỐNG)
        // PATCH: api/hoan-hang/admin/nhan-hang-nhap-kho/{maHd}
        // =========================================================================
        [HttpPatch("admin/nhan-hang-nhap-kho/{maHd}")]
        [AllowAnonymous]
        public async Task<IActionResult> AdminNhanHangNhapKho(string maHd)
        {
            try
            {
                var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == maHd);
                if (yeuCau == null)
                    return NotFound(new { success = false, message = "Không tìm thấy yêu cầu hoàn hàng." });

                // KIỂM TRA MÃ VẬN ĐƠN: Khách bắt buộc phải có mã vận đơn mới cho phép nhận hàng (chống khai khống)
                if (string.IsNullOrWhiteSpace(yeuCau.MaVanDon))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Khách hàng chưa cập nhật mã vận đơn bưu phẩm! Không thể xác nhận nhận hàng để tránh khai khống."
                    });
                }

                // 1. TỰ ĐỘNG CỘNG LẠI SỐ LƯỢNG TỒN KHO TỪ HÓA ĐƠN CHI TIẾT
                var items = await _context.ChitietHoadonSps.Where(x => x.MaHd == maHd).ToListAsync();
                foreach (var item in items)
                {
                    var bienThe = await _context.BienTheSanphams.FirstOrDefaultAsync(b => b.MaBienThe == item.MaBienThe);
                    if (bienThe != null && item.SoLuong.HasValue)
                    {
                        bienThe.SoLuongTon = (bienThe.SoLuongTon ?? 0) + item.SoLuong.Value;
                    }
                }

                // 2. Chuyển trạng thái yêu cầu sang 4 (Đã nhận hàng nhập kho), Hóa đơn sang 5 (Đã nhận hàng - Chờ hoàn tiền)
                yeuCau.TrangThai = 4;
                var hd = await _context.HoaDons.FirstOrDefaultAsync(x => x.MaHd == maHd);
                if (hd != null)
                {
                    hd.TrangThai = 5; // 5: Đã nhận hàng vào kho, chờ chuyển tiền hoàn
                }
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã nhận kiện hàng và tự động cập nhật cộng lại số lượng tồn kho thành công! Bây giờ bạn có thể quét VietQR để hoàn tiền cho khách.",
                    trangThai = yeuCau.TrangThai
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi nhập kho hàng hoàn: " + ex.Message });
            }
        }

        // =========================================================================
        // API 6: ADMIN QUÉT XONG VIETQR & XÁC NHẬN ĐÃ HOÀN TIỀN (CHUYỂN SANG TRẠNG THÁI 5)
        // CHỈ ĐƯỢC HOÀN TIỀN KHI ĐÃ NHẬP KHO XONG (TRẠNG THÁI 4) HOẶC THỦ CÔNG
        // PATCH: api/hoan-hang/admin/xac-nhan-hoan-tien-xong/{maHd}
        // =========================================================================
        [HttpPatch("admin/xac-nhan-hoan-tien-xong/{maHd}")]
        [AllowAnonymous]
        public async Task<IActionResult> AdminXacNhanHoanTienXong(string maHd)
        {
            try
            {
                var yeuCau = await _context.YeuCauHoanHangs.FirstOrDefaultAsync(x => x.MaHd == maHd);
                
                // Kiểm tra điều kiện nghiêm ngặt: Phải qua bước nhập kho (Trạng thái 4)
                if (yeuCau != null && yeuCau.TrangThai < 4)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Kiện hàng chưa được nhận và nhập kho (Trạng thái " + yeuCau.TrangThai + "). Vui lòng bấm 'Nhận hàng & Nhập kho' trước khi xác nhận hoàn tiền!"
                    });
                }

                if (yeuCau != null)
                {
                    yeuCau.TrangThai = 5; // 5: Hoàn tất hoàn tiền
                }

                var hd = await _context.HoaDons.FirstOrDefaultAsync(x => x.MaHd == maHd);
                if (hd != null)
                {
                    hd.TrangThai = 6; // Trạng thái 6: Đã hoàn tiền hoàn tất 100%
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã hoàn tất chuyển tiền hoàn cho đơn hàng #" + maHd + " (Trạng thái: Đã hoàn hàng)!"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xác nhận hoàn tất: " + ex.Message });
            }
        }
    }

    public class YeuCauHoanHangDto
    {
        public string? MaHd { get; set; }
        public string? LyDoHoan { get; set; }
        public string? GhiChuKhach { get; set; }
        public string? MaNganHang { get; set; }
        public string? TenNganHang { get; set; }
        public string? SoTaiKhoan { get; set; }
        public string? TenChuTaiKhoan { get; set; }
    }

    public class CapNhatVanDonDto
    {
        public string? MaHd { get; set; }
        public string? DonViVanChuyen { get; set; }
        public string? MaVanDon { get; set; }
        public string? HinhAnhBienLai { get; set; }
    }

    public class DuyetHoanHangDto
    {
        public bool Duyet { get; set; } = true;
        public bool DongY { get; set; } = true;
        public string? LyDoTuChoi { get; set; }
    }
}

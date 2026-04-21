using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // [Authorize(Policy = "AdminPolicy")] // Tạm thời comment lại
    public class ThongKeController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public ThongKeController(MinhTamHairContext context)
        {
            _context = context;
        }

        [HttpGet("admin-dashboard")]
        public async Task<IActionResult> GetAdminDashboard([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? searchMaHd)
        {
            try
            {
                var today = DateTime.Today;
                var start = startDate ?? new DateTime(today.Year, today.Month, 1);
                var end = endDate ?? today;
                end = end.Date.AddDays(1).AddTicks(-1);

                // ===============================================================
                // BƯỚC 1: LẤY DỮ LIỆU RA TRƯỚC (Theo đúng chuẩn DatDichVuController)
                // Bỏ qua lỗi ngầm của EF Core bằng cách lấy data thẳng ra List
                // ===============================================================
                var danhSachHoaDon = await _context.HoaDons
                    .Include(h => h.MaKhNavigation)
                    .Include(h => h.MaNvNavigation)
                    .Where(h => h.NgayTao >= start && h.NgayTao <= end)
                    .ToListAsync();

                // Chỉ lấy những hóa đơn đã thanh toán xong
                var hoaDonThanhToan = danhSachHoaDon.Where(h => h.TrangThai == 1).ToList();

                // ===============================================================
                // BƯỚC 2: TÍNH TOÁN CÁC CHỈ SỐ (Thay ThanhTien = TongTien)
                // ===============================================================
                var doanhThu = hoaDonThanhToan.Sum(h => h.TongTien ?? 0);
                var soHoaDon = hoaDonThanhToan.Count();

                // Đếm voucher: Hóa đơn nào có mã Code thì tính là đã dùng voucher
                var soVoucher = hoaDonThanhToan.Count(h => !string.IsNullOrEmpty(h.MaCode));

                // ===============================================================
                // BƯỚC 3: TOP 3 NHÂN VIÊN XUẤT SẮC
                // ===============================================================
                var topNhanVien = hoaDonThanhToan
                    .Where(h => h.MaNvNavigation != null)
                    .GroupBy(h => h.MaNvNavigation.HoTenNv)
                    .Select(g => new {
                        TenNhanVien = g.Key ?? "Chưa rõ",
                        DoanhThuMangLai = g.Sum(h => h.TongTien ?? 0), // Lấy TongTien
                        SoHoaDonPhuTrach = g.Count()
                    })
                    .OrderByDescending(x => x.DoanhThuMangLai)
                    .Take(3)
                    .ToList();

                // ===============================================================
                // BƯỚC 4: DANH SÁCH 10 HÓA ĐƠN GẦN ĐÂY
                // ===============================================================
                var hoaDonGanDay = danhSachHoaDon
                    .OrderByDescending(h => h.NgayTao)
                    .Take(10)
                    .Select(h => new {
                        MaHd = h.MaHd,
                        KhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                        NgayTao = h.NgayTao,
                        TongTien = h.TongTien ?? 0, // Lấy TongTien
                        TrangThai = h.TrangThai
                    })
                    .ToList();

                // ===============================================================
                // BƯỚC 5: BIỂU ĐỒ DOANH THU THEO NGÀY
                // ===============================================================
                var doanhThuTheoNgay = hoaDonThanhToan
                    .Where(h => h.NgayTao != null)
                    .GroupBy(x => x.NgayTao.Value.Date)
                    .OrderBy(g => g.Key) // Sắp xếp theo ngày tháng trực tiếp
                    .Select(g => new {
                        Ngay = g.Key.ToString("dd/MM"),
                        DoanhThu = g.Sum(x => x.TongTien ?? 0) // Lấy TongTien
                    })
                    .ToList();

                return Ok(new
                {
                    doanhThuThangNay = doanhThu,
                    soHoaDonThangNay = soHoaDon,
                    soVoucherDaDung = soVoucher,
                    topNhanVien,
                    hoaDonGanDay,
                    bieuDo = doanhThuTheoNgay
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }
    }
}


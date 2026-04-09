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
                // Nếu không truyền ngày, mặc định lấy từ đầu tháng đến hiện tại
                var today = DateTime.Today;
                var start = startDate ?? new DateTime(today.Year, today.Month, 1);
                var end = endDate ?? today;

                // Cộng thêm 1 ngày và trừ đi 1 tick để bao gồm trọn vẹn ngày endDate (đến 23:59:59)
                end = end.Date.AddDays(1).AddTicks(-1);

                // 1. Lọc chung theo ngày tháng
                var baseQuery = _context.HoaDons.Where(h => h.NgayTao >= start && h.NgayTao <= end);

                var doanhThu = await baseQuery.Where(h => h.TrangThai == 1).SumAsync(h => h.ThanhTien ?? 0);
                var soHoaDon = await baseQuery.CountAsync();
                var soVoucher = await baseQuery.Where(h => h.MaCode != null).CountAsync();

                // 2. Top Nhân Viên theo khoảng thời gian đã lọc
                var topNhanVien = await baseQuery
                    .Where(h => h.TrangThai == 1 && h.MaNv != null)
                    .Include(h => h.MaNvNavigation)
                    .GroupBy(h => h.MaNvNavigation.HoTenNv)
                    .Select(g => new {
                        TenNhanVien = g.Key ?? "Chưa rõ",
                        DoanhThuMangLai = g.Sum(h => h.ThanhTien ?? 0),
                        SoHoaDonPhuTrach = g.Count()
                    })
                    .OrderByDescending(x => x.DoanhThuMangLai)
                    .Take(3)
                    .ToListAsync();

                // 3. Tìm kiếm theo Mã Hóa Đơn
                var queryHoaDon = baseQuery.AsQueryable();
                if (!string.IsNullOrEmpty(searchMaHd))
                {
                    // Lọc mã hóa đơn chứa chuỗi tìm kiếm (không phân biệt hoa thường do cấu hình DB)
                    queryHoaDon = queryHoaDon.Where(h => h.MaHd.Contains(searchMaHd));
                }

                var hoaDonGanDay = await queryHoaDon
                    .Include(h => h.MaKhNavigation)
                    .OrderByDescending(h => h.NgayTao)
                    .Take(10) // Lấy 10 hóa đơn để xem rõ kết quả tìm kiếm
                    .Select(h => new {
                        h.MaHd,
                        KhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                        h.NgayTao,
                        h.ThanhTien,
                        h.TrangThai
                    })
                    .ToListAsync();

                // 4. Dữ liệu cho BIỂU ĐỒ (Nhóm doanh thu theo từng ngày)
                var rawData = await baseQuery
                    .Where(h => h.TrangThai == 1 && h.NgayTao != null)
                    .Select(h => new { Date = h.NgayTao.Value.Date, h.ThanhTien })
                    .ToListAsync();

                var doanhThuTheoNgay = rawData
                    .GroupBy(x => x.Date)
                    .Select(g => new {
                        Ngay = g.Key.ToString("dd/MM"), // Trả về định dạng Ngày/Tháng
                        DoanhThu = g.Sum(x => x.ThanhTien ?? 0)
                    })
                    // Sắp xếp tăng dần theo ngày
                    .OrderBy(x => DateTime.ParseExact(x.Ngay + "/" + today.Year, "dd/MM/yyyy", null))
                    .ToList();

                return Ok(new
                {
                    doanhThuThangNay = doanhThu,
                    soHoaDonThangNay = soHoaDon,
                    soVoucherDaDung = soVoucher,
                    topNhanVien,
                    hoaDonGanDay,
                    bieuDo = doanhThuTheoNgay // Gửi mảng này về Vue để vẽ
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }
    }
}
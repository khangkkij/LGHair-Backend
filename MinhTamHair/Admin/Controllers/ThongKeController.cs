using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
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
                // BƯỚC 1: LẤY DỮ LIỆU HOÁ ĐƠN VÀ CHI TIẾT HĐ VỀ BỘ NHỚ
                // ===============================================================
                var query = _context.HoaDons
                    .Include(h => h.MaKhNavigation)
                    .Include(h => h.MaNvNavigation)
                    .Include(h => h.ChitietHoadonSps)
                        .ThenInclude(ct => ct.MaBienTheNavigation)
                        .ThenInclude(bt => bt.MaSpNavigation)
                    .Where(h => h.NgayTao >= start && h.NgayTao <= end);

                if (!string.IsNullOrEmpty(searchMaHd))
                {
                    query = query.Where(h => h.MaHd.Contains(searchMaHd.Trim()));
                }

                var danhSachHoaDon = await query.ToListAsync();

                // ĐỒNG BỘ: Chỉ loại bỏ đơn hủy (== 0), các đơn trạng thái 1, 2, 3 đều được tính doanh thu vào biểu đồ
                var hoaDonThanhToan = danhSachHoaDon.Where(h => h.TrangThai > 0).ToList();

                // ===============================================================
                // BƯỚC 2: TÍNH TOÁN CÁC CHỈ SỐ DOANH THU THỰC TẾ
                // ===============================================================
                var doanhThu = hoaDonThanhToan.Sum(h => h.ThanhTien ?? h.TongTien ?? 0);
                var soHoaDon = hoaDonThanhToan.Count();
                var soVoucher = hoaDonThanhToan.Count(h => !string.IsNullOrEmpty(h.MaCode));

                // ===============================================================
                // BƯỚC 3: TOP NHÂN VIÊN XUẤT SẮC NHẤT
                // ===============================================================
                var topNhanVien = hoaDonThanhToan
                    .Where(h => h.MaNvNavigation != null)
                    .GroupBy(h => h.MaNvNavigation.HoTenNv)
                    .Select(g => new {
                        TenNhanVien = g.Key ?? "Chưa rõ",
                        DoanhThuMangLai = g.Sum(h => h.ThanhTien ?? h.TongTien ?? 0),
                        SoHoaDonPhuTrach = g.Count()
                    })
                    .OrderByDescending(x => x.SoHoaDonPhuTrach)
                    .Take(5)
                    .ToList();

                // ===============================================================
                // BƯỚC 4: TÁCH BIỆT ĐƠN HÀNG VÀ ĐỒNG BỘ CHI TIẾT
                // ===============================================================
                // 4.1 Hóa đơn sản phẩm thương mại
                var hoaDonSanPham = danhSachHoaDon
                    .Where(h => h.MaHd != null && (h.MaHd.StartsWith("SP") || h.MaHd.StartsWith("HDSP")))
                    .OrderByDescending(h => h.NgayTao)
                    .Select(h => new {
                        MaHd = h.MaHd,
                        KhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                        NgayTao = h.NgayTao,
                        TongTien = h.TongTien ?? 0,
                        ThanhTien = h.ThanhTien ?? h.TongTien ?? 0,
                        TrangThai = h.TrangThai ?? 1,
                        ChiTiet = h.ChitietHoadonSps.Select(ct => new {
                            Ten = ct.MaBienTheNavigation?.MaSpNavigation?.TenSp ?? "Sản phẩm kinh doanh",
                            SoLuong = ct.SoLuong ?? 1,
                            Gia = ct.ThanhTien ?? ((ct.DonGiaLucBan ?? 0) * (ct.SoLuong ?? 1))
                        }).ToList()
                    })
                    .ToList();

                // 4.2 Hóa đơn dịch vụ tại Salon
                var hoaDonDichVu = danhSachHoaDon
                    .Where(h => h.MaHd != null && !h.MaHd.StartsWith("SP") && !h.MaHd.StartsWith("HDSP"))
                    .OrderByDescending(h => h.NgayTao)
                    .Select(h => new {
                        MaHd = h.MaHd,
                        KhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                        NgayTao = h.NgayTao,
                        TongTien = h.TongTien ?? 0,
                        ThanhTien = h.ThanhTien ?? h.TongTien ?? 0,
                        TrangThai = h.TrangThai ?? 1,
                        ChiTiet = new List<object> {
                            new {
                                Ten = "Dịch vụ chăm sóc tóc Salon",
                                SoLuong = 1,
                                Gia = h.ThanhTien ?? h.TongTien ?? 0
                            }
                        }
                    })
                    .ToList();

                // ===============================================================
                // BƯỚC 5: BIỂU ĐỒ DOANH THU THEO NGÀY
                // ===============================================================
                var doanhThuTheoNgay = hoaDonThanhToan
                    .Where(h => h.NgayTao != null)
                    .GroupBy(x => x.NgayTao.Value.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new {
                        Ngay = g.Key.ToString("dd/MM"),
                        DoanhThu = g.Sum(x => x.ThanhTien ?? x.TongTien ?? 0)
                    })
                    .ToList();

                return Ok(new
                {
                    doanhThuThangNay = doanhThu,
                    soHoaDonThangNay = soHoaDon,
                    soVoucherDaDung = soVoucher,
                    topNhanVien,
                    hoaDonSanPham,
                    hoaDonDichVu,
                    bieuDo = doanhThuTheoNgay
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi hệ thống: " + ex.Message);
            }
        }
    }
}
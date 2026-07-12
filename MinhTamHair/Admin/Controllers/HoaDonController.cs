using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/admin/hoa-don")]
    [ApiController]
    // ĐÃ SỬA LỖI 403: Mở rộng tiếp nhận mọi chuỗi định danh quyền hạn Quản trị viên hệ thống
    [Authorize(Roles = "Admin,Admin,Quản trị viên,Quản trị viên (Admin),R01,R02")]
    public class HoaDonController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IServiceScopeFactory _scopeFactory;
        public HoaDonController(MinhTamHairContext context, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _scopeFactory = scopeFactory;
        }

        public class UpdateStatusRequest
        {
            public int TrangThai { get; set; }
        }

        [HttpGet("danh-sach")]
        public async Task<IActionResult> GetDanhSach([FromQuery] string? tuNgay, [FromQuery] string? denNgay, [FromQuery] string? search)
        {
            try
            {
                var query = _context.HoaDons
                    .Include(h => h.MaKhNavigation)
                    .Include(h => h.MaNvNavigation)
                    .Include(h => h.ChitietHoadonSps)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(tuNgay) && DateTime.TryParseExact(tuNgay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
                {
                    query = query.Where(h => h.NgayTao >= startDate.Date);
                }
                if (!string.IsNullOrEmpty(denNgay) && DateTime.TryParseExact(denNgay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                {
                    query = query.Where(h => h.NgayTao <= endDate.Date.AddDays(1).AddTicks(-1));
                }

                if (!string.IsNullOrEmpty(search))
                {
                    var cleanSearch = search.Trim().ToLower();
                    query = query.Where(h => h.MaHd.ToLower().Contains(cleanSearch) ||
                                             (h.MaKhNavigation != null && h.MaKhNavigation.HoTen != null && h.MaKhNavigation.HoTen.ToLower().Contains(cleanSearch)));
                }

                var allInvoices = await query.OrderByDescending(h => h.NgayTao).ToListAsync();

                var danhSachSanPham = allInvoices
                    .Where(h => h.MaHd != null && (h.MaHd.StartsWith("SP") || h.MaHd.StartsWith("HDSP") || h.ChitietHoadonSps.Any()))
                    .Select(h => new {
                        maHd = h.MaHd,
                        tenKhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                        tenNhanVien = h.MaNvNavigation != null ? h.MaNvNavigation.HoTenNv : "Hệ thống",
                        ngayTao = h.NgayTao,
                        tongTien = h.TongTien ?? 0,
                        thanhTien = h.ThanhTien ?? h.TongTien ?? 0,
                        trangThai = h.TrangThai ?? 1
                    }).ToList();

                var danhSachDichVu = allInvoices
                    .Where(h => h.MaHd != null && !h.MaHd.StartsWith("SP") && !h.MaHd.StartsWith("HDSP") && !h.ChitietHoadonSps.Any())
                    .Select(h => new {
                        maHd = h.MaHd,
                        tenKhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                        tenNhanVien = h.MaNvNavigation != null ? h.MaNvNavigation.HoTenNv : "Hệ thống",
                        ngayTao = h.NgayTao,
                        tongTien = h.TongTien ?? 0,
                        thanhTien = h.ThanhTien ?? h.TongTien ?? 0,
                        trangThai = h.TrangThai ?? 1
                    }).ToList();

                return Ok(new { success = true, hoaDonSanPham = danhSachSanPham, hoaDonDichVu = danhSachDichVu });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPatch("doi-trang-thai/{maHd}")]
        public async Task<IActionResult> UpdateStatus(string maHd, [FromBody] UpdateStatusRequest request)
        {
            try
            {
                var hd = await _context.HoaDons
                    .Include(h => h.MaLdNavigation)
                    .FirstOrDefaultAsync(h => h.MaHd == maHd);

                if (hd == null) return NotFound(new { success = false, message = "Không tìm thấy hóa đơn" });

                // HoaDon.TrangThai: 1 = Chờ xử lý, 2 = Đang giao, 3 = Đã hoàn thành.
                bool vuaHoanThanh = request.TrangThai == 3;

                hd.TrangThai = request.TrangThai;

                if (hd.MaLdNavigation != null && hd.MaLdNavigation.TrangThai != 4)
                {
                    hd.MaLdNavigation.TrangThai = request.TrangThai;
                }

                await _context.SaveChangesAsync();

                if (vuaHoanThanh)
                {
                    var hddtNhap = await _context.HoaDonDienTus
                        .Where(x => x.MaHd == maHd && x.TrangThai == 0)
                        .OrderByDescending(x => x.MaHddt)
                        .FirstOrDefaultAsync();

                    if (hddtNhap != null)
                    {
                        var maHddt = hddtNhap.MaHddt;
                        _ = Task.Run(async () =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                            await eInvoiceService.PhatHanhHoaDonAsync(maHddt);
                        });
                    }
                    var thanhToan = await _context.Thanhtoans.FirstOrDefaultAsync(t => t.MaHd == maHd);
                    if (thanhToan != null && thanhToan.TrangThai != 1)
                    {
                        thanhToan.TrangThai = 1;
                        await _context.SaveChangesAsync();
                    }
                }

                if (request.TrangThai >= 2)
                {
                    bool daXacNhanCheckout = hd.MaLdNavigation != null && hd.MaLdNavigation.TrangThai == 4;
                    if (daXacNhanCheckout)
                    {
                        var hddtGopChoXuat = await _context.HoaDonDienTus
                            .Where(x => x.MaHd == maHd && x.TrangThai == 3)
                            .OrderByDescending(x => x.MaHddt)
                            .FirstOrDefaultAsync();

                        if (hddtGopChoXuat != null)
                        {
                            var maHddtGop = hddtGopChoXuat.MaHddt;
                            hddtGopChoXuat.TrangThai = 0; // Sẵn sàng xuất
                            await _context.SaveChangesAsync();
                            _ = Task.Run(async () =>
                            {
                                using var scope = _scopeFactory.CreateScope();
                                var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                                await eInvoiceService.PhatHanhHoaDonAsync(maHddtGop);
                            });
                        }
                    }
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("chi-tiet/{maHd}")]
        public async Task<IActionResult> GetChiTiet(string maHd)
        {
            try
            {
                var hd = await _context.HoaDons
                    .Include(h => h.MaKhNavigation)
                    .Include(h => h.MaNvNavigation)
                    .Include(h => h.MaLdNavigation)
                        .ThenInclude(l => l!.ChitietLichdats)
                            .ThenInclude(ct => ct.MaDvNavigation)
                    .Include(h => h.MaLdNavigation)
                        .ThenInclude(l => l!.ChitietLichdats)
                            .ThenInclude(ct => ct.MaCbNavigation)
                    .Include(h => h.MaLdNavigation)
                        .ThenInclude(l => l!.ChitietLichdats)
                            .ThenInclude(ct => ct.MaNvNavigation)
                    .Include(h => h.ChitietHoadonSps)
                        .ThenInclude(ct => ct.MaBienTheNavigation)
                            .ThenInclude(bt => bt.MaSpNavigation)
                .FirstOrDefaultAsync(h => h.MaHd == maHd);

                if (hd == null) return NotFound();

                bool laDonSanPham = maHd.StartsWith("SP") || maHd.StartsWith("HDSP") || (hd.ChitietHoadonSps != null && hd.ChitietHoadonSps.Any());
                var chiTietList = new List<object>();

                if (laDonSanPham)
                {
                    var items = hd.ChitietHoadonSps ?? new List<ChitietHoadonSp>();
                    foreach (var ct in items)
                    {
                        chiTietList.Add(new
                        {
                            TenHangMuc = ct.MaBienTheNavigation?.MaSpNavigation?.TenSp ?? "Sản phẩm kinh doanh",
                            ThoThucHien = "Mua mang về",
                            Gia = ct.ThanhTien ?? ((ct.DonGiaLucBan ?? 0) * (ct.SoLuong ?? 1))
                        });
                    }
                }
                else
                {
                    if (hd.MaLdNavigation != null && hd.MaLdNavigation.ChitietLichdats != null)
                    {
                        foreach (var ct in hd.MaLdNavigation.ChitietLichdats)
                        {
                            chiTietList.Add(new
                            {
                                TenHangMuc = ct.MaCbNavigation != null ? ct.MaCbNavigation.TenCombo : (ct.MaDvNavigation != null ? ct.MaDvNavigation.TenDv : "Dịch vụ Salon"),
                                ThoThucHien = ct.MaNvNavigation != null ? ct.MaNvNavigation.HoTenNv : "Thợ Salon",
                                Gia = ct.GiaLucDat ?? 0
                            });
                        }
                    }
                }

                var result = new
                {
                    hd.MaHd,
                    NgayTao = hd.NgayTao,
                    TenKh = hd.MaKhNavigation?.HoTen ?? "Khách vãng lai",
                    TenThuNgan = hd.MaNvNavigation?.HoTenNv ?? "Hệ thống",
                    TongTien = hd.TongTien ?? 0,
                    GiamGiaVip = hd.PhanTramGiamVip ?? 0,
                    ThanhTien = hd.ThanhTien ?? hd.TongTien ?? 0,
                    TrangThai = hd.TrangThai ?? 1,
                    ChiTietDichVu = chiTietList
                };
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
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

        // =========================================================================
        // 1. LẤY DANH SÁCH HÓA ĐƠN
        // =========================================================================
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

                // Đồng bộ trạng thái từ Yêu Cầu Hoàn Hàng để đảm bảo chính xác 100%
                var yeuCauHoanList = await _context.YeuCauHoanHangs.AsNoTracking().ToListAsync();
                var yeuCauMap = yeuCauHoanList
                    .Where(y => !string.IsNullOrEmpty(y.MaHd))
                    .GroupBy(y => y.MaHd)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.NgayYeuCau).First());

                foreach (var h in allInvoices)
                {
                    if (h.MaHd != null && yeuCauMap.TryGetValue(h.MaHd, out var ycHh))
                    {
                        if (ycHh.TrangThai == 5)
                        {
                            h.TrangThai = 6; // Đã hoàn tất chuyển tiền hoàn
                        }
                        else if (ycHh.TrangThai == 4)
                        {
                            h.TrangThai = 5; // Đã nhận hàng nhập kho, chờ hoàn tiền
                        }
                        else if (ycHh.TrangThai == 2 || ycHh.TrangThai == 3)
                        {
                            h.TrangThai = 4; // Đang hoàn hàng (chờ gửi / đang gửi về)
                        }
                    }
                }

                // Phân loại: Có gắn lịch hẹn (Dịch vụ) hay không gắn (Sản phẩm lẻ)
                var danhSachSanPham = allInvoices
                    .Where(h => h.MaHd != null && string.IsNullOrEmpty(h.MaLd))
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
                    .Where(h => h.MaHd != null && !string.IsNullOrEmpty(h.MaLd))
                    .OrderByDescending(h => h.NgayTao)
                    .Select(h => {
                        decimal soTienHoanThucTe = 0;
                        if (h.TienDaTra.HasValue && h.TienDaTra.Value > 0)
                        {
                            soTienHoanThucTe = h.TienDaTra.Value;
                        }
                        else if (h.TrangThai == 1)
                        {
                            soTienHoanThucTe = Math.Round((h.ThanhTien ?? h.TongTien ?? 0) * 0.1m);
                        }
                        else
                        {
                            soTienHoanThucTe = h.ThanhTien ?? h.TongTien ?? 0;
                        }

                        if (h.MaHd != null && yeuCauMap.TryGetValue(h.MaHd, out var ycHh) && ycHh != null && ycHh.SoTienHoan > 0)
                          {
                              soTienHoanThucTe = ycHh.SoTienHoan;
                          }

                        return new {
                            maHd = h.MaHd,
                            maLd = h.MaLd,
                            tenKhachHang = h.MaKhNavigation != null ? h.MaKhNavigation.HoTen : "Khách vãng lai",
                            tenNhanVien = h.MaNvNavigation != null ? h.MaNvNavigation.HoTenNv : "Hệ thống",
                            ngayTao = h.NgayTao,
                            tongTien = h.TongTien ?? 0,
                            thanhTien = h.ThanhTien ?? h.TongTien ?? 0,
                            tienDaTra = h.TienDaTra ?? (h.TrangThai == 1 ? Math.Round((h.ThanhTien ?? h.TongTien ?? 0) * 0.1m) : (h.ThanhTien ?? h.TongTien ?? 0)),
                            soTienHoan = soTienHoanThucTe,
                            trangThai = h.TrangThai ?? 1
                        };
                    }).ToList();

                return Ok(new { success = true, hoaDonSanPham = danhSachSanPham, hoaDonDichVu = danhSachDichVu });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // =========================================================================
        // 2. CHI TIẾT HÓA ĐƠN
        // =========================================================================
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

                                var chiTietList = new List<object>();

                // 1. Thêm danh sách dịch vụ / combo của lịch hẹn
                if (hd.MaLdNavigation != null && hd.MaLdNavigation.ChitietLichdats != null)
                {
                    foreach (var ct in hd.MaLdNavigation.ChitietLichdats)
                    {
                        chiTietList.Add(new
                        {
                            TenHangMuc = ct.MaCbNavigation != null ? ct.MaCbNavigation.TenCombo : (ct.MaDvNavigation != null ? ct.MaDvNavigation.TenDv : "Dịch vụ Salon"),
                            ThoThucHien = ct.MaNvNavigation != null ? ct.MaNvNavigation.HoTenNv : (hd.MaNvNavigation != null ? hd.MaNvNavigation.HoTenNv : "Thợ Salon"),
                            Gia = ct.GiaLucDat ?? 0
                        });
                    }
                }

                // 2. Thêm danh sách sản phẩm (mua trực tiếp hoặc mua kèm tại quầy)
                if (hd.ChitietHoadonSps != null && hd.ChitietHoadonSps.Any())
                {
                    foreach (var ct in hd.ChitietHoadonSps)
                    {
                        var tenSp = ct.MaBienTheNavigation?.MaSpNavigation?.TenSp ?? "Sản phẩm";
                        var tenBt = ct.MaBienTheNavigation?.TenBienThe;
                        var tenHienThi = !string.IsNullOrEmpty(tenBt) ? $"{tenSp} ({tenBt})" : tenSp;

                        chiTietList.Add(new
                        {
                            TenHangMuc = $"{tenHienThi} (x{ct.SoLuong ?? 1})",
                            ThoThucHien = "Sản phẩm",
                            Gia = ct.ThanhTien ?? ((ct.DonGiaLucBan ?? 0) * (ct.SoLuong ?? 1))
                        });
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

        // =========================================================================
        // 3. ĐỔI TRẠNG THÁI HÓA ĐƠN & TỰ ĐỘNG XỬ LÝ KHO KHI HOÀN HÀNG / HỦY ĐƠN
        // =========================================================================
        [HttpPatch("doi-trang-thai/{maHd}")]
        public async Task<IActionResult> DoiTrangThai(string maHd, [FromBody] DoiTrangThaiHoaDonDto request)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var cleanMaHd = maHd.Trim();
                    var hd = await _context.HoaDons
                        .Include(h => h.MaLdNavigation)
                        .FirstOrDefaultAsync(h => h.MaHd == cleanMaHd);

                    if (hd == null)
                    {
                        return NotFound(new { success = false, message = "Không tìm thấy hóa đơn cần cập nhật." });
                    }

                    int oldStatus = hd.TrangThai ?? 1;
                    int newStatus = request.TrangThai;

                    // Không cho phép sửa nếu đơn hàng đã chốt trạng thái đóng cuối cùng (3: Hoàn thành, 0: Đã hủy, 5: Đã nhập kho hoàn)
                    if ((oldStatus == 3 || oldStatus == 0 || oldStatus == 5) && oldStatus != newStatus)
                    {
                        return BadRequest(new { success = false, message = "Đơn hàng đã hoàn tất hoặc đã hủy/nhập kho đóng đơn, không thể thay đổi." });
                    }

                    // ---------------------------------------------------------------------------------
                    // TRƯỜNG HỢP 1: Chuyển sang 5 (ĐÃ HOÀN HÀNG & NHẬP KHO) hoặc 0 (HỦY ĐƠN TRƯỚC KHI GỬI)
                    // => TỰ ĐỘNG CỘNG LẠI SỐ LƯỢNG VÀO KHO & GHI LỊCH SỬ BIẾN ĐỘNG KHO
                    // ---------------------------------------------------------------------------------
                    if ((newStatus == 5 && oldStatus != 5) || (newStatus == 0 && oldStatus != 0))
                    {
                        var chiTietSps = await _context.ChitietHoadonSps
                            .Where(ct => ct.MaHd == cleanMaHd)
                            .ToListAsync();

                        foreach (var item in chiTietSps)
                        {
                            var bt = await _context.BienTheSanphams.FindAsync(item.MaBienThe);
                            if (bt != null && item.SoLuong.HasValue && item.SoLuong.Value > 0)
                            {
                                var tonTruoc = bt.SoLuongTon ?? 0;
                                bt.SoLuongTon += item.SoLuong.Value; // Cộng lại tồn kho thực tế

                                _context.LichSuTonKhos.Add(new LichSuTonKho
                                {
                                    MaBienThe = bt.MaBienThe,
                                    LoaiBienDong = newStatus == 5 ? "NHAP_HOAN_HANG" : "HUY_DON",
                                    SoLuongThayDoi = item.SoLuong.Value,
                                    TonTruoc = tonTruoc,
                                    TonSau = bt.SoLuongTon.Value,
                                    MaChungTu = cleanMaHd,
                                    GhiChu = newStatus == 5
                                        ? $"Nhập kho hàng hoàn từ đơn {cleanMaHd}. Lý do: {request.GhiChu ?? "Khách trả hàng/Bom hàng"}"
                                        : $"Hoàn kho do hủy đơn {cleanMaHd}. Lý do: {request.GhiChu ?? "Hủy đơn"}",
                                    ThoiGian = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()
                                });
                            }
                        }
                    }

                    // ---------------------------------------------------------------------------------
                    // TRƯỜNG HỢP 2: Chuyển sang 3 (ĐÃ GIAO HÀNG THÀNH CÔNG)
                    // => Nếu là COD chưa thanh toán thì tự động ghi nhận đã thu đủ tiền
                    // ---------------------------------------------------------------------------------
                    if (newStatus == 3)
                    {
                        if ((hd.TienDaTra ?? 0) < (hd.ThanhTien ?? 0))
                        {
                            decimal soTienThuThem = (hd.ThanhTien ?? 0) - (hd.TienDaTra ?? 0);
                            hd.TienDaTra = hd.ThanhTien;

                            _context.Thanhtoans.Add(new Thanhtoan
                            {
                                MaHd = cleanMaHd,
                                PhuongThuc = "COD / Tiền mặt khi nhận hàng",
                                SoTien = soTienThuThem,
                                TrangThai = 1
                            });
                        }
                    }

                    // Cập nhật trạng thái mới cho Hóa đơn
                    hd.TrangThai = newStatus;

                    if (hd.MaLdNavigation != null)
                    {
                        hd.MaLdNavigation.TrangThai = newStatus;
                    }

                    await _context.SaveChangesAsync();

                    // ---------------------------------------------------------------------------------
                    // XỬ LÝ PHÁT HÀNH HÓA ĐƠN ĐIỆN TỬ KHI HOÀN THÀNH (Trạng thái 3)
                    // ---------------------------------------------------------------------------------
                    if (newStatus == 3)
                    {
                        var hddtNhap = await _context.HoaDonDienTus
                            .Where(x => x.MaHd == cleanMaHd && x.TrangThai == 0)
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

                        var thanhToan = await _context.Thanhtoans.FirstOrDefaultAsync(t => t.MaHd == cleanMaHd);
                        if (thanhToan != null && thanhToan.TrangThai != 1)
                        {
                            thanhToan.TrangThai = 1;
                            await _context.SaveChangesAsync();
                        }
                    }

                    if (newStatus >= 2)
                    {
                        bool daXacNhanCheckout = hd.MaLdNavigation != null && hd.MaLdNavigation.TrangThai == 4;
                        if (daXacNhanCheckout)
                        {
                            var hddtGopChoXuat = await _context.HoaDonDienTus
                                .Where(x => x.MaHd == cleanMaHd && x.TrangThai == 3)
                                .OrderByDescending(x => x.MaHddt)
                                .FirstOrDefaultAsync();

                            if (hddtGopChoXuat != null)
                            {
                                var maHddtGop = hddtGopChoXuat.MaHddt;
                                hddtGopChoXuat.TrangThai = 0;
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

                    await transaction.CommitAsync();

                    return Ok(new
                    {
                        success = true,
                        message = $"Cập nhật trạng thái đơn hàng #{cleanMaHd} thành công!",
                        trangThaiMoi = newStatus
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new { success = false, message = "Lỗi khi đổi trạng thái: " + ex.Message });
                }
            });
        }
    }

    // =========================================================================
    // DTO NHẬN REQUEST TỪ FRONTEND
    // =========================================================================
    public class DoiTrangThaiHoaDonDto
    {
        public int TrangThai { get; set; }     // 0: Hủy, 1: Chờ xử lý, 2: Đang giao, 3: Đã giao, 4: Đang hoàn hàng, 5: Đã nhập kho hoàn
        public string? GhiChu { get; set; }    // Lý do hoàn hàng / Lý do hủy
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaQRController : Controller
    {
        private readonly MinhTamHairContext _context;
        public MaQRController(MinhTamHairContext context)
        {
            _context = context;

        }
        [HttpGet("get-invoice-qr/{maLd}")]
        public async Task<IActionResult> GetInvoiceForQR(string maLd)
        {
            // 1. Tìm thông tin lịch đặt trong SQL Server
            var lichDat = await _context.Lichdats
                .Include(ld => ld.MaKhNavigation) // Kết nối bảng Khách hàng
                .FirstOrDefaultAsync(ld => ld.MaLd.Trim() == maLd.Trim());

            if (lichDat == null)
            {
                return NotFound(new { message = "Không tìm thấy hóa đơn cho mã này!" });
            }

            // 2. Lấy chi tiết các dịch vụ khách đã chọn
            var chiTiet = await _context.ChitietLichdats
                .Where(ct => ct.MaLd == maLd)
                .Select(ct => new InvoiceServiceItem
                {
                    // Lấy tên Dịch vụ hoặc tên Combo (nếu dịch vụ lẻ null)
                    Ten = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                    Gia = ct.GiaLucDat ?? 0 // Nếu giá lúc đặt null, mặc định 100 (để tránh lỗi)
                }).ToListAsync();

            // 3. Tính toán tiền đã cọc (10% hoặc 100%) từ bảng THANHTOAN
            var tienDaTra = await _context.Thanhtoans
                .Where(tt => tt.MaHdNavigation.MaLd == maLd)
                .SumAsync(tt => tt.SoTien);

            // 4. Tổng hợp dữ liệu thành một "Hóa đơn điện tử"
            var invoice = new InvoiceDTO
            {
                MaLd = lichDat.MaLd,
                TenKhach = lichDat.MaKhNavigation.HoTen,
                SoDienThoai = lichDat.MaKhNavigation.Sdt,
                ThoiGian = $"{lichDat.NgayHen:dd/MM/yyyy} lúc {lichDat.GioBatDau}",
                DanhSachDichVu = chiTiet,
                TongTien = lichDat.TongTienDuKien ?? 0,
                DaThanhToan = tienDaTra ?? 0,
                ConLai = (lichDat.TongTienDuKien ?? 0) - tienDaTra ?? 0
            };

            // TRẢ DỮ LIỆU VỀ CHO ĐIỆN THOẠI SAU KHI QUÉT QR
            return Ok(invoice);
        }

    }
}

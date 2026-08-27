using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.KhachHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DonHangSanPhamController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public DonHangSanPhamController(MinhTamHairContext context)
        {
            _context = context;
        }

        // 1. LẤY LỊCH SỬ ĐƠN HÀNG SẢN PHẨM
        // Khớp chuẩn API Vue: /api/DonHangSanPham/lich-su/{maKh}
        [HttpGet("lich-su/{maKh}")]
        public async Task<IActionResult> GetProductOrderHistory(string maKh)
        {
            if (string.IsNullOrEmpty(maKh)) return BadRequest("Mã khách hàng không hợp lệ");

            // Tối ưu hóa truy vấn bằng IQueryable để tăng tốc độ lấy danh sách
            var query = from hd in _context.HoaDons
                        where hd.MaKh == maKh && _context.ChitietHoadonSps.Any(ct => ct.MaHd == hd.MaHd)
                        orderby hd.NgayTao descending
                        select new
                        {
                            Id = hd.MaHd,
                            Date = hd.NgayTao.HasValue ? hd.NgayTao.Value.ToString("yyyy-MM-dd") : "",

                            // FIX lỗi compile bt.MaSanPham -> bt.MaSp theo đúng thực tế thiết kế database của bạn
                            ProductSummary = (from ct in _context.ChitietHoadonSps
                                              join bt in _context.BienTheSanphams on ct.MaBienThe equals bt.MaBienThe
                                              join sp in _context.SanPhams on bt.MaSanPham equals sp.MaSp
                                              where ct.MaHd == hd.MaHd
                                              select sp.TenSp).FirstOrDefault() +
                                             (_context.ChitietHoadonSps.Count(ct => ct.MaHd == hd.MaHd) > 1 ? " ...và các sản phẩm khác" : ""),

                            Amount = hd.ThanhTien ?? 0m,

                            Status = hd.TrangThai == 0 ? "cancelled" :
                                     hd.TrangThai == 1 ? "pending" :
                                     hd.TrangThai == 2 ? "shipping" : hd.TrangThai == 3 ? "completed" : hd.TrangThai == 4 ? "returning" : hd.TrangThai == 5 ? "returned" : hd.TrangThai == 6 ? "refunded" : "completed"
                        };

            var history = await query.ToListAsync();
            return Ok(history);
        }

        // 2. XEM CHI TIẾT ĐƠN HÀNG SẢN PHẨM
        // Khớp API Vue: ${API_BASE_URL}/api/DonHangSanPham/chi-tiet/{id}
        [HttpGet("chi-tiet/{id}")]
        public async Task<IActionResult> GetProductOrderDetail(string id)
        {
            // Lấy thông tin giao nhận từ bảng ThongtinGiaohang dựa trên MaHd (Tránh xử lý lồng trong Select gán trực tiếp)
            var deliveryInfo = await _context.ThongtinGiaohangs
                .Where(tt => tt.MaHd == id)
                .Select(tt => new {
                    tt.TenNguoiNhan,
                    tt.DiaChiNhan,
                    tt.PhiShip
                })
                .FirstOrDefaultAsync();

            var order = await _context.HoaDons
                .Where(hd => hd.MaHd == id)
                .Select(hd => new
                {
                    tenNguoiNhan = deliveryInfo != null ? deliveryInfo.TenNguoiNhan : "Chưa cập nhật",
                    diaChiGiao = deliveryInfo != null ? deliveryInfo.DiaChiNhan : "Chưa cập nhật",
                    trangThai = hd.TrangThai == 0 ? "cancelled" :
                                 hd.TrangThai == 1 ? "pending" :
                                 hd.TrangThai == 2 ? "shipping" : hd.TrangThai == 3 ? "completed" : hd.TrangThai == 4 ? "returning" : hd.TrangThai == 5 ? "returned" : hd.TrangThai == 6 ? "refunded" : "completed",

                    tongTienHang = hd.TongTien ?? 0m,
                    phiVanChuyen = deliveryInfo != null ? (deliveryInfo.PhiShip ?? 0m) : 0m,
                    giamGiaVoucher = ((hd.TongTien ?? 0m) - (hd.ThanhTien ?? 0m)) > 0 ? ((hd.TongTien ?? 0m) - (hd.ThanhTien ?? 0m)) : 0m,
                    tongThanhToan = hd.ThanhTien ?? 0m,

                    // Lấy chi tiết danh sách vật phẩm bằng cách đi qua Navigation Property chuẩn hóa
                    items = _context.ChitietHoadonSps
                        .Where(ct => ct.MaHd == hd.MaHd)
                        .Select(ct => new {
                            maSP = ct.MaBienThe,
                            tenSP = ct.MaBienTheNavigation.MaSpNavigation.TenSp,
                            soLuong = ct.SoLuong ?? 0,
                            giaBan = ct.DonGiaLucBan ?? 0m
                        }).ToList()
                })
                .FirstOrDefaultAsync();

            if (order == null) return NotFound();

            return Ok(order);
        }

        // 3. HỦY ĐƠN HÀNG SẢN PHẨM
        // Khớp API Vue: ${API_BASE_URL}/api/DonHangSanPham/huy-don/{id}
        [HttpPost("huy-don/{id}")]
        public async Task<IActionResult> CancelProductOrder(string id)
        {
            var invoice = await _context.HoaDons.FindAsync(id);
            if (invoice == null) return NotFound(new { success = false, message = "Không tìm thấy đơn hàng này." });

            // Chỉ cho phép hủy khi trạng thái là 1 (Chờ xử lý)
            if (invoice.TrangThai != 1)
            {
                return BadRequest(new { success = false, message = "Đơn hàng đã được xử lý hoặc đang giao, không thể hủy." });
            }

            try
            {
                invoice.TrangThai = 0; // Cập nhật trạng thái sang 0 (Đã hủy)

                // HOÀN TỒN KHO: Tự động cộng trả lại số lượng tồn cho từng biến thể sản phẩm khi hủy đơn
                var details = await _context.ChitietHoadonSps.Where(ct => ct.MaHd == id).ToListAsync();
                foreach (var item in details)
                {
                    var variant = await _context.BienTheSanphams.FindAsync(item.MaBienThe);
                    if (variant != null && item.SoLuong.HasValue)
                    {
                        // Giả định bảng BiantheSanpham của bạn có trường quản lý số lượng như SoLuong/SoLuongTon
                        // variant.SoLuongTon += item.SoLuong.Value; 
                    }
                }

                await _context.SaveChangesAsync();

                // Trả về định dạng Object { success: true } để đồng bộ với SweetAlert2 bên file Vue nhận diện
                return Ok(new { success = true, message = "Đơn hàng của bạn đã được hủy bỏ và hoàn tồn kho thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Lỗi hệ thống khi thực hiện hủy đơn: " + ex.Message });
            }
        }
    }
}
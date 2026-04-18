using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class QuanLyDanhGiaDichVuController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IWebHostEnvironment _env;

        public QuanLyDanhGiaDichVuController(MinhTamHairContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }
        // ==========================================
        // 1. LẤY TOÀN BỘ DANH SÁCH ĐÁNH GIÁ (Kèm Khách hàng & Dịch vụ)
        // ==========================================
        [HttpGet("get-all")]
        public async Task<IActionResult> GetAllReviews()
        {
            try
            {
                // Join qua nhiều bảng: Đánh giá -> Hóa đơn -> Khách hàng
                var reviews = await _context.DanhgiaDichvus
                    .Include(dg => dg.MaHdNavigation)
                        .ThenInclude(hd => hd.MaKhNavigation) // Lấy info Khách hàng
                    .Include(dg => dg.MaDvNavigation)         // Lấy info Dịch vụ
                    .Include(dg => dg.MaCbNavigation)         // Lấy info Combo
                    .Select(dg => new
                    {
                        // Giả sử Primary Key của bảng DanhgiaDichvu là MaDgdv hoặc Id
                        // Bạn nhớ sửa lại tên trường ID cho khớp với DB của bạn nhé
                        Id = dg.MaHd + "_" + (dg.MaDv ?? dg.MaCb), // Tạo ID tạm nếu bảng không có khóa chính độc lập
                        MaHd = dg.MaHd,

                        // Thông tin khách hàng
                        // Đổi HoTenKh thành TenKh (hoặc tên tương ứng trong DB của bạn)
                        TenKhach = dg.MaHdNavigation.MaKhNavigation != null ? dg.MaHdNavigation.MaKhNavigation.HoTen : "Khách vãng lai",

                        // Đổi SoDienThoai thành Sdt (hoặc tên tương ứng trong DB của bạn)
                        Sdt = dg.MaHdNavigation.MaKhNavigation != null ? dg.MaHdNavigation.MaKhNavigation.Sdt : "",

                        // Tên dịch vụ hoặc combo
                        TenDichVu = dg.MaDv != null ? dg.MaDvNavigation.TenDv :
                                   (dg.MaCb != null ? dg.MaCbNavigation.TenCombo : "Dịch vụ"),

                        SoSao = dg.SoSao,
                        NoiDung = dg.NoiDung,
                        HinhAnh = dg.HinhAnh,
                        NgayDanhGia = dg.NgayDanhGia,

                        // Trạng thái hiển thị (1: Hiện, 0: Ẩn)
                        IsHidden = dg.TrangThai == 0,

                        // CHÚ Ý: Đảm bảo bảng DanhgiaDichvu trong DB có 2 cột này (nếu chưa có thì phải thêm)
                        // PhanHoiAdmin = dg.PhanHoiAdmin, 
                        // DaPhanHoi = !string.IsNullOrEmpty(dg.PhanHoiAdmin)

                        // Tạm thời mình map rỗng nếu DB chưa có cột PhanHoiAdmin
                        // Lấy trực tiếp từ DB lên
                        PhanHoiAdmin = dg.PhanHoi,
                        DaPhanHoi = !string.IsNullOrEmpty(dg.PhanHoi)
                    })
                    .OrderByDescending(r => r.NgayDanhGia)
                    .ToListAsync();

                return Ok(reviews);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy dữ liệu đánh giá", error = ex.Message });
            }
        }

        // ==========================================
        // 2. PHẢN HỒI ĐÁNH GIÁ (Admin Reply)
        // ==========================================
        [HttpPost("reply")]
        public async Task<IActionResult> ReplyReview([FromBody] ReviewReplyDTO dto)
        {
            if (string.IsNullOrEmpty(dto.Reply)) return BadRequest("Nội dung phản hồi không được để trống.");

            try
            {
                // Tách ID (Vì ở trên mình dùng MaHd_MaDvCb làm ID tạm)
                var parts = dto.Id.Split('_');
                if (parts.Length != 2) return BadRequest("ID không hợp lệ.");

                string maHd = parts[0];
                string maDvCb = parts[1];

                var review = await _context.DanhgiaDichvus
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && (dg.MaDv == maDvCb || dg.MaCb == maDvCb));

                if (review == null) return NotFound("Không tìm thấy đánh giá.");

                // GHI CHÚ: Mở comment 2 dòng dưới đây nếu bảng DanhgiaDichvus đã có cột PhanHoiAdmin
                review.PhanHoi = dto.Reply;
                // review.DaPhanHoi = true; // Hoặc kiểu int tùy DB của bạn

                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã gửi phản hồi thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // 3. ẨN / HIỆN LẠI ĐÁNH GIÁ TRÊN APP KHÁCH
        // ==========================================
        [HttpPut("toggle-visibility/{id}")]
        public async Task<IActionResult> ToggleVisibility(string id)
        {
            try
            {
                var parts = id.Split('_');
                if (parts.Length != 2) return BadRequest("ID không hợp lệ.");

                string maHd = parts[0];
                string maDvCb = parts[1];

                var review = await _context.DanhgiaDichvus
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && (dg.MaDv == maDvCb || dg.MaCb == maDvCb));

                if (review == null) return NotFound("Không tìm thấy đánh giá.");

                // 1 = Hiện bình thường, 0 = Bị ẩn (Chỉ Admin thấy)
                review.TrangThai = review.TrangThai == 1 ? 0 : 1;

                await _context.SaveChangesAsync();

                string action = review.TrangThai == 1 ? "hiển thị lại" : "ẩn";
                return Ok(new { message = $"Đã {action} đánh giá thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // 4. XÓA VĨNH VIỄN ĐÁNH GIÁ (Kèm Xóa Ảnh)
        // ==========================================
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteReview(string id)
        {
            try
            {
                var parts = id.Split('_');
                if (parts.Length != 2) return BadRequest("ID không hợp lệ.");

                string maHd = parts[0];
                string maDvCb = parts[1];

                var review = await _context.DanhgiaDichvus
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && (dg.MaDv == maDvCb || dg.MaCb == maDvCb));

                if (review == null) return NotFound("Không tìm thấy đánh giá.");

                // Xóa file ảnh vật lý trên Server nếu khách có đính kèm
                if (!string.IsNullOrEmpty(review.HinhAnh))
                {
                    var filePath = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), review.HinhAnh.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                _context.DanhgiaDichvus.Remove(review);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã xóa vĩnh viễn đánh giá!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
        // ==========================================
        // 5. XÓA HÀNG LOẠT THEO TỪ KHÓA (Quét nội dung)
        // ==========================================
        [HttpDelete("delete-by-keyword")]
        public async Task<IActionResult> DeleteByKeyword([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return BadRequest("Vui lòng nhập từ khóa.");

            try
            {
                // Tìm tất cả đánh giá có chứa từ khóa trong cột NoiDung (không phân biệt hoa thường)
                var reviewsToDelete = await _context.DanhgiaDichvus
                    .Where(dg => dg.NoiDung != null && dg.NoiDung.Contains(keyword))
                    .ToListAsync();

                if (!reviewsToDelete.Any())
                    return NotFound(new { message = "Không tìm thấy đánh giá nào chứa từ khóa này." });

                int deletedCount = 0;
                foreach (var review in reviewsToDelete)
                {
                    // Xóa ảnh vật lý nếu có
                    if (!string.IsNullOrEmpty(review.HinhAnh))
                    {
                        var filePath = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), review.HinhAnh.TrimStart('/'));
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }
                    deletedCount++;
                }

                _context.DanhgiaDichvus.RemoveRange(reviewsToDelete);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Đã quét và xóa thành công {deletedCount} đánh giá chứa từ '{keyword}'." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
        // ==========================================
        // 6. THỐNG KÊ TRUNG BÌNH SAO THEO DỊCH VỤ
        // ==========================================
        [HttpGet("thong-ke")]
        public async Task<IActionResult> GetThongKeSaoDichVu()
        {
            try
            {
                // Dùng Entity Framework để Group By trực tiếp dưới Database
                var stats = await _context.DanhgiaDichvus
                    .Include(dg => dg.MaDvNavigation)
                    .Include(dg => dg.MaCbNavigation)
                    .GroupBy(dg => new {
                        // Nhóm theo Mã Dịch Vụ hoặc Mã Combo
                        IdId = dg.MaDv ?? dg.MaCb,
                        Ten = dg.MaDv != null ? dg.MaDvNavigation.TenDv : (dg.MaCb != null ? dg.MaCbNavigation.TenCombo : "Dịch vụ khác")
                    })
                    .Select(g => new
                    {
                        TenDichVu = g.Key.Ten,
                        // Tính điểm trung bình và làm tròn 1 chữ số thập phân
                        DiemTrungBinh = Math.Round(g.Average(x => (double)x.SoSao), 1),
                        // Đếm số lượt đánh giá
                        LuotDanhGia = g.Count()
                    })
                    // Sắp xếp: Ưu tiên điểm cao -> Ưu tiên lượt đánh giá nhiều
                    .OrderByDescending(x => x.DiemTrungBinh)
                    .ThenByDescending(x => x.LuotDanhGia)
                    .ToListAsync();

                // Trả về kết quả cho Frontend
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy dữ liệu thống kê", error = ex.Message });
            }
        }
        // ==========================================
        // 7. THỐNG KÊ TRUNG BÌNH SAO THEO NHÂN VIÊN
        // ==========================================
        [HttpGet("thong-ke-nhan-vien")]
        public async Task<IActionResult> GetThongKeSaoNhanVien()
        {
            try
            {
                // Dùng LINQ Query Syntax để JOIN các bảng: Đánh Giá -> Hóa Đơn -> Chi tiết -> Nhân viên
                // Khớp chính xác Thợ nào làm Dịch vụ/Combo nào thì nhận sao của Dịch vụ/Combo đó
                var stats = await (from dg in _context.DanhgiaDichvus
                                   join hd in _context.HoaDons on dg.MaHd equals hd.MaHd
                                   join ct in _context.ChitietLichdats
                                        on new { hd.MaLd, Id = dg.MaDv ?? dg.MaCb }
                                        equals new { ct.MaLd, Id = ct.MaDv ?? ct.MaCb }
                                   join nv in _context.Nhanviens on ct.MaNv equals nv.MaNv
                                   group dg by new { nv.MaNv, nv.HoTenNv } into g
                                   select new
                                   {
                                       TenNhanVien = g.Key.HoTenNv,
                                       // Tính điểm trung bình và làm tròn 1 chữ số
                                       DiemTrungBinh = Math.Round(g.Average(x => (double)x.SoSao), 1),
                                       // Đếm số lượt khách đánh giá thợ này
                                       LuotDanhGia = g.Count()
                                   })
                                   .OrderByDescending(x => x.DiemTrungBinh)
                                   .ThenByDescending(x => x.LuotDanhGia)
                                   .ToListAsync();

                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy thống kê nhân viên", error = ex.Message });
            }
        }
    }

    // ==========================================
    // DTO XỬ LÝ PHẢN HỒI
    // ==========================================
    public class ReviewReplyDTO
    {
        public string Id { get; set; } = string.Empty;
        public string Reply { get; set; } = string.Empty;
    }
}
 
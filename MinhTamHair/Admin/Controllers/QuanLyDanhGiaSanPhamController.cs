using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.IO;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class QuanLyDanhGiaSanPhamController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IWebHostEnvironment _env;

        public QuanLyDanhGiaSanPhamController(MinhTamHairContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ==========================================
        // 1. LẤY TOÀN BỘ DANH SÁCH ĐÁNH GIÁ SẢN PHẨM
        // ==========================================
        [HttpGet("get-all")]
        public async Task<IActionResult> GetAllProductReviews()
        {
            try
            {
                var reviews = await _context.DanhgiaSanphams
                    .Include(dg => dg.MaHdNavigation)
                        .ThenInclude(hd => hd.MaKhNavigation) // Lấy thông tin khách hàng từ hóa đơn
                    .Include(dg => dg.MaSpNavigation)         // Lấy thông tin sản phẩm
                    .Select(dg => new
                    {
                        Id = dg.MaHd + "_" + dg.MaSanPham, // Tạo ID ảo để xử lý ở FE
                        MaHd = dg.MaHd,

                        // Lấy tên khách hàng
                        TenKhach = dg.MaHdNavigation.MaKhNavigation != null
                                    ? dg.MaHdNavigation.MaKhNavigation.HoTen : "Khách vãng lai",
                        Sdt = dg.MaHdNavigation.MaKhNavigation != null
                                    ? dg.MaHdNavigation.MaKhNavigation.Sdt : "",

                        TenSanPham = dg.MaSpNavigation != null ? dg.MaSpNavigation.TenSp : "Sản phẩm",

                        SoSao = dg.SoSao,
                        NoiDung = dg.NoiDung,
                        HinhAnh = dg.HinhAnh,
                        NgayDanhGia = dg.NgayDanhGia,

                        IsHidden = dg.TrangThai == 0, // 1: Hiện, 0: Ẩn
                        PhanHoiAdmin = dg.PhanHoi,
                        DaPhanHoi = !string.IsNullOrEmpty(dg.PhanHoi)
                    })
                    .OrderByDescending(r => r.NgayDanhGia)
                    .ToListAsync();

                return Ok(reviews);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy dữ liệu đánh giá sản phẩm", error = ex.Message });
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
                var parts = dto.Id.Split('_');
                if (parts.Length != 2) return BadRequest("ID không hợp lệ.");

                string maHd = parts[0];
                string maSanPham = parts[1];

                var review = await _context.DanhgiaSanphams
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && dg.MaSanPham == maSanPham);

                if (review == null) return NotFound("Không tìm thấy đánh giá sản phẩm.");

                review.PhanHoi = dto.Reply;

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
                string maSanPham = parts[1];

                var review = await _context.DanhgiaSanphams
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && dg.MaSanPham == maSanPham);

                if (review == null) return NotFound("Không tìm thấy đánh giá sản phẩm.");

                // Đảo ngược trạng thái: 1 = Hiện, 0 = Ẩn
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
                string maSanPham = parts[1];

                var review = await _context.DanhgiaSanphams
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && dg.MaSanPham == maSanPham);

                if (review == null) return NotFound("Không tìm thấy đánh giá.");

                // Xóa file ảnh vật lý trên Server
                if (!string.IsNullOrEmpty(review.HinhAnh))
                {
                    var filePath = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), review.HinhAnh.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                _context.DanhgiaSanphams.Remove(review);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã xóa vĩnh viễn đánh giá sản phẩm!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // 5. THỐNG KÊ TRUNG BÌNH SAO THEO SẢN PHẨM
        // ==========================================
        [HttpGet("thong-ke")]
        public async Task<IActionResult> GetThongKeSaoSanPham()
        {
            try
            {
                var stats = await _context.DanhgiaSanphams
                    .Include(dg => dg.MaSpNavigation)
                    .GroupBy(dg => new {
                        Id = dg.MaSanPham,
                        Ten = dg.MaSpNavigation != null ? dg.MaSpNavigation.TenSp : "Sản phẩm khác"
                    })
                    .Select(g => new
                    {
                        TenSanPham = g.Key.Ten,
                        // Làm tròn 1 chữ số thập phân
                        DiemTrungBinh = Math.Round(g.Average(x => (double)(x.SoSao ?? 0)), 1),
                        LuotDanhGia = g.Count()
                    })
                    .OrderByDescending(x => x.DiemTrungBinh)
                    .ThenByDescending(x => x.LuotDanhGia)
                    .ToListAsync();

                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy dữ liệu thống kê sản phẩm", error = ex.Message });
            }
        }
    }
}
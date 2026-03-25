using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Security.Claims;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Yêu cầu phải có Token (đã đăng nhập)
    public class ProfileController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IWebHostEnvironment _env;

        public ProfileController(MinhTamHairContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // --- 1. LẤY THÔNG TIN PROFILE ---
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

            var user = await _context.Khachhangs
                .Include(k => k.MaRankNavigation)
                .FirstOrDefaultAsync(k => k.MaKh == userId);

            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            return Ok(new
            {
                success = true,
                data = new
                {
                    hoTen = user.HoTen,
                    email = user.Email,
                    sdt = user.Sdt,
                    hinhAnh = user.HinhAnh,
                    gioiTinh = user.GioiTinh, // Lấy thêm giới tính từ DB
                    diemTichLuy = user.DiemTichLuy,
                    tenRank = user.MaRankNavigation?.TenRank ?? "Thành viên Mới"
                }
            });
        }

        // --- 2. CẬP NHẬT THÔNG TIN CƠ BẢN ---
        [HttpPut("update")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Khachhangs.FirstOrDefaultAsync(k => k.MaKh == userId);

            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            // 1. Kiểm tra Email hoặc SĐT này đã có người KHÁC dùng chưa
            var checkDuplicate = await _context.Khachhangs
                .FirstOrDefaultAsync(k => (k.Email == request.Email || k.Sdt == request.Sdt) && k.MaKh != userId);

            if (checkDuplicate != null)
            {
                if (checkDuplicate.Sdt == request.Sdt)
                    return BadRequest(new { success = false, message = "Số điện thoại này đã được đăng ký bởi tài khoản khác." });
                if (checkDuplicate.Email == request.Email)
                    return BadRequest(new { success = false, message = "Email này đã được đăng ký bởi tài khoản khác." });
            }

            // 2. Cập nhật thông tin thực tế có trong DB
            user.HoTen = request.HoTen;
            user.Email = request.Email;
            user.Sdt = request.Sdt;
            user.GioiTinh = request.GioiTinh;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật thông tin thành công!" });
        }

        // --- 3. UPLOAD ẢNH ĐẠI DIỆN VẬT LÝ ---
        [HttpPost("upload-avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            // (Phần này giữ nguyên như code hôm trước của bạn đã chạy thành công)
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Vui lòng chọn ảnh." });

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng .jpg, .jpeg, .png." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Khachhangs.FirstOrDefaultAsync(k => k.MaKh == userId);
            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            string uploadsFolder = Path.Combine(_env.ContentRootPath, "UserUploads", "avatars");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            user.HinhAnh = "/UserUploads/avatars/" + uniqueFileName;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, imageUrl = user.HinhAnh, message = "Cập nhật ảnh thành công!" });
        }

        // --- 4. LẤY DANH SÁCH HẠNG MỨC THÀNH VIÊN ---
        [HttpGet("ranks")]
        public async Task<IActionResult> GetRanks()
        {
            // Lấy danh sách hạng và sắp xếp theo điểm tối thiểu tăng dần
            var ranks = await _context.Hangthanhviens
                .Where(r => r.TrangThai == 1)
                .OrderBy(r => r.DiemToiThieu)
                .Select(r => new {
                    maRank = r.MaRank,
                    tenRank = r.TenRank,
                    diemToiThieu = r.DiemToiThieu,
                    phanTramGiamGia = r.PhanTramGiamGia
                })
                .ToListAsync();

            return Ok(new { success = true, data = ranks });
        }
    }

    // Class nhận dữ liệu cập nhật
    public class UpdateProfileRequest
    {
        public string HoTen { get; set; } = null!;
        public string Sdt { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? GioiTinh { get; set; }
    }
}
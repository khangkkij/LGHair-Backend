using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Security.Claims;
using System.IO;
using MinhTamHair.Services.UploadIMG; // 📌 Namespace chính xác trong project của bạn

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Yêu cầu phải có Token (đã đăng nhập)
    public class ProfileController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IUploadIMG _uploadImg; // 📌 Service Cloudinary chính xác

        public ProfileController(MinhTamHairContext context, IUploadIMG uploadImg)
        {
            _context = context;
            _uploadImg = uploadImg;
        }

        // --- 1. LẤY THÔNG TIN PROFILE (TỰ ĐỘNG TÍNH ĐIỂM DỰA TRÊN TỔNG TIỀN) ---
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

            var user = await _context.Khachhangs
                .Include(k => k.MaRankNavigation)
                .FirstOrDefaultAsync(k => k.MaKh == userId);

            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            // 1. TÍNH TỔNG TIỀN ĐỂ XÉT HẠNG
            var tongTienDaChi = await _context.HoaDons
                .Where(h => h.MaKh == userId)
                .SumAsync(h => h.TongTien ?? 0);

            // 2. Quy đổi ra điểm (1 điểm = 10.000 VNĐ)
            int diemMoi = (int)(tongTienDaChi / 10000);

            // 3. Lấy danh sách các hạng thành viên sắp xếp từ CAO xuống THẤP
            var allRanks = await _context.Hangthanhviens
                .Where(r => r.TrangThai == 1)
                .OrderByDescending(r => r.DiemToiThieu)
                .ToListAsync();

            // 4. Tìm hạng phù hợp nhất với số điểm hiện tại
            string? maRankMoi = null;
            foreach (var rank in allRanks)
            {
                if (diemMoi >= rank.DiemToiThieu)
                {
                    maRankMoi = rank.MaRank;
                    break;
                }
            }

            // 5. Nếu có sự thay đổi về Điểm hoặc Hạng thì cập nhật ngay vào CSDL
            if (user.DiemTichLuy != diemMoi || user.MaRank != maRankMoi)
            {
                user.DiemTichLuy = diemMoi;
                user.MaRank = maRankMoi;
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(maRankMoi))
                {
                    await _context.Entry(user).Reference(u => u.MaRankNavigation).LoadAsync();
                }
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    hoTen = user.HoTen,
                    email = user.Email,
                    sdt = user.Sdt,
                    hinhAnh = user.HinhAnh,
                    gioiTinh = user.GioiTinh,
                    diemTichLuy = user.DiemTichLuy,
                    tongTienDaChi = tongTienDaChi,
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

            var checkDuplicate = await _context.Khachhangs
                .FirstOrDefaultAsync(k => (k.Email == request.Email || k.Sdt == request.Sdt) && k.MaKh != userId);

            if (checkDuplicate != null)
            {
                if (checkDuplicate.Sdt == request.Sdt)
                    return BadRequest(new { success = false, message = "Số điện thoại này đã được đăng ký bởi tài khoản khác." });
                if (checkDuplicate.Email == request.Email)
                    return BadRequest(new { success = false, message = "Email này đã được đăng ký bởi tài khoản khác." });
            }

            user.HoTen = request.HoTen;
            user.Email = request.Email;
            user.Sdt = request.Sdt;
            user.GioiTinh = request.GioiTinh;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật thông tin thành công!" });
        }

        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] System.Text.Json.JsonElement request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Khachhangs.FirstOrDefaultAsync(k => k.MaKh == userId);

            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            if (!request.TryGetProperty("oldPassword", out var op) || !request.TryGetProperty("newPassword", out var np))
                return BadRequest(new { success = false, message = "Thiếu thông tin mật khẩu." });

            string oldPassword = op.GetString()!;
            string newPassword = np.GetString()!;

            // Kiểm tra mật khẩu cũ
            if (user.MatKhau?.Trim() != oldPassword.Trim())
            {
                return BadRequest(new { success = false, message = "Mật khẩu cũ không chính xác." });
            }

            // Cập nhật mật khẩu mới
            user.MatKhau = newPassword.Trim();
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đổi mật khẩu thành công!" });
        }

        // --- 3. UPLOAD ẢNH ĐẠI DIỆN LÊN CLOUDINARY ---
        [HttpPost("upload-avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Vui lòng chọn ảnh." });

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng .jpg, .jpeg, .png, .webp." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Khachhangs.FirstOrDefaultAsync(k => k.MaKh == userId);
            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            try
            {
                // 📌 Tải ảnh trực tiếp lên Cloudinary bằng IUploadIMG của dự án
                var uploadResult = await _uploadImg.AddIMG(file);

                if (uploadResult?.Error != null || string.IsNullOrEmpty(uploadResult?.SecureUrl?.ToString()))
                {
                    return StatusCode(500, new { success = false, message = "Lỗi khi tải ảnh lên Cloudinary: " + uploadResult?.Error?.Message });
                }

                // 📌 Lưu link Cloudinary vào CSDL
                user.HinhAnh = uploadResult.SecureUrl.ToString();
                await _context.SaveChangesAsync();

                return Ok(new { success = true, imageUrl = user.HinhAnh, message = "Cập nhật ảnh đại diện thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        // --- 4. LẤY DANH SÁCH HẠNG MỨC THÀNH VIÊN ---
        [HttpGet("ranks")]
        public async Task<IActionResult> GetRanks()
        {
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

    public class UpdateProfileRequest
    {
        public string HoTen { get; set; } = null!;
        public string Sdt { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? GioiTinh { get; set; }
    }
}

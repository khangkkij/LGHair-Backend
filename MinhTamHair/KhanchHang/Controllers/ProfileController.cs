using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Security.Claims;
using System.IO;
using MinhTamHair.Services.UploadIMG;
using Google.Apis.Auth;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Yêu cầu phải có Token (đã đăng nhập)
    public class ProfileController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IUploadIMG _uploadImg;
        private readonly IConfiguration _configuration;

        public ProfileController(MinhTamHairContext context, IUploadIMG uploadImg, IConfiguration configuration)
        {
            _context = context;
            _uploadImg = uploadImg;
            _configuration = configuration;
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

            if (user.TrangThai == 0)
            {
                return Unauthorized(new { success = false, isLocked = true, message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });
            }

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

            bool isGoogleAccount = user.MaKh != null && user.MaKh.StartsWith("GG", StringComparison.OrdinalIgnoreCase);

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
                    tenRank = user.MaRankNavigation?.TenRank ?? "Thành viên Mới",
                    isGoogleAccount = isGoogleAccount,
                    hasPassword = !string.IsNullOrEmpty(user.MatKhau)
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

            if (user.TrangThai == 0)
            {
                return Unauthorized(new { success = false, isLocked = true, message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });
            }

            // Nếu người dùng xóa trống Email khi sửa nhưng trong hệ thống đã có Email -> Giữ nguyên Email cũ
            string? finalEmail = !string.IsNullOrWhiteSpace(request.Email) ? request.Email.Trim() : user.Email;
            string finalSdt = !string.IsNullOrWhiteSpace(request.Sdt) ? request.Sdt.Trim() : (user.Sdt ?? "");

            var checkDuplicate = await _context.Khachhangs
                .FirstOrDefaultAsync(k => ((!string.IsNullOrEmpty(finalEmail) && k.Email == finalEmail) || 
                                           (!string.IsNullOrEmpty(finalSdt) && k.Sdt == finalSdt)) && k.MaKh != userId);

            if (checkDuplicate != null)
            {
                if (!string.IsNullOrEmpty(finalSdt) && checkDuplicate.Sdt == finalSdt)
                    return BadRequest(new { success = false, message = "Số điện thoại này đã được đăng ký bởi tài khoản khác." });
                if (!string.IsNullOrEmpty(finalEmail) && checkDuplicate.Email == finalEmail)
                    return BadRequest(new { success = false, message = "Email này đã được đăng ký bởi tài khoản khác." });
            }

            if (!string.IsNullOrWhiteSpace(request.HoTen))
            {
                user.HoTen = request.HoTen.Trim();
            }

            // Cập nhật email nếu có truyền vào giá trị mới, nếu xóa trống thì giữ nguyên email cũ của user
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = request.Email.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Sdt))
            {
                user.Sdt = request.Sdt.Trim();
            }
            if (!string.IsNullOrWhiteSpace(request.GioiTinh))
            {
                user.GioiTinh = request.GioiTinh.Trim();
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, email = user.Email, message = "Cập nhật thông tin thành công!" });
        }

        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] System.Text.Json.JsonElement request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Khachhangs.FirstOrDefaultAsync(k => k.MaKh == userId);

            if (user == null) return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

            if (user.TrangThai == 0)
            {
                return Unauthorized(new { success = false, isLocked = true, message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });
            }

            if (!request.TryGetProperty("newPassword", out var np) || string.IsNullOrWhiteSpace(np.GetString()))
            {
                return BadRequest(new { success = false, message = "Vui lòng nhập mật khẩu mới." });
            }

            string newPassword = np.GetString()!.Trim();
            if (newPassword.Length < 6)
            {
                return BadRequest(new { success = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự." });
            }

            // PHƯƠNG ÁN 1: XÁC MINH DANH TÍNH BẰNG TÀI KHOẢN GOOGLE
            if (request.TryGetProperty("googleCredential", out var gc) && !string.IsNullOrWhiteSpace(gc.GetString()))
            {
                try
                {
                    string googleToken = gc.GetString()!;
                    var settings = new GoogleJsonWebSignature.ValidationSettings()
                    {
                        Audience = new List<string>() { _configuration["Google:ClientId"] }
                    };

                    var payload = await GoogleJsonWebSignature.ValidateAsync(googleToken, settings);
                    if (payload == null || string.IsNullOrEmpty(payload.Email))
                    {
                        return BadRequest(new { success = false, message = "Xác thực danh tính Google không hợp lệ." });
                    }

                    // Email Google phải trùng với Email tài khoản
                    if (!string.IsNullOrEmpty(user.Email) && !string.Equals(user.Email.Trim(), payload.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { success = false, message = $"Tài khoản Google ({payload.Email}) không khớp với Email của tài khoản này ({user.Email})." });
                    }

                    // Cập nhật mật khẩu mới bằng BCrypt
                    user.MatKhau = BCrypt.Net.BCrypt.HashPassword(newPassword);
                    await _context.SaveChangesAsync();

                    return Ok(new { success = true, message = "Xác minh Google thành công! Đã cập nhật mật khẩu mới." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[GOOGLE VERIFY ERROR] " + ex.Message);
                    return BadRequest(new { success = false, message = "Xác minh Google thất bại hoặc phiên xác thực đã hết hạn." });
                }
            }

            // PHƯƠNG ÁN 2: XÁC MINH BẰNG MẬT KHẨU CŨ
            if (!request.TryGetProperty("oldPassword", out var op) || string.IsNullOrWhiteSpace(op.GetString()))
            {
                return BadRequest(new { success = false, message = "Vui lòng nhập mật khẩu hiện tại hoặc chọn xác minh bằng tài khoản Google." });
            }

            string oldPassword = op.GetString()!.Trim();

            bool isOldPasswordCorrect = false;
            if (!string.IsNullOrEmpty(user.MatKhau))
            {
                if (user.MatKhau.StartsWith("$2a$") || user.MatKhau.StartsWith("$2b$") || user.MatKhau.StartsWith("$2y$"))
                {
                    isOldPasswordCorrect = BCrypt.Net.BCrypt.Verify(oldPassword, user.MatKhau);
                }
                else
                {
                    isOldPasswordCorrect = (user.MatKhau.Trim() == oldPassword);
                }
            }
            else
            {
                return BadRequest(new { success = false, message = "Tài khoản của bạn được tạo qua Google và chưa có mật khẩu. Vui lòng bấm 'Xác minh Google' để thiết lập mật khẩu mới." });
            }

            if (!isOldPasswordCorrect)
            {
                return BadRequest(new { success = false, message = "Mật khẩu hiện tại không chính xác." });
            }

            // Cập nhật mật khẩu mới
            user.MatKhau = BCrypt.Net.BCrypt.HashPassword(newPassword);
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

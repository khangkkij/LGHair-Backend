using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MinhTamHair.Admin.Models;
using MinhTamHair.Models;
namespace MinhTamHair.Admin.Controllers
{
    [Route("api/admin/auth")]
    [ApiController]
    public class AccountController : Controller
    {

        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;

        public AccountController(MinhTamHairContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] AdminLoginRequest request)
        {
            var nhanVien = await _context.Nhanviens
                .Include(nv => nv.MaRoleNavigation)
                .Include(nv => nv.MaLoaiNvNavigation)
                .FirstOrDefaultAsync(nv =>
                    (nv.TenDangNhap == request.Username || nv.Email == request.Username));

            // LƯU Ý: Chỗ này hãy nhớ dùng BCrypt để check mật khẩu như mình đã nhắc ở bước trước nhé!
            // Tạm thời mình vẫn để logic cũ để bạn dễ hình dung sườn code
            if (nhanVien == null || nhanVien.MatKhau != request.Password)
            {
                return Unauthorized(new { success = false, message = "Tài khoản hoặc mật khẩu không chính xác." });
            }

            if (nhanVien.TrangThai != 1)
            {
                return BadRequest(new { success = false, message = "Tài khoản của bạn đã bị khóa." });
            }

            // 1. Tạo cặp Token
            var accessToken = GenerateJwtToken(nhanVien); // JWT sống 15 phút
            var refreshToken = GenerateRefreshToken();    // Chuỗi ngẫu nhiên

            // 2. LƯU VÀO BẢNG Refreshtoken CÓ SẴN CỦA BẠN
            var rtRecord = new Refreshtoken
            {
                UserId = nhanVien.MaNv, // Dùng MaNv gán vào cột UserId
                Token = refreshToken,
                IssuedAt = DateTime.UtcNow,
                ExpiredAt = DateTime.UtcNow.AddDays(7) // Sống 7 ngày
            };

            _context.Refreshtokens.Add(rtRecord);
            await _context.SaveChangesAsync();

            // 3. Giấu vào HttpOnly Cookie
            SetTokenCookie("adminAccessToken", accessToken, 15);
            SetTokenCookie("adminRefreshToken", refreshToken, 7 * 24 * 60);

            return Ok(new
            {
                success = true,
                message = "Đăng nhập thành công",
                accessToken = accessToken,
                user = new
                {
                    maNv = nhanVien.MaNv,
                    hoTen = nhanVien.HoTenNv,
                    hinhAnh = nhanVien.HinhAnh,
                    chucVu = nhanVien.MaRoleNavigation?.TenChucVu ?? "Chưa phân quyền",
                    maRole = nhanVien.MaRole
                }
            });
        }

        // ==========================================
        // THÊM MỚI: API LÀM MỚI TOKEN (REFRESH TOKEN)
        // ==========================================
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            // 1. Lấy Refresh Token cũ từ Cookie
            if (!Request.Cookies.TryGetValue("adminRefreshToken", out var oldRefreshToken))
            {
                return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
            }

            // 2. TÌM TRONG BẢNG Refreshtoken
            var rtRecord = await _context.Refreshtokens.FirstOrDefaultAsync(rt => rt.Token == oldRefreshToken);

            // 3. Kiểm tra tính hợp lệ (Tồn tại không? Hết hạn chưa?)
            if (rtRecord == null || rtRecord.ExpiredAt <= DateTime.UtcNow)
            {
                // Nếu token lởm, xóa luôn cookie cũ cho sạch
                Response.Cookies.Delete("adminAccessToken");
                Response.Cookies.Delete("adminRefreshToken");
                return Unauthorized(new { message = "Refresh Token không hợp lệ hoặc đã hết hạn." });
            }

            // 4. Lấy thông tin Nhân viên dựa trên UserId đã lưu
            var nhanVien = await _context.Nhanviens
                .Include(nv => nv.MaRoleNavigation)
                .FirstOrDefaultAsync(nv => nv.MaNv == rtRecord.UserId);

            if (nhanVien == null || nhanVien.TrangThai != 1)
            {
                return Unauthorized(new { message = "Tài khoản nhân viên không tồn tại hoặc bị khóa." });
            }

            // 5. Tạo cặp Token mới
            var newAccessToken = GenerateJwtToken(nhanVien);
            var newRefreshTokenString = GenerateRefreshToken();

            // 6. Cập nhật đè lên Record cũ trong bảng (Tái sử dụng dòng dữ liệu, đỡ rác DB)
            rtRecord.Token = newRefreshTokenString;
            rtRecord.IssuedAt = DateTime.UtcNow;
            rtRecord.ExpiredAt = DateTime.UtcNow.AddDays(7);
            await _context.SaveChangesAsync();

            // 7. Gắn Cookie mới cho trình duyệt
            SetTokenCookie("adminAccessToken", newAccessToken, 15);
            SetTokenCookie("adminRefreshToken", newRefreshTokenString, 7 * 24 * 60);

            return Ok(new { success = true, message = "Làm mới Token thành công." });
        }
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // 1. Lấy Refresh Token của thiết bị hiện tại từ Cookie
            if (Request.Cookies.TryGetValue("adminRefreshToken", out string refreshToken))
            {
                // Tìm token này trong Database
                var refreshTokenEntry = await _context.Refreshtokens
                    .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

                // Nếu có, tiến hành xóa để vô hiệu hóa nó vĩnh viễn
                if (refreshTokenEntry != null)
                {
                    _context.Refreshtokens.Remove(refreshTokenEntry);
                    await _context.SaveChangesAsync();
                }
            }

            // 2. Ra lệnh cho trình duyệt xóa sạch Cookie bảo mật của Admin
            var cookieOptions = new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Strict
            };

            Response.Cookies.Delete("adminAccessToken", cookieOptions);
            Response.Cookies.Delete("adminRefreshToken", cookieOptions);

            return Ok(new { success = true, message = "Đã đăng xuất an toàn khỏi thiết bị này." });
        }


        // ==========================================
        // CÁC HÀM HỖ TRỢ (HELPER METHODS)
        // ==========================================

        // Hàm tạo chuỗi Refresh Token ngẫu nhiên và bảo mật
        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        // Hàm set Cookie an toàn
        // Hàm set Cookie an toàn (Đã sửa để chạy được trên Localhost)
        private void SetTokenCookie(string cookieName, string token, int expireMinutes)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = false, // Đổi thành FALSE để chạy được trên HTTP Localhost
                SameSite = SameSiteMode.Lax, // Đổi thành LAX để Frontend nhận được Cookie
                Expires = DateTime.UtcNow.AddMinutes(expireMinutes)
            };
            Response.Cookies.Append(cookieName, token, cookieOptions);
        }

        // --- HÀM TẠO JWT TOKEN CHO ADMIN/NHÂN VIÊN ---
        private string GenerateJwtToken(Nhanvien nv)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

            // Chuẩn hóa mã role
            var maRole = (nv.MaRole ?? "").Trim().ToUpper();

            // Nếu là R01 thì coi là Admin
            // Các role khác coi là Staff
            var roleName = maRole == "R01" ? "Admin" : "Staff";

            var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, nv.MaNv),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.NameIdentifier, nv.MaNv),
        new Claim(ClaimTypes.Name, nv.HoTenNv ?? ""),

        // Lưu mã nhân viên
        new Claim("MaNv", nv.MaNv),

        // Lưu mã role gốc trong DB, ví dụ R01, R03, R04
        new Claim("MaRole", maRole),

        // QUAN TRỌNG:
        // Cho phép controller check [Authorize(Roles = "R01")]
        new Claim(ClaimTypes.Role, maRole),

        // QUAN TRỌNG:
        // Cho phép controller check [Authorize(Roles = "Admin")]
        new Claim(ClaimTypes.Role, roleName)
    };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(15),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"],
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }
    }

}

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

			if (nhanVien == null)
			{
				return Unauthorized(new { success = false, message = "Tài khoản hoặc mật khẩu không chính xác." });
			}

			bool isPasswordValid = false;
			if (!string.IsNullOrEmpty(nhanVien.MatKhau))
			{
				if (nhanVien.MatKhau.StartsWith("$2a$") || nhanVien.MatKhau.StartsWith("$2b$") || nhanVien.MatKhau.StartsWith("$2y$"))
				{
					isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, nhanVien.MatKhau);
				}
				else
				{
					isPasswordValid = (nhanVien.MatKhau == request.Password);
				}
			}

			if (!isPasswordValid)
			{
				return Unauthorized(new { success = false, message = "Tài khoản hoặc mật khẩu không chính xác." });
			}

			if (nhanVien.TrangThai != 1)
			{
				return BadRequest(new { success = false, message = "Tài khoản của bạn đã bị khóa." });
			}

			var accessToken = GenerateJwtToken(nhanVien);
			var refreshToken = GenerateRefreshToken();

			// Kiểm tra khắt khe: Mỗi nhân viên chỉ được 1 phiên đăng nhập
			// Xóa toàn bộ token cũ của nhân viên này để thiết bị cũ bị đăng xuất
			var oldTokens = await _context.Refreshtokens
				.Where(rt => rt.UserId == nhanVien.MaNv)
				.ToListAsync();
			if (oldTokens.Any())
			{
				_context.Refreshtokens.RemoveRange(oldTokens);
			}

			var rtRecord = new Refreshtoken
			{
				UserId = nhanVien.MaNv,
				Token = refreshToken,
				IssuedAt = DateTime.UtcNow,
				ExpiredAt = DateTime.UtcNow.AddDays(7)
			};

			_context.Refreshtokens.Add(rtRecord);
			await _context.SaveChangesAsync();

			SetTokenCookie("adminAccessToken", accessToken, 120);
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
		//  LÀM MỚI TOKEN (REFRESH TOKEN)
		// ==========================================
		[HttpPost("refresh-token")]
		public async Task<IActionResult> RefreshToken()
		{
			if (!Request.Cookies.TryGetValue("adminRefreshToken", out var oldRefreshToken))
			{
				return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
			}

			var rtRecord = await _context.Refreshtokens.FirstOrDefaultAsync(rt => rt.Token == oldRefreshToken);

			if (rtRecord == null || rtRecord.ExpiredAt <= DateTime.UtcNow)
			{
				Response.Cookies.Delete("adminAccessToken");
				Response.Cookies.Delete("adminRefreshToken");

				// Nếu token hết hạn, dọn luôn nó khỏi DB
				if (rtRecord != null && rtRecord.ExpiredAt <= DateTime.UtcNow)
				{
					_context.Refreshtokens.Remove(rtRecord);
					await _context.SaveChangesAsync();
				}

				return Unauthorized(new { message = "Refresh Token không hợp lệ hoặc đã hết hạn." });
			}

			// Dọn dẹp các token rác khác của nhân viên này
			var expiredTokens = await _context.Refreshtokens
				.Where(rt => rt.UserId == rtRecord.UserId && rt.ExpiredAt <= DateTime.UtcNow && rt.Id != rtRecord.Id)
				.ToListAsync();
			if (expiredTokens.Any())
			{
				_context.Refreshtokens.RemoveRange(expiredTokens);
			}

			var nhanVien = await _context.Nhanviens
				.Include(nv => nv.MaRoleNavigation)
				.FirstOrDefaultAsync(nv => nv.MaNv == rtRecord.UserId);

			if (nhanVien == null || nhanVien.TrangThai != 1)
			{
				return Unauthorized(new { message = "Tài khoản nhân viên không tồn tại hoặc bị khóa." });
			}

			var newAccessToken = GenerateJwtToken(nhanVien);
			var newRefreshTokenString = GenerateRefreshToken();

			rtRecord.Token = newRefreshTokenString;
			rtRecord.IssuedAt = DateTime.UtcNow;
			rtRecord.ExpiredAt = DateTime.UtcNow.AddDays(7);
			await _context.SaveChangesAsync();

			SetTokenCookie("adminAccessToken", newAccessToken, 120);
			SetTokenCookie("adminRefreshToken", newRefreshTokenString, 7 * 24 * 60);

			return Ok(new { success = true, message = "Làm mới Token thành công.", accessToken = newAccessToken });
		}
		[HttpPost("logout")]
		public async Task<IActionResult> Logout()
		{
			if (Request.Cookies.TryGetValue("adminRefreshToken", out string refreshToken))
			{
				var refreshTokenEntry = await _context.Refreshtokens
					.FirstOrDefaultAsync(rt => rt.Token == refreshToken);

				if (refreshTokenEntry != null)
				{
					_context.Refreshtokens.Remove(refreshTokenEntry);
					await _context.SaveChangesAsync();
				}
			}

			var cookieOptions = new CookieOptions
			{
				Secure = true,
				SameSite = SameSiteMode.Strict
			};

			Response.Cookies.Delete("adminAccessToken", cookieOptions);
			Response.Cookies.Delete("adminRefreshToken", cookieOptions);

			return Ok(new { success = true, message = "Đã đăng xuất an toàn khỏi thiết bị này." });
		}

		private string GenerateRefreshToken()
		{
			var randomNumber = new byte[64];
			using var rng = RandomNumberGenerator.Create();
			rng.GetBytes(randomNumber);
			return Convert.ToBase64String(randomNumber);
		}
		private void SetTokenCookie(string cookieName, string token, int expireMinutes)
		{
			var cookieOptions = new CookieOptions
			{
				HttpOnly = true,
				Secure = false, 
				SameSite = SameSiteMode.Lax,
				Expires = DateTime.UtcNow.AddMinutes(expireMinutes)
			};
			Response.Cookies.Append(cookieName, token, cookieOptions);
		}

		// --- HÀM TẠO JWT TOKEN CHO ADMIN/NHÂN VIÊN ---
		private string GenerateJwtToken(Nhanvien nv)
		{
			var jwtSettings = _configuration.GetSection("Jwt");
			var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);
			var maRole = (nv.MaRole ?? "").Trim().ToUpper();
			var roleName = maRole == "R01" ? "Admin" : "Staff";
			var claims = new List<Claim>
			{
				new Claim(JwtRegisteredClaimNames.Sub, nv.MaNv),
				new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
				new Claim(ClaimTypes.NameIdentifier, nv.MaNv),
				new Claim(ClaimTypes.Name, nv.HoTenNv ?? ""),
				new Claim("MaNv", nv.MaNv),
				new Claim("MaRole", maRole),
				new Claim(ClaimTypes.Role, maRole),
				new Claim(ClaimTypes.Role, roleName)
			};

			var tokenDescriptor = new SecurityTokenDescriptor
			{
				Subject = new ClaimsIdentity(claims),
				Expires = DateTime.UtcNow.AddMinutes(120),
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

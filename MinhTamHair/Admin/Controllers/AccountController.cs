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
				IssuedAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
				ExpiredAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().AddHours(12)
			};

			_context.Refreshtokens.Add(rtRecord);
			await _context.SaveChangesAsync();

			SetTokenCookie("adminAccessToken", accessToken, 30);
			SetTokenCookie("adminRefreshToken", refreshToken, 12 * 60);

			return Ok(new
			{
				success = true,
				message = "Đăng nhập thành công",
				accessToken = accessToken,
				refreshToken = refreshToken,
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
		public async Task<IActionResult> RefreshToken([FromBody] AdminRefreshTokenRequest? request = null)
		{
			// 1. Ưu tiên lấy Refresh Token từ Body (nếu client gửi trực tiếp)
			string oldRefreshToken = request?.RefreshToken;

			// Nếu Body không có, lấy từ Cookie
			if (string.IsNullOrEmpty(oldRefreshToken))
			{
				Request.Cookies.TryGetValue("adminRefreshToken", out oldRefreshToken);
			}

			if (string.IsNullOrEmpty(oldRefreshToken))
			{
				return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
			}

			var rtRecord = await _context.Refreshtokens.FirstOrDefaultAsync(rt => rt.Token == oldRefreshToken);

			if (rtRecord == null || rtRecord.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
			{
				var deleteCookieOptions = new CookieOptions { Secure = true, SameSite = SameSiteMode.None };
				Response.Cookies.Delete("adminAccessToken", deleteCookieOptions);
				Response.Cookies.Delete("adminRefreshToken", deleteCookieOptions);

				// Nếu token hết hạn, dọn luôn nó khỏi DB
				if (rtRecord != null && rtRecord.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
				{
					_context.Refreshtokens.Remove(rtRecord);
					await _context.SaveChangesAsync();
				}

				return Unauthorized(new { message = "Refresh Token không hợp lệ hoặc đã hết hạn." });
			}

			// Dọn dẹp các token rác khác của nhân viên này
			var expiredTokens = await _context.Refreshtokens
				.Where(rt => rt.UserId == rtRecord.UserId && rt.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now() && rt.Id != rtRecord.Id)
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
			rtRecord.IssuedAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
			rtRecord.ExpiredAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().AddHours(12);
			await _context.SaveChangesAsync();

			SetTokenCookie("adminAccessToken", newAccessToken, 30);
			SetTokenCookie("adminRefreshToken", newRefreshTokenString, 12 * 60);

			return Ok(new
			{
				success = true,
				message = "Làm mới Token thành công.",
				accessToken = newAccessToken,
				refreshToken = newRefreshTokenString
			});
		}
		[HttpPost("logout")]
		public async Task<IActionResult> Logout([FromBody] AdminRefreshTokenRequest? request = null)
		{
			string refreshToken = request?.RefreshToken;
			if (string.IsNullOrEmpty(refreshToken))
			{
				Request.Cookies.TryGetValue("adminRefreshToken", out refreshToken);
			}

			if (!string.IsNullOrEmpty(refreshToken))
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
				SameSite = SameSiteMode.None
			};

			Response.Cookies.Delete("adminAccessToken", cookieOptions);
			Response.Cookies.Delete("adminRefreshToken", cookieOptions);

			return Ok(new { success = true, message = "Đã đăng xuất an toàn khỏi thiết bị này." });
		}

		// ==========================================
		//  KIỂM TRA PHIÊN NGẦM (HEARTBEAT / PING)
		// ==========================================
		[HttpGet("ping")]
		[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,Quản trị viên,Quản trị viên (Admin),Staff,R01,R02,R03,R04")]
		public IActionResult Ping()
		{
			return Ok(new { success = true, serverTime = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now() });
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
				Secure = true, 
				SameSite = SameSiteMode.None,
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
				new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
				new Claim(ClaimTypes.NameIdentifier, nv.MaNv),
				new Claim(ClaimTypes.Name, nv.HoTenNv ?? ""),
				new Claim("MaNv", nv.MaNv),
				new Claim("MaRole", maRole),
				new Claim(ClaimTypes.Role, maRole),
				new Claim(ClaimTypes.Role, roleName)
			};

			var nowUtc = DateTime.UtcNow;
			var tokenDescriptor = new SecurityTokenDescriptor
			{
				Subject = new ClaimsIdentity(claims),
				IssuedAt = nowUtc,
				NotBefore = nowUtc,
				Expires = nowUtc.AddMinutes(30), // JWT Access token 30 phút
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

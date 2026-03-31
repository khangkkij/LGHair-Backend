using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;


namespace MinhTamHair.KhanchHang.Controllers
{

	[Route("api/[controller]")]
	[ApiController]
	public class AccountController : ControllerBase
	{
		private readonly MinhTamHairContext _context;
		private readonly IConfiguration _configuration;
		private readonly IMemoryCache _cache;
		private readonly IEmailService _emailService;

		public AccountController(MinhTamHairContext context, IConfiguration configuration, IMemoryCache cache, IEmailService emailService)
		{
			_context = context;
			_configuration = configuration;
			_cache = cache;
			_emailService = emailService;
		}

		[HttpPost("login")]
		public async Task<IActionResult> Login([FromBody] KhachHangLoginRequest request)
		{
			if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
				return BadRequest(new { success = false, message = "Vui lòng nhập tài khoản và mật khẩu." });

			var khachHang = await _context.Khachhangs
				.FirstOrDefaultAsync(kh => (kh.Email == request.Username || kh.Sdt == request.Username)
											&& kh.MatKhau == request.Password && kh.TrangThai == 1);

			if (khachHang == null)
				return Unauthorized(new { success = false, message = "Tài khoản hoặc mật khẩu không chính xác." });

			// Cập nhật lại Rank dựa trên điểm hiện tại trước khi trả về cho khách
			khachHang.MaRank = await GetRankByPointsAsync(khachHang.DiemTichLuy);
			await _context.SaveChangesAsync();

			var accessToken = GenerateJwtToken(khachHang);
			var refreshToken = GenerateRefreshToken();

			// Lưu Refresh Token
			var refreshTokenEntity = new Refreshtoken
			{
				UserId = khachHang.MaKh,
				Token = refreshToken,
				IssuedAt = DateTime.Now,
				ExpiredAt = DateTime.Now.AddDays(7)
			};
			_context.Refreshtokens.Add(refreshTokenEntity);
			await _context.SaveChangesAsync();

			// Lấy thông tin user kèm tên Rank để hiển thị ở Frontend
			var userResponse = await _context.Khachhangs
				.Include(rank => rank.MaRankNavigation)
				.FirstOrDefaultAsync(kh => kh.MaKh == khachHang.MaKh);

			return Ok(new
			{
				success = true,
				message = "Đăng nhập thành công!",
				accessToken,
				refreshToken,
				user = new
				{
					maKH = userResponse.MaKh,
					hoTen = userResponse.HoTen,
					hinhAnh = userResponse.HinhAnh,
					maRank = userResponse.MaRank,
					tenRank = userResponse.MaRankNavigation?.TenRank // Trả về tên hạng thực tế
				}
			});
		}
		// --- HÀM HỖ TRỢ TẠO JWT TOKEN ---
		private string GenerateJwtToken(Khachhang kh)
		{
			var jwtSettings = _configuration.GetSection("Jwt");
			var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

			// Đưa thông tin user vào Payload của Token (gọi là Claims)
			var claims = new List<Claim>
			{
				new Claim(JwtRegisteredClaimNames.Sub, kh.MaKh),
				new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
				new Claim(ClaimTypes.NameIdentifier, kh.MaKh),
				new Claim(ClaimTypes.Name, kh.HoTen ?? ""),
				new Claim(ClaimTypes.Email, kh.Email ?? ""),
				new Claim(ClaimTypes.Role, "KhachHang") // Phân quyền là Khách hàng
            };

			var tokenDescriptor = new SecurityTokenDescriptor
			{
				Subject = new ClaimsIdentity(claims),
				Expires = DateTime.UtcNow.AddMinutes(30), // Access token sống 30 phút
				Issuer = jwtSettings["Issuer"],
				Audience = jwtSettings["Audience"],
				SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
			};

			var tokenHandler = new JwtSecurityTokenHandler();
			var token = tokenHandler.CreateToken(tokenDescriptor);

			return tokenHandler.WriteToken(token);
		}

		// --- HÀM HỖ TRỢ TẠO REFRESH TOKEN ---
		private string GenerateRefreshToken()
		{
			var randomBytes = new byte[32];
			using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(randomBytes);
				return Convert.ToBase64String(randomBytes);
			}
		}

		[HttpPost("register")]
		public async Task<IActionResult> Register([FromBody] KhachHangRegisterRequest request)
		{
			// 1. Validate dữ liệu cơ bản
			if (string.IsNullOrEmpty(request.HoTen) || string.IsNullOrEmpty(request.Sdt) || string.IsNullOrEmpty(request.MatKhau))
			{
				return BadRequest(new { success = false, message = "Vui lòng nhập đầy đủ thông tin bắt buộc." });
			}

			// 2. Kiểm tra Số điện thoại hoặc Email đã tồn tại chưa
			var isExist = await _context.Khachhangs.AnyAsync(kh => kh.Sdt == request.Sdt || (request.Email != "" && kh.Email == request.Email));
			if (isExist)
			{
				return BadRequest(new { success = false, message = "Số điện thoại hoặc Email này đã được đăng ký." });
			}

			// 3. Tạo mã Khách Hàng mới (Ví dụ: KH + chuỗi ngẫu nhiên 6 số)
			// Lưu ý: Tùy logic công ty bạn mà sửa cách tạo mã này cho đẹp nhé
			string newMaKh = "KH" + new Random().Next(100000, 999999).ToString();

			// 4. Tạo Object Khách hàng mới
			var newKhachHang = new Khachhang
			{
				MaKh = newMaKh,
				HoTen = request.HoTen,
				Sdt = request.Sdt,
				Email = request.Email,
				MatKhau = request.MatKhau, // Thực tế nên Hash (mã hóa) mật khẩu trước khi lưu
				NgayTao = DateTime.Now,
				MaRank = await GetRankByPointsAsync(0),
				TrangThai = 1, // 1 là Active
				DiemTichLuy = 0
			};

			_context.Khachhangs.Add(newKhachHang);
			await _context.SaveChangesAsync();

			// 5. TỰ ĐỘNG ĐĂNG NHẬP: Tạo luôn Token cho user vừa đăng ký
			var accessToken = GenerateJwtToken(newKhachHang);
			var refreshToken = GenerateRefreshToken();

			var refreshTokenEntity = new Refreshtoken
			{
				UserId = newKhachHang.MaKh,
				Token = refreshToken,
				IssuedAt = DateTime.Now,
				ExpiredAt = DateTime.Now.AddDays(7)
			};

			_context.Refreshtokens.Add(refreshTokenEntity);
			await _context.SaveChangesAsync();

			var userResponse = await _context.Khachhangs
	.Include(kh => kh.MaRankNavigation)
	.FirstOrDefaultAsync(kh => kh.MaKh == newKhachHang.MaKh);

			// 6. Trả về kết quả giống hệt API Login
			return Ok(new
			{
				success = true,
				message = "Đăng ký thành công!",
				accessToken = accessToken,
				refreshToken = refreshToken,
				user = new
				{
					maKH = userResponse.MaKh,
					hoTen = userResponse.HoTen,
					hinhAnh = userResponse.HinhAnh,
					maRank = userResponse.MaRank,
					tenRank = userResponse.MaRankNavigation?.TenRank
				}
			});
		}
		[HttpPost("logout")]
		public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
		{
			if (string.IsNullOrEmpty(request.RefreshToken))
			{
				return BadRequest(new { success = false, message = "RefreshToken không hợp lệ." });
			}

			// Tìm RefreshToken trong CSDL (Lưu ý: Tên bảng Refreshtokens và class Refreshtoken viết thường chữ t)
			var refreshTokenEntry = await _context.Refreshtokens
				.FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

			if (refreshTokenEntry != null)
			{
				// Xóa Refresh Token để không thể dùng nó cấp mới Access Token được nữa
				_context.Refreshtokens.Remove(refreshTokenEntry);
				await _context.SaveChangesAsync();
			}

			return Ok(new { success = true, message = "Đã đăng xuất ở máy chủ." });
		}
		[HttpPost("google-login")]
		public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
		{
			try
			{
				// 1. Xác thực Token gửi từ Frontend với Google
				var settings = new GoogleJsonWebSignature.ValidationSettings()
				{
					Audience = new List<string>() { _configuration["Google:ClientId"] }
				};

				var payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential, settings);

				// 2. Kiểm tra khách hàng đã tồn tại trong DB chưa (dựa vào Email)
				var khachHang = await _context.Khachhangs
				.Include(kh => kh.MaRankNavigation)
					.FirstOrDefaultAsync(kh => kh.Email == payload.Email);

				// 3. Nếu chưa có, tạo tài khoản mới tự động
				if (khachHang == null)
				{
					khachHang = new Khachhang
					{
						MaKh = "GG" + new Random().Next(100000, 999999).ToString(),
						HoTen = payload.Name,
						Email = payload.Email,
						HinhAnh = payload.Picture,
						NgayTao = DateTime.Now,
						TrangThai = 1,
						MaRank = await GetRankByPointsAsync(0), // Không gán cứng RANK01
						DiemTichLuy = 0,
						MatKhau = Guid.NewGuid().ToString()
					};
					_context.Khachhangs.Add(khachHang);
					await _context.SaveChangesAsync();
				}
				else
				{
					// Nếu khách đã có tài khoản, cập nhật rank theo điểm hiện tại
					khachHang.MaRank = await GetRankByPointsAsync(khachHang.DiemTichLuy);
					await _context.SaveChangesAsync();
				}

				// 4. Tạo bộ Token của hệ thống bạn (Tái sử dụng hàm cũ)
				var accessToken = GenerateJwtToken(khachHang);
				var refreshToken = GenerateRefreshToken();

				var refreshTokenEntity = new Refreshtoken
				{
					UserId = khachHang.MaKh,
					Token = refreshToken,
					IssuedAt = DateTime.Now,
					ExpiredAt = DateTime.Now.AddDays(7)
				};

				_context.Refreshtokens.Add(refreshTokenEntity);
				await _context.SaveChangesAsync();

				var userResponse = await _context.Khachhangs
	.Include(kh => kh.MaRankNavigation)
	.FirstOrDefaultAsync(kh => kh.MaKh == khachHang.MaKh);

				return Ok(new
				{
					success = true,
					accessToken,
					refreshToken,
					user = new
					{
						maKH = userResponse.MaKh,
						hoTen = userResponse.HoTen,
						hinhAnh = userResponse.HinhAnh,
						maRank = userResponse.MaRank,
						tenRank = userResponse.MaRankNavigation?.TenRank
					}
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine("Lỗi xác thực Google: " + ex.Message);
				return BadRequest(new { success = false, message = "Xác thực Google thất bại." });
			}
		}
		// --- PHƯƠNG ÁN 1: XÁC MINH QUA GOOGLE ---
		[HttpPost("forgot-password-google")]
		public async Task<IActionResult> ForgotPasswordGoogle([FromBody] GoogleLoginRequest request)
		{
			try
			{
				var settings = new GoogleJsonWebSignature.ValidationSettings()
				{
					Audience = new List<string>() { _configuration["Google:ClientId"] }
				};
				var payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential, settings);

				var user = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.Email == payload.Email);
				if (user == null) return BadRequest(new { success = false, message = "Email này chưa đăng ký thành viên." });

				// Lưu trạng thái đã xác minh vào Cache trong 10 phút để cho phép đổi pass
				_cache.Set($"Verified_{payload.Email}", true, TimeSpan.FromMinutes(10));

				return Ok(new { success = true, email = payload.Email });
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return BadRequest(new { success = false, message = "Xác thực Google thất bại." });
			}
		}

		// --- PHƯƠNG ÁN 2: GỬI OTP QUA EMAIL ---
		[HttpPost("send-otp")]
		public async Task<IActionResult> SendOTP([FromBody] string email)
		{
			var user = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.Email == email);
			if (user == null) return BadRequest(new { success = false, message = "Email không tồn tại trên hệ thống." });

			string otp = new Random().Next(100000, 999999).ToString();
			_cache.Set(email, otp, TimeSpan.FromMinutes(5));

			// Gửi mail thực tế với giao diện HTML đẹp mắt
			string subject = "Mã xác nhận khôi phục mật khẩu - Minh Tâm Hair";
			string body = $@"
        <div style='font-family: Arial, sans-serif; border: 1px solid #d4af37; padding: 20px; border-radius: 10px; max-width: 500px;'>
            <h2 style='color: #8a6d3b;'>Xác nhận thay đổi mật khẩu</h2>
            <p>Chào bạn,</p>
            <p>Mã OTP của bạn là: <b style='font-size: 24px; color: #d4af37;'>{otp}</b></p>
            <p>Mã này có hiệu lực trong <b>5 phút</b>. Vui lòng không cung cấp mã này cho bất kỳ ai.</p>
            <hr style='border: 0.5px solid #eee;'>
            <p style='font-size: 12px; color: #888;'>Đây là email tự động, vui lòng không phản hồi.</p>
        </div>";

			try
			{
				await _emailService.SendEmailAsync(email, subject, body);
				return Ok(new { success = true, message = "Mã xác nhận đã được gửi vào Email." });
			}
			catch (Exception ex)
			{
				return BadRequest(new { success = false, message = "Không thể gửi mail: " + ex.Message });
			}
		}

		// --- BƯỚC CUỐI: ĐẶT LẠI MẬT KHẨU ---
		[HttpPost("reset-password")]
		public async Task<IActionResult> ResetPassword([FromBody] KhachHangResetPasswordRequest request)
		{
			// 1. Kiểm tra xác minh (OTP hoặc Google)
			bool isVerified = false;

			if (!string.IsNullOrEmpty(request.OTP))
			{
				// Kiểm tra OTP từ cache
				if (_cache.TryGetValue(request.Email, out string savedOtp) && savedOtp == request.OTP)
				{
					isVerified = true;
					_cache.Remove(request.Email); // Dùng xong thì xóa OTP
				}
			}
			else
			{
				// Kiểm tra trạng thái xác minh từ Google
				if (_cache.TryGetValue($"Verified_{request.Email}", out bool googleVerified) && googleVerified)
				{
					isVerified = true;
					_cache.Remove($"Verified_{request.Email}");
				}
			}

			if (!isVerified) return BadRequest(new { success = false, message = "Mã xác nhận không đúng hoặc đã hết hạn." });

			// 2. Cập nhật mật khẩu mới
			var user = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.Email == request.Email);
			if (user == null) return BadRequest(new { success = false, message = "Người dùng không tồn tại." });

			user.MatKhau = request.NewPassword; // Nhớ Hash mật khẩu ở đây
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Đổi mật khẩu thành công!" });
		}
		// Hàm xác định MaRank bằng cách truy vấn Database mốc điểm từ bảng HANGTHANHVIEN
		private async Task<string> GetRankByPointsAsync(int? points)
		{
			int p = points ?? 0;

			// Lấy danh sách các hạng đang hoạt động, sắp xếp theo điểm tối thiểu giảm dần
			var ranks = await _context.Hangthanhviens
				.Where(r => r.TrangThai == 1)
				.OrderByDescending(r => r.DiemToiThieu)
				.ToListAsync();

			// Tìm hạng đầu tiên mà điểm của khách >= điểm tối thiểu của hạng đó
			var currentRank = ranks.FirstOrDefault(r => p >= r.DiemToiThieu);

			// Trả về MaRank tìm được, mặc định là RANK01 nếu không tìm thấy
			return currentRank?.MaRank ?? "RANK01";
		}
		[HttpGet("get-profile")]
		[Authorize]
		public async Task<IActionResult> GetProfile()
		{
			var maKh = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

			if (string.IsNullOrEmpty(maKh)) return Unauthorized();

			// Chỉ truy vấn DB đúng 1 lần
			var khachHang = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.MaKh == maKh);
			if (khachHang == null) return NotFound();

			// Tính lại Rank mới nhất
			khachHang.MaRank = await GetRankByPointsAsync(khachHang.DiemTichLuy);
			await _context.SaveChangesAsync();

			// TỐI ƯU Ở ĐÂY: Thay vì query DB tìm khách hàng lần nữa để Include, 
			// ta chỉ yêu cầu EF Core nạp thêm dữ liệu của MaRankNavigation vào object hiện tại
			await _context.Entry(khachHang).Reference(k => k.MaRankNavigation).LoadAsync();

			return Ok(new
			{
				success = true,
				user = new
				{
					maKH = khachHang.MaKh,
					hoTen = khachHang.HoTen,
					hinhAnh = khachHang.HinhAnh,
					maRank = khachHang.MaRank,
					tenRank = khachHang.MaRankNavigation?.TenRank,
					diemTichLuy = khachHang.DiemTichLuy
				}
			});
		}
	}
}




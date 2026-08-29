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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;


namespace MinhTamHair.KhanchHang.Controllers
{
    // Thêm class này để hỗ trợ nhận RefreshToken từ Body cho App Mobile
    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly IDistributedCache _distributedCache;
        private readonly IEmailService _emailService;

        public AccountController(MinhTamHairContext context, IConfiguration configuration, IMemoryCache cache, IDistributedCache distributedCache, IEmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _cache = cache;
            _distributedCache = distributedCache;
            _emailService = emailService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] KhachHangLoginRequest request)
        {
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
                return BadRequest(new { success = false, message = "Vui lòng nhập tài khoản và mật khẩu." });

            var khachHang = await _context.Khachhangs
                .FirstOrDefaultAsync(kh => kh.Email == request.Username || kh.Sdt == request.Username);

            if (khachHang == null)
                return Unauthorized(new { success = false, message = "Tài khoản hoặc mật khẩu không chính xác." });
                
            bool isPasswordValid = false;
            if (!string.IsNullOrEmpty(khachHang.MatKhau))
            {
                if (khachHang.MatKhau.StartsWith("$2a$") || khachHang.MatKhau.StartsWith("$2b$") || khachHang.MatKhau.StartsWith("$2y$"))
                {
                    isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, khachHang.MatKhau);
                }
                else
                {
                    isPasswordValid = (khachHang.MatKhau == request.Password);
                    // Có thể hash lại mật khẩu cũ ở đây nếu muốn: 
                    // if (isPasswordValid) khachHang.MatKhau = BCrypt.Net.BCrypt.HashPassword(request.Password);
                }
            }

            if (!isPasswordValid)
                return Unauthorized(new { success = false, message = "Tài khoản hoặc mật khẩu không chính xác." });

			if (khachHang.TrangThai == 0)
			{
				return BadRequest(new { success = false, message = "Tài khoản đã bị khóa. Vui lòng đăng nhập lại hoặc liên hệ hỗ trợ." });
			}

            // Cập nhật lại Rank dựa trên điểm hiện tại trước khi trả về cho khách
            khachHang.MaRank = await GetRankByPointsAsync(khachHang.DiemTichLuy);
            await _context.SaveChangesAsync();

            var accessToken = GenerateJwtToken(khachHang);
            var refreshToken = GenerateRefreshToken();

            // Dọn dẹp các token đã hết hạn của khách hàng này
            var expiredTokens = await _context.Refreshtokens
                .Where(rt => rt.UserId == khachHang.MaKh && rt.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
                .ToListAsync();
            if (expiredTokens.Any())
            {
                _context.Refreshtokens.RemoveRange(expiredTokens);
            }

            // Lưu Refresh Token
            var refreshTokenEntity = new Refreshtoken
            {
                UserId = khachHang.MaKh,
                Token = refreshToken,
                IssuedAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                ExpiredAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().AddDays(7)
            };
            _context.Refreshtokens.Add(refreshTokenEntity);

            // Gọi hàm lưu vào Cookie
            int expireDays = request.RememberMe ? 30 : 1; // Tích thì 30 ngày, không thì 1 ngày
            SetTokenCookie("accessToken", accessToken, expireDays);
            SetTokenCookie("refreshToken", refreshToken, 7); // Refresh Token sống 7 ngày

            await _context.SaveChangesAsync();

            // Lấy thông tin user kèm tên Rank để hiển thị ở Frontend
            var userResponse = await _context.Khachhangs
                .Include(rank => rank.MaRankNavigation)
                .FirstOrDefaultAsync(kh => kh.MaKh == khachHang.MaKh);

            return Ok(new
            {
                success = true,
                message = "Đăng nhập thành công!",
                user = new
                {
                    maKH = userResponse.MaKh,
                    hoTen = userResponse.HoTen,
                    hinhAnh = userResponse.HinhAnh,
                    maRank = userResponse.MaRank,
                    tenRank = userResponse.MaRankNavigation?.TenRank, // Trả về tên hạng thực tế
                    email = userResponse.Email,
                    sdt = userResponse.Sdt,
                    token = accessToken, // Có thể trả token luôn nếu Frontend muốn lưu vào localStorage thay vì Cookie
                    refreshToken = refreshToken
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
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(ClaimTypes.NameIdentifier, kh.MaKh),
                new Claim(ClaimTypes.Name, kh.HoTen ?? ""),
                new Claim(ClaimTypes.Email, kh.Email ?? ""),
                new Claim(ClaimTypes.Role, "KhachHang") // Phân quyền là Khách hàng
            };

            var nowVn = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                IssuedAt = nowVn,
                NotBefore = nowVn,
                Expires = nowVn.AddMinutes(30), // Access token sống 30 phút
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

            // 1.5 Validate Số điện thoại hợp lệ (đầu số nhà mạng Việt Nam: 03, 05, 07, 08, 09 và gồm 10 chữ số)
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Sdt, @"^(03|05|07|08|09)\d{8}$"))
            {
                return BadRequest(new { success = false, message = "Số điện thoại không hợp lệ (phải bắt đầu bằng 03, 05, 07, 08, 09 và bao gồm 10 chữ số)." });
            }

            // 1.6 Validate Email độ dài tối đa 100 ký tự
            if (!string.IsNullOrEmpty(request.Email) && request.Email.Trim().Length > 100)
            {
                return BadRequest(new { success = false, message = "Email không được vượt quá 100 ký tự." });
            }

            // 2. Kiểm tra Số điện thoại hoặc Email đã tồn tại chưa
            var isExist = await _context.Khachhangs.AnyAsync(kh => kh.Sdt == request.Sdt || (!string.IsNullOrEmpty(request.Email) && kh.Email == request.Email));
            if (isExist)
            {
                return BadRequest(new { success = false, message = "Số điện thoại hoặc Email này đã được đăng ký." });
            }

            // 2.5 Kiểm tra mã OTP nếu có nhập Email
            if (!string.IsNullOrEmpty(request.Email))
            {
                if (string.IsNullOrEmpty(request.OTP))
                {
                    return BadRequest(new { success = false, message = "Vui lòng nhập mã xác nhận OTP từ Email." });
                }

                if (_cache.TryGetValue($"RegOTP_{request.Email}", out string savedOtp) && savedOtp == request.OTP)
                {
                    _cache.Remove($"RegOTP_{request.Email}"); // Dùng xong thì xóa OTP
                }
                else
                {
                    return BadRequest(new { success = false, message = "Mã xác nhận không đúng hoặc đã hết hạn." });
                }
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
                MatKhau = BCrypt.Net.BCrypt.HashPassword(request.MatKhau), // Đã mã hóa mật khẩu bằng BCrypt
                NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                MaRank = await GetRankByPointsAsync(0),
                TrangThai = 1, // 1 là Active
                DiemTichLuy = 0
            };

            _context.Khachhangs.Add(newKhachHang);
            await _context.SaveChangesAsync();

            // 5. TỰ ĐỘNG ĐĂNG NHẬP: Tạo luôn Token cho user vừa đăng ký
            var accessToken = GenerateJwtToken(newKhachHang);
            var refreshToken = GenerateRefreshToken();

            // Gọi hàm lưu vào Cookie
            SetTokenCookie("accessToken", accessToken, 1); // Access Token sống 1 ngày
            SetTokenCookie("refreshToken", refreshToken, 7); // Refresh Token sống 7 ngày

            var refreshTokenEntity = new Refreshtoken
            {
                UserId = newKhachHang.MaKh,
                Token = refreshToken,
                IssuedAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                ExpiredAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().AddDays(7)
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
                user = new
                {
                    maKH = userResponse.MaKh,
                    hoTen = userResponse.HoTen,
                    hinhAnh = userResponse.HinhAnh,
                    maRank = userResponse.MaRank,
                    tenRank = userResponse.MaRankNavigation?.TenRank,
                    email = userResponse.Email,
                    sdt = userResponse.Sdt,
                    token = accessToken,
                    refreshToken = refreshToken
                }
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request = null)
        {
            // 1. Tìm Refresh Token từ Body hoặc Cookie
            string? refreshToken = request?.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                Request.Cookies.TryGetValue("refreshToken", out refreshToken);
            }

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var refreshTokenEntry = await _context.Refreshtokens
                    .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

                if (refreshTokenEntry != null)
                {
                    // Xóa Refresh Token trong Database
                    _context.Refreshtokens.Remove(refreshTokenEntry);
                    await _context.SaveChangesAsync();
                }
            }

            // 2. "Ra lệnh" cho trình duyệt xóa sạch Cookie bảo mật
            var cookieOptions = new CookieOptions { Secure = true, SameSite = SameSiteMode.None };
            Response.Cookies.Delete("accessToken", cookieOptions);
            Response.Cookies.Delete("refreshToken", cookieOptions);

            return Ok(new { success = true, message = "Đã đăng xuất an toàn." });
        }

        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> LogoutAll()
        {
            var maKh = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(maKh)) return Unauthorized();

            // 1. Thu hồi JWT Token ngay lập tức bằng cách ghi nhận thời điểm hủy vào Cache
            var currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                await _distributedCache.SetStringAsync($"UserRevokedAt_{maKh}", currentUnixTime.ToString(), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CACHE SET WARNING] {ex.Message}");
            }
            _cache.Set($"UserRevokedAt_{maKh}", currentUnixTime, TimeSpan.FromHours(24));

            // 2. Tìm và xóa TẤT CẢ Refresh Token của User này
            var allRefreshTokens = await _context.Refreshtokens
                .Where(rt => rt.UserId == maKh)
                .ToListAsync();

            if (allRefreshTokens.Any())
            {
                _context.Refreshtokens.RemoveRange(allRefreshTokens);
                await _context.SaveChangesAsync();
            }

            // 3. Xóa Cookie ở máy hiện tại
            var cookieOptions = new CookieOptions { Secure = true, SameSite = SameSiteMode.None };
            Response.Cookies.Delete("accessToken", cookieOptions);
            Response.Cookies.Delete("refreshToken", cookieOptions);

            return Ok(new { success = true, message = "Đã đăng xuất và hủy toàn bộ phiên làm việc trên mọi thiết bị." });
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

                if (khachHang != null && khachHang.TrangThai == 0)
                {
                    if (khachHang.MaKh != null && khachHang.MaKh.StartsWith("GG", StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { success = false, message = "Tài khoản đã bị khóa. Vui lòng đăng nhập lại hoặc liên hệ hỗ trợ." });
                    }
                    return BadRequest(new { success = false, message = "Tài khoản Google này liên kết với một tài khoản đã bị khóa. Không thể đăng nhập." });
                }

                // 3. Nếu chưa có, tạo tài khoản mới tự động
                if (khachHang == null)
                {
                    khachHang = new Khachhang
                    {
                        MaKh = "GG" + new Random().Next(100000, 999999).ToString(),
                        HoTen = payload.Name,
                        Email = payload.Email,
                        HinhAnh = payload.Picture,
                        NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                        TrangThai = 1,
                        MaRank = await GetRankByPointsAsync(0), // Không gán cứng RANK01
                        DiemTichLuy = 0,
                        MatKhau = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString())
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

                // Gọi hàm lưu vào Cookie
                SetTokenCookie("accessToken", accessToken, 1); // Access Token sống 1 ngày
                SetTokenCookie("refreshToken", refreshToken, 7); // Refresh Token sống 7 ngày

                var refreshTokenEntity = new Refreshtoken
                {
                    UserId = khachHang.MaKh,
                    Token = refreshToken,
                    IssuedAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                    ExpiredAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().AddDays(7)
                };

                _context.Refreshtokens.Add(refreshTokenEntity);
                await _context.SaveChangesAsync();

                var userResponse = await _context.Khachhangs
                    .Include(kh => kh.MaRankNavigation)
                    .FirstOrDefaultAsync(kh => kh.MaKh == khachHang.MaKh);

                return Ok(new
                {
                    success = true,
                    user = new
                    {
                        maKH = userResponse.MaKh,
                        hoTen = userResponse.HoTen,
                        hinhAnh = userResponse.HinhAnh,
                        maRank = userResponse.MaRank,
                        tenRank = userResponse.MaRankNavigation?.TenRank,
                        email = userResponse.Email,
                        sdt = userResponse.Sdt,
                        token = accessToken,
                        refreshToken = refreshToken
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
            if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 100)
                return BadRequest(new { success = false, message = "Email không hợp lệ hoặc vượt quá độ dài tối đa 100 ký tự." });

            // Kiểm tra Rate Limit (Chặn gửi quá dồn dập trong vòng 60 giây)
            if (_cache.TryGetValue($"OTP_Limit_{email}", out _))
            {
                return BadRequest(new { success = false, message = "Bạn vừa gửi yêu cầu OTP. Vui lòng thử lại sau 60 giây." });
            }

            var user = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.Email == email);
            if (user == null) return BadRequest(new { success = false, message = "Email không tồn tại trên hệ thống." });

            string otp = new Random().Next(100000, 999999).ToString();
            _cache.Set(email, otp, TimeSpan.FromMinutes(5));
            // Lưu thời gian khóa tần suất gửi (Cooldown 60 giây)
            _cache.Set($"OTP_Limit_{email}", true, TimeSpan.FromSeconds(60));

            // Gửi mail thực tế với giao diện HTML đẹp mắt
            string subject = "Mã xác nhận khôi phục mật khẩu - LGHair";
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

        // --- PHƯƠNG ÁN 2.5: GỬI OTP XÁC MINH ĐĂNG KÝ ---
        [HttpPost("send-register-otp")]
        public async Task<IActionResult> SendRegisterOTP([FromBody] string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 100) 
                return BadRequest(new { success = false, message = "Email không hợp lệ hoặc vượt quá độ dài tối đa 100 ký tự." });

            // Kiểm tra Rate Limit (Chặn gửi dồn dập trong vòng 60 giây)
            if (_cache.TryGetValue($"RegOTP_Limit_{email}", out _))
            {
                return BadRequest(new { success = false, message = "Bạn vừa gửi yêu cầu OTP. Vui lòng thử lại sau 60 giây." });
            }

            var user = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.Email == email);
            if (user != null) return BadRequest(new { success = false, message = "Email này đã được sử dụng." });

            string otp = new Random().Next(100000, 999999).ToString();
            _cache.Set($"RegOTP_{email}", otp, TimeSpan.FromMinutes(5));
            // Lưu thời gian khóa tần suất gửi (Cooldown 60 giây)
            _cache.Set($"RegOTP_Limit_{email}", true, TimeSpan.FromSeconds(60));

            string subject = "Mã xác nhận đăng ký tài khoản - LGHair";
            string body = $@"
        <div style='font-family: Arial, sans-serif; border: 1px solid #d4af37; padding: 20px; border-radius: 10px; max-width: 500px;'>
            <h2 style='color: #8a6d3b;'>Xác nhận đăng ký tài khoản mới</h2>
            <p>Chào bạn,</p>
            <p>Mã OTP của bạn là: <b style='font-size: 24px; color: #d4af37;'>{otp}</b></p>
            <p>Mã này có hiệu lực trong <b>5 phút</b>. Vui lòng sử dụng mã này để hoàn tất đăng ký tài khoản.</p>
            <hr style='border: 0.5px solid #eee;'>
            <p style='font-size: 12px; color: #888;'>Đây là email tự động, vui lòng không phản hồi.</p>
        </div>";

            try
            {
                await _emailService.SendEmailAsync(email, subject, body);
                return Ok(new { success = true, message = "Mã xác nhận đăng ký đã được gửi vào Email." });
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

            user.MatKhau = BCrypt.Net.BCrypt.HashPassword(request.NewPassword); // Đã Hash mật khẩu
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
                    diemTichLuy = khachHang.DiemTichLuy,
                    email = khachHang.Email,
                    sdt = khachHang.Sdt
                }
            });
        }

        private void SetTokenCookie(string cookieName, string token, int expireDays)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true, // KHÔNG cho phép JavaScript (Vue) đọc cookie này -> Chống XSS
                Secure = true, // Bắt buộc dùng HTTPS (localhost của bạn đang dùng https://localhost:7196 là OK)
                SameSite = SameSiteMode.None, // RẤT QUAN TRỌNG: Cho phép gửi cookie chéo cổng (ví dụ Vue chạy port 5173, .NET chạy 7196)
                Expires = DateTime.UtcNow.AddDays(expireDays)
            };
            Response.Cookies.Append(cookieName, token, cookieOptions);
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest? request = null)
        {
            // 1. Lấy Refresh Token từ Body (ưu tiên cho Mobile App)
            string oldRefreshToken = request?.RefreshToken;

            // Nếu Body không có (có thể do gọi từ Web), thử lấy từ Cookie
            if (string.IsNullOrEmpty(oldRefreshToken))
            {
                Request.Cookies.TryGetValue("refreshToken", out oldRefreshToken);
            }

            if (string.IsNullOrEmpty(oldRefreshToken))
            {
                return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn." });
            }

            // 2. Tìm trong DB xem Token này có hợp lệ không
            var rtRecord = await _context.Refreshtokens.FirstOrDefaultAsync(rt => rt.Token == oldRefreshToken);

            if (rtRecord == null || rtRecord.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
            {
                // Token lởm hoặc hết hạn 7 ngày -> Xóa cookie luôn
                var cookieOptions = new CookieOptions { Secure = true, SameSite = SameSiteMode.None };
                Response.Cookies.Delete("accessToken", cookieOptions);
                Response.Cookies.Delete("refreshToken", cookieOptions);

                // Nếu token hết hạn, xóa nó khỏi DB
                if (rtRecord != null && rtRecord.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now())
                {
                    _context.Refreshtokens.Remove(rtRecord);
                    await _context.SaveChangesAsync();
                }

                return Unauthorized(new { message = "Vui lòng đăng nhập lại." });
            }

            // Dọn dẹp các token rác khác của khách hàng này
            var expiredTokens = await _context.Refreshtokens
                .Where(rt => rt.UserId == rtRecord.UserId && rt.ExpiredAt <= MinhTamHair.Services.TimeHelper.VnTimeHelper.Now() && rt.Id != rtRecord.Id)
                .ToListAsync();
            if (expiredTokens.Any())
            {
                _context.Refreshtokens.RemoveRange(expiredTokens);
            }

            // 3. Lấy thông tin khách hàng
            var khachHang = await _context.Khachhangs.FirstOrDefaultAsync(kh => kh.MaKh == rtRecord.UserId);

            if (khachHang == null) return Unauthorized();

            if (khachHang.TrangThai == 0)
            {
                return Unauthorized(new { message = "Tài khoản đã bị khóa. Vui lòng đăng nhập lại hoặc liên hệ hỗ trợ." });
            }

            // 4. Cấp bộ Token mới
            var newAccessToken = GenerateJwtToken(khachHang);
            var newRefreshTokenString = GenerateRefreshToken();

            // 5. Cập nhật vào Database
            rtRecord.Token = newRefreshTokenString;
            rtRecord.IssuedAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            rtRecord.ExpiredAt = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().AddDays(7);
            await _context.SaveChangesAsync();

            // 6. Gắn lại vào Cookie (cho Web)
            SetTokenCookie("accessToken", newAccessToken, 1);
            SetTokenCookie("refreshToken", newRefreshTokenString, 7);

            // 7. TRẢ VỀ JSON CHO MOBILE APP LƯU LẠI
            return Ok(new
            {
                success = true,
                message = "Làm mới Token thành công.",
                token = newAccessToken,
                refreshToken = newRefreshTokenString
            });
        }

        [HttpGet("verify-session")]
        [Authorize]
        public async Task<IActionResult> VerifySession()
        {
            // Lấy Refresh Token từ Cookie của máy đang gọi API này
            if (Request.Cookies.TryGetValue("refreshToken", out string refreshToken))
            {
                // Kiểm tra xem token này còn tồn tại trong DB không (nếu bị máy khác logout-all thì sẽ mất)
                var exists = await _context.Refreshtokens.AnyAsync(rt => rt.Token == refreshToken);
                if (!exists)
                {
                    return Unauthorized(new { message = "Phiên đăng nhập đã bị thu hồi." });
                }
                return Ok(new { success = true, message = "Session is valid" });
            }
            return Unauthorized(new { message = "Không tìm thấy token." });
        }
    }
}

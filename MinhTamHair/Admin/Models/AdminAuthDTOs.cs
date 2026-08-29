using System.ComponentModel.DataAnnotations;

namespace MinhTamHair.Admin.Models
{

	public class AdminLoginRequest
	{
		[Required(ErrorMessage = "Vui lòng nhập tài khoản.")]
		public string Username { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
		public string Password { get; set; } = null!;
	}

	public class AdminRefreshTokenRequest
	{
		public string? RefreshToken { get; set; }
	}

}

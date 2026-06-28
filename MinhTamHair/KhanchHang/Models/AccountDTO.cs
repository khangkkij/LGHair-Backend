namespace MinhTamHair.KhanchHang.Models
{
	public class KhachHangLoginRequest
	{
		public string Username { get; set; }
		public string Password { get; set; }
		public bool RememberMe { get; set; }
	}

	public class KhachHangRegisterRequest
	{
		public string HoTen { get; set; }
		public string Sdt { get; set; }
		public string Email { get; set; }
		public string MatKhau { get; set; }
		public string? OTP { get; set; }
	}
	public class LogoutRequest
	{
		public string RefreshToken { get; set; }
	}

	public class GoogleLoginRequest
	{
		public string Credential { get; set; }
	}

	public class KhachHangResetPasswordRequest
	{
		public string Email { get; set; }
		public string? OTP { get; set; }
		public string NewPassword { get; set; }
	}

}

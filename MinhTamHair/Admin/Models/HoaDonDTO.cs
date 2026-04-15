namespace MinhTamHair.Admin.Models
{
	public class HoaDonResponse
	{
		public string MaHd { get; set; } = null!;
		public string? MaLd { get; set; }
		public string? TenKhachHang { get; set; }
		public string? TenNhanVien { get; set; }
		public DateTime? NgayTao { get; set; }
		public decimal? TongTien { get; set; }
		public decimal? ThanhTien { get; set; }
		public decimal? TienDaTra { get; set; }
		public int? TrangThai { get; set; } // 0: Chờ, 1: Đã thanh toán, 2: Đã hủy
	}
}

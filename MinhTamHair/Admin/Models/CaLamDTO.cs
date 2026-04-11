using System.ComponentModel.DataAnnotations;

namespace MinhTamHair.Admin.Models
{
	public class CaLamDTO
	{
		// DTO để nhận yêu cầu Tạo ca làm mới
		public class CreateLichLamViecRequest
		{
			[Required]
			public string MaNv { get; set; } = null!;

			[Required]
			public string NgayLamViec { get; set; } = null!; // Nhận chuỗi định dạng yyyy-MM-dd
			
			public string? NgayKetThuc { get; set; }


			[Required]
			public string CaLam { get; set; } = null!; // VD: "Sáng", "Chiều", "Cả ngày"
		}

		// DTO để trả về danh sách lịch làm việc cho Frontend vẽ Giao diện Lịch
		public class LichLamViecResponse
		{
			public int MaLich { get; set; }
			public string MaNv { get; set; } = null!;
			public string HoTenNv { get; set; } = null!;
			public string? TenLoaiNv { get; set; }
			public string NgayLamViec { get; set; } = null!;
			public string? NgayKetThuc { get; set; }
			public string CaLam { get; set; } = null!;
			public int? TrangThai { get; set; } // 1: Làm việc, 0: Nghỉ phép, 2: Đi trễ
		}
	}
}

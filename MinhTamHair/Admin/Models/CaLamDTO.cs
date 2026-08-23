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

			public string? GhiChu { get; set; } // Lý do hoặc ghi chú cho ca làm / nghỉ phép
			public string? LyDo { get; set; }
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
			public string? GhiChu { get; set; }
		}

		// DTO để nhận yêu cầu cập nhật trạng thái điểm danh (Có mặt, Vắng mặt, Rời ca, Muộn)
		public class UpdateDiemDanhRequest
		{
			[Required]
			public int TrangThai { get; set; } // 2: Có mặt, 3: Rời ca, 5: Vắng mặt, 6: Muộn
			public string? GhiChu { get; set; } // Lý do
		}

		// DTO để nhận yêu cầu Rời ca sớm
		public class RoiCaSomRequest
		{
			[Required]
			public string GhiChu { get; set; } = null!; // Bắt buộc nhập lý do rời ca sớm
		}

		// DTO để nhận yêu cầu Đổi ca / Xin nghỉ
		public class YeuCauDoiCaRequest
		{
			[Required]
			public string CaMoi { get; set; } = null!;
			[Required]
			public string LyDo { get; set; } = null!;
		}

		// DTO để nhận yêu cầu Nghỉ lễ chung cho toàn bộ nhân viên
		public class NghiLeChungRequest
		{
			[Required]
			public string TuNgay { get; set; } = null!;
			[Required]
			public string DenNgay { get; set; } = null!;
			[Required]
			public string LyDo { get; set; } = null!;
		}
	}
}

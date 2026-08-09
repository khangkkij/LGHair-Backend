using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Security.Claims;
using MinhTamHair.Services.Chat;

namespace MinhTamHair.NhanVien.Controllers
{
	[Route("api/nhan-vien/ca-lam")]
	[ApiController]
	[Authorize] // Chỉ cần đăng nhập là gọi được, không cần check cứng R01, R02 ở đây
	public class NhanVienCaLamController : ControllerBase
	{
		private readonly MinhTamHairContext _context;
		private readonly IFirestoreService _firestoreService;

		public NhanVienCaLamController(MinhTamHairContext context, IFirestoreService firestoreService)
		{
			_context = context;
			_firestoreService = firestoreService;
		}

		// Hàm phụ để lấy MaNv từ Token đăng nhập
		private string? GetCurrentMaNv() => User.FindFirstValue(ClaimTypes.NameIdentifier);

		[HttpGet("ca-lam-va-lich-su")]
		public async Task<IActionResult> GetMyShifts([FromQuery] string maNv)
		{
			if (string.IsNullOrEmpty(maNv)) return BadRequest(new { message = "Thiếu mã nhân viên" });

			var today = DateOnly.FromDateTime(DateTime.Now);
			var yesterday = today.AddDays(-1); // Tạo thêm biến Ngày Hôm Qua
			var sevenDaysAgo = today.AddDays(-7);
			var sevenDaysAhead = today.AddDays(7);

			// 1. KIỂM TRA XEM HÔM NAY ĐÃ CÓ CA CHƯA
			var todayShift = await _context.Lichlamviecs
				.FirstOrDefaultAsync(l => l.MaNv == maNv && l.NgayLamViec == today);

			if (todayShift == null)
			{
				// 2. TÌM LỊCH LÀM VIỆC CỦA NGÀY HÔM QUA
				var yesterdayShift = await _context.Lichlamviecs
					.FirstOrDefaultAsync(l => l.MaNv == maNv && l.NgayLamViec == yesterday);

				// 3. LẤY TÊN CA: Nếu hôm qua có ca thì copy sang, nếu hôm qua nghỉ thì mới dùng "Ca Linh Động"
				string tenCaMoi = (yesterdayShift != null && !string.IsNullOrEmpty(yesterdayShift.CaLam))
									? yesterdayShift.CaLam
									: "Chưa xếp";

				// Tự tạo ca mới
				var newShift = new Lichlamviec
				{
					MaNv = maNv,
					NgayLamViec = today,
					CaLam = tenCaMoi, // Gán tên ca vừa lấy được vào đây
					TrangThai = 1 // 1: Trạng thái chờ điểm danh
				};
				_context.Lichlamviecs.Add(newShift);
				await _context.SaveChangesAsync();
			}

			var rawShifts = await _context.Lichlamviecs
				.Where(l => l.MaNv == maNv && l.NgayLamViec >= sevenDaysAgo && l.NgayLamViec <= sevenDaysAhead)
				.OrderByDescending(l => l.NgayLamViec)
				.ToListAsync();

			bool isModified = false;
			var currentTime = DateTime.Now.TimeOfDay;

			foreach (var l in rawShifts)
			{
				if (l.TrangThai == 1 && l.CaLam != "Chưa xếp" && l.CaLam != "Nghỉ lễ")
				{
					bool isAbsent = false;
					if (l.NgayLamViec < today)
					{
						isAbsent = true; // Đã qua ngày hôm sau
					}
					else if (l.NgayLamViec == today)
					{
						TimeSpan shiftEndTime;
						if (l.CaLam.Contains("Ca sáng")) shiftEndTime = new TimeSpan(17, 0, 0);
						else if (l.CaLam.Contains("Ca chiều")) shiftEndTime = new TimeSpan(18, 0, 0);
						else if (l.CaLam.Contains("Ca tối")) shiftEndTime = new TimeSpan(23, 0, 0);
						else shiftEndTime = new TimeSpan(23, 59, 59);

						if (currentTime >= shiftEndTime)
						{
							isAbsent = true;
						}
					}

					if (isAbsent)
					{
						l.TrangThai = 5; // Vắng mặt
						l.GhiChu = string.IsNullOrEmpty(l.GhiChu) ? "Hệ thống tự đánh vắng do quá giờ" : l.GhiChu;
						isModified = true;
					}
				}
			}

			if (isModified)
			{
				await _context.SaveChangesAsync();
			}

			var shifts = rawShifts.Select(l => new {
				MaLich = l.MaLich,
				NgayLamViec = l.NgayLamViec.Value.ToString("yyyy-MM-dd"),
				CaLam = l.CaLam,
				TrangThai = l.TrangThai,
				GhiChu = l.GhiChu
			}).ToList();

			return Ok(new { success = true, data = shifts });
		}

		[HttpPatch("diem-danh/{maLich}")]
		public async Task<IActionResult> CheckIn(int maLich, [FromQuery] string maNv)
		{
			if (string.IsNullOrEmpty(maNv)) return BadRequest(new { message = "Thiếu mã nhân viên" });

			// Tìm lịch theo maLich và maNv được truyền từ Vue.js xuống
			var lich = await _context.Lichlamviecs.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);

			if (lich == null) return NotFound(new { message = "Không tìm thấy ca làm của bạn." });
			if (lich.CaLam == "Chưa xếp") return BadRequest(new { message = "Ca làm chưa được xếp, không thể điểm danh." });
			if (lich.TrangThai == 2) return BadRequest(new { message = "Bạn đã điểm danh rồi." });

			// ==========================================
			// THÊM MỚI: KIỂM TRA GIỜ GIẤC ĐIỂM DANH
			// ==========================================
			var now = DateTime.Now.TimeOfDay; // Lấy giờ phút hiện tại
			TimeSpan shiftStartTime;

			// Xác định giờ bắt đầu dựa vào tên ca (Hiếu nhớ chỉnh lại giờ cho đúng với Salon nhé)
			switch (lich.CaLam)
			{
				case "Ca sáng (7h-17h)":
					shiftStartTime = new TimeSpan(7, 0, 0); 
					break;
				case "Ca tối (13h-20h)":
					shiftStartTime = new TimeSpan(13, 0, 0); 
					break;
				case "Ca sáng (Part-time)":
					shiftStartTime = new TimeSpan(7, 0, 0); 
					break;
				case "Ca chiều (Part-time)":
					shiftStartTime = new TimeSpan(13, 0, 0); 
					break;
				case "Ca tối (Part-time)":
					shiftStartTime = new TimeSpan(16, 0, 0); 
					break;
				default:
					// Dành cho các ca linh động hoặc ghi chú khác, tạm thời không block giờ
					shiftStartTime = new TimeSpan(0, 0, 0);
					break;
			}

			// Cho phép điểm danh sớm 30 phút trước khi vào ca
			// Ví dụ: Ca 8h thì 7h30 mới được hiện nút bấm (hợp lệ)
			if (shiftStartTime.TotalHours > 0)
			{
				var allowedCheckInTime = shiftStartTime.Subtract(TimeSpan.FromMinutes(30));

				if (now < allowedCheckInTime)
				{
					return BadRequest(new { message = $"Chưa đến giờ làm! {lich.CaLam} bắt đầu lúc {shiftStartTime:hh\\:mm}. Bạn chỉ được điểm danh sớm 30 phút." });
				}
			}
			// ==========================================

			lich.TrangThai = 2; // Đánh dấu Có mặt
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Điểm danh thành công! Chúc bạn làm việc hiệu quả." });
		}

		[HttpPatch("diem-danh-qr/{maLich}")]
		public async Task<IActionResult> CheckInQR(int maLich, [FromQuery] string maNv, [FromBody] string qrCode)
		{
			if (string.IsNullOrEmpty(maNv)) return BadRequest(new { message = "Thiếu mã nhân viên" });

			var dateStr = DateTime.Now.ToString("yyyyMMdd");
			var expectedQR = $"MinhTamHair-DiemDanh-{dateStr}";

			if (qrCode != expectedQR)
			{
				return BadRequest(new { message = "Mã QR không hợp lệ hoặc đã hết hạn." });
			}

			// Tìm lịch theo maLich và maNv được truyền từ Vue.js xuống
			var lich = await _context.Lichlamviecs.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);

			if (lich == null) return NotFound(new { message = "Không tìm thấy ca làm của bạn." });
			if (lich.CaLam == "Chưa xếp") return BadRequest(new { message = "Ca làm chưa được xếp, không thể điểm danh." });
			if (lich.TrangThai == 2) return BadRequest(new { message = "Bạn đã điểm danh rồi." });

			// ==========================================
			// THÊM MỚI: KIỂM TRA GIỜ GIẤC ĐIỂM DANH
			// ==========================================
			var now = DateTime.Now.TimeOfDay; // Lấy giờ phút hiện tại
			TimeSpan shiftStartTime;

			// Xác định giờ bắt đầu dựa vào tên ca
			switch (lich.CaLam)
			{
				case "Ca sáng (7h-17h)":
					shiftStartTime = new TimeSpan(7, 0, 0); 
					break;
				case "Ca tối (13h-20h)":
					shiftStartTime = new TimeSpan(13, 0, 0); 
					break;
				case "Ca sáng (Part-time)":
					shiftStartTime = new TimeSpan(7, 0, 0); 
					break;
				case "Ca chiều (Part-time)":
					shiftStartTime = new TimeSpan(13, 0, 0); 
					break;
				case "Ca tối (Part-time)":
					shiftStartTime = new TimeSpan(16, 0, 0); 
					break;
				default:
					shiftStartTime = new TimeSpan(0, 0, 0);
					break;
			}

			if (shiftStartTime.TotalHours > 0)
			{
				var allowedCheckInTime = shiftStartTime.Subtract(TimeSpan.FromMinutes(30));

				if (now < allowedCheckInTime)
				{
					return BadRequest(new { message = $"Chưa đến giờ làm! {lich.CaLam} bắt đầu lúc {shiftStartTime:hh\\:mm}. Bạn chỉ được điểm danh sớm 30 phút." });
				}

				var lateTime = shiftStartTime.Add(TimeSpan.FromMinutes(15));
				if (now > lateTime)
				{
					lich.TrangThai = 6; // Đi muộn
					lich.GhiChu = $"Điểm danh muộn lúc {now:hh\\:mm}";
				}
				else
				{
					lich.TrangThai = 2; // Đánh dấu Có mặt đúng giờ
				}
			}
			else
			{
				lich.TrangThai = 2; // Nếu không xác định được giờ (ca linh động), mặc định là Có mặt
			}

			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Điểm danh mã QR thành công! Chúc bạn làm việc hiệu quả." });
		}

		[HttpPatch("roi-ca-som/{maLich}")]
		public async Task<IActionResult> CheckOut(int maLich, [FromQuery] string maNv, [FromBody] MinhTamHair.Admin.Models.CaLamDTO.RoiCaSomRequest request)
		{
			var lich = await _context.Lichlamviecs.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);
			if (lich == null) return NotFound(new { message = "Không tìm thấy ca làm của bạn." });

			var today = DateOnly.FromDateTime(DateTime.Now);
			if (lich.NgayLamViec > today)
			{
				return BadRequest(new { message = "Không thể đánh dấu rời ca sớm cho ca làm trong tương lai." });
			}

			if (lich.TrangThai != 2 && lich.TrangThai != 6)
			{
				return BadRequest(new { message = "Chỉ có thể đánh dấu rời ca sớm cho ca làm đã điểm danh có mặt." });
			}

			if (string.IsNullOrWhiteSpace(request.GhiChu))
			{
				return BadRequest(new { message = "Vui lòng nhập lý do rời ca sớm." });
			}

			lich.TrangThai = 3;
			lich.GhiChu = request.GhiChu;
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Đã đánh dấu rời ca sớm!" });
		}

		// ==========================================
		// THÊM MỚI: API CẬP NHẬT TRẠNG THÁI ĐIỂM DANH (Có mặt, Vắng mặt, Rời ca, Muộn)
		// ==========================================
		[HttpPatch("cap-nhat-diem-danh/{maLich}")]
		public async Task<IActionResult> CapNhatDiemDanh(int maLich, [FromQuery] string maNv, [FromBody] MinhTamHair.Admin.Models.CaLamDTO.UpdateDiemDanhRequest request)
		{
			if (string.IsNullOrEmpty(maNv)) return BadRequest(new { message = "Thiếu mã nhân viên" });

			var lich = await _context.Lichlamviecs.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);
			if (lich == null) return NotFound(new { message = "Không tìm thấy ca làm của bạn." });

			// Yêu cầu nhập lý do nếu không phải "Có mặt"
			if (request.TrangThai != 2 && string.IsNullOrWhiteSpace(request.GhiChu))
			{
				return BadRequest(new { message = "Vui lòng nhập lý do." });
			}

			lich.TrangThai = request.TrangThai;
			lich.GhiChu = request.GhiChu;

			await _context.SaveChangesAsync();
			return Ok(new { success = true, message = "Cập nhật trạng thái điểm danh thành công!" });
		}
		// 3. API GỬI YÊU CẦU ĐỔI CA (Trạng thái 4)
		[HttpPatch("gui-yeu-cau/{maLich}")]
		public async Task<IActionResult> RequestChange(int maLich, [FromQuery] string? maNv, [FromBody] MinhTamHair.Admin.Models.CaLamDTO.YeuCauDoiCaRequest request)
		{
			if (string.IsNullOrEmpty(maNv)) maNv = GetCurrentMaNv();
			if (string.IsNullOrEmpty(maNv)) return Unauthorized();

			var lich = await _context.Lichlamviecs
				.Include(l => l.MaNvNavigation)
				.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);

			if (lich == null && maLich != 0) return NotFound();

			if (lich == null && maLich == 0)
			{
				var date = DateOnly.FromDateTime(DateTime.Now);
				lich = await _context.Lichlamviecs
					.Include(l => l.MaNvNavigation)
					.FirstOrDefaultAsync(l => l.MaNv == maNv && l.NgayLamViec == date);

				if (lich == null)
				{
					lich = new Lichlamviec
					{
						MaNv = maNv,
						NgayLamViec = date,
						CaLam = "Chưa xếp",
						TrangThai = 1, // Khởi tạo với trạng thái mặc định (Chờ điểm danh) thay vì 4
						GhiChu = $"Xin xếp ca: {request.CaMoi}. Lý do: {request.LyDo}"
					};
					_context.Lichlamviecs.Add(lich);
				}
				else
				{
					// Không đổi trạng thái
					lich.GhiChu = $"Xin đổi ca thành: {request.CaMoi}. Lý do: {request.LyDo}";
				}
			}
			else
			{
				// Không đổi trạng thái
				lich.GhiChu = $"Xin đổi ca thành: {request.CaMoi}. Lý do: {request.LyDo}";
			}

			await _context.SaveChangesAsync();

			// Gửi thông báo cho Admin qua Firebase
			string tenNv = lich.MaNvNavigation?.HoTenNv ?? maNv;
			string title = "Yêu cầu đổi ca / Nghỉ phép";
			string desc = $"Nhân viên {tenNv} xin đổi ca {lich.CaLam} thành {request.CaMoi}. Lý do: {request.LyDo}";
			string link = $"/admin/quan-ly-ca-lam?search={maNv}";

			try 
			{
				await _firestoreService.PushAdminNotificationAsync(title, desc, link, maNv);
			} 
			catch (Exception ex) 
			{
				Console.WriteLine("Lỗi khi gửi thông báo Firebase: " + ex.Message);
			}

			return Ok(new { success = true, message = "Yêu cầu đã được gửi tới quản lý." });
		}
	}
}

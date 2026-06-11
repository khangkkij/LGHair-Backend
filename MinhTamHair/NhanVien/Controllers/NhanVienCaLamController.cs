using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Security.Claims;
namespace MinhTamHair.NhanVien.Controllers
{
	[Route("api/nhan-vien/ca-lam")]
	[ApiController]
	[Authorize] // Chỉ cần đăng nhập là gọi được, không cần check cứng R01, R02 ở đây
	public class NhanVienCaLamController : ControllerBase
	{
		private readonly MinhTamHairContext _context;

		public NhanVienCaLamController(MinhTamHairContext context)
		{
			_context = context;
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

			// 4. LẤY DANH SÁCH (Bao gồm cả ca vừa tạo)
			var shifts = await _context.Lichlamviecs
				.Where(l => l.MaNv == maNv && l.NgayLamViec >= sevenDaysAgo && l.NgayLamViec <= today)
				.OrderByDescending(l => l.NgayLamViec)
				.Select(l => new {
					MaLich = l.MaLich,
					NgayLamViec = l.NgayLamViec.Value.ToString("yyyy-MM-dd"),
					CaLam = l.CaLam,
					TrangThai = l.TrangThai,
					GhiChu = l.GhiChu
				})
				.ToListAsync();

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
				case "Ca ngày":
					shiftStartTime = new TimeSpan(7, 0, 0); // Ca ngày bắt đầu lúc 07:00
					break;
				case "Ca tối":
					shiftStartTime = new TimeSpan(13, 0, 0); // Ca tối bắt đầu lúc 13:00
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

		// LÀM TƯƠNG TỰ CHO API RỜI CA SỚM (NẾU CÓ)
		[HttpPatch("roi-ca-som/{maLich}")]
		public async Task<IActionResult> CheckOut(int maLich, [FromQuery] string maNv)
		{
			var lich = await _context.Lichlamviecs.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);
			if (lich == null) return NotFound(new { message = "Không tìm thấy ca làm của bạn." });

			lich.TrangThai = 3;
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
		public async Task<IActionResult> RequestChange(int maLich, [FromBody] string lyDo)
		{
			var maNv = GetCurrentMaNv();
			var lich = await _context.Lichlamviecs.FirstOrDefaultAsync(l => l.MaLich == maLich && l.MaNv == maNv);

			if (lich == null) return NotFound();

			lich.TrangThai = 4; // Trạng thái yêu cầu
								// lich.GhiChu = lyDo;

			await _context.SaveChangesAsync();
			return Ok(new { success = true, message = "Yêu cầu đã được gửi tới quản lý." });
		}
	}
}

using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Admin.Models;
using MinhTamHair.Models;
using static MinhTamHair.Admin.Models.CaLamDTO;

namespace MinhTamHair.Admin.Controllers
{
	[Route("api/admin/lich-lam-viec")]
	[ApiController]
	[Authorize(Roles = "R01,R02")] // Phân quyền: Chỉ Admin (R01) hoặc Quản lý (R02) được thao tác
	public class LichLamViecController : ControllerBase
	{
		private readonly MinhTamHairContext _context;

		public LichLamViecController(MinhTamHairContext context)
		{
			_context = context;
		}

		// 1. LẤY DANH SÁCH NHÂN VIÊN ĐƯỢC PHÉP XẾP CA (Thợ cắt, Thợ gội...)
		[HttpGet("nhan-vien-hop-le")]
		public async Task<IActionResult> GetEligibleEmployees()
		{
			var nhanViens = await _context.Nhanviens
				.Include(nv => nv.MaLoaiNvNavigation)
				.Where(nv => nv.TrangThai == 1 && nv.MaLoaiNvNavigation!.DuocDatLich == true)
				.Select(nv => new
				{
					nv.MaNv,
					nv.HoTenNv,
					LoaiNhanVien = nv.MaLoaiNvNavigation.TenLoai
				})
				.ToListAsync();

			return Ok(new { success = true, data = nhanViens });
		}

		// 2. XEM LỊCH LÀM VIỆC (Nâng cấp: Lọc linh hoạt + Trạng thái)
		[HttpGet("danh-sach")]
		public async Task<IActionResult> GetLichLamViec([FromQuery] string? tuNgay, [FromQuery] string? denNgay, [FromQuery] int? trangThai)
		{
			var today = DateOnly.FromDateTime(DateTime.Now);
			var sixtyDaysAgo = today.AddDays(-60); // Mốc giới hạn 2 tháng

			var query = _context.Lichlamviecs
				.Include(l => l.MaNvNavigation)
				.ThenInclude(nv => nv.MaLoaiNvNavigation)
				.Where(l => l.NgayLamViec >= sixtyDaysAgo) // Ràng buộc cứng: Chỉ lấy dữ liệu trong 60 ngày
				.AsQueryable();

			// Lọc theo dải ngày người dùng chọn (nhưng vẫn nằm trong khoảng 60 ngày trên)
			if (!string.IsNullOrEmpty(tuNgay) && DateOnly.TryParse(tuNgay, out DateOnly fromDate))
			{
				// Nếu ngày người dùng chọn cũ hơn 60 ngày, ta lấy mốc 60 ngày làm chuẩn
				var actualFromDate = fromDate < sixtyDaysAgo ? sixtyDaysAgo : fromDate;
				query = query.Where(l => l.NgayLamViec >= actualFromDate);
			}

			if (!string.IsNullOrEmpty(denNgay) && DateOnly.TryParse(denNgay, out DateOnly toDate))
			{
				query = query.Where(l => l.NgayLamViec <= toDate);
			}

			// Lọc theo trạng thái nếu có
			if (trangThai.HasValue)
			{
				query = query.Where(l => l.TrangThai == trangThai.Value);
			}

			var lichs = await query
				.Select(l => new LichLamViecResponse
				{
					MaLich = l.MaLich,
					MaNv = l.MaNv!,
					HoTenNv = l.MaNvNavigation!.HoTenNv ?? "",
					TenLoaiNv = l.MaNvNavigation.MaLoaiNvNavigation!.TenLoai,
					NgayLamViec = l.NgayLamViec.Value.ToString("yyyy-MM-dd"),
					NgayKetThuc = l.NgayKetThuc.HasValue ? l.NgayKetThuc.Value.ToString("yyyy-MM-dd") : null,
					CaLam = l.CaLam ?? "",
					TrangThai = l.TrangThai
				})
				.ToListAsync();

			return Ok(new { success = true, data = lichs });
		}

		[HttpDelete("xoa/{maLich}")]
		public async Task<IActionResult> DeleteLichLamViec(int maLich)
		{
			var lich = await _context.Lichlamviecs.FindAsync(maLich);
			if (lich == null) return NotFound(new { success = false, message = "Không tìm thấy lịch làm việc." });
			DateOnly start = lich.NgayLamViec.Value;
			DateOnly end = lich.NgayKetThuc ?? start;

			// 1. Định nghĩa khung giờ cho từng loại Ca làm (Phải khớp với lúc bạn thêm ca)
			TimeOnly caStart, caEnd;
			switch (lich.CaLam)
			{
				case "Sáng":
					caStart = new TimeOnly(8, 0); caEnd = new TimeOnly(12, 0);
					break;
				case "Chiều":
					caStart = new TimeOnly(13, 0); caEnd = new TimeOnly(17, 0);
					break;
				case "Tối":
					caStart = new TimeOnly(17, 0); caEnd = new TimeOnly(21, 0);
					break;
				case "Cả ngày":
					caStart = new TimeOnly(8, 0); caEnd = new TimeOnly(21, 0);
					break;
				default:
					// Nếu không xác định được ca, mặc định chặn xóa nếu ngày đó có bất kỳ lịch đặt nào
					caStart = new TimeOnly(0, 0); caEnd = new TimeOnly(23, 59);
					break;
			}

			// 2. Kiểm tra xem có lịch đặt nào của khách đè lên khung giờ này không
			// Công thức Overlap: (Bắt đầu ca < Kết thúc khách) VÀ (Bắt đầu khách < Kết thúc ca)
			var hasBooking = await _context.ChitietLichdats
		.Include(ct => ct.MaLdNavigation)
		.AnyAsync(ct =>
			ct.MaNv == lich.MaNv &&
			ct.MaLdNavigation!.NgayHen >= start && // Từ ngày bắt đầu dải
			ct.MaLdNavigation!.NgayHen <= end &&   // Đến ngày kết thúc dải
			ct.MaLdNavigation.TrangThai != 0 &&
			caStart < ct.MaLdNavigation.GioKetThuc &&
			ct.MaLdNavigation.GioBatDau < caEnd
		);

			if (hasBooking)
			{
				return BadRequest(new
				{
					success = false,
					message = $"Không thể xóa ca {lich.CaLam} vì nhân viên đã có lịch khách đặt trong khung giờ này."
				});
			}

			_context.Lichlamviecs.Remove(lich);
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Xóa ca làm thành công." });
		}

		[HttpPost("tao-moi")] // Hoặc đổi tên thành "cap-nhat-ca"
		public async Task<IActionResult> UpdateTodayShift([FromBody] CreateLichLamViecRequest request)
		{
			if (!DateOnly.TryParse(request.NgayLamViec, out DateOnly ngaySua))
				return BadRequest(new { message = "Ngày không hợp lệ." });

			// Tìm xem hôm nay đã có dòng dữ liệu (do tự động tạo hoặc có sẵn) chưa
			var lichHienTai = await _context.Lichlamviecs
				.FirstOrDefaultAsync(l => l.MaNv == request.MaNv && l.NgayLamViec == ngaySua);

			if (lichHienTai != null)
			{
				// Nếu đã điểm danh rồi thì KHÔNG cho sửa ca nữa để tránh gian lận
				if (lichHienTai.TrangThai == 2)
					return BadRequest(new { message = "Không thể sửa ca khi nhân viên đã điểm danh xong." });

				// Cập nhật ca làm mới
				lichHienTai.CaLam = request.CaLam;
				lichHienTai.TrangThai = 1; // Khi đã xếp ca thì chuyển từ 0 (Chưa xếp) sang 1 (Chờ điểm danh)
				await _context.SaveChangesAsync();
				return Ok(new { success = true, message = "Cập nhật ca làm thành công." });
			}

			// Nếu trường hợp hy hữu chưa có dòng dữ liệu thì mới tạo mới
			var newLich = new Lichlamviec
			{
				MaNv = request.MaNv,
				NgayLamViec = ngaySua,
				CaLam = request.CaLam,
				TrangThai = 1
			};
			_context.Lichlamviecs.Add(newLich);
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Đã xếp ca làm mới." });
		}

		// 5. CẬP NHẬT TRẠNG THÁI CA LÀM (Bật/Tắt)
		[HttpPatch("doi-trang-thai/{maLich}")]
		public async Task<IActionResult> ToggleStatus(int maLich)
		{
			var lich = await _context.Lichlamviecs.FindAsync(maLich);
			if (lich == null) return NotFound(new { success = false, message = "Không tìm thấy lịch làm việc." });

			// Đảo trạng thái: 1 (Hoạt động) <-> 0 (Tạm nghỉ)
			lich.TrangThai = (lich.TrangThai == 1) ? 0 : 1;

			await _context.SaveChangesAsync();

			return Ok(new
			{
				success = true,
				message = lich.TrangThai == 1 ? "Đã kích hoạt ca làm." : "Đã chuyển sang trạng thái tạm nghỉ.",
				newStatus = lich.TrangThai
			});
		}
		// API Điểm danh ca làm
		[HttpPatch("diem-danh/{maLich}")]
		public async Task<IActionResult> CheckIn(int maLich)
		{
			var lich = await _context.Lichlamviecs.FindAsync(maLich);
			if (lich == null) return NotFound();
			if (lich.TrangThai == 2) return BadRequest(new { message = "Ca làm này đã được điểm danh." });

			var now = DateTime.Now;
			var today = DateOnly.FromDateTime(now);
			var currentTime = TimeOnly.FromDateTime(now);

			// 1. Kiểm tra ngày
			if (lich.NgayLamViec != today)
				return BadRequest(new { message = "Chỉ được điểm danh ca làm của ngày hôm nay." });

			// 2. Xác định khung giờ ca làm
			TimeOnly start, end;
			switch (lich.CaLam)
			{
				case "Sáng": start = new TimeOnly(8, 0); end = new TimeOnly(12, 0); break;
				case "Chiều": start = new TimeOnly(13, 0); end = new TimeOnly(17, 0); break;
				case "Tối": start = new TimeOnly(17, 0); end = new TimeOnly(21, 0); break;
				case "Cả ngày": start = new TimeOnly(8, 0); end = new TimeOnly(21, 0); break;
				default: return BadRequest(new { message = "Ca làm không hợp lệ." });
			}

			// 3. Kiểm tra giờ hiện tại
			if (currentTime < start)
				return BadRequest(new { message = $"Chưa đến giờ làm. Ca {lich.CaLam} bắt đầu lúc {start}." });

			if (currentTime > end)
				return BadRequest(new { message = $"Đã quá giờ điểm danh của ca {lich.CaLam} ({end})." });

			// Hợp lệ -> Chuyển trạng thái
			lich.TrangThai = 2;
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Điểm danh thành công." });
		}

		[HttpPost("khoi-tao-ngay")]
		public async Task<IActionResult> InitializeTodayShifts([FromBody] JsonElement body)
		{
			DateOnly targetDate;
			if (body.TryGetProperty("date", out var dateProp) && DateOnly.TryParse(dateProp.GetString(), out var parsedDate))
			{
				targetDate = parsedDate;
			}
			else
			{
				targetDate = DateOnly.FromDateTime(DateTime.Now);
			}

			// --- BẮT ĐẦU LOGIC CLEAR TỰ ĐỘNG ---
			try
			{
				var sixtyDaysAgo = targetDate.AddDays(-60); // Mốc 2 tháng trước

				// Tìm những bản ghi cũ hơn 60 ngày
				var oldRecords = _context.Lichlamviecs.Where(l => l.NgayLamViec < sixtyDaysAgo);

				if (oldRecords.Any())
				{
					_context.Lichlamviecs.RemoveRange(oldRecords); // Xóa hàng loạt
																   // Không cần SaveChanges ngay đây, nó sẽ được lưu cùng lúc với phần khởi tạo ở dưới
				}
			}
			catch (Exception ex)
			{
				// Log lỗi nếu cần, nhưng không chặn quá trình khởi tạo ngày mới
				Console.WriteLine($"Lỗi dọn dẹp DB: {ex.Message}");
			}

			var yesterday = targetDate.AddDays(-1);

			// 2. Lấy danh sách thợ hợp lệ
			var eligibleStaff = await _context.Nhanviens
				.Where(nv => nv.TrangThai == 1 && nv.MaLoaiNvNavigation!.DuocDatLich == true)
				.ToListAsync();

			// 3. Kiểm tra xem ngày targetDate đã có lịch chưa
			var existingMaNv = await _context.Lichlamviecs
				.Where(l => l.NgayLamViec == targetDate)
				.Select(l => l.MaNv)
				.ToListAsync();

			var staffToInit = eligibleStaff.Where(nv => !existingMaNv.Contains(nv.MaNv)).ToList();

			if (staffToInit.Any())
			{
				// 2. SỬA LỖI TẠI ĐÂY: Lấy lịch ngày hôm trước nhưng xử lý trùng lặp nhân viên
				var yesterdayShifts = await _context.Lichlamviecs
					.Where(l => l.NgayLamViec == yesterday && l.MaNv != null)
					.GroupBy(l => l.MaNv) // Nhóm tất cả các ca của cùng 1 người lại
					.Select(g => new {
						MaNv = g.Key,
						CaLam = g.FirstOrDefault()!.CaLam // Chỉ lấy ca đầu tiên làm mẫu để tránh lỗi Duplicate Key
					})
					.ToDictionaryAsync(x => x.MaNv!, x => x.CaLam);

				foreach (var nv in staffToInit)
				{
					var inheritedCaLam = yesterdayShifts.GetValueOrDefault(nv.MaNv) ?? "Chưa xếp";
					_context.Lichlamviecs.Add(new Lichlamviec
					{
						MaNv = nv.MaNv,
						NgayLamViec = targetDate,
						// Sử dụng GetValueOrDefault để an toàn hơn
						CaLam = inheritedCaLam,
						TrangThai = inheritedCaLam == "Chưa xếp" ? 0 : 1
					});
				}
			}
				await _context.SaveChangesAsync();

			return Ok(new { success = true, message = $"Đã khởi tạo cho ngày {targetDate}" });
		}

		[HttpPatch("roi-ca-som/{maLich}")]
		public async Task<IActionResult> MarkAsLeftEarly(int maLich)
		{
			var lich = await _context.Lichlamviecs.FindAsync(maLich);
			if (lich == null) return NotFound();

			// Chỉ cho phép chuyển từ "Đã có mặt" (2) sang "Rời ca sớm" (3)
			if (lich.TrangThai != 2)
			{
				return BadRequest(new { message = "Chỉ có thể đánh dấu rời ca cho nhân viên đã điểm danh có mặt." });
			}

			lich.TrangThai = 3; // Trạng thái mới: Rời ca sớm
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Đã ghi nhận nhân viên rời ca sớm." });
		}

	}
}
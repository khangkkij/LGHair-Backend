using System.Collections;
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
			var query = _context.Lichlamviecs
				.Include(l => l.MaNvNavigation)
				.ThenInclude(nv => nv.MaLoaiNvNavigation)
				.AsQueryable();

			// Nếu có truyền ngày thì mới lọc theo ngày
			if (!string.IsNullOrEmpty(tuNgay) && DateOnly.TryParse(tuNgay, out DateOnly fromDate))
			{
				query = query.Where(l => l.NgayLamViec >= fromDate);
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

		[HttpPost("tao-moi")]
		public async Task<IActionResult> CreateLichLamViec([FromBody] CreateLichLamViecRequest request)
		{
			if (!DateOnly.TryParse(request.NgayLamViec, out DateOnly ngayBatDau))
				return BadRequest(new { success = false, message = "Ngày bắt đầu không hợp lệ." });

			DateOnly? ngayKetThuc = null;
			if (!string.IsNullOrEmpty(request.NgayKetThuc) && DateOnly.TryParse(request.NgayKetThuc, out DateOnly parsedKetThuc))
			{
				ngayKetThuc = parsedKetThuc;
				if (ngayKetThuc < ngayBatDau)
					return BadRequest(new { success = false, message = "Ngày kết thúc không được nhỏ hơn ngày bắt đầu." });
			}

			// Kiểm tra chồng lấn lịch (Giao thoa dải ngày) để tránh một nhân viên có 2 lịch trùng nhau
			var isOverlap = await _context.Lichlamviecs.AnyAsync(l =>
				l.MaNv == request.MaNv &&
				l.CaLam == request.CaLam &&
				((ngayBatDau >= l.NgayLamViec && ngayBatDau <= (l.NgayKetThuc ?? l.NgayLamViec)) ||
				 (ngayKetThuc >= l.NgayLamViec && ngayKetThuc <= (l.NgayKetThuc ?? l.NgayLamViec))));

			if (isOverlap)
				return BadRequest(new { success = false, message = "Lịch này bị chồng lấn với lịch đã có của nhân viên." });

			var newLich = new Lichlamviec
			{
				MaNv = request.MaNv,
				NgayLamViec = ngayBatDau,
				NgayKetThuc = ngayKetThuc, // LƯU VÀO CỘT MỚI
				CaLam = request.CaLam,
				TrangThai = 1
			};

			_context.Lichlamviecs.Add(newLich);
			await _context.SaveChangesAsync();

			return Ok(new { success = true, message = "Đã lưu lịch làm việc theo giai đoạn thành công." });
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


	}
}
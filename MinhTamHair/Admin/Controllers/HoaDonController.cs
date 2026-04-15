using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Admin.Models;
using MinhTamHair.Models;

namespace MinhTamHair.Admin.Controllers
{
	[Route("api/admin/hoa-don")]
	[ApiController]
	[Authorize(Roles = "R01,R02")]
	public class HoaDonController : ControllerBase
	{
		private readonly MinhTamHairContext _context;
		public HoaDonController(MinhTamHairContext context) => _context = context;

		[HttpGet("danh-sach")]
		public async Task<IActionResult> GetDanhSach([FromQuery] string? tuNgay, [FromQuery] string? denNgay, [FromQuery] string? search)
		{
			var query = _context.HoaDons
				.Include(h => h.MaKhNavigation)
				.Include(h => h.MaNvNavigation)
				.AsQueryable();

			// Lọc theo ngày
			if (DateTime.TryParse(tuNgay, out var start)) query = query.Where(h => h.NgayTao >= start);
			if (DateTime.TryParse(denNgay, out var end)) query = query.Where(h => h.NgayTao <= end);

			// Tìm kiếm theo mã HD hoặc tên KH
			if (!string.IsNullOrEmpty(search))
				query = query.Where(h => h.MaHd.Contains(search) || h.MaKhNavigation!.HoTen!.Contains(search));

			var data = await query.OrderByDescending(h => h.NgayTao)
				.Select(h => new HoaDonResponse
				{
					MaHd = h.MaHd,
					MaLd = h.MaLd,
					TenKhachHang = h.MaKhNavigation!.HoTen,
					TenNhanVien = h.MaNvNavigation!.HoTenNv,
					NgayTao = h.NgayTao,
					TongTien = h.TongTien,
					ThanhTien = h.ThanhTien,
					TienDaTra = h.TienDaTra,
					TrangThai = h.TrangThai
				}).ToListAsync();

			return Ok(new { success = true, data });
		}

		[HttpPatch("doi-trang-thai/{maHd}")]
		public async Task<IActionResult> UpdateStatus(string maHd, [FromBody] int trangThai)
		{
			var hd = await _context.HoaDons.FindAsync(maHd);
			if (hd == null) return NotFound();

			hd.TrangThai = trangThai;
			await _context.SaveChangesAsync();
			return Ok(new { success = true });
		}

		[HttpGet("chi-tiet/{maHd}")]
		public async Task<IActionResult> GetChiTiet(string maHd)
		{
			var hd = await _context.HoaDons
				.Include(h => h.MaKhNavigation)
				.Include(h => h.MaNvNavigation) // Người thu ngân
				.Include(h => h.MaLdNavigation)
					.ThenInclude(l => l!.ChitietLichdats)
						.ThenInclude(ct => ct.MaDvNavigation)
				.Include(h => h.MaLdNavigation)
					.ThenInclude(l => l!.ChitietLichdats)
						.ThenInclude(ct => ct.MaCbNavigation)
				.Include(h => h.MaLdNavigation)
					.ThenInclude(l => l!.ChitietLichdats)
						.ThenInclude(ct => ct.MaNvNavigation) // Thợ thực hiện dịch vụ
				.FirstOrDefaultAsync(h => h.MaHd == maHd);

			if (hd == null) return NotFound();

			var result = new
			{
				hd.MaHd,
				NgayTao = hd.NgayTao,
				TenKh = hd.MaKhNavigation?.HoTen,
				TenThuNgan = hd.MaNvNavigation?.HoTenNv,
				TongTien = hd.TongTien,
				GiamGiaVip = hd.PhanTramGiamVip,
				ThanhTien = hd.ThanhTien,
				TrangThai = hd.TrangThai,

				// Chi tiết từng món trong hóa đơn
				ChiTietDichVu = hd.MaLdNavigation?.ChitietLichdats.Select(ct => new {
					// Ưu tiên hiện tên Combo, nếu không có thì hiện tên Dịch vụ
					TenHangMuc = ct.MaCbNavigation?.TenCombo ?? ct.MaDvNavigation?.TenDv,
					ThoThucHien = ct.MaNvNavigation?.HoTenNv ?? "N/A",
					Gia = ct.GiaLucDat // Sử dụng giá tại thời điểm đặt để chuẩn xác
				})
			};

			return Ok(new { success = true, data = result });
		}
	}
}

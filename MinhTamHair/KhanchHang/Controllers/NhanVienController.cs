using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NhanVienController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public NhanVienController(MinhTamHairContext context)
        {
            _context = context;
        }

        [HttpGet("types")]
        public async Task<IActionResult> GetStaffTypes()
        {
            var types = await _context.Loainhanviens
                .Where(l => l.TrangThai == null || l.TrangThai == 1)
                .Select(l => new
                {
                    maLoaiNv = l.MaLoaiNv,
                    tenLoai = l.TenLoai,
                    moTa = l.MoTa
                })
                .ToListAsync();

            return Ok(types);
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetStaffList()
        {
            var staffs = await _context.Nhanviens
                .Include(n => n.MaLoaiNvNavigation)
                .Where(n => n.TrangThai == null || n.TrangThai == 1)
                .Select(n => new
                {
                    maNv = n.MaNv,
                    hoTen = n.HoTenNv,
                    hinhAnh = n.HinhAnh,
                    maLoaiNv = n.MaLoaiNv,
                    tenLoai = n.MaLoaiNvNavigation != null ? n.MaLoaiNvNavigation.TenLoai : null,
                    moTa = n.MaLoaiNvNavigation != null ? n.MaLoaiNvNavigation.MoTa : null
                })
                .ToListAsync();

            return Ok(staffs);
        }
    }
}

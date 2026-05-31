using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.SanPham.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DanhMucSPController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public DanhMucSPController(MinhTamHairContext context)
        {
            _context = context;
        }

        // ============================================
        // GET: api/DanhMuc/san-pham
        // ============================================
        [HttpGet("san-pham")]
        public async Task<IActionResult> GetDanhMucSanPham()
        {
            try
            {
                var data = await _context.DanhMucSPs
                    .Select(dm => new
                    {
                        maDM = dm.MaDMSP,
                        tenDanhMuc = dm.TenDanhMuc,
                        hinhAnh = dm.HinhAnh,

                        soLuongSP = _context.SanPhams
                            .Count(sp => sp.MaDanhMuc == dm.MaDMSP
                                      && sp.TrangThai == 1)
                    })
                    .OrderBy(x => x.tenDanhMuc)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Lỗi server",
                    error = ex.Message
                });
            }
        }
    }
}
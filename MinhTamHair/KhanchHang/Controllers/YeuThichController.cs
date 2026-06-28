using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class YeuThichController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public YeuThichController(MinhTamHairContext context)
        {
            _context = context;
        }

        // =========================================================================
        // 🌟 SECTION 1: XỬ LÝ DỊCH VỤ YÊU THÍCH
        // =========================================================================

        [HttpGet("services/{maKH}")]
        public async Task<IActionResult> GetFavoriteServices(string maKH)
        {
            var result = await _context.Set<YeuThichDichVu>()
                .Where(y => y.MaKH == maKH)
                .Select(y => new
                {
                    Id = y.Dichvu.MaDv,
                    Name = y.Dichvu.TenDv,
                    Image = y.Dichvu.Hinhanhs.Select(img => img.DuongDan).FirstOrDefault() ?? string.Empty,
                    Description = y.Dichvu.MoTa,
                    Price = y.Dichvu.Gia ?? 0,
                    OriginalPrice = y.Dichvu.Gia ?? 0,
                    IsDiscount = false,
                    Category = "Dịch vụ"
                })
                .ToListAsync();

            return Ok(result);
        }

        [HttpPost("add-service")]
        public async Task<IActionResult> AddServiceToFavorite([FromBody] FavoriteGlobalRequest request)
        {
            if (string.IsNullOrEmpty(request.MaKH) || string.IsNullOrEmpty(request.ItemId))
            {
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
            }

            var exists = await _context.Set<YeuThichDichVu>()
                .AnyAsync(y => y.MaKH == request.MaKH && y.MaDV == request.ItemId);

            if (exists)
            {
                return BadRequest(new { success = false, message = "Dịch vụ đã có trong danh sách." });
            }

            var favoriteService = new YeuThichDichVu
            {
                MaKH = request.MaKH,
                MaDV = request.ItemId, // Gán itemId trực tiếp vào trường MaDV của bảng trung gian
                NgayThem = DateTime.Now
            };

            _context.Set<YeuThichDichVu>().Add(favoriteService);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã lưu dịch vụ vào mục yêu thích!" });
        }

        [HttpDelete("remove-service")]
        public async Task<IActionResult> RemoveServiceFromFavorite([FromBody] FavoriteGlobalRequest request)
        {
            var item = await _context.Set<YeuThichDichVu>()
                .FirstOrDefaultAsync(y => y.MaKH == request.MaKH && y.MaDV == request.ItemId);

            if (item == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy mục yêu thích." });
            }

            _context.Set<YeuThichDichVu>().Remove(item);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa khỏi danh sách yêu thích!" });
        }

        // =========================================================================
        // 🌟 SECTION 2: XỬ LÝ SẢN PHẨM YÊU THÍCH
        // =========================================================================

        [HttpGet("products/{maKH}")]
        public async Task<IActionResult> GetFavoriteProducts(string maKH)
        {
            var result = await _context.Set<YeuThichSanPham>()
                .Where(y => y.MaKH == maKH)
                .Select(y => new
                {
                    Id = y.SanPham.MaSp,
                    Name = y.SanPham.TenSp,
                    Image = y.SanPham.HinhAnhDaiDien,
                    Description = y.SanPham.MoTa,
                    Price = y.SanPham.BienTheSanphams.Any() ? y.SanPham.BienTheSanphams.Min(bt => bt.GiaBan) : 0,
                    OriginalPrice = y.SanPham.BienTheSanphams.Any() ? y.SanPham.BienTheSanphams.Max(bt => bt.GiaBan) : 0,
                    IsDiscount = false,
                    Category = "Sản phẩm"
                })
                .ToListAsync();

            return Ok(result);
        }

        [HttpPost("add-product")]
        public async Task<IActionResult> AddProductToFavorite([FromBody] FavoriteGlobalRequest request)
        {
            if (string.IsNullOrEmpty(request.MaKH) || string.IsNullOrEmpty(request.ItemId))
            {
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
            }

            var exists = await _context.Set<YeuThichSanPham>()
                .AnyAsync(y => y.MaKH == request.MaKH && y.MaSP == request.ItemId);

            if (exists)
            {
                return BadRequest(new { success = false, message = "Sản phẩm đã có trong danh sách." });
            }

            var favoriteProduct = new YeuThichSanPham
            {
                MaKH = request.MaKH,
                MaSP = request.ItemId, // Gán itemId trực tiếp vào trường MaSP của bảng trung gian
                NgayThem = DateTime.Now
            };

            _context.Set<YeuThichSanPham>().Add(favoriteProduct);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã thêm sản phẩm vào mục yêu thích!" });
        }

        [HttpDelete("remove-product")]
        public async Task<IActionResult> RemoveProductFromFavorite([FromBody] FavoriteGlobalRequest request)
        {
            var item = await _context.Set<YeuThichSanPham>()
                .FirstOrDefaultAsync(y => y.MaKH == request.MaKH && y.MaSP == request.ItemId);

            if (item == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy mục yêu thích." });
            }

            _context.Set<YeuThichSanPham>().Remove(item);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa khỏi danh sách yêu thích!" });
        }
    }

    // Class nhận DTO đồng bộ trường dữ liệu với Client Vue.js công khai
    public class FavoriteGlobalRequest
    {
        public string MaKH { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
    }
}
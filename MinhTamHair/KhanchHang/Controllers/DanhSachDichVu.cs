using Microsoft.AspNetCore.Mvc;
using MinhTamHair.Models;
using System.Linq;

namespace MinhTamHair.KhachHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DanhSachDichVuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public DanhSachDichVuController(MinhTamHairContext context)
        {
            _context = context;
        }

        // ==============================
        // GET ALL DATA
        // ==============================
        [HttpGet]
        public IActionResult GetAll()
        {
            var categories = _context.DanhMucs
                .Where(x => x.TrangThai==1)
                .Select(x => new
                {
                    x.MaDm,
                    x.TenDanhMuc,
                })
                .ToList();

            var services = _context.Dichvus
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaDv,
                    x.TenDv,
                    x.Gia,
                    x.MaDmNavigation,
                    x.ThoiGianLam,
                    x.MoTa,
                    HinhAnh = x.Hinhanhs
                        .Select(h => h.DuongDan)
                        .FirstOrDefault()
                })
                .ToList();

            var combos = _context.Combos
                .Where(x => x.TrangThai == 1)
                .Select(x => new
                {
                    x.MaCb,
                    x.TenCombo,
                    x.Gia,
                    x.MoTa,
                    x.HinhAnh
                })
                .ToList();

            return Ok(new
            {
                categories,
                services,
                combos
            });
        }

        // ==============================
        // FILTER SERVICES
        // ==============================
        [HttpGet("filter")]
        public IActionResult Filter(
            string? search,
            string? category,
            string? priceRange,
            int page = 1,
            int pageSize = 6)
        {
            var query = _context.Dichvus
                .Where(x => x.TrangThai == 1)
                .AsQueryable();

            // SEARCH
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(x => x.TenDv.Contains(search));
            }

            // CATEGORY
            if (!string.IsNullOrEmpty(category) && category != "all")
            {
                query = query.Where(x => x.MaDm == category);
            }

            // PRICE
            if (!string.IsNullOrEmpty(priceRange))
            {
                if (priceRange == "low")
                    query = query.Where(x => x.Gia < 300000);
                else if (priceRange == "mid")
                    query = query.Where(x => x.Gia >= 300000 && x.Gia <= 700000);
                else if (priceRange == "high")
                    query = query.Where(x => x.Gia > 700000);
            }

            // 🔥 TOTAL COUNT
            var totalItems = query.Count();

            // 🔥 PAGINATION
            var services = query
                .OrderBy(x => x.MaDv)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.MaDv,
                    x.TenDv,
                    x.Gia,
                    x.MaDmNavigation,
                    x.ThoiGianLam,
                    x.MoTa,
                    HinhAnh = x.Hinhanhs
                        .Select(h => h.DuongDan)
                        .FirstOrDefault()
                })
                .ToList();

            return Ok(new
            {
                data = services,
                totalItems,
                totalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                currentPage = page
            });
        }
    }
}
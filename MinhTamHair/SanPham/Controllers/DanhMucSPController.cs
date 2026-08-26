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
        private readonly IProductCacheService _cache;

        // TTL 10 phút — khớp với CacheTtl bên SanPhamController để hành vi cache nhất quán
        // giữa 2 controller cùng phục vụ trang khách hàng.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        public DanhMucSPController(MinhTamHairContext context, IProductCacheService cache)
        {
            _context = context;
            _cache = cache;
        }

        // ============================================
        // GET: api/DanhMuc/san-pham
        // ============================================
        [HttpGet("san-pham")]
        public async Task<IActionResult> GetDanhMucSanPham()
        {
            try
            {
                // Cache theo version (cùng cơ chế với SanPhamController) — Upstash không hỗ trợ
                // tốt SCAN/KEYS để xóa theo prefix, nên invalidate bằng cách tăng version thay vì
                // xóa key. QuanLyDanhMucSPController gọi InvalidateAllDanhMucCacheAsync() sau mọi
                // thao tác Create/Update/Delete/đổi-thứ-tự/banner để version này tăng lên.
                var ver = await _cache.GetCurrentDanhMucVersionAsync();
                var json = await _cache.GetOrCreateJsonAsync($"dm:v{ver}:san-pham", async () =>
                {
                    var data = await _context.DanhMucSPs
                        .Where(dm => dm.TrangThai == 1)
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

                    // Trả THẲNG list (không bọc { data }) để giữ nguyên shape JSON top-level là
                    // mảng — Shop.vue đang gán trực tiếp "categoryList.value = res.data", bọc
                    // thêm object sẽ làm vỡ FE.
                    return data;
                }, CacheTtl);

                return Content(json, "application/json");
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
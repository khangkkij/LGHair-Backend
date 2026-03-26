using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MinhTamHair.Models;
using MinhTamHair.KhanchHang.Models;
using Microsoft.EntityFrameworkCore;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServicesController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public ServicesController(MinhTamHairContext context)
        {
            _context = context;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetServiceDetail(string id)
        {
            var service = await _context.Dichvus
                .Include(d => d.Hinhanhs)
                .FirstOrDefaultAsync(d => d.MaDv == id);

            if (service == null)
                return NotFound();

            // 🔹 Combos chứa service này
            var combos = await _context.Combos
                .Include(c => c.MaDvs)
                .Where(c => c.MaDvs.Any(d => d.MaDv == id))
                .ToListAsync();

            // 🔹 Suggested services
            var suggested = await _context.Dichvus
                .Where(d => d.MaDv != id)
                .Take(3)
                .ToListAsync();

            var now = DateTime.Now;

            // 🔹 Flash sale active
            var flashSales = await _context.ChitietFlashsales
                .Where(ct => ct.MaFlNavigation.TrangThai == 1
                    && ct.MaFlNavigation.NgayBatDau <= now
                    && ct.MaFlNavigation.NgayKetThuc >= now)
                .Select(ct => new
                {
                    ct.MaDv,
                    ct.PhanTramGiam,
                    ct.MaFlNavigation.NgayKetThuc
                })
                .ToListAsync();

            // ================= MAIN SERVICE =================
            var fsMain = flashSales.FirstOrDefault(f => f.MaDv == service.MaDv);

            var giaGocMain = service.Gia ?? 0;
            var giaSauMain = giaGocMain;
            int? ptgMain = null;
            DateTime? ngayKetThucMain = null;

            if (fsMain != null && fsMain.PhanTramGiam.HasValue)
            {
                ptgMain = fsMain.PhanTramGiam;
                giaSauMain = giaGocMain * (100 - ptgMain.Value) / 100;
                ngayKetThucMain = fsMain.NgayKetThuc;
            }

            // ================= RESULT =================
            var result = new
            {
                MaDv = service.MaDv,
                TenDv = service.TenDv,
                Gia = giaSauMain,
                GiaGoc = giaGocMain,
                PhanTramGiam = ptgMain,
                NgayKetThuc = ngayKetThucMain,

                ThoiGianLam = service.ThoiGianLam ?? 0,
                MoTa = service.MoTa,

                Images = service.Hinhanhs
                    .Select(h => h.DuongDan)
                    .ToList(),

                // ================= COMBOS =================
                RelatedCombos = combos.Select(c =>
                {
                    var fsList = flashSales
                        .Where(f => c.MaDvs.Any(d => d.MaDv == f.MaDv))
                        .ToList();

                    DateTime? ngayKetThuc = null;

                    if (fsList.Any())
                    {
                        // lấy sale gần nhất (sắp hết)
                        ngayKetThuc = fsList.Min(f => f.NgayKetThuc);
                    }

                    return new ComboDto
                    {
                        Id = c.MaCb,
                        TenCombo = c.TenCombo,
                        Gia = c.Gia ?? 0,
                        MoTa = c.MoTa,
                        Items = c.MaDvs.Select(d => d.TenDv).ToList(),
                    };
                }).ToList(),

                // ================= SUGGESTED =================
                SuggestedServices = suggested.Select(s =>
                {
                    var fs = flashSales.FirstOrDefault(f => f.MaDv == s.MaDv);

                    var giaGoc = s.Gia ?? 0;
                    var giaSau = giaGoc;
                    int? ptg = null;
                    DateTime? ngayKetThuc = null;

                    if (fs != null && fs.PhanTramGiam.HasValue)
                    {
                        ptg = fs.PhanTramGiam;
                        giaSau = giaGoc * (100 - ptg.Value) / 100;
                        ngayKetThuc = fs.NgayKetThuc;
                    }

                    return new ServiceItemDto
                    {
                        Id = s.MaDv,
                        TenDv = s.TenDv,
                        Gia = giaSau,
                        GiaGoc = giaGoc,
                        PhanTramGiam = ptg,
                        NgayKetThuc = ngayKetThuc,

                        ThoiGianLam = s.ThoiGianLam ?? 0,
                        MoTa = s.MoTa,
                        HinhAnh = _context.Hinhanhs
                            .Where(h => h.MaDv == s.MaDv)
                            .Select(h => h.DuongDan)
                            .FirstOrDefault()
                    };
                }).ToList()
            };

            return Ok(result);
        }
    }
}

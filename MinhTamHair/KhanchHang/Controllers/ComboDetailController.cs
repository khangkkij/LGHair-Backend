using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers
{
    [Route("api/combos")]
    [ApiController]
    public class ComboDetailController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public ComboDetailController(MinhTamHairContext context)
        {
            _context = context;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetComboDetail(string id)
        {
            var now = DateTime.Now;

            // ================= COMBO =================
            var combo = await _context.Combos
                .FirstOrDefaultAsync(c => c.MaCb == id && c.TrangThai == 1);

            if (combo == null)
                return NotFound(new { message = "Combo không tồn tại hoặc đã bị ẩn." });

            // ================= SERVICES IN COMBO =================
            var dichVuRaw = await _context.Combo_DichVus
                .Where(cd => cd.MaCB == id)
                .Include(cd => cd.DichVu)
                .Select(cd => new
                {
                    maDv = cd.DichVu.MaDv,
                    tenDv = cd.DichVu.TenDv,
                    thoiGianLam = cd.DichVu.ThoiGianLam,
                    gia = cd.DichVu.Gia,
                    moTa = cd.DichVu.MoTa,

                    hinhAnh = _context.Hinhanhs
                        .Where(h => h.MaDv == cd.DichVu.MaDv)
                        .Select(h => h.DuongDan)
                        .FirstOrDefault(),

                    flashGiam = _context.ChitietFlashsales
                        .Where(cf =>
                            cf.MaDv == cd.DichVu.MaDv &&
                            cf.MaFlNavigation.TrangThai == 1 &&
                            cf.MaFlNavigation.NgayBatDau <= now &&
                            cf.MaFlNavigation.NgayKetThuc >= now)
                        .Select(cf => (int?)cf.PhanTramGiam)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var dichVuTrongCombo = dichVuRaw.Select(d =>
            {
                var giaGoc = d.gia ?? 0;

                decimal gia = d.flashGiam.HasValue
                    ? giaGoc * (100 - d.flashGiam.Value) / 100
                    : giaGoc;

                return new
                {
                    d.maDv,
                    d.tenDv,
                    gia,
                    giaGoc,
                    phanTramGiam = d.flashGiam,
                    d.thoiGianLam,
                    d.moTa,
                    d.hinhAnh
                };
            }).ToList();

            // ================= TOTAL TIME =================
            int tongThoiGian = dichVuTrongCombo.Sum(x => x.thoiGianLam ?? 0);

            // ================= PRICE BASE =================
            decimal giaGocCombo = dichVuTrongCombo.Sum(x => x.giaGoc);

            // ================= FLASH SALE COMBO =================
            var flashSale = await _context.ChitietFlashsales
                .Where(cf => cf.MaCb == id)
                .Include(cf => cf.MaFlNavigation)
                .Where(cf =>
                    cf.MaFlNavigation.TrangThai == 1 &&
                    cf.MaFlNavigation.NgayBatDau <= now &&
                    cf.MaFlNavigation.NgayKetThuc >= now
                )
                .OrderByDescending(cf => cf.PhanTramGiam)
                .FirstOrDefaultAsync();

            var basePrice = combo.Gia ?? giaGocCombo;

            decimal giaSauCombo = basePrice;
            int? phanTramFlash = null;
            DateTime? ngayKetThucFlash = null;

            if (flashSale != null)
            {
                phanTramFlash = flashSale.PhanTramGiam;
                ngayKetThucFlash = flashSale.MaFlNavigation.NgayKetThuc;

                giaSauCombo = basePrice * (100 - phanTramFlash.Value) / 100;
            }

            // ================= REVIEWS =================
            var danhGias = await _context.DanhgiaDichvus
                .Where(d => d.MaCb == id && d.TrangThai == 1)
                .ToListAsync();

            double? diemTrungBinh = danhGias.Count > 0
                ? Math.Round(danhGias.Average(d => d.SoSao ?? 0), 1)
                : null;

            // ================= RELATED COMBOS =================
            var maDvTrongCombo = dichVuTrongCombo.Select(d => d.maDv).ToList();

            var relatedCombos = await _context.Combo_DichVus
                .Where(cd => maDvTrongCombo.Contains(cd.MaDV) && cd.MaCB != id)
                .Include(cd => cd.Combo)
                    .ThenInclude(c => c.Combo_DichVus)
                        .ThenInclude(x => x.DichVu)
                .Where(cd => cd.Combo.TrangThai == 1)
                .Select(cd => cd.Combo)
                .Distinct()
                .Take(3)
                .ToListAsync();

            var relatedCombosFormatted = relatedCombos.Select(c =>
            {
                var giaGoc = c.Combo_DichVus.Sum(x => x.DichVu?.Gia ?? 0);
                var gia = c.Gia ?? 0;

                int? pt = null;

                if (giaGoc > 0 && gia < giaGoc)
                {
                    pt = (int)Math.Round((1 - (double)gia / (double)giaGoc) * 100);
                }

                return new
                {
                    id = c.MaCb,
                    tenCombo = c.TenCombo,
                    gia,
                    giaGoc,
                    phanTramGiam = pt,
                    moTa = c.MoTa,
                    hinhAnh = c.HinhAnh,
                    items = c.Combo_DichVus.Select(x => x.DichVu.TenDv).ToList()
                };
            }).ToList();

            // ================= SUGGESTED SERVICES =================
            var maDmList = await _context.Dichvus
                .Where(dv => maDvTrongCombo.Contains(dv.MaDv))
                .Select(dv => dv.MaDm)
                .Distinct()
                .ToListAsync();

            var suggestedRaw = await _context.Dichvus
                .Where(dv =>
                    maDmList.Contains(dv.MaDm) &&
                    !maDvTrongCombo.Contains(dv.MaDv) &&
                    dv.TrangThai == 1
                )
                .OrderBy(dv => Guid.NewGuid())
                .Take(3)
                .Select(dv => new
                {
                    id = dv.MaDv,
                    tenDv = dv.TenDv,
                    gia = dv.Gia,
                    moTa = dv.MoTa,
                    thoiGianLam = dv.ThoiGianLam,

                    hinhAnh = _context.Hinhanhs
                        .Where(h => h.MaDv == dv.MaDv)
                        .Select(h => h.DuongDan)
                        .FirstOrDefault(),

                    flashGiam = _context.ChitietFlashsales
                        .Where(cf =>
                            cf.MaDv == dv.MaDv &&
                            cf.MaFlNavigation.TrangThai == 1 &&
                            cf.MaFlNavigation.NgayBatDau <= now &&
                            cf.MaFlNavigation.NgayKetThuc >= now)
                        .Select(cf => (int?)cf.PhanTramGiam)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var suggestedServices = suggestedRaw.Select(s =>
            {
                var giaGoc = s.gia ?? 0;

                decimal gia = s.flashGiam.HasValue
                    ? giaGoc * (100 - s.flashGiam.Value) / 100
                    : giaGoc;

                return new
                {
                    s.id,
                    s.tenDv,
                    gia,
                    giaGoc,
                    phanTramGiam = s.flashGiam,
                    s.moTa,
                    s.thoiGianLam,
                    s.hinhAnh
                };
            }).ToList();

            // ================= RESULT =================
            var result = new
            {
                maCb = combo.MaCb,
                tenCombo = combo.TenCombo,
                moTa = combo.MoTa,
                hinhAnh = combo.HinhAnh,

                gia = giaSauCombo,
                giaGoc = basePrice,
                phanTramGiam = phanTramFlash,
                ngayKetThuc = ngayKetThucFlash,

                tongThoiGian,
                soLuongDichVu = dichVuTrongCombo.Count,

                diemTrungBinh,
                soLuongDanhGia = danhGias.Count,

                dichVuTrongCombo,
                relatedCombos = relatedCombosFormatted,
                suggestedServices
            };

            return Ok(result);
        }
    }
}
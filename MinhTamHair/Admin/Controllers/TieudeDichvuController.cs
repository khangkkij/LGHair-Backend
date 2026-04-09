using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MinhTamHair.KhanchHang.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TieudeDichvuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;

        public TieudeDichvuController(MinhTamHairContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ═══════════════════════════════════════════
        // TIEU DE (Service Titles) CRUD
        // ═══════════════════════════════════════════
        [HttpGet("bydv/{maDv}")]
        public async Task<IActionResult> GetByDichVu(string maDv)
        {
            var tieudes = await _context.TieudeDichvus
                .Where(t => t.MaDv == maDv)
                .OrderBy(t => t.SoThuTu)
                .Select(t => new
                {
                    t.MaTd,
                    t.MaDv,
                    t.TenTieuDe,
                    t.MoTaTieuDe,
                    t.SoThuTu,
                    t.TrangThai,
                    ChiTiet = _context.ChitietTieudes
                        .Where(ct => ct.MaTd == t.MaTd)
                        .OrderBy(ct => ct.SoThuTu)
                        .Select(ct => new
                        {
                            ct.MaCttd,
                            ct.MaTd,
                            ct.MaDv,
                            ct.TenTieuDe,
                            ct.MoTaTieuDe,
                            ct.HinhAnh,
                            ct.SoThuTu
                        })
                        .ToList()
                })
                .ToListAsync();
            return Ok(tieudes);
        }

        /// <summary>
        /// Tạo tiêu đề dịch vụ mới
        /// </summary>
        [HttpPost("tieude")]
        public async Task<IActionResult> CreateTieuDe([FromBody] TieuDeDto dto)
        {
            var maDv = dto.MaDv?.Trim().ToUpper();
            if (string.IsNullOrEmpty(maDv))
                return BadRequest(new { message = "Mã dịch vụ không được để trống." });

            var dv = await _context.Dichvus.FindAsync(maDv);
            if (dv == null)
                return NotFound(new { message = "Không tìm thấy dịch vụ." });

            // 🔥 Lấy mã lớn nhất hiện tại
            var lastMa = await _context.TieudeDichvus
                .OrderByDescending(x => x.MaTd)
                .Select(x => x.MaTd)
                .FirstOrDefaultAsync();

            int number = 0;

            if (!string.IsNullOrEmpty(lastMa))
            {
                int.TryParse(lastMa.Substring(2), out number);
            }

            string newMaTd;

            // 🔥 Loop tránh trùng (quan trọng)
            do
            {
                number++;
                newMaTd = "TD" + number.ToString("D3");
            }
            while (await _context.TieudeDichvus.AnyAsync(x => x.MaTd == newMaTd));

            var tieude = new TieudeDichvu
            {
                MaTd = newMaTd,
                MaDv = maDv,
                TenTieuDe = dto.TenTieuDe?.Trim() ?? "",
                MoTaTieuDe = dto.MoTaTieuDe?.Trim() ?? "",
                SoThuTu = dto.SoThuTu ?? 0,
                TrangThai = dto.TrangThai,
                LoaiHienThi = 1
            };

            try
            {
                _context.TieudeDichvus.Add(tieude);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }

            return Ok(new
            {
                tieude.MaTd,
                tieude.MaDv,
                tieude.TenTieuDe,
                tieude.MoTaTieuDe,
                tieude.SoThuTu,
                tieude.TrangThai,
                ChiTiet = new List<object>()
            });
        }

        /// <summary>
        /// Sửa tiêu đề dịch vụ
        /// </summary>
        [HttpPut("tieude/{maTd}")]
        public async Task<IActionResult> UpdateTieuDe(string maTd, [FromBody] TieuDeDto dto)
        {
            var tieude = await _context.TieudeDichvus.FindAsync(maTd);
            if (tieude == null)
                return NotFound(new { message = "Không tìm thấy tiêu đề." });

            tieude.TenTieuDe = dto.TenTieuDe?.Trim() ?? tieude.TenTieuDe;
            tieude.MoTaTieuDe = dto.MoTaTieuDe?.Trim();
            tieude.SoThuTu = dto.SoThuTu;
            tieude.TrangThai = dto.TrangThai;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                tieude.MaTd,
                tieude.MaDv,
                tieude.TenTieuDe,
                tieude.MoTaTieuDe,
                tieude.SoThuTu,
                tieude.TrangThai
            });
        }

        /// <summary>
        /// Xóa tiêu đề dịch vụ (kèm chi tiết)
        /// </summary>
        [HttpDelete("tieude/{maTd}")]
        public async Task<IActionResult> DeleteTieuDe(string maTd)
        {
            var tieude = await _context.TieudeDichvus
                .Include(t => t.ChitietTieudes)
                .FirstOrDefaultAsync(t => t.MaTd == maTd);

            if (tieude == null)
                return NotFound(new { message = "Không tìm thấy tiêu đề." });

            // Xóa chi tiết trước
            if (tieude.ChitietTieudes != null && tieude.ChitietTieudes.Any())
            {
                _context.ChitietTieudes.RemoveRange(tieude.ChitietTieudes);
            }

            _context.TieudeDichvus.Remove(tieude);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa tiêu đề và chi tiết." });
        }

        // ═══════════════════════════════════════════
        // CHI TIET TIEU DE (Title Details) CRUD
        // ═══════════════════════════════════════════

        /// <summary>
        /// Tạo chi tiết tiêu đề mới
        /// </summary>
        [HttpPost("chitiet")]
        public async Task<IActionResult> CreateChiTiet([FromForm] ChiTietDto dto)
        {
            var maTd = dto.MaTd?.Trim();
            var maDv = dto.MaDv?.Trim().ToUpper();

            if (string.IsNullOrEmpty(maTd) || string.IsNullOrEmpty(maDv))
                return BadRequest(new { message = "Thiếu mã tiêu đề hoặc mã dịch vụ." });

            var tieude = await _context.TieudeDichvus.FindAsync(maTd);
            if (tieude == null)
                return NotFound(new { message = "Không tìm thấy tiêu đề." });

            string? hinhAnhPath = null;
            if (dto.File != null && dto.File.Length > 0)
            {
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/assets/images");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var fileName = Guid.NewGuid() + Path.GetExtension(dto.File.FileName);
                var filePath = Path.Combine(folder, fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await dto.File.CopyToAsync(stream);

                hinhAnhPath = "/assets/images/" + fileName;
            }
            var chitiet = new ChitietTieude
            {
                MaTd = maTd,
                MaDv = maDv,
                TenTieuDe = dto.TenTieuDe?.Trim() ?? "",
                MoTaTieuDe = dto.MoTaTieuDe?.Trim(),
                HinhAnh = hinhAnhPath,
                SoThuTu = dto.SoThuTu
            };

            _context.ChitietTieudes.Add(chitiet);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                chitiet.MaCttd,
                chitiet.MaTd,
                chitiet.MaDv,
                chitiet.TenTieuDe,
                chitiet.MoTaTieuDe,
                chitiet.HinhAnh,
                chitiet.SoThuTu
            });
        }

        /// <summary>
        /// Sửa chi tiết tiêu đề
        /// </summary>
        [HttpPut("chitiet/{maCttd}")]
        public async Task<IActionResult> UpdateChiTiet(int maCttd, [FromForm] ChiTietDto dto)
        {
            var chitiet = await _context.ChitietTieudes.FindAsync(maCttd);
            if (chitiet == null)
                return NotFound(new { message = "Không tìm thấy chi tiết." });

            chitiet.TenTieuDe = dto.TenTieuDe?.Trim() ?? chitiet.TenTieuDe;
            chitiet.MoTaTieuDe = dto.MoTaTieuDe?.Trim() ?? "";
            chitiet.SoThuTu = dto.SoThuTu ?? 0;

            // Upload ảnh mới nếu có
            if (dto.File != null && dto.File.Length > 0)
            {
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/assets/images");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var fileName = Guid.NewGuid() + Path.GetExtension(dto.File.FileName);
                var filePath = Path.Combine(folder, fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await dto.File.CopyToAsync(stream);

                chitiet.HinhAnh = "/assets/images/" + fileName;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                chitiet.MaCttd,
                chitiet.MaTd,
                chitiet.MaDv,
                chitiet.TenTieuDe,
                chitiet.MoTaTieuDe,
                chitiet.HinhAnh,
                chitiet.SoThuTu
            });
        }

        /// <summary>
        /// Xóa chi tiết tiêu đề
        /// </summary>
        [HttpDelete("chitiet/{maCttd}")]
        public async Task<IActionResult> DeleteChiTiet(int maCttd)
        {
            var chitiet = await _context.ChitietTieudes.FindAsync(maCttd);
            if (chitiet == null)
                return NotFound(new { message = "Không tìm thấy chi tiết." });

            // Xóa file ảnh trên server
            if (!string.IsNullOrEmpty(chitiet.HinhAnh))
            {
                var path = Path.Combine("wwwroot", chitiet.HinhAnh.TrimStart('/'));
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }

            _context.ChitietTieudes.Remove(chitiet);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa chi tiết." });
        }
    }
}

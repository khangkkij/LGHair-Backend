using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using MinhTamHair.Services.UploadIMG;

namespace MinhTamHair.KhanchHang.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TieudeDichvuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IUploadDVSP _uploadImg;
        private readonly IServiceCacheService _cache;

        public TieudeDichvuController(MinhTamHairContext context, IUploadDVSP uploadImg, IServiceCacheService cache)
        {
            _context = context;
            _uploadImg = uploadImg;
            _cache = cache;
        }

        // Upload ảnh lên Cloudinary (cloud CloudinaryDVSP, folder "DVSP").
        private async Task<(string? Url, string? Error)> SaveImageAsync(IFormFile file)
        {
            var result = await _uploadImg.AddIMG(file);
            if (result?.Error != null)
                return (null, result.Error.Message);
            if (string.IsNullOrEmpty(result?.SecureUrl?.ToString()))
                return (null, "Upload ảnh lên Cloudinary thất bại.");

            return (result.SecureUrl.ToString(), null);
        }

        // Xóa ảnh trên Cloudinary dựa vào URL đầy đủ đã lưu trong DB.
        private async Task DeleteImageAsync(string? imageUrl)
        {
            var publicId = GetPublicIdFromUrl(imageUrl);
            if (publicId == null) return;
            await _uploadImg.DelIMG(publicId);
        }

        // URL Cloudinary dạng: https://res.cloudinary.com/<cloud>/image/upload/v.../DVSP/abc.jpg
        // Public ID là phần "DVSP/abc" (bỏ domain, "v<version>/" và đuôi mở rộng).
        private static string? GetPublicIdFromUrl(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.Contains("res.cloudinary.com"))
                return null;

            var uploadIndex = imageUrl.IndexOf("/upload/", StringComparison.OrdinalIgnoreCase);
            if (uploadIndex < 0) return null;

            var afterUpload = imageUrl[(uploadIndex + "/upload/".Length)..];
            var parts = afterUpload.Split('/');
            var startIdx = (parts.Length > 0 && parts[0].StartsWith('v') && parts[0].Length > 1 && parts[0][1..].All(char.IsDigit))
                ? 1
                : 0;

            var withoutVersion = string.Join('/', parts[startIdx..]);
            var lastDot = withoutVersion.LastIndexOf('.');
            return lastDot > 0 ? withoutVersion[..lastDot] : withoutVersion;
        }

        // ═══════════════════════════════════════════
        // TIEU DE (Service Titles) CRUD
        // ═══════════════════════════════════════════
        [HttpGet("bydv/{maDv}")]
        public async Task<IActionResult> GetByDichVu(string maDv)
        {
            maDv = maDv?.Trim().ToUpper();

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

            await _cache.InvalidateAllServiceCacheAsync();

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
            await _cache.InvalidateAllServiceCacheAsync();

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

            // 🔥 Xóa ảnh của từng chi tiết (trên Cloudinary)
            if (tieude.ChitietTieudes != null && tieude.ChitietTieudes.Any())
            {
                foreach (var ct in tieude.ChitietTieudes)
                {
                    await DeleteImageAsync(ct.HinhAnh);
                }

                _context.ChitietTieudes.RemoveRange(tieude.ChitietTieudes);
            }

            _context.TieudeDichvus.Remove(tieude);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();

            return Ok(new { message = "Đã xóa tiêu đề và toàn bộ ảnh liên quan." });
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
                var (url, error) = await SaveImageAsync(dto.File);
                if (error != null)
                    return BadRequest(new { message = error });

                hinhAnhPath = url;
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
            await _cache.InvalidateAllServiceCacheAsync();

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

            // 🔥 Nếu có ảnh mới → xóa ảnh cũ + lưu ảnh mới
            if (dto.File != null && dto.File.Length > 0)
            {
                // ✅ XÓA ẢNH CŨ (trên Cloudinary)
                await DeleteImageAsync(chitiet.HinhAnh);

                // ✅ LƯU ẢNH MỚI (lên Cloudinary)
                var (url, error) = await SaveImageAsync(dto.File);
                if (error != null)
                    return BadRequest(new { message = error });

                chitiet.HinhAnh = url;
            }

            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();

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

            // Xóa ảnh trên Cloudinary
            await DeleteImageAsync(chitiet.HinhAnh);

            _context.ChitietTieudes.Remove(chitiet);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();

            return Ok(new { message = "Đã xóa chi tiết." });
        }
    }
}
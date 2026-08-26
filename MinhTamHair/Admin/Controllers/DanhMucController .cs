using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System.Text.Json;
using MinhTamHair.Services.UploadIMG;
namespace MinhTamHair.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DanhMucController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IUploadDVSP _uploadImg;
        private readonly IServiceCacheService _cache;

        public DanhMucController(MinhTamHairContext context, IWebHostEnvironment env, IUploadDVSP uploadImg, IServiceCacheService cache)
        {
            _context = context;
            _env = env;
            _uploadImg = uploadImg;
            _cache = cache;
        }

        private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

        private (bool ok, string? error) ValidateImage(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExt.Contains(ext))
                return (false, "Chỉ chấp nhận ảnh định dạng JPG, PNG, WEBP, GIF.");
            if (file.Length > MaxFileSize)
                return (false, "Dung lượng ảnh tối đa 5MB.");
            return (true, null);
        }

        // Upload ảnh lên Cloudinary (cloud CloudinaryDVSP, folder "DVSP").
        // Trả về SecureUrl đầy đủ (https://res.cloudinary.com/...) để lưu thẳng vào cột HinhAnh.
        private async Task<string?> SaveImageAsync(IFormFile file)
        {
            var result = await _uploadImg.AddIMG(file);
            if (result?.Error != null) return null;
            return result?.SecureUrl?.ToString();
        }

        // Xóa ảnh trên Cloudinary dựa vào URL đầy đủ đã lưu trong DB.
        private async Task DeleteImageFile(string? imageUrl)
        {
            var publicId = GetPublicIdFromUrl(imageUrl);
            if (publicId == null) return;
            await _uploadImg.DelIMG(publicId);
        }

        // Cột HinhAnh giờ lưu URL Cloudinary đầy đủ — không cần ghép domain nữa,
        // chỉ trả về nguyên giá trị (giữ tên hàm cũ để không phải sửa chỗ gọi).
        private string? GetImageUrl(string? hinhAnh)
        {
            return string.IsNullOrEmpty(hinhAnh) ? null : hinhAnh;
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

        // 🔢 Sinh mã danh mục tự động dạng DM01, DM02, DM03...
        private async Task<string> GenerateMaDanhMucAsync()
        {
            var maDanhMucs = await _context.DanhMucs
                .Where(d => d.MaDm.StartsWith("DM"))
                .Select(d => d.MaDm.Substring(2))
                .ToListAsync();

            var maxNumber = 0;
            foreach (var s in maDanhMucs)
            {
                if (int.TryParse(s, out var n) && n > maxNumber)
                {
                    maxNumber = n;
                }
            }

            var next = maxNumber + 1;
            return $"DM{next:D2}";
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(string? keyword, int? trangThai)
        {
            var query = _context.DanhMucs
                .Include(d => d.Dichvus)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(d =>
                    d.TenDanhMuc.Contains(keyword) ||
                    d.MaDm.Contains(keyword));

            if (trangThai.HasValue)
                query = query.Where(d => d.TrangThai == trangThai.Value);

            var list = await query.ToListAsync();

            // 🆕 Mã danh mục mới nhất (số lớn hơn) lên đầu, mã cũ xuống sau
            list = list
                .OrderByDescending(d =>
                {
                    var suffix = d.MaDm != null && d.MaDm.StartsWith("DM")
                        ? d.MaDm.Substring(2)
                        : d.MaDm;
                    return int.TryParse(suffix, out var n) ? n : -1;
                })
                .ToList();

            var result = list.Select(d => new
            {
                maDM = d.MaDm,
                tenDanhMuc = d.TenDanhMuc,
                hinhAnh = GetImageUrl(d.HinhAnh),
                ngayTao = d.NgayTao,
                trangThai = d.TrangThai,
                soDichVu = d.Dichvus?.Count ?? 0
            });
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var d = await _context.DanhMucs
                .Include(x => x.Dichvus)
                .FirstOrDefaultAsync(x => x.MaDm == id);

            if (d == null) return NotFound(new { message = "Không tìm thấy danh mục." });

            var result = new
            {
                maDM = d.MaDm,
                tenDanhMuc = d.TenDanhMuc,
                hinhAnh = GetImageUrl(d.HinhAnh),
                ngayTao = d.NgayTao,
                trangThai = d.TrangThai,
                soDichVu = d.Dichvus?.Count ?? 0
            };
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] DanhMucDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenDanhMuc))
                return BadRequest(new { message = "Tên danh mục không được để trống." });
            // ❌ Check tên danh mục
            if (await _context.DanhMucs.AnyAsync(x => x.TenDanhMuc.ToLower() == dto.TenDanhMuc.Trim().ToLower()))
                return BadRequest(new { message = "Tên danh mục đã tồn tại." });

            // 🔢 Mã danh mục được sinh tự động (DM01, DM02, ...), không nhận từ client
            var maDm = await GenerateMaDanhMucAsync();

            // ❌ Validate & lưu ảnh (nếu có upload)
            string? fileName = null;
            if (dto.HinhAnh != null && dto.HinhAnh.Length > 0)
            {
                var (ok, error) = ValidateImage(dto.HinhAnh);
                if (!ok) return BadRequest(new { message = error });
                fileName = await SaveImageAsync(dto.HinhAnh);
            }

            var entity = new DanhMuc
            {
                MaDm = maDm,
                TenDanhMuc = dto.TenDanhMuc.Trim(),
                HinhAnh = fileName,
                NgayTao = DateOnly.FromDateTime(DateTime.Today),
                TrangThai = dto.TrangThai
            };

            _context.DanhMucs.Add(entity);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();

            return Ok(new { message = "Thêm danh mục thành công.", maDM = entity.MaDm });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromForm] DanhMucDTO dto)
        {
            var entity = await _context.DanhMucs.FindAsync(id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy danh mục." });

            if (string.IsNullOrWhiteSpace(dto.TenDanhMuc))
                return BadRequest(new { message = "Tên danh mục không được để trống." });

            if (await _context.DanhMucs.AnyAsync(x => x.MaDm != id && x.TenDanhMuc.ToLower() == dto.TenDanhMuc.Trim().ToLower()))
                return BadRequest(new { message = "Tên danh mục đã tồn tại." });

            entity.TenDanhMuc = dto.TenDanhMuc.Trim();

            if (dto.HinhAnh != null && dto.HinhAnh.Length > 0)
            {
                var (ok, error) = ValidateImage(dto.HinhAnh);
                if (!ok) return BadRequest(new { message = error });

                var oldImage = entity.HinhAnh;
                entity.HinhAnh = await SaveImageAsync(dto.HinhAnh);
                await DeleteImageFile(oldImage);
            }
            else if (dto.XoaHinh)
            {
                await DeleteImageFile(entity.HinhAnh);
                entity.HinhAnh = null;
            }

            if (dto.TrangThai.HasValue && dto.TrangThai != entity.TrangThai)
            {
                entity.TrangThai = dto.TrangThai;
                await CascadeTrangThaiDichVuAsync(id, dto.TrangThai.Value);
            }

            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();
            return Ok(new { message = "Cập nhật danh mục thành công." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var entity = await _context.DanhMucs
                .Include(x => x.Dichvus)
                .FirstOrDefaultAsync(x => x.MaDm == id);

            if (entity == null)
                return NotFound(new { message = "Không tìm thấy danh mục." });

            if (entity.Dichvus != null && entity.Dichvus.Any())
                return BadRequest(new
                {
                    message = $"Không thể xóa! Danh mục đang chứa {entity.Dichvus.Count} dịch vụ."
                });

            // ❌ Xóa file ảnh vật lý trên đĩa (nếu có) trước khi xóa bản ghi
            await DeleteImageFile(entity.HinhAnh);

            _context.DanhMucs.Remove(entity);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();

            return Ok(new { message = "Xóa danh mục thành công." });
        }

        [HttpPatch("{id}/trangthai")]
        public async Task<IActionResult> ToggleTrangThai(string id, [FromBody] ToggleDTO dto)
        {
            var entity = await _context.DanhMucs.FindAsync(id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy danh mục." });

            if (dto.TrangThai != entity.TrangThai)
            {
                entity.TrangThai = dto.TrangThai;
                await CascadeTrangThaiDichVuAsync(id, dto.TrangThai);
            }

            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();
            return Ok(new { message = "Cập nhật trạng thái thành công." });
        }

        private async Task CascadeTrangThaiDichVuAsync(string maDM, int trangThaiMoi)
        {
            var dichVus = await _context.Dichvus
                .Where(dv => dv.MaDm == maDM && dv.TrangThai != trangThaiMoi)
                .ToListAsync();

            foreach (var dv in dichVus)
                dv.TrangThai = trangThaiMoi;
        }

    }
}
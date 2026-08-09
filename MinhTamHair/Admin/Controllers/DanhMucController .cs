using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MinhTamHair.Models;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
namespace MinhTamHair.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DanhMucController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;
        private readonly IWebHostEnvironment _env;

        // Thư mục lưu ảnh danh mục (bên trong wwwroot)
        private const string UploadFolder = "uploads/danhmuc";
        private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

        public DanhMucController(MinhTamHairContext context, IDistributedCache cache, IWebHostEnvironment env)
        {
            _context = context;
            _cache = cache;
            _env = env;
        }

        private (bool ok, string? error) ValidateImage(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExt.Contains(ext))
                return (false, "Chỉ chấp nhận ảnh định dạng JPG, PNG, WEBP, GIF.");
            if (file.Length > MaxFileSize)
                return (false, "Dung lượng ảnh tối đa 5MB.");
            return (true, null);
        }

        private string GetUploadFolderPath()
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folderPath = Path.Combine(webRoot, UploadFolder);
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        private async Task<string> SaveImageAsync(IFormFile file)
        {
            var folderPath = GetUploadFolderPath();
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(folderPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            return fileName;
        }

        private void DeleteImageFile(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            var filePath = Path.Combine(GetUploadFolderPath(), fileName);
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        private string? GetImageUrl(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            return $"{Request.Scheme}://{Request.Host}/{UploadFolder}/{fileName}";
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

            var list = await query
                .OrderByDescending(d => d.NgayTao)
                .ToListAsync();

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
            // Validate mã
            if (string.IsNullOrWhiteSpace(dto.MaDM))
                return BadRequest(new { message = "Mã danh mục không được để trống." });

            if (await _context.DanhMucs.AnyAsync(x => x.MaDm == dto.MaDM.Trim()))
                return BadRequest(new { message = "Mã danh mục đã tồn tại." });

            if (string.IsNullOrWhiteSpace(dto.TenDanhMuc))
                return BadRequest(new { message = "Tên danh mục không được để trống." });
            // ❌ Check tên danh mục
            if (await _context.DanhMucs.AnyAsync(x => x.TenDanhMuc.ToLower() == dto.TenDanhMuc.Trim().ToLower()))
                return BadRequest(new { message = "Tên danh mục đã tồn tại." });

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
                MaDm = dto.MaDM.Trim().ToUpper(),
                TenDanhMuc = dto.TenDanhMuc.Trim(),
                HinhAnh = fileName,
                NgayTao = DateOnly.FromDateTime(DateTime.Today),
                TrangThai = dto.TrangThai
            };

            _context.DanhMucs.Add(entity);
            await _context.SaveChangesAsync();

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
                DeleteImageFile(oldImage);
            }
            else if (dto.XoaHinh)
            {
                DeleteImageFile(entity.HinhAnh);
                entity.HinhAnh = null;
            }

            if (dto.TrangThai.HasValue && dto.TrangThai != entity.TrangThai)
            {
                entity.TrangThai = dto.TrangThai;
                await CascadeTrangThaiDichVuAsync(id, dto.TrangThai.Value);
            }

            await _context.SaveChangesAsync();
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
            DeleteImageFile(entity.HinhAnh);

            _context.DanhMucs.Remove(entity);
            await _context.SaveChangesAsync();

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
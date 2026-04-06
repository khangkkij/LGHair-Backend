using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DanhMucController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IWebHostEnvironment _env;

        public DanhMucController(MinhTamHairContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ──────────────────────────────────────────────────────────
        // GET /api/DanhMuc?keyword=&trangThai=
        // ──────────────────────────────────────────────────────────
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
                hinhAnh = string.IsNullOrEmpty(d.HinhAnh) ? null : d.HinhAnh,
                ngayTao = d.NgayTao,
                trangThai = d.TrangThai,
                soDichVu = d.Dichvus?.Count ?? 0
            });

            return Ok(result);
        }

        // ──────────────────────────────────────────────────────────
        // GET /api/DanhMuc/{id}
        // ──────────────────────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var d = await _context.DanhMucs
                .Include(x => x.Dichvus)
                .FirstOrDefaultAsync(x => x.MaDm == id);

            if (d == null) return NotFound(new { message = "Không tìm thấy danh mục." });

            return Ok(new
            {
                maDM = d.MaDm,
                tenDanhMuc = d.TenDanhMuc,
                hinhAnh = d.HinhAnh,
                ngayTao = d.NgayTao,
                trangThai = d.TrangThai,
                soDichVu = d.Dichvus?.Count ?? 0
            });
        }

        // ──────────────────────────────────────────────────────────
        // POST /api/DanhMuc  (multipart/form-data)
        // ──────────────────────────────────────────────────────────
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

            string? hinhAnhPath = null;
            if (dto.File != null)
            {
                hinhAnhPath = await SaveImageAsync(dto.File);
                if (hinhAnhPath == null)
                    return BadRequest(new { message = "File không hợp lệ. Chỉ nhận PNG, JPG, WEBP và ≤ 5MB." });
            }

            var entity = new DanhMuc
            {
                MaDm = dto.MaDM.Trim().ToUpper(),
                TenDanhMuc = dto.TenDanhMuc.Trim(),
                HinhAnh = hinhAnhPath,
                NgayTao = DateOnly.FromDateTime(DateTime.Today),
                TrangThai = dto.TrangThai
            };

            _context.DanhMucs.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Thêm danh mục thành công.", maDM = entity.MaDm });
        }

        // ──────────────────────────────────────────────────────────
        // PUT /api/DanhMuc/{id}  (multipart/form-data)
        // ──────────────────────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromForm] DanhMucDTO dto)
        {
            var entity = await _context.DanhMucs.FindAsync(id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy danh mục." });

            if (string.IsNullOrWhiteSpace(dto.TenDanhMuc))
                return BadRequest(new { message = "Tên danh mục không được để trống." });

            // Xử lý ảnh
            if (dto.File != null)
            {
                // Xóa ảnh cũ
                DeleteImageFile(entity.HinhAnh);

                var newPath = await SaveImageAsync(dto.File);
                if (newPath == null)
                    return BadRequest(new { message = "File không hợp lệ. Chỉ nhận PNG, JPG, WEBP và ≤ 5MB." });

                entity.HinhAnh = newPath;
            }
            else if (dto.XoaAnh == true)
            {
                // Người dùng bấm xóa ảnh nhưng không upload ảnh mới
                DeleteImageFile(entity.HinhAnh);
                entity.HinhAnh = null;
            }
            // Nếu không gửi File và XoaAnh != true → giữ nguyên ảnh cũ

            entity.TenDanhMuc = dto.TenDanhMuc.Trim();
            entity.TrangThai = dto.TrangThai;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật danh mục thành công." });
        }

        // ──────────────────────────────────────────────────────────
        // DELETE /api/DanhMuc/{id}
        // ──────────────────────────────────────────────────────────
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

            DeleteImageFile(entity.HinhAnh);
            _context.DanhMucs.Remove(entity);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa danh mục thành công." });
        }

        // ──────────────────────────────────────────────────────────
        // PATCH /api/DanhMuc/{id}/trangthai  — toggle nhanh
        // ──────────────────────────────────────────────────────────
        [HttpPatch("{id}/trangthai")]
        public async Task<IActionResult> ToggleTrangThai(string id, [FromBody] ToggleDTO dto)
        {
            var entity = await _context.DanhMucs.FindAsync(id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy danh mục." });

            entity.TrangThai = dto.TrangThai;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật trạng thái thành công." });
        }

        // ──────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────
        private async Task<string?> SaveImageAsync(IFormFile file)
        {
            var allowed = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
            if (!allowed.Contains(file.ContentType.ToLower())) return null;
            if (file.Length > 5 * 1024 * 1024) return null;

            var folder = Path.Combine(_env.WebRootPath, "uploads", "danhmuc");
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(folder, fileName);

            await using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);

            return $"/uploads/danhmuc/{fileName}";
        }

        private void DeleteImageFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            var fullPath = Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
    }
}

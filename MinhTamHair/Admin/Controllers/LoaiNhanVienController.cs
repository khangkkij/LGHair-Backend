using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MinhTamHair.Controllers
{
    public class LoaiNhanVienDto
    {
        public string? MaLoaiNv { get; set; }
        public string TenLoai { get; set; } = null!;
        public decimal? Luong { get; set; }
        public string? MoTa { get; set; }
        public bool? DuocDatLich { get; set; }
        public int? TrangThai { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class LoaiNhanVienController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public LoaiNhanVienController(MinhTamHairContext context)
        {
            _context = context;
        }

        // ──────────────────────────────────────────────────────────
        // GET /api/LoaiNhanVien?keyword=&trangThai=
        // ──────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? keyword, [FromQuery] int? trangThai)
        {
            try
            {
                var query = _context.Loainhanviens
                    .Include(l => l.Nhanviens)
                    .AsNoTracking()
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var kw = keyword.Trim().ToLower();
                    query = query.Where(l =>
                        (l.TenLoai != null && l.TenLoai.ToLower().Contains(kw)) ||
                        l.MaLoaiNv.ToLower().Contains(kw));
                }

                if (trangThai.HasValue)
                {
                    query = query.Where(l => l.TrangThai == trangThai.Value);
                }

                var list = await query
                    .OrderByDescending(l => l.TrangThai)
                    .ThenBy(l => l.TenLoai)
                    .Select(l => new
                    {
                        maLoaiNv = l.MaLoaiNv,
                        tenLoai = l.TenLoai ?? "Chưa đặt tên",
                        luong = l.Luong ?? 0,
                        moTa = l.MoTa ?? "",
                        duocDatLich = l.DuocDatLich ?? false,
                        trangThai = l.TrangThai ?? 1,
                        soNhanVien = l.Nhanviens.Count(nv => nv.TrangThai == 1) // Chỉ đếm nhân viên đang làm việc
                    })
                    .ToListAsync();

                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────
        // GET /api/LoaiNhanVien/{id}
        // ──────────────────────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var l = await _context.Loainhanviens
                    .Include(x => x.Nhanviens)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MaLoaiNv == id);

                if (l == null)
                    return NotFound(new { message = "Không tìm thấy chức vụ." });

                var result = new
                {
                    maLoaiNv = l.MaLoaiNv,
                    tenLoai = l.TenLoai ?? "",
                    luong = l.Luong ?? 0,
                    moTa = l.MoTa ?? "",
                    duocDatLich = l.DuocDatLich ?? false,
                    trangThai = l.TrangThai ?? 1,
                    soNhanVien = l.Nhanviens.Count(nv => nv.TrangThai == 1)
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────
        // POST /api/LoaiNhanVien
        // ──────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] LoaiNhanVienDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.TenLoai))
                    return BadRequest(new { message = "Tên chức vụ không được để trống." });

                var tenClean = dto.TenLoai.Trim();

                // Kiểm tra trùng tên chức vụ
                if (await _context.Loainhanviens.AnyAsync(x => x.TenLoai != null && x.TenLoai.ToLower() == tenClean.ToLower()))
                    return BadRequest(new { message = "Tên chức vụ đã tồn tại trong hệ thống." });

                string maLoai;
                if (!string.IsNullOrWhiteSpace(dto.MaLoaiNv))
                {
                    maLoai = dto.MaLoaiNv.Trim().ToUpper();
                    if (await _context.Loainhanviens.AnyAsync(x => x.MaLoaiNv == maLoai))
                        return BadRequest(new { message = "Mã chức vụ đã tồn tại." });
                }
                else
                {
                    maLoai = "LNV" + MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().ToString("yyMMddHHmmss");
                }

                var entity = new Loainhanvien
                {
                    MaLoaiNv = maLoai,
                    TenLoai = tenClean,
                    Luong = dto.Luong ?? 0,
                    MoTa = dto.MoTa?.Trim(),
                    DuocDatLich = dto.DuocDatLich ?? false,
                    TrangThai = dto.TrangThai ?? 1
                };

                _context.Loainhanviens.Add(entity);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Thêm chức vụ thành công!",
                    data = new
                    {
                        maLoaiNv = entity.MaLoaiNv,
                        tenLoai = entity.TenLoai,
                        luong = entity.Luong,
                        moTa = entity.MoTa,
                        duocDatLich = entity.DuocDatLich,
                        trangThai = entity.TrangThai,
                        soNhanVien = 0
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────
        // PUT /api/LoaiNhanVien/{id}
        // ──────────────────────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] LoaiNhanVienDto dto)
        {
            try
            {
                var entity = await _context.Loainhanviens.FindAsync(id);
                if (entity == null)
                    return NotFound(new { message = "Không tìm thấy chức vụ cần cập nhật." });

                if (string.IsNullOrWhiteSpace(dto.TenLoai))
                    return BadRequest(new { message = "Tên chức vụ không được để trống." });

                var tenClean = dto.TenLoai.Trim();

                // Kiểm tra trùng tên với chức vụ khác
                if (await _context.Loainhanviens.AnyAsync(x => x.MaLoaiNv != id && x.TenLoai != null && x.TenLoai.ToLower() == tenClean.ToLower()))
                    return BadRequest(new { message = "Tên chức vụ này đã được sử dụng bởi một chức vụ khác." });

                entity.TenLoai = tenClean;
                if (dto.Luong.HasValue) entity.Luong = dto.Luong.Value;
                if (dto.MoTa != null) entity.MoTa = dto.MoTa.Trim();
                if (dto.DuocDatLich.HasValue) entity.DuocDatLich = dto.DuocDatLich.Value;
                if (dto.TrangThai.HasValue) entity.TrangThai = dto.TrangThai.Value;

                await _context.SaveChangesAsync();

                return Ok(new { message = "Cập nhật chức vụ thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────
        // DELETE /api/LoaiNhanVien/{id}
        // ──────────────────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var entity = await _context.Loainhanviens
                    .Include(l => l.Nhanviens)
                    .FirstOrDefaultAsync(l => l.MaLoaiNv == id);

                if (entity == null)
                    return NotFound(new { message = "Không tìm thấy chức vụ cần xóa." });

                // Kiểm tra xem có nhân viên nào đang thuộc chức vụ này không
                if (entity.Nhanviens != null && entity.Nhanviens.Any())
                {
                    return BadRequest(new
                    {
                        message = $"Không thể xóa chức vụ '{entity.TenLoai}' vì đang có {entity.Nhanviens.Count} nhân viên giữ chức vụ này. Vui lòng đổi chức vụ của nhân viên hoặc chuyển trạng thái sang Khóa."
                    });
                }

                _context.Loainhanviens.Remove(entity);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Xóa chức vụ thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }
    }
}

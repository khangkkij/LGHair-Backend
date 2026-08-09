using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuanLyDanhMucSPController : ControllerBase
{
    private readonly MinhTamHairContext _context;
    private readonly IWebHostEnvironment _env;

    public QuanLyDanhMucSPController(MinhTamHairContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // ══ GET ALL ══════════════════════════════════════════════════════
    /// <summary>Lấy danh sách tất cả danh mục sản phẩm kèm số lượng SP</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? keyword, [FromQuery] int? trangThai)
    {
        var query = _context.DanhMucSPs
            .Include(d => d.SanPhams)
            .Where(d => d.TrangThai != TRANG_THAI_BANNER) // loại bỏ các row banner khỏi danh sách danh mục
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(d =>
                d.TenDanhMuc!.Contains(keyword) ||
                d.MaDMSP.Contains(keyword));

        if (trangThai.HasValue)
            query = query.Where(d => d.TrangThai == trangThai.Value);

        var data = await query
            .OrderBy(d => d.TenDanhMuc)
            .Select(d => new DanhMucSPDto
            {
                MaDMSP = d.MaDMSP,
                TenDanhMuc = d.TenDanhMuc,
                HinhAnh = d.HinhAnh,
                TrangThai = d.TrangThai ?? 1,
                SoSanPham = d.SanPhams.Count,
            })
            .ToListAsync();

        return Ok(data);
    }

    // ══ GET BY ID ═════════════════════════════════════════════════════
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var d = await _context.DanhMucSPs
            .Include(x => x.SanPhams)
            .FirstOrDefaultAsync(x => x.MaDMSP == id && x.TrangThai != TRANG_THAI_BANNER);

        if (d == null) return NotFound(new { message = "Không tìm thấy danh mục." });

        return Ok(new DanhMucSPDto
        {
            MaDMSP = d.MaDMSP,
            TenDanhMuc = d.TenDanhMuc,
            HinhAnh = d.HinhAnh,
            TrangThai = d.TrangThai ?? 1,
            SoSanPham = d.SanPhams.Count,
        });
    }

    // ══ CREATE ════════════════════════════════════════════════════════
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateDanhMucSPRequest req)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault();
            return BadRequest(new { message = firstError });
        }

        var maDMSP = req.MaDMSP?.Trim().ToUpper();
        if (string.IsNullOrWhiteSpace(maDMSP))
            return BadRequest(new { message = "Mã danh mục không được để trống." });

        if (await _context.DanhMucSPs.AnyAsync(d => d.MaDMSP == maDMSP))
            return Conflict(new { message = $"Mã danh mục \"{maDMSP}\" đã tồn tại trong hệ thống." });

        // Kiểm tra tên trùng
        var tenNorm = req.TenDanhMuc?.Trim();
        if (await _context.DanhMucSPs.AnyAsync(d =>
                d.TenDanhMuc != null &&
                d.TenDanhMuc.ToLower() == tenNorm!.ToLower()))
            return Conflict(new { message = $"Tên danh mục \"{tenNorm}\" đã được sử dụng bởi danh mục khác." });

        string? imgPath = null;
        if (req.File != null)
        {
            var result = await SaveImageAsync(req.File, "danhmuc");
            if (result.Error != null)
                return BadRequest(new { message = result.Error });
            imgPath = result.Path;
        }

        var dm = new DanhMucSP
        {
            MaDMSP = maDMSP,
            TenDanhMuc = tenNorm,
            HinhAnh = imgPath,
            TrangThai = req.TrangThai ?? 1,
        };

        _context.DanhMucSPs.Add(dm);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = dm.MaDMSP }, new DanhMucSPDto
        {
            MaDMSP = dm.MaDMSP,
            TenDanhMuc = dm.TenDanhMuc,
            HinhAnh = dm.HinhAnh,
            TrangThai = dm.TrangThai ?? 1,
            SoSanPham = 0,
        });
    }

    // ══ UPDATE ════════════════════════════════════════════════════════
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromForm] UpdateDanhMucSPRequest req)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault();
            return BadRequest(new { message = firstError });
        }

        var dm = await _context.DanhMucSPs.FindAsync(id);
        if (dm == null || dm.TrangThai == TRANG_THAI_BANNER)
            return NotFound(new { message = "Không tìm thấy danh mục." });

        if (!string.IsNullOrWhiteSpace(req.TenDanhMuc))
        {
            var tenNorm = req.TenDanhMuc.Trim();

            // Kiểm tra tên trùng với danh mục khác
            var dup = await _context.DanhMucSPs.FirstOrDefaultAsync(d =>
                d.MaDMSP != id &&
                d.TrangThai != TRANG_THAI_BANNER &&
                d.TenDanhMuc != null &&
                d.TenDanhMuc.ToLower() == tenNorm.ToLower());
            if (dup != null)
                return Conflict(new { message = $"Tên danh mục \"{tenNorm}\" đã được sử dụng bởi danh mục \"{dup.MaDMSP}\"." });

            dm.TenDanhMuc = tenNorm;
        }

        if (req.TrangThai.HasValue && req.TrangThai.Value != dm.TrangThai)
        {
            dm.TrangThai = req.TrangThai.Value;
            await CascadeTrangThaiSanPhamAsync(id, req.TrangThai.Value);
        }

        if (req.File != null)
        {
            DeleteImageIfExists(dm.HinhAnh);

            var result = await SaveImageAsync(req.File, "danhmuc");
            if (result.Error != null)
                return BadRequest(new { message = result.Error });
            dm.HinhAnh = result.Path;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ══ PATCH TRANG THAI ══════════════════════════════════════════════
    [HttpPatch("{id}/trangthai")]
    public async Task<IActionResult> UpdateTrangThai(string id, [FromBody] TrangThaiRequest req)
    {
        var dm = await _context.DanhMucSPs.FindAsync(id);
        if (dm == null || dm.TrangThai == TRANG_THAI_BANNER)
            return NotFound(new { message = "Không tìm thấy danh mục." });

        if (req.TrangThai != dm.TrangThai)
        {
            dm.TrangThai = req.TrangThai;
            await CascadeTrangThaiSanPhamAsync(id, req.TrangThai);
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Đồng bộ trạng thái toàn bộ sản phẩm theo trạng thái mới của danh mục:
    /// Tạm ẩn (0) → mọi sản phẩm trong danh mục chuyển Ngừng bán (0).
    /// Đang hiện (1) → mọi sản phẩm trong danh mục chuyển Đang bán (1).
    /// </summary>
    private async Task CascadeTrangThaiSanPhamAsync(string maDMSP, int trangThaiMoi)
    {
        var sanPhams = await _context.SanPhams
            .Where(sp => sp.MaDanhMuc == maDMSP && sp.TrangThai != trangThaiMoi)
            .ToListAsync();

        foreach (var sp in sanPhams)
            sp.TrangThai = trangThaiMoi;
    }

    // ══ DELETE ════════════════════════════════════════════════════════
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var dm = await _context.DanhMucSPs
            .Include(d => d.SanPhams)
            .FirstOrDefaultAsync(d => d.MaDMSP == id && d.TrangThai != TRANG_THAI_BANNER);

        if (dm == null) return NotFound(new { message = "Không tìm thấy danh mục." });

        if (dm.SanPhams.Any())
            return BadRequest(new { message = $"Không thể xóa — danh mục đang có {dm.SanPhams.Count} sản phẩm." });

        DeleteImageIfExists(dm.HinhAnh);

        _context.DanhMucSPs.Remove(dm);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ══════════════════════════════════════════════════════════════════
    // BANNER DANH MỤC
    // Banner được lưu chung bảng DanhMucSP, với:
    //   - MaDMSP dạng "<MaDanhMucCha>-<STT>"  (vd: DMSP01-1, DMSP01-2)
    //     Dùng dấu "-" thay vì "." vì ASP.NET Core route có thể hiểu nhầm
    //     dấu "." cuối path segment là phần mở rộng file, gây lỗi 404/405.
    //   - TrangThai = 2  (để phân biệt với danh mục thật: 0 = ẩn, 1 = hiện)
    //   - TenDanhMuc dùng làm tiêu đề/mô tả banner (không bắt buộc)
    //   - HinhAnh là ảnh banner
    // ══════════════════════════════════════════════════════════════════

    private const int TRANG_THAI_BANNER = 2;

    // GET: api/QuanLyDanhMucSP/banner/danh-sach/{maDanhMucCha}
    [HttpGet("banner/danh-sach/{maDanhMucCha}")]
    public async Task<IActionResult> GetBanners(string maDanhMucCha)
    {
        if (string.IsNullOrWhiteSpace(maDanhMucCha))
            return BadRequest(new { message = "Mã danh mục cha không hợp lệ." });

        var prefix = maDanhMucCha.Trim().ToUpper() + "-";

        var banners = await _context.DanhMucSPs
            .Where(dm => dm.TrangThai == TRANG_THAI_BANNER
                      && dm.MaDMSP.ToUpper().StartsWith(prefix))
            .OrderBy(dm => dm.MaDMSP)
            .Select(dm => new
            {
                maBanner = dm.MaDMSP,
                maDanhMucCha = maDanhMucCha,
                tieuDe = dm.TenDanhMuc,
                hinhAnh = dm.HinhAnh
            })
            .ToListAsync();

        return Ok(banners);
    }

    // POST: api/QuanLyDanhMucSP/banner
    // Body: multipart/form-data — MaDanhMucCha (bắt buộc), TieuDe (tùy chọn), File (bắt buộc)
    [HttpPost("banner")]
    public async Task<IActionResult> AddBanner([FromForm] string maDanhMucCha, [FromForm] string? tieuDe, IFormFile file)
    {
        if (string.IsNullOrWhiteSpace(maDanhMucCha))
            return BadRequest(new { message = "Vui lòng chọn danh mục cha." });

        maDanhMucCha = maDanhMucCha.Trim().ToUpper();

        var danhMucChaEntity = await _context.DanhMucSPs
            .FirstOrDefaultAsync(d => d.MaDMSP == maDanhMucCha && d.TrangThai != TRANG_THAI_BANNER);
        if (danhMucChaEntity == null)
            return BadRequest(new { message = $"Không tìm thấy danh mục cha \"{maDanhMucCha}\"." });

        if (file == null)
            return BadRequest(new { message = "Vui lòng chọn ảnh banner." });

        // Sinh số thứ tự tiếp theo cho banner của danh mục này (DMSP01-1, DMSP01-2, ...)
        var prefix = maDanhMucCha + "-";
        var existingBanners = await _context.DanhMucSPs
            .Where(d => d.TrangThai == TRANG_THAI_BANNER && d.MaDMSP.StartsWith(prefix))
            .Select(d => d.MaDMSP)
            .ToListAsync();

        int nextStt = 1;
        if (existingBanners.Any())
        {
            nextStt = existingBanners
                .Select(m =>
                {
                    var suffix = m.Substring(prefix.Length);
                    return int.TryParse(suffix, out var n) ? n : 0;
                })
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        var maBanner = $"{maDanhMucCha}-{nextStt}";

        var result = await SaveImageAsync(file, "danhmuc-banner");
        if (result.Error != null)
            return BadRequest(new { message = result.Error });

        var banner = new DanhMucSP
        {
            MaDMSP = maBanner,
            TenDanhMuc = string.IsNullOrWhiteSpace(tieuDe) ? danhMucChaEntity.TenDanhMuc : tieuDe.Trim(),
            HinhAnh = result.Path,
            TrangThai = TRANG_THAI_BANNER,
        };

        _context.DanhMucSPs.Add(banner);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Thêm banner thành công!",
            maBanner = banner.MaDMSP,
            hinhAnh = banner.HinhAnh,
            tieuDe = banner.TenDanhMuc,
        });
    }

    // PUT: api/QuanLyDanhMucSP/banner/{maBanner}
    // Body: multipart/form-data — TieuDe (tùy chọn), File (tùy chọn)
    [HttpPut("banner/{maBanner:regex(^[[A-Za-z0-9]]+-[[0-9]]+$)}")]
    public async Task<IActionResult> UpdateBanner(string maBanner, [FromForm] string? tieuDe, IFormFile? file)
    {
        var banner = await _context.DanhMucSPs
            .FirstOrDefaultAsync(d => d.MaDMSP == maBanner && d.TrangThai == TRANG_THAI_BANNER);

        if (banner == null)
            return NotFound(new { message = "Không tìm thấy banner." });

        if (!string.IsNullOrWhiteSpace(tieuDe))
            banner.TenDanhMuc = tieuDe.Trim();

        if (file != null)
        {
            DeleteImageIfExists(banner.HinhAnh);

            var result = await SaveImageAsync(file, "danhmuc-banner");
            if (result.Error != null)
                return BadRequest(new { message = result.Error });
            banner.HinhAnh = result.Path;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Cập nhật banner thành công!",
            maBanner = banner.MaDMSP,
            hinhAnh = banner.HinhAnh,
            tieuDe = banner.TenDanhMuc,
        });
    }

    // DELETE: api/QuanLyDanhMucSP/banner/{maBanner}
    [HttpDelete("banner/{maBanner:regex(^[[A-Za-z0-9]]+-[[0-9]]+$)}")]
    public async Task<IActionResult> DeleteBanner(string maBanner)
    {
        var banner = await _context.DanhMucSPs
            .FirstOrDefaultAsync(d => d.MaDMSP == maBanner && d.TrangThai == TRANG_THAI_BANNER);

        if (banner == null)
            return NotFound(new { message = "Không tìm thấy banner." });

        DeleteImageIfExists(banner.HinhAnh);

        _context.DanhMucSPs.Remove(banner);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa banner!" });
    }

    // ══ IMAGE HELPERS ═════════════════════════════════════════════════
    private async Task<(string? Path, string? Error)> SaveImageAsync(IFormFile file, string folder)
    {
        if (!file.ContentType.StartsWith("image/"))
            return (null, "File phải là ảnh (image/*).");
        if (file.Length > 5 * 1024 * 1024)
            return (null, "File vượt quá 5MB.");

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(uploadsDir);

        var ext = Path.GetExtension(file.FileName).ToLower();
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return ($"/uploads/{folder}/{fileName}", null);
    }

    private void DeleteImageIfExists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var fullPath = Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));
        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);
    }
}

// ══ DTOs ══════════════════════════════════════════════════════════════

public class DanhMucSPDto
{
    public string MaDMSP { get; set; } = null!;
    public string? TenDanhMuc { get; set; }
    public string? HinhAnh { get; set; }
    public int TrangThai { get; set; }
    public int SoSanPham { get; set; }
}

public class CreateDanhMucSPRequest
{
    [Required(ErrorMessage = "Mã danh mục không được để trống.")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Mã danh mục phải từ 2 đến 10 ký tự.")]
    [RegularExpression(@"^[A-Za-z0-9]+$",
        ErrorMessage = "Mã danh mục chỉ được chứa chữ cái (A–Z) và chữ số, không dấu, không khoảng trắng.")]
    public string? MaDMSP { get; set; }

    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên danh mục phải từ 2 đến 100 ký tự.")]
    public string? TenDanhMuc { get; set; }

    [Range(0, 1, ErrorMessage = "Trạng thái không hợp lệ (0 hoặc 1).")]
    public int? TrangThai { get; set; } = 1;

    public IFormFile? File { get; set; }
}

public class UpdateDanhMucSPRequest
{
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên danh mục phải từ 2 đến 100 ký tự.")]
    public string? TenDanhMuc { get; set; }

    [Range(0, 1, ErrorMessage = "Trạng thái không hợp lệ (0 hoặc 1).")]
    public int? TrangThai { get; set; }

    public IFormFile? File { get; set; }
}

public class TrangThaiRequest
{
    [Range(0, 1, ErrorMessage = "Trạng thái không hợp lệ (0 hoặc 1).")]
    public int TrangThai { get; set; }
}
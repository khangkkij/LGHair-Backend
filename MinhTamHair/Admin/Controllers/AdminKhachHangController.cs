using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Admin.Models;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

[Route("api/admin/khach-hang")]
[ApiController]
public class AdminKhachHangController : ControllerBase
{
    private readonly MinhTamHairContext _context;
    private readonly IWebHostEnvironment _env;

    public AdminKhachHangController(MinhTamHairContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    [HttpGet("ranks")]
    public async Task<ActionResult<List<HangRankOptionDto>>> GetRanks(CancellationToken cancellationToken)
    {
        var list = await _context.Hangthanhviens.AsNoTracking()
            .Where(r => r.TrangThai == null || r.TrangThai == 1)
            .OrderBy(r => r.DiemToiThieu)
            .Select(r => new HangRankOptionDto
            {
                MaRank = r.MaRank,
                TenRank = r.TenRank,
                DiemToiThieu = r.DiemToiThieu
            })
            .ToListAsync(cancellationToken);
        return Ok(list);
    }

    /// <summary>Upload ảnh đại diện khách (multipart, field name: <c>file</c>). Trả về đường dẫn tương đối lưu DB.</summary>
    [HttpPost("upload-avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Chưa chọn file ảnh." });

        var path = await SaveAvatarAsync(file, cancellationToken);
        if (path == null)
            return BadRequest(new { message = "File không hợp lệ. Chỉ nhận PNG, JPG, WEBP và ≤ 5MB." });

        return Ok(new { duongDan = path });
    }

    [HttpGet]
    public async Task<ActionResult<AdminKhachHangListResponse>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? keyword = null,
        [FromQuery] int? trangThai = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Khachhangs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(x =>
                (x.MaKh != null && x.MaKh.Contains(k)) ||
                (x.HoTen != null && x.HoTen.Contains(k)) ||
                (x.Sdt != null && x.Sdt.Contains(k)) ||
                (x.Email != null && x.Email.Contains(k)));
        }

        if (trangThai.HasValue)
            query = query.Where(x => x.TrangThai == trangThai);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.NgayTao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminKhachHangListItemDto
            {
                MaKh = x.MaKh,
                HoTen = x.HoTen,
                Sdt = x.Sdt,
                Email = x.Email,
                MaRank = x.MaRank,
                TenRank = x.MaRankNavigation != null ? x.MaRankNavigation.TenRank : null,
                DiemTichLuy = x.DiemTichLuy,
                TrangThai = x.TrangThai,
                NgayTao = x.NgayTao
            })
            .ToListAsync(cancellationToken);

        return Ok(new AdminKhachHangListResponse
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{maKh}")]
    public async Task<ActionResult<AdminKhachHangDetailDto>> GetByMa(string maKh, CancellationToken cancellationToken)
    {
        var row = await _context.Khachhangs.AsNoTracking()
            .Where(x => x.MaKh == maKh)
            .Select(x => new AdminKhachHangDetailDto
            {
                MaKh = x.MaKh,
                HoTen = x.HoTen,
                Sdt = x.Sdt,
                Email = x.Email,
                MaRank = x.MaRank,
                TenRank = x.MaRankNavigation != null ? x.MaRankNavigation.TenRank : null,
                DiemTichLuy = x.DiemTichLuy,
                TrangThai = x.TrangThai,
                NgayTao = x.NgayTao,
                GioiTinh = x.GioiTinh,
                HinhAnh = x.HinhAnh,
                MatKhau = x.MatKhau
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
            return NotFound(new { message = "Không tìm thấy khách hàng." });

        return Ok(row);
    }

    [HttpPut("{maKh}")]
    public async Task<IActionResult> Update(string maKh, [FromBody] AdminKhachHangUpdateDto dto, CancellationToken cancellationToken)
    {
        var entity = await _context.Khachhangs.FirstOrDefaultAsync(x => x.MaKh == maKh, cancellationToken);
        if (entity == null)
            return NotFound(new { message = "Không tìm thấy khách hàng." });

        if (!string.IsNullOrWhiteSpace(dto.Sdt) && dto.Sdt != entity.Sdt)
        {
            var dupSdt = await _context.Khachhangs.AnyAsync(
                x => x.MaKh != maKh && x.Sdt == dto.Sdt.Trim(), cancellationToken);
            if (dupSdt)
                return BadRequest(new { message = "Số điện thoại đã được khách khác sử dụng." });
        }

        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Email không được để trống." });

        var emailTrim = dto.Email.Trim();
        if (emailTrim != (entity.Email ?? ""))
        {
            var dupEmail = await _context.Khachhangs.AnyAsync(
                x => x.MaKh != maKh && x.Email == emailTrim, cancellationToken);
            if (dupEmail)
                return BadRequest(new { message = "Email đã được khách khác sử dụng." });
        }

        if (dto.HoTen != null) entity.HoTen = dto.HoTen.Trim();
        entity.Email = emailTrim;
        if (dto.Sdt != null) entity.Sdt = dto.Sdt.Trim();
        if (dto.GioiTinh != null) entity.GioiTinh = string.IsNullOrWhiteSpace(dto.GioiTinh) ? null : dto.GioiTinh.Trim();
        if (dto.HinhAnh != null) entity.HinhAnh = string.IsNullOrWhiteSpace(dto.HinhAnh) ? null : dto.HinhAnh.Trim();
        if (dto.TrangThai.HasValue) entity.TrangThai = dto.TrangThai;
        if (dto.DiemTichLuy.HasValue) entity.DiemTichLuy = dto.DiemTichLuy;

        entity.MaRank = await GetRankByPointsAsync(entity.DiemTichLuy, cancellationToken);

        string? matKhauTraVe = null;
        if (!string.IsNullOrWhiteSpace(dto.MatKhauMoi))
        {
            var plainNewPassword = dto.MatKhauMoi.Trim();
            entity.MatKhau = BCrypt.Net.BCrypt.HashPassword(plainNewPassword);
            matKhauTraVe = plainNewPassword;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Cập nhật khách hàng thành công.", matKhau = matKhauTraVe });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] AdminKhachHangCreateDto dto, CancellationToken cancellationToken)
    {
        var sdt = dto.Sdt.Trim();
        if (await _context.Khachhangs.AnyAsync(x => x.Sdt == sdt, cancellationToken))
            return BadRequest(new { message = "Số điện thoại đã tồn tại." });

        var emailTrim = dto.Email.Trim();
        if (await _context.Khachhangs.AnyAsync(x => x.Email == emailTrim, cancellationToken))
            return BadRequest(new { message = "Email đã tồn tại." });

        var gioiTinhTrim = dto.GioiTinh.Trim();
        var hinhAnhTrim = dto.HinhAnh.Trim();
        if (string.IsNullOrEmpty(gioiTinhTrim))
            return BadRequest(new { message = "Vui lòng chọn giới tính." });
        if (string.IsNullOrEmpty(hinhAnhTrim))
            return BadRequest(new { message = "Vui lòng tải ảnh đại diện." });

        string newMaKh;
        do
        {
            newMaKh = "KH" + Random.Shared.Next(100000, 999999).ToString();
        } while (await _context.Khachhangs.AnyAsync(x => x.MaKh == newMaKh, cancellationToken));

        var matKhau = string.IsNullOrWhiteSpace(dto.MatKhau)
            ? Guid.NewGuid().ToString("N")[..12]
            : dto.MatKhau.Trim();

        var entity = new Khachhang
        {
            MaKh = newMaKh,
            HoTen = dto.HoTen.Trim(),
            Sdt = sdt,
            Email = emailTrim,
            GioiTinh = gioiTinhTrim,
            MatKhau = BCrypt.Net.BCrypt.HashPassword(matKhau),
            NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
            TrangThai = 1,
            DiemTichLuy = 0,
            MaRank = await GetRankByPointsAsync(0, cancellationToken),
            HinhAnh = hinhAnhTrim
        };

        _context.Khachhangs.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetByMa),
            new { maKh = entity.MaKh },
            new { maKh = entity.MaKh, message = "Đã tạo khách hàng.", matKhau });
    }

    [HttpDelete("{maKh}")]
    public async Task<IActionResult> Delete(string maKh, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _context.Khachhangs
                .Include(x => x.HoaDons)
                .Include(x => x.Lichdats)
                .Include(x => x.Giohangs)
                .Include(x => x.Thongbaos)
                .FirstOrDefaultAsync(x => x.MaKh == maKh, cancellationToken);

            if (entity == null)
                return NotFound(new { message = "Không tìm thấy khách hàng cần xóa." });

            if ((entity.HoaDons != null && entity.HoaDons.Any()) || (entity.Lichdats != null && entity.Lichdats.Any()))
            {
                return BadRequest(new { message = $"Không thể xóa khách hàng '{entity.HoTen}' vì đã có lịch sử đặt lịch hoặc hóa đơn giao dịch. Bạn nên chuyển trạng thái sang Khóa để lưu trữ lịch sử." });
            }

            if (entity.Giohangs != null && entity.Giohangs.Any()) _context.Giohangs.RemoveRange(entity.Giohangs);
            if (entity.Thongbaos != null && entity.Thongbaos.Any()) _context.Thongbaos.RemoveRange(entity.Thongbaos);

            _context.Khachhangs.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Xóa khách hàng thành công!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
        }
    }

    private async Task<string> GetRankByPointsAsync(int? points, CancellationToken cancellationToken)
    {
        int p = points ?? 0;
        var ranks = await _context.Hangthanhviens
            .Where(r => r.TrangThai == 1)
            .OrderByDescending(r => r.DiemToiThieu)
            .ToListAsync(cancellationToken);

        var currentRank = ranks.FirstOrDefault(r => p >= (r.DiemToiThieu ?? 0));
        return currentRank?.MaRank ?? "RANK01";
    }

    private async Task<string?> SaveAvatarAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/jpg", "image/png", "image/webp"
        };
        var ct = file.ContentType ?? "";
        if (!allowed.Contains(ct)) return null;
        if (file.Length > 5 * 1024 * 1024) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var folder = Path.Combine(webRoot, "uploads", "khachhang");
        Directory.CreateDirectory(folder);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".jpg";
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(stream, cancellationToken);

        return $"/uploads/khachhang/{fileName}";
    }
}

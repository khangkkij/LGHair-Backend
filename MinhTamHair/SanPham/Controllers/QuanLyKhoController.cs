using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuanLyKhoController : ControllerBase
{
    private readonly MinhTamHairContext _db;
    public QuanLyKhoController(MinhTamHairContext db) => _db = db;

    [HttpGet("nhacungcap")]
    public async Task<IActionResult> GetNhaCungCap()
    {
        var list = await _db.Nhacungcaps
            .Select(n => new
            {
                n.MaNcc,
                n.TenNcc,
                n.SoDienThoai,
                n.DiaChi,
                n.Email,
                n.TrangThai,
                SoPhieuNhap = n.Phieunhaps.Count
            })
            .OrderBy(n => n.MaNcc)
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("nhacungcap")]
    public async Task<IActionResult> CreateNhaCungCap([FromBody] NhaCungCapDto dto)
    {
        var ncc = new Nhacungcap
        {
            MaNcc = dto.MaNcc,
            TenNcc = dto.TenNcc,
            SoDienThoai = dto.SoDienThoai,
            DiaChi = dto.DiaChi,
            Email = dto.Email,
            TrangThai = 1
        };

        _db.Nhacungcaps.Add(ncc);
        await _db.SaveChangesAsync();

        return Ok(ncc);
    }

    [HttpPut("nhacungcap/{id}")]
    public async Task<IActionResult> UpdateNhaCungCap(string id,[FromBody] NhaCungCapDto dto)
    {
        var entity = await _db.Nhacungcaps.FindAsync(id);
        if (entity == null) return NotFound();

        entity.TenNcc = dto.TenNcc;
        entity.SoDienThoai = dto.SoDienThoai;
        entity.DiaChi = dto.DiaChi;
        entity.Email = dto.Email;
        entity.TrangThai = dto.TrangThai;

        await _db.SaveChangesAsync();

        return Ok(entity);
    }

    [HttpDelete("nhacungcap/{id}")]
    public async Task<IActionResult> DeleteNhaCungCap(string id)
    {
        var entity = await _db.Nhacungcaps.Include(n => n.Phieunhaps).FirstOrDefaultAsync(n => n.MaNcc == id);
        if (entity == null) return NotFound();
        if (entity.Phieunhaps.Any())
            return BadRequest(new { message = "Không thể xóa: nhà cung cấp đã có phiếu nhập." });

        _db.Nhacungcaps.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("phieunhap")]
    public async Task<IActionResult> GetPhieuNhap(
        [FromQuery] string? maNcc,
        [FromQuery] int? trangThai,
        [FromQuery] DateTime? tuNgay,
        [FromQuery] DateTime? denNgay)
    {
        var q = _db.Phieunhaps
            .Include(p => p.MaNccNavigation)
            .Include(p => p.MaNvNavigation)
            .Include(p => p.ChitietPhieunhaps)
                .ThenInclude(c => c.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSpNavigation)
            .AsQueryable();

        if (!string.IsNullOrEmpty(maNcc)) q = q.Where(p => p.MaNcc == maNcc);
        if (trangThai.HasValue) q = q.Where(p => p.TrangThai == trangThai.Value);
        if (tuNgay.HasValue) q = q.Where(p => p.NgayNhap >= tuNgay.Value);
        if (denNgay.HasValue) q = q.Where(p => p.NgayNhap <= denNgay.Value.AddDays(1));

        var list = await q
            .OrderByDescending(p => p.NgayNhap)
            .Select(p => new
            {
                p.MaPn,
                p.MaNcc,
                TenNcc = p.MaNccNavigation != null ? p.MaNccNavigation.TenNcc : null,
                p.MaNv,
                TenNv = p.MaNvNavigation != null ? p.MaNvNavigation.HoTenNv : null,
                p.NgayNhap,
                p.TongTien,
                p.GhiChu,
                p.TrangThai,
                SoLuongDong = p.ChitietPhieunhaps.Count,
                ChiTiet = p.ChitietPhieunhaps.Select(c => new
                {
                    c.MaPn,
                    c.MaBienThe,
                    TenBienThe = c.MaBienTheNavigation.TenBienThe,
                    TenSanPham = c.MaBienTheNavigation.MaSpNavigation != null
                        ? c.MaBienTheNavigation.MaSpNavigation.TenSp : null,
                    HinhAnh = c.MaBienTheNavigation.HinhAnhDaiDien,
                    c.SoLuong,
                    c.GiaNhap,
                    ThanhTien = c.SoLuong * c.GiaNhap
                }).ToList()
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("phieunhap/{id}")]
    public async Task<IActionResult> GetPhieuNhapDetail(string id)
    {
        var p = await _db.Phieunhaps
            .Include(p => p.MaNccNavigation)
            .Include(p => p.MaNvNavigation)
            .Include(p => p.ChitietPhieunhaps)
                .ThenInclude(c => c.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSpNavigation)
            .FirstOrDefaultAsync(x => x.MaPn == id);

        if (p == null) return NotFound();

        return Ok(new
        {
            p.MaPn,
            p.MaNcc,
            TenNcc = p.MaNccNavigation?.TenNcc,
            p.MaNv,
            TenNv = p.MaNvNavigation?.HoTenNv,
            p.NgayNhap,
            p.TongTien,
            p.GhiChu,
            p.TrangThai,
            ChiTiet = p.ChitietPhieunhaps.Select(c => new
            {
                c.MaBienThe,
                TenBienThe = c.MaBienTheNavigation.TenBienThe,
                TenSanPham = c.MaBienTheNavigation.MaSpNavigation?.TenSp,
                HinhAnh = c.MaBienTheNavigation.HinhAnhDaiDien,
                GiaBanHienTai = c.MaBienTheNavigation.GiaBan,
                c.SoLuong,
                c.GiaNhap,
                ThanhTien = c.SoLuong * c.GiaNhap
            })
        });
    }

    [HttpPost("phieunhap")]
    public async Task<IActionResult> CreatePhieuNhap([FromBody] CreatePhieuNhapRequest req)
    {
        if (req.ChiTiet == null || !req.ChiTiet.Any())
            return BadRequest(new { message = "Phiếu nhập phải có ít nhất một dòng chi tiết." });

        // Validate biến thể tồn tại
        var maBTs = req.ChiTiet.Select(c => c.MaBienThe).Distinct().ToList();
        var existing = await _db.BienTheSanphams
            .Where(b => maBTs.Contains(b.MaBienThe))
            .Select(b => b.MaBienThe)
            .ToListAsync();
        var missing = maBTs.Except(existing).ToList();
        if (missing.Any())
            return BadRequest(new { message = $"Biến thể không tồn tại: {string.Join(", ", missing)}" });

        var tongTien = req.ChiTiet.Sum(c => (c.SoLuong ?? 0) * (c.GiaNhap ?? 0));

        var phieu = new Phieunhap
        {
            MaPn = req.MaPn,
            MaNcc = req.MaNcc,
            MaNv = req.MaNv,
            NgayNhap = req.NgayNhap ?? DateTime.Now,
            TongTien = tongTien,
            GhiChu = req.GhiChu,
            TrangThai = 0,
        };

        foreach (var ct in req.ChiTiet)
        {
            phieu.ChitietPhieunhaps.Add(new ChitietPhieunhap
            {
                MaPn = req.MaPn,
                MaBienThe = ct.MaBienThe,
                SoLuong = ct.SoLuong,
                GiaNhap = ct.GiaNhap
            });
        }

        _db.Phieunhaps.Add(phieu);
        await _db.SaveChangesAsync();
        return Ok(new { phieu.MaPn, phieu.TongTien, message = "Tạo phiếu nhập thành công." });
    }

    [HttpPost("phieunhap/{id}/duyet")]
    public async Task<IActionResult> DuyetPhieuNhap(string id)
    {
        var phieu = await _db.Phieunhaps.Include(p => p.ChitietPhieunhaps).FirstOrDefaultAsync(p => p.MaPn == id);

        if (phieu == null) return NotFound();
        if (phieu.TrangThai == 1)
            return BadRequest(new { message = "Phiếu đã được duyệt trước đó." });
        if (phieu.TrangThai == 2)
            return BadRequest(new { message = "Phiếu đã bị hủy, không thể duyệt." });

        // Cập nhật tồn kho
        var maBTs = phieu.ChitietPhieunhaps.Select(c => c.MaBienThe).ToList();
        var bienThes = await _db.BienTheSanphams
            .Where(b => maBTs.Contains(b.MaBienThe))
            .ToListAsync();

        foreach (var ct in phieu.ChitietPhieunhaps)
        {
            var bt = bienThes.FirstOrDefault(b => b.MaBienThe == ct.MaBienThe);
            if (bt != null)
                bt.SoLuongTon = (bt.SoLuongTon ?? 0) + (ct.SoLuong ?? 0);
        }

        phieu.TrangThai = 1;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Duyệt phiếu nhập thành công. Tồn kho đã được cập nhật." });
    }

    [HttpPost("phieunhap/{id}/huy")]
    public async Task<IActionResult> HuyPhieuNhap(string id)
    {
        var phieu = await _db.Phieunhaps.FindAsync(id);
        if (phieu == null) return NotFound();
        if (phieu.TrangThai == 1)
            return BadRequest(new { message = "Không thể hủy phiếu đã duyệt." });

        phieu.TrangThai = 2;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Phiếu nhập đã bị hủy." });
    }

    [HttpDelete("phieunhap/{id}")]
    public async Task<IActionResult> DeletePhieuNhap(string id)
    {
        var phieu = await _db.Phieunhaps
            .Include(p => p.ChitietPhieunhaps)
            .FirstOrDefaultAsync(p => p.MaPn == id);

        if (phieu == null) return NotFound();
        if (phieu.TrangThai == 1)
            return BadRequest(new { message = "Không thể xóa phiếu đã duyệt." });

        _db.ChitietPhieunhaps.RemoveRange(phieu.ChitietPhieunhaps);
        _db.Phieunhaps.Remove(phieu);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("tonkho")]
    public async Task<IActionResult> GetTonKho(
        [FromQuery] string? search,
        [FromQuery] string? maDanhMuc,
        [FromQuery] int? trangThai)
    {
        var q = _db.BienTheSanphams
            .Include(b => b.MaSpNavigation)
                .ThenInclude(s => s!.MaDanhMucTheNavigation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(b =>
                (b.TenBienThe != null && b.TenBienThe.Contains(search)) ||
                (b.MaSpNavigation != null && b.MaSpNavigation.TenSp != null
                    && b.MaSpNavigation.TenSp.Contains(search)));

        if (!string.IsNullOrEmpty(maDanhMuc))
            q = q.Where(b => b.MaSpNavigation != null && b.MaSpNavigation.MaDanhMuc == maDanhMuc);

        if (trangThai.HasValue)
            q = q.Where(b => b.TrangThai == trangThai.Value);

        var list = await q
            .OrderBy(b => b.MaSpNavigation!.TenSp)
            .ThenBy(b => b.TenBienThe)
            .Select(b => new
            {
                b.MaBienThe,
                b.TenBienThe,
                MaSanPham = b.MaSanPham,
                TenSanPham = b.MaSpNavigation != null ? b.MaSpNavigation.TenSp : null,
                DanhMuc = b.MaSpNavigation != null && b.MaSpNavigation.MaDanhMucTheNavigation != null
                    ? b.MaSpNavigation.MaDanhMucTheNavigation.TenDanhMuc : null,
                HinhAnh = b.HinhAnhDaiDien,
                b.SoLuongTon,
                b.GiaBan,
                b.TrangThai,
                MucTon = b.SoLuongTon == null ? "unknown"
                       : b.SoLuongTon == 0 ? "het"
                       : b.SoLuongTon < 10 ? "thap"
                       : "on"
            })
            .ToListAsync();

        return Ok(list);
    }
    [HttpGet("timkiembienthe")]
    public async Task<IActionResult> GetBienTheLookup([FromQuery] string? q)
    {
        var query = _db.BienTheSanphams
            .Include(b => b.MaSpNavigation)
            .Where(b => b.TrangThai == 1)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(b =>
                (b.MaBienThe != null && b.MaBienThe.Contains(q)) ||
                (b.TenBienThe != null && b.TenBienThe.Contains(q)) ||
                (b.MaSpNavigation != null &&
                 b.MaSpNavigation.TenSp != null &&
                 b.MaSpNavigation.TenSp.Contains(q)));

        var list = await query
            .Take(30)
            .Select(b => new
            {
                b.MaBienThe,
                Label = (b.MaSpNavigation != null
                            ? b.MaSpNavigation.TenSp + " — "
                            : "") + b.TenBienThe,
                b.GiaBan,
                b.SoLuongTon,
                HinhAnh = b.HinhAnhDaiDien
            })
            .ToListAsync();

        return Ok(list);
    }
}

public class CreatePhieuNhapRequest
{
    public string MaPn { get; set; } = null!;
    public string? MaNcc { get; set; }
    public string? MaNv { get; set; }
    public DateTime? NgayNhap { get; set; }
    public string? GhiChu { get; set; }
    public List<ChiTietPhieuNhapDto> ChiTiet { get; set; } = new();
}

public class ChiTietPhieuNhapDto
{
    public string MaBienThe { get; set; } = null!;
    public int? SoLuong { get; set; }
    public decimal? GiaNhap { get; set; }
}
public class NhaCungCapDto
{
    public string MaNcc { get; set; }
    public string TenNcc { get; set; }
    public string SoDienThoai { get; set; }
    public string DiaChi { get; set; }
    public string Email { get; set; }
    public int TrangThai { get; set; }
}
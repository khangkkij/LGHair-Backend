using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

// =======================
// DTOs
// =======================
public class DanhMucDVDto
{
    public string MaDM { get; set; } = null!;
    public string? TenDanhMuc { get; set; }
    public string? HinhAnh { get; set; }
    public DateOnly? NgayTao { get; set; }
    public int? TrangThai { get; set; }
    public int SoDichVu { get; set; }
}

public class TaoDanhMucDVDto
{
    public string? TenDanhMuc { get; set; }
    public IFormFile? HinhAnh { get; set; }
    public int TrangThai { get; set; } = 1;
}

public class SuaDanhMucDVDto
{
    public string? TenDanhMuc { get; set; }
    public IFormFile? HinhAnh { get; set; }
    public int? TrangThai { get; set; }
}

// =======================
// CONTROLLER
// =======================
[ApiController]
[Route("api/[controller]")]
public class DanhMucDVController : ControllerBase
{
    private readonly MinhTamHairContext _ctx;
    private readonly IWebHostEnvironment _env;

    public DanhMucDVController(MinhTamHairContext ctx, IWebHostEnvironment env)
    {
        _ctx = ctx;
        _env = env;
    }

    // -------------------------------------------------------
    // GET /api/DanhMucDV/danh-sach
    // Lấy tất cả danh mục kèm số lượng dịch vụ (cho trang DMDichVu)
    // -------------------------------------------------------
    [HttpGet("danh-sach")]
    public async Task<IActionResult> GetDanhSach()
    {
        var list = await _ctx.DanhMucs
            .Where(d => d.TrangThai != 0)
            .Select(d => new DanhMucDVDto
            {
                MaDM = d.MaDm,
                TenDanhMuc = d.TenDanhMuc,
                HinhAnh = d.HinhAnh,
                NgayTao = d.NgayTao,
                TrangThai = d.TrangThai,
                SoDichVu = d.Dichvus.Count(dv => dv.TrangThai == 1)
            })
            .OrderBy(d => d.MaDM)
            .ToListAsync();

        return Ok(list);
    }

    // -------------------------------------------------------
    // GET /api/DanhMucDV/tat-ca   (dành cho trang quản lý admin)
    // Trả về cả danh mục đang tắt
    // -------------------------------------------------------
    [HttpGet("tat-ca")]
    public async Task<IActionResult> GetTatCa()
    {
        var list = await _ctx.DanhMucs
            .Select(d => new DanhMucDVDto
            {
                MaDM = d.MaDm,
                TenDanhMuc = d.TenDanhMuc,
                HinhAnh = d.HinhAnh,
                NgayTao = d.NgayTao,
                TrangThai = d.TrangThai,
                SoDichVu = d.Dichvus.Count()
            })
            .OrderBy(d => d.MaDM)
            .ToListAsync();

        return Ok(list);
    }

    // -------------------------------------------------------
    // GET /api/DanhMucDV/{maDM}
    // -------------------------------------------------------
    [HttpGet("{maDM}")]
    public async Task<IActionResult> GetChiTiet(string maDM)
    {
        var dm = await _ctx.DanhMucs
            .Where(d => d.MaDm == maDM)
            .Select(d => new DanhMucDVDto
            {
                MaDM = d.MaDm,
                TenDanhMuc = d.TenDanhMuc,
                HinhAnh = d.HinhAnh,
                NgayTao = d.NgayTao,
                TrangThai = d.TrangThai,
                SoDichVu = d.Dichvus.Count(dv => dv.TrangThai == 1)
            })
            .FirstOrDefaultAsync();

        if (dm == null)
            return NotFound(new { message = $"Không tìm thấy danh mục '{maDM}'." });

        return Ok(dm);
    }
}
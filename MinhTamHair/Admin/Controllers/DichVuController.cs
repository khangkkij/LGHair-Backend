using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MinhTamHair.KhanchHang.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DichVuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public DichVuController(MinhTamHairContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(string? keyword, string? maDm, int? trangThai)
        {
            var query = _context.Dichvus
                .Include(x => x.Hinhanhs)
                .AsQueryable();

            // tìm kiếm theo tên
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(x => x.TenDv.Contains(keyword));
            }

            // lọc theo danh mục
            if (!string.IsNullOrEmpty(maDm))
            {
                query = query.Where(x => x.MaDm == maDm);
            }

            // lọc theo trạng thái
            if (trangThai.HasValue)
            {
                query = query.Where(x => x.TrangThai == trangThai);
            }

            var data = await query
                .Select(dv => new
                {
                    dv.MaDv,
                    dv.TenDv,
                    dv.Gia,
                    dv.PhanTramHoaHong,
                    dv.MoTa,
                    dv.ThoiGianLam,
                    dv.TrangThai,
                    dv.MaDm,

                    HinhAnhs = dv.Hinhanhs.Select(h => new
                    {
                        h.MaHa,
                        h.DuongDan
                    })
                })
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] DichVuCreateDTO dto)
        {
            var dv = new Dichvu
            {
                MaDv = dto.MaDv,
                TenDv = dto.TenDv,
                Gia = dto.Gia,
                PhanTramHoaHong = dto.PhanTramHoaHong,
                MaDm = dto.MaDm,
                MoTa = dto.MoTa,
                ThoiGianLam = dto.ThoiGianLam,
                TrangThai = dto.TrangThai
            };

            _context.Dichvus.Add(dv);

            if (dto.Files != null && dto.Files.Count > 0)
            {
                foreach (var file in dto.Files)
                {
                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);

                    // 1. Lấy đường dẫn tuyệt đối đến thư mục chứa ảnh
                    var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "images");

                    // 2. Kiểm tra xem thư mục đã tồn tại chưa, chưa có thì tự động tạo mới
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                    // 3. Tiến hành lưu file
                    var path = Path.Combine(folderPath, fileName);
                    using var stream = new FileStream(path, FileMode.Create);
                    await file.CopyToAsync(stream);

                    _context.Hinhanhs.Add(new Hinhanh
                    {
                        MaDv = dv.MaDv,
                        DuongDan = "/assets/images/" + fileName
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(dv);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromForm] DichVuCreateDTO dto)
        {
            var dv = await _context.Dichvus
                .Include(x => x.Hinhanhs)
                .FirstOrDefaultAsync(x => x.MaDv == id);

            if (dv == null) return NotFound();

            dv.TenDv = dto.TenDv;
            dv.Gia = dto.Gia;
            dv.PhanTramHoaHong = dto.PhanTramHoaHong;
            dv.MaDm = dto.MaDm;
            dv.ThoiGianLam = dto.ThoiGianLam;
            dv.MoTa = dto.MoTa;
            dv.TrangThai = dto.TrangThai;

            // thêm ảnh mới
            // chỉ xử lý khi thực sự có file
            if (dto.Files != null && dto.Files.Count > 0)
            {
                foreach (var file in dto.Files)
                {
                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var path = Path.Combine("wwwroot/assets/images", fileName);

                    using var stream = new FileStream(path, FileMode.Create);
                    await file.CopyToAsync(stream);

                    _context.Hinhanhs.Add(new Hinhanh
                    {
                        MaDv = dv.MaDv,
                        DuongDan = "/assets/images/" + fileName
                    });
                }
            }
            
            if (dto.DeletedImages != null && dto.DeletedImages.Count > 0)
            {
                foreach (var imgUrl in dto.DeletedImages)
                {
                    var relativePath = imgUrl.Replace("https://localhost:7196", "").Replace("https://latuthien2005-001-site1.dtempurl.com", "");

                    var img = dv.Hinhanhs.FirstOrDefault(x => x.DuongDan == relativePath);

                    if (img != null)
                    {
                        var path = Path.Combine("wwwroot", img.DuongDan.TrimStart('/'));

                        if (System.IO.File.Exists(path))
                        {
                            System.IO.File.Delete(path);
                        }

                        _context.Hinhanhs.Remove(img);
                    }
                }
            }
            await _context.SaveChangesAsync();
            return Ok(dv);
        }

        [HttpDelete("image/{id}")]
        public async Task<IActionResult> DeleteImage(int id)
        {
            var img = await _context.Hinhanhs.FindAsync(id);
            if (img == null) return NotFound();

            var path = Path.Combine("wwwroot", img.DuongDan.TrimStart('/'));

            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            _context.Hinhanhs.Remove(img);
            await _context.SaveChangesAsync();

            return Ok();
        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var isUsedInCombo = await _context.Combo_DichVus.AnyAsync(x => x.MaDV == id);

            if (isUsedInCombo)
            {
                return BadRequest("Dịch vụ đang nằm trong combo, không thể xóa!");
            }
            var usedInFlashSale = await _context.ChitietFlashsales.AnyAsync(x => x.MaDv == id);

            if (usedInFlashSale)
            {
                return BadRequest("Dịch vụ đang nằm trong Flash Sale, không thể xóa!");
            }

            var dv = await _context.Dichvus
                .Include(x => x.Hinhanhs)
                .Include(x => x.TieudeDichvus)
                    .ThenInclude(td => td.ChitietTieudes)
                .FirstOrDefaultAsync(x => x.MaDv == id);

            if (dv == null) return NotFound();

            if (dv.TrangThai == 1)
            {
                return BadRequest("Dịch vụ đang được sử dụng, không thể xóa!");
            }
            foreach (var img in dv.Hinhanhs)
            {
                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    img.DuongDan.TrimStart('/')
                );

                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            foreach (var td in dv.TieudeDichvus)
            {
                foreach (var ct in td.ChitietTieudes)
                {
                    if (!string.IsNullOrEmpty(ct.HinhAnh))
                    {
                        var path = Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot",
                            ct.HinhAnh.TrimStart('/')
                        );

                        if (System.IO.File.Exists(path))
                        {
                            System.IO.File.Delete(path);
                        }
                    }
                }
            }
            foreach (var td in dv.TieudeDichvus)
            {
                _context.ChitietTieudes.RemoveRange(td.ChitietTieudes);
            }
            _context.TieudeDichvus.RemoveRange(dv.TieudeDichvus);
            _context.Hinhanhs.RemoveRange(dv.Hinhanhs);
            _context.Dichvus.Remove(dv);

            await _context.SaveChangesAsync();

            return Ok();
        }
        [HttpGet("danhmuc")]
        public async Task<IActionResult> GetDanhMuc()
        {
            var data = await _context.DanhMucs
                .Select(dm => new
                {
                    dm.MaDm,
                    dm.TenDanhMuc,
                })
                .ToListAsync();
            return Ok(data);
        }
    }
}
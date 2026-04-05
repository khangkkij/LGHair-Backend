using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System;

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
        public async Task<IActionResult> GetAll(string? keyword,string? maDm,int? trangThai)
        {
            var query = _context.Dichvus
                .Include(x => x.Hinhanhs)
                .AsQueryable();

            // 🔍 tìm kiếm theo tên
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(x => x.TenDv.Contains(keyword));
            }

            // 📂 lọc theo danh mục
            if (!string.IsNullOrEmpty(maDm))
            {
                query = query.Where(x => x.MaDm == maDm);
            }

            // 🔄 lọc theo trạng thái
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
        public async Task<IActionResult> Create([FromForm] ComboDTO dto)
        {
            var maCB = dto.MaCB.Trim().ToUpper();
            var tenCombo = dto.TenCombo.Trim().ToLower();

            // ❌ Check mã
            if (await _context.Combos.AnyAsync(x => x.MaCb == maCB))
                return BadRequest(new { message = "Mã combo đã tồn tại." });

            // ❌ Check tên
            if (await _context.Combos.AnyAsync(x => x.TenCombo.ToLower() == tenCombo))
                return BadRequest(new { message = "Tên combo đã tồn tại." });

            var combo = new Combo
            {
                MaCb = maCB,
                TenCombo = dto.TenCombo.Trim(),
                Gia = dto.Gia,
                MoTa = dto.MoTa,
                TrangThai = dto.TrangThai
            };

            // upload ảnh
            if (dto.File != null && dto.File.Length > 0)
            {
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/assets/images");

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var fileName = Guid.NewGuid() + Path.GetExtension(dto.File.FileName);
                var filePath = Path.Combine(folder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await dto.File.CopyToAsync(stream);

                combo.HinhAnh = "/assets/images/" + fileName;
            }

            _context.Combos.Add(combo);

            foreach (var dv in dto.DichVus)
            {
                _context.Combo_DichVus.Add(new Combo_DichVu
                {
                    MaCB = maCB,
                    MaDV = dv
                });
            }

            await _context.SaveChangesAsync();
            return Ok(combo);
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
            dv.MaDm = dto.MaDm;
            dv.ThoiGianLam = dto.ThoiGianLam;
            dv.MoTa = dto.MoTa;
            dv.TrangThai = dto.TrangThai;

            // thêm ảnh mới
            // ✅ chỉ xử lý khi thực sự có file
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
            // ✅ XÓA ẢNH
            if (dto.DeletedImages != null && dto.DeletedImages.Count > 0)
            {
                foreach (var imgUrl in dto.DeletedImages)
                {
                    var relativePath = imgUrl.Replace("https://localhost:7196", "");

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
            var dv = await _context.Dichvus
                .Include(x => x.Hinhanhs)
                .FirstOrDefaultAsync(x => x.MaDv == id);

            if (dv == null) return NotFound();
            if (dv.TrangThai == 1)
            {
                return BadRequest("Dịch vụ đang được sử dụng, không thể xóa!");
            }
            // ✅ XÓA FILE ẢNH TRONG SERVER
            foreach (var img in dv.Hinhanhs)
            {
                var path = Path.Combine("wwwroot", img.DuongDan.TrimStart('/'));

                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }

            // ✅ XÓA DB
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

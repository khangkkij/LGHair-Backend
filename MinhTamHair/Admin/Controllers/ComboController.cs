using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MinhTamHair.KhanchHang.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComboController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;

        public ComboController(MinhTamHairContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(string? keyword, int? trangThai)
        {
            var query = _context.Combos
                .Include(c => c.Combo_DichVus)
                .ThenInclude(cd => cd.DichVu)
                .AsQueryable();

            // 🔍 Tìm theo tên
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(c => c.TenCombo.Contains(keyword));
            }

            // 🎯 Lọc trạng thái
            if (trangThai.HasValue)
            {
                query = query.Where(c => c.TrangThai == trangThai.Value);
            }

            var combos = await query.ToListAsync();

            var result = combos.Select(c =>
            {
                var gia = c.Gia ?? 0; // 👈 tránh null
                var giaGoc = c.Combo_DichVus.Sum(x => x.DichVu.Gia) ?? 0;

                return new
                {
                    maCB = c.MaCb,
                    tenCombo = c.TenCombo,
                    gia = gia,
                    moTa = c.MoTa,
                    trangThai = c.TrangThai,
                    hinhAnh = c.HinhAnh,

                    giaGoc = giaGoc,

                    discount = giaGoc == 0
                        ? 0
                        : Math.Round((1 - gia / giaGoc) * 100), // 👈 FIX Ở ĐÂY

                    dichvus = c.Combo_DichVus.Select(x => x.MaDV)
                };
            });

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] ComboDTO dto)
        {
            var combo = new Combo
            {
                MaCb = dto.MaCB,
                TenCombo = dto.TenCombo,
                Gia = dto.Gia,
                MoTa = dto.MoTa,
                TrangThai = dto.TrangThai
            };
            if (dto.File != null && dto.File.Length > 0)
            {
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/assets/images");

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.File.FileName);

                var filePath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.File.CopyToAsync(stream);
                }

                combo.HinhAnh = "/assets/images/"+ fileName;
            }

            _context.Combos.Add(combo);

            foreach (var dv in dto.DichVus)
            {
                _context.Combo_DichVus.Add(new Combo_DichVu
                {
                    MaCB = dto.MaCB,
                    MaDV = dv
                });
            }

            await _context.SaveChangesAsync();
            return Ok(combo);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromForm] ComboDTO dto)
        {
            var combo = await _context.Combos
                .Include(c => c.Combo_DichVus)
                .FirstOrDefaultAsync(c => c.MaCb == id);

            if (combo == null) return NotFound();

            combo.TenCombo = dto.TenCombo;
            combo.Gia = dto.Gia;
            combo.MoTa = dto.MoTa;
            combo.TrangThai = dto.TrangThai;

            // 🔥 Nếu có ảnh mới → xóa ảnh cũ + lưu ảnh mới
            if (dto.File != null && dto.File.Length > 0)
            {
                // ✅ XÓA ẢNH CŨ
                if (!string.IsNullOrEmpty(combo.HinhAnh))
                {
                    var oldPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        combo.HinhAnh.TrimStart('/')
                    );

                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                // ✅ LƯU ẢNH MỚI
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/assets/images");

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.File.FileName);
                var filePath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.File.CopyToAsync(stream);
                }

                combo.HinhAnh = "/assets/images/" + fileName;
            }

            // Xóa dịch vụ cũ
            _context.Combo_DichVus.RemoveRange(combo.Combo_DichVus);

            // Thêm lại
            foreach (var dv in dto.DichVus)
            {
                _context.Combo_DichVus.Add(new Combo_DichVu
                {
                    MaCB = id,
                    MaDV = dv
                });
            }

            await _context.SaveChangesAsync();
            return Ok(combo);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var combo = await _context.Combos.FindAsync(id);
            if (combo == null) return NotFound();

            if (combo.TrangThai == 1)
            {
                return BadRequest("Combo đang được sử dụng, không thể xóa!");
            }
            var usedInFlashSale = await _context.ChitietFlashsales.AnyAsync(x => x.MaCb == id);

            if (usedInFlashSale)
            {
                return BadRequest("Combo đang nằm trong Flash Sale, không thể xóa!");
            }
            // 🔥 Xóa ảnh nếu có
            if (!string.IsNullOrEmpty(combo.HinhAnh))
            {
                var filePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    combo.HinhAnh.TrimStart('/') // bỏ dấu /
                );

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            // Xóa chi tiết trước
            var chiTiet = _context.Combo_DichVus.Where(x => x.MaCB == id);
            _context.Combo_DichVus.RemoveRange(chiTiet);

            _context.Combos.Remove(combo);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}

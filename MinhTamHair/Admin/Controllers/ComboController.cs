using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using MinhTamHair.Services.UploadIMG;

namespace MinhTamHair.KhanchHang.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComboController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;
        private readonly IUploadDVSP _uploadImg;

        public ComboController(MinhTamHairContext context, IDistributedCache cache, IUploadDVSP uploadImg)
        {
            _context = context;
            _cache = cache;
            _uploadImg = uploadImg;
        }

        // Upload ảnh lên Cloudinary (cloud CloudinaryDVSP, folder "DVSP") thay vì lưu wwwroot.
        private async Task<(string? Url, string? Error)> SaveImageAsync(IFormFile file)
        {
            var result = await _uploadImg.AddIMG(file);
            if (result?.Error != null)
                return (null, result.Error.Message);
            if (string.IsNullOrEmpty(result?.SecureUrl?.ToString()))
                return (null, "Upload ảnh lên Cloudinary thất bại.");

            return (result.SecureUrl.ToString(), null);
        }

        // Xóa ảnh trên Cloudinary dựa vào URL đã lưu trong DB.
        private async Task DeleteImageAsync(string? imageUrl)
        {
            var publicId = GetPublicIdFromUrl(imageUrl);
            if (publicId == null) return;

            await _uploadImg.DelIMG(publicId);
        }

        // URL Cloudinary dạng: https://res.cloudinary.com/<cloud>/image/upload/v.../DVSP/abc.jpg
        // Public ID là phần "DVSP/abc" (bỏ domain, "v<version>/" và đuôi mở rộng).
        private static string? GetPublicIdFromUrl(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.Contains("res.cloudinary.com"))
                return null;

            var uploadIndex = imageUrl.IndexOf("/upload/", StringComparison.OrdinalIgnoreCase);
            if (uploadIndex < 0) return null;

            var afterUpload = imageUrl[(uploadIndex + "/upload/".Length)..];
            var parts = afterUpload.Split('/');
            var startIdx = (parts.Length > 0 && parts[0].StartsWith('v') && parts[0].Length > 1 && parts[0][1..].All(char.IsDigit))
                ? 1
                : 0;

            var withoutVersion = string.Join('/', parts[startIdx..]);
            var lastDot = withoutVersion.LastIndexOf('.');
            return lastDot > 0 ? withoutVersion[..lastDot] : withoutVersion;
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

            // 🆕 Mã combo mới nhất (số lớn hơn) lên đầu, mã cũ xuống sau
            combos = combos
                .OrderByDescending(c =>
                {
                    var suffix = c.MaCb != null && c.MaCb.StartsWith("CB")
                        ? c.MaCb.Substring(2)
                        : c.MaCb;
                    return int.TryParse(suffix, out var n) ? n : -1;
                })
                .ToList();

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

        // 🔢 Sinh mã combo tự động dạng CB01, CB02, CB03...
        private async Task<string> GenerateMaComboAsync()
        {
            var maCombos = await _context.Combos
                .Where(c => c.MaCb.StartsWith("CB"))
                .Select(c => c.MaCb.Substring(2))
                .ToListAsync();

            var maxNumber = 0;
            foreach (var s in maCombos)
            {
                if (int.TryParse(s, out var n) && n > maxNumber)
                {
                    maxNumber = n;
                }
            }

            var next = maxNumber + 1;
            return $"CB{next:D2}";
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] ComboDTO dto)
        {
            // 🔢 Mã combo được sinh tự động (CB01, CB02, ...), không nhận từ client
            var maCb = await GenerateMaComboAsync();

            var combo = new Combo
            {
                MaCb = maCb,
                TenCombo = dto.TenCombo,
                Gia = dto.Gia,
                MoTa = dto.MoTa,
                TrangThai = dto.TrangThai
            };
            if (dto.File != null && dto.File.Length > 0)
            {
                var (url, error) = await SaveImageAsync(dto.File);
                if (error != null)
                    return BadRequest(new { message = error });

                combo.HinhAnh = url;
            }

            _context.Combos.Add(combo);

            foreach (var dv in dto.DichVus)
            {
                _context.Combo_DichVus.Add(new Combo_DichVu
                {
                    MaCB = maCb,
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
                // ✅ XÓA ẢNH CŨ (trên Cloudinary)
                await DeleteImageAsync(combo.HinhAnh);

                // ✅ LƯU ẢNH MỚI (lên Cloudinary)
                var (url, error) = await SaveImageAsync(dto.File);
                if (error != null)
                    return BadRequest(new { message = error });

                combo.HinhAnh = url;
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
            // 🔥 Xóa ảnh nếu có (trên Cloudinary)
            await DeleteImageAsync(combo.HinhAnh);

            // Xóa chi tiết trước
            var chiTiet = _context.Combo_DichVus.Where(x => x.MaCB == id);
            _context.Combo_DichVus.RemoveRange(chiTiet);

            _context.Combos.Remove(combo);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
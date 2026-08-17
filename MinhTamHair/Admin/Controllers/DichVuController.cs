using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System;
using MinhTamHair.Services;
using MinhTamHair.Services.UploadIMG;

namespace MinhTamHair.KhanchHang.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DichVuController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IUploadDVSP _uploadImg;
        private readonly IServiceCacheService _cache;

        public DichVuController(MinhTamHairContext context, IUploadDVSP uploadImg, IServiceCacheService cache)
        {
            _context = context;
            _uploadImg = uploadImg;
            _cache = cache;
        }

        // Upload ảnh lên Cloudinary (cloud CloudinaryDVSP, folder "DVSP").
        // Trả về SecureUrl đầy đủ, hoặc null nếu lỗi (bỏ qua ảnh đó, giữ hành vi cũ
        // là không chặn cả request khi 1 ảnh trong nhiều ảnh bị lỗi).
        private async Task<string?> SaveImageAsync(IFormFile file)
        {
            var result = await _uploadImg.AddIMG(file);
            if (result?.Error != null) return null;
            return result?.SecureUrl?.ToString();
        }

        // Xóa ảnh trên Cloudinary dựa vào URL đầy đủ đã lưu trong DB.
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

            var raw = await query
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

            // Sắp xếp mã đúng chuẩn "DV" + số (DV001, DV002...) theo số giảm dần, mã mới nhất lên trước.
            // Mã không theo chuẩn (vd "vip") luôn bị đẩy xuống cuối danh sách, không xen vào giữa.
            var data = raw
                .OrderByDescending(x => x.MaDv.StartsWith("DV") && int.TryParse(x.MaDv.Substring(2), out _))
                .ThenByDescending(x =>
                    x.MaDv.StartsWith("DV") && int.TryParse(x.MaDv.Substring(2), out var so) ? so : 0)
                .ToList();

            return Ok(data);
        }

        // Tự sinh mã dịch vụ dạng DV001, DV002, DV003...
        private async Task<string> GenerateMaDvAsync()
        {
            var maxSo = await _context.Dichvus
                .Where(x => x.MaDv.StartsWith("DV"))
                .Select(x => x.MaDv.Substring(2))
                .ToListAsync();

            int max = 0;
            foreach (var s in maxSo)
            {
                if (int.TryParse(s, out var so) && so > max)
                {
                    max = so;
                }
            }

            return "DV" + (max + 1).ToString("D3");
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] DichVuCreateDTO dto)
        {
            var maDv = await GenerateMaDvAsync();

            var dv = new Dichvu
            {
                MaDv = maDv,
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
                    var url = await SaveImageAsync(file);
                    if (url == null) continue; // ảnh lỗi thì bỏ qua, không chặn cả request

                    _context.Hinhanhs.Add(new Hinhanh
                    {
                        MaDv = dv.MaDv,
                        DuongDan = url
                    });
                }
            }

            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();
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
                    var url = await SaveImageAsync(file);
                    if (url == null) continue;

                    _context.Hinhanhs.Add(new Hinhanh
                    {
                        MaDv = dv.MaDv,
                        DuongDan = url
                    });
                }
            }

            if (dto.DeletedImages != null && dto.DeletedImages.Count > 0)
            {
                foreach (var imgUrl in dto.DeletedImages)
                {
                    // Ảnh Cloudinary: DuongDan trong DB đã là URL đầy đủ, FE gửi lên y nguyên
                    // -> so khớp trực tiếp là đủ.
                    // Ảnh local cũ (trước khi có Cloudinary): DuongDan trong DB chỉ là path
                    // tương đối (vd "/assets/images/xxx.png"), nhưng FE luôn gửi URL đã ghép
                    // domain (vd "https://localhost:7196/assets/images/xxx.png") vì dùng chung
                    // hàm toAbsUrl() cho mọi ảnh. So sánh "==" nên luôn fail với nhóm này.
                    // Dùng EndsWith để khớp được cả hai trường hợp.
                    var img = dv.Hinhanhs.FirstOrDefault(x =>
                        x.DuongDan == imgUrl ||
                        (!string.IsNullOrEmpty(x.DuongDan) && imgUrl.EndsWith(x.DuongDan, StringComparison.OrdinalIgnoreCase)));

                    if (img != null)
                    {
                        await DeleteImageAsync(img.DuongDan);
                        _context.Hinhanhs.Remove(img);
                    }
                }
            }
            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();
            return Ok(dv);
        }

        [HttpDelete("image/{id}")]
        public async Task<IActionResult> DeleteImage(int id)
        {
            var img = await _context.Hinhanhs.FindAsync(id);
            if (img == null) return NotFound();

            await DeleteImageAsync(img.DuongDan);

            _context.Hinhanhs.Remove(img);
            await _context.SaveChangesAsync();
            await _cache.InvalidateAllServiceCacheAsync();

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
                await DeleteImageAsync(img.DuongDan);
            }
            foreach (var td in dv.TieudeDichvus)
            {
                foreach (var ct in td.ChitietTieudes)
                {
                    if (!string.IsNullOrEmpty(ct.HinhAnh))
                    {
                        await DeleteImageAsync(ct.HinhAnh);
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
            await _cache.InvalidateAllServiceCacheAsync();

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
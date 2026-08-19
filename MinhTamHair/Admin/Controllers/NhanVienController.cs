using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MinhTamHair.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MinhTamHair.Services.UploadIMG;

namespace MinhTamHair.Controllers
{
    public class NhanVienDto
    {
        public string MaNv { get; set; } = null!;
        public string? MaRole { get; set; }
        public string? MaLoaiNv { get; set; }
        public string? HoTenNv { get; set; }
        public string? GioiTinh { get; set; }
        public string? Cccd { get; set; }
        public int? NamSinh { get; set; }
        public string? Email { get; set; }
        public string? Sdt { get; set; }
        public string? DiaChi { get; set; }
        public DateTime? NgayVaoLam { get; set; }
        public int? TrangThai { get; set; }
        public string? TenDangNhap { get; set; }
        public string? MatKhau { get; set; }
        public string? MatKhauCu { get; set; }
        public IFormFile? HinhAnhFile { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class NhanVienController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IUploadIMG _uploadImg;
        private readonly IConfiguration _configuration;

        public NhanVienController(MinhTamHairContext context, IUploadIMG uploadImg, IConfiguration configuration)
        {
            _context = context;
            _uploadImg = uploadImg;
            _configuration = configuration;
        }

        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            try
            {
                var roles = await _context.Roles
                    .Select(r => new { maRole = r.MaRole, tenChucVu = r.TenChucVu })
                    .ToListAsync();
                return Ok(roles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("loai-nv")]
        public async Task<IActionResult> GetLoaiNvs()
        {
            try
            {
                var loaiNvs = await _context.Loainhanviens
                    .Select(l => new { maLoaiNv = l.MaLoaiNv, tenLoai = l.TenLoai })
                    .ToListAsync();
                return Ok(loaiNvs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetNhanViens([FromQuery] string? search, [FromQuery] int? trangThai, [FromQuery] string? gioiTinh)
        {
            try
            {
                var currentMaNv = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                var query = _context.Nhanviens
                    .Include(nv => nv.MaRoleNavigation)
                    .Include(nv => nv.MaLoaiNvNavigation)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(currentMaNv))
                {
                    query = query.Where(nv => nv.MaNv != currentMaNv);
                }

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(nv => nv.HoTenNv.Contains(search) || nv.MaNv.Contains(search) || nv.Sdt.Contains(search) || nv.Cccd.Contains(search));

                if (trangThai.HasValue)
                    query = query.Where(nv => nv.TrangThai == trangThai.Value);

                if (!string.IsNullOrEmpty(gioiTinh))
                    query = query.Where(nv => nv.GioiTinh == gioiTinh);

                var danhSach = await query
                    .OrderByDescending(nv => nv.TrangThai)
                    .Select(nv => new
                    {
                        nv.MaNv,
                        nv.MaRole,
                        TenChucVu = nv.MaRoleNavigation != null ? nv.MaRoleNavigation.TenChucVu : "Chưa phân quyền",
                        nv.MaLoaiNv,
                        TenLoaiNv = nv.MaLoaiNvNavigation != null ? nv.MaLoaiNvNavigation.TenLoai : "Chưa phân loại",
                        nv.HoTenNv,
                        nv.GioiTinh,
                        nv.Cccd,
                        nv.NamSinh,
                        nv.Email,
                        nv.Sdt,
                        nv.DiaChi,
                        nv.NgayVaoLam,
                        nv.TrangThai,
                        nv.TenDangNhap,
                        nv.HinhAnh
                    })
                    .ToListAsync();

                return Ok(danhSach);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }

        private async Task<string?> SaveImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0) return null;
            var uploadResult = await _uploadImg.AddIMG(file);
            if (uploadResult?.Error != null || string.IsNullOrEmpty(uploadResult?.SecureUrl?.ToString()))
            {
                return null;
            }
            return uploadResult.SecureUrl.ToString();
        }

        [HttpPost]
        public async Task<IActionResult> AddNhanVien([FromForm] NhanVienDto dto)
        {
            if (dto.HinhAnhFile != null && dto.HinhAnhFile.Length > 5 * 1024 * 1024)
                return BadRequest("Dung lượng ảnh đại diện không được vượt quá 5MB.");
            try
            {
                if (await _context.Nhanviens.AnyAsync(x => x.MaNv == dto.MaNv))
                    return BadRequest("Mã nhân viên đã tồn tại.");

                if (!string.IsNullOrEmpty(dto.TenDangNhap) && await _context.Nhanviens.AnyAsync(x => x.TenDangNhap == dto.TenDangNhap))
                    return BadRequest("Tên đăng nhập đã được sử dụng.");

                var nv = new Nhanvien
                {
                    MaNv = dto.MaNv,
                    MaRole = dto.MaRole,
                    MaLoaiNv = dto.MaLoaiNv,
                    HoTenNv = dto.HoTenNv,
                    GioiTinh = dto.GioiTinh,
                    Cccd = dto.Cccd,
                    NamSinh = dto.NamSinh,
                    Email = dto.Email,
                    Sdt = dto.Sdt,
                    DiaChi = dto.DiaChi,
                    NgayVaoLam = dto.NgayVaoLam.HasValue ? DateOnly.FromDateTime(dto.NgayVaoLam.Value) : null,
                    TrangThai = dto.TrangThai ?? 1,
                    TenDangNhap = dto.TenDangNhap,
                    MatKhau = BCrypt.Net.BCrypt.HashPassword(string.IsNullOrEmpty(dto.MatKhau) ? "123456" : dto.MatKhau)
                };

                if (dto.HinhAnhFile != null) nv.HinhAnh = await SaveImageAsync(dto.HinhAnhFile);

                _context.Nhanviens.Add(nv);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Thêm nhân viên thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateNhanVien(string id, [FromForm] NhanVienDto dto)
        {
            if (dto.HinhAnhFile != null && dto.HinhAnhFile.Length > 5 * 1024 * 1024)
                return BadRequest("Dung lượng ảnh đại diện không được vượt quá 5MB.");

            var currentMaNv = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentMaNv) || !currentMaNv.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                var isUserAdmin = User.IsInRole("Admin") || User.IsInRole("R01");
                if (!isUserAdmin)
                {
                    return StatusCode(403, "Bạn không có quyền sửa hồ sơ của nhân viên này.");
                }
            }

            try
            {
                var nv = await _context.Nhanviens.FindAsync(id);
                if (nv == null) return NotFound("Không tìm thấy nhân viên.");

                if (!string.IsNullOrEmpty(dto.TenDangNhap) && dto.TenDangNhap != nv.TenDangNhap &&
                    await _context.Nhanviens.AnyAsync(x => x.TenDangNhap == dto.TenDangNhap))
                {
                    return BadRequest("Tên đăng nhập đã được người khác sử dụng.");
                }

                nv.MaRole = dto.MaRole;
                nv.MaLoaiNv = dto.MaLoaiNv;
                nv.HoTenNv = dto.HoTenNv;
                nv.GioiTinh = dto.GioiTinh;
                nv.Cccd = dto.Cccd;
                nv.NamSinh = dto.NamSinh;
                nv.Email = dto.Email;
                nv.Sdt = dto.Sdt;
                nv.DiaChi = dto.DiaChi;

                if (dto.NgayVaoLam.HasValue) nv.NgayVaoLam = DateOnly.FromDateTime(dto.NgayVaoLam.Value);
                else nv.NgayVaoLam = null;

                nv.TrangThai = dto.TrangThai ?? 1;
                nv.TenDangNhap = dto.TenDangNhap;

                if (!string.IsNullOrEmpty(dto.MatKhau))
                {
                    if (!string.IsNullOrEmpty(nv.MatKhau))
                    {
                        if (string.IsNullOrEmpty(dto.MatKhauCu))
                        {
                            return BadRequest("Bắt buộc nhập mật khẩu cũ để xác thực thay đổi.");
                        }

                        bool isOldPasswordCorrect = false;
                        if (nv.MatKhau.StartsWith("$2a$") || nv.MatKhau.StartsWith("$2b$") || nv.MatKhau.StartsWith("$2y$"))
                        {
                            isOldPasswordCorrect = BCrypt.Net.BCrypt.Verify(dto.MatKhauCu, nv.MatKhau);
                        }
                        else
                        {
                            isOldPasswordCorrect = (nv.MatKhau.Trim() == dto.MatKhauCu.Trim());
                        }

                        if (!isOldPasswordCorrect)
                        {
                            return BadRequest("Mật khẩu cũ không chính xác.");
                        }
                    }
                    nv.MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhau);
                }

                if (dto.HinhAnhFile != null) nv.HinhAnh = await SaveImageAsync(dto.HinhAnhFile);

                await _context.SaveChangesAsync();
                return Ok(new { message = "Cập nhật thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNhanVien(string id)
        {
            try
            {
                var nv = await _context.Nhanviens.FindAsync(id);
                if (nv == null) return NotFound("Không tìm thấy nhân viên.");

                _context.Nhanviens.Remove(nv);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Xóa nhân viên thành công!" });
            }
            catch (Exception)
            {
                return BadRequest("Không thể xóa nhân viên này vì họ đã có dữ liệu liên quan. Hãy đổi trạng thái thành 'Đã nghỉ'.");
            }
        }

        [HttpGet("{id}/danh-gia")]
        public async Task<IActionResult> GetDanhGiaNhanVien(string id)
        {
            try
            {
                var nv = await _context.Nhanviens
                    .Include(n => n.DanhgiaNhanviens)
                    .FirstOrDefaultAsync(n => n.MaNv == id);

                if (nv == null) return NotFound("Không tìm thấy nhân viên.");
                return Ok(nv.DanhgiaNhanviens);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }

        [HttpPost("scan-cccd")]
        public async Task<IActionResult> ScanCccd(IFormFile image)
        {
            if (image == null || image.Length == 0)
                return BadRequest("Vui lòng tải lên ảnh CCCD.");

            string apiKey = _configuration["Gemini:ApiKey"] ?? "AQ.Ab8RN6JAPsZ0pNQtL1tBawiPxO133EesUg6PPbYQT2zO46wuEA";
            if (string.IsNullOrEmpty(apiKey))
                return StatusCode(500, "Chưa cấu hình API Key Gemini trên server.");

            try
            {
                using var ms = new MemoryStream();
                await image.CopyToAsync(ms);
                byte[] imageBytes = ms.ToArray();
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = string.IsNullOrEmpty(image.ContentType) ? "image/jpeg" : image.ContentType;

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Hãy trích xuất thông tin từ thẻ Căn cước công dân Việt Nam trong ảnh này. Chỉ trả về dữ liệu bằng định dạng JSON chuẩn. Không giải thích thêm, không dùng định dạng markdown. Các key bắt buộc: soCccd, hoTen, namSinh (chỉ lấy năm), gioiTinh (Nam hoặc Nữ), diaChi." },
                                new { inlineData = new { mimeType = mimeType, data = base64Image } }
                            }
                        }
                    },
                    generationConfig = new { responseMimeType = "application/json" }
                };

                using var httpClient = new HttpClient();
                string cleanKey = apiKey.Trim();
                var models = new[] { "gemini-3.5-flash", "gemini-3.5-flash-lite", "gemini-3.7-flash" };
                HttpResponseMessage? response = null;
                string error = "";

                foreach (var model in models)
                {
                    string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={cleanKey}";
                    try
                    {
                        response = await httpClient.PostAsJsonAsync(url, payload);
                        if (response.IsSuccessStatusCode)
                        {
                            break;
                        }
                        error = await response.Content.ReadAsStringAsync();
                    }
                    catch
                    {
                        // thử model kế tiếp
                    }
                }

                if (response == null || !response.IsSuccessStatusCode)
                {
                    return StatusCode(500, $"Lỗi từ Gemini AI: {error}");
                }

                var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
                string textResult = jsonResponse
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString()!;

                return Content(textResult, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server khi quét AI: " + ex.Message);
            }
        }
    }
}
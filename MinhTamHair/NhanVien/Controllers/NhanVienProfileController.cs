using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Text.RegularExpressions;
using System.IO;
using MinhTamHair.Services.UploadIMG; // 📌 Namespace upload Cloudinary

namespace MinhTamHair.Controllers
{
    [Route("api/NhanVien")]
    [ApiController]
    public class NhanVienProfileController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IUploadIMG _uploadImg; // 📌 Inject Cloudinary Service

        public NhanVienProfileController(MinhTamHairContext context, IUploadIMG uploadImg)
        {
            _context = context;
            _uploadImg = uploadImg;
        }

        // 1. API: LẤY THÔNG TIN HỒ SƠ
        [HttpGet("profile/{id}")]
        public async Task<IActionResult> GetProfile(string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest(new { message = "Mã nhân viên không hợp lệ." });

            var employee = await _context.Nhanviens
                .Include(nv => nv.MaLoaiNvNavigation)
                .Where(nv => nv.MaNv == id && nv.TrangThai == 1)
                .Select(nv => new
                {
                    maNv = nv.MaNv,
                    hoTenNv = nv.HoTenNv ?? "Chưa cập nhật",
                    email = nv.Email ?? "Chưa cập nhật",
                    sdt = nv.Sdt ?? "Chưa cập nhật",
                    diaChi = nv.DiaChi ?? "Chưa cập nhật",
                    cccd = nv.Cccd ?? "Chưa cập nhật",
                    gioiTinh = nv.GioiTinh ?? "Nam",
                    namSinh = nv.NamSinh ?? 2000,
                    ngayVaoLam = nv.NgayVaoLam.HasValue ? nv.NgayVaoLam.Value.ToString("dd/MM/yyyy") : "Chưa rõ",
                    hinhAnh = nv.HinhAnh ?? "",
                    tenDangNhap = nv.TenDangNhap ?? "",
                    tenLoaiNv = nv.MaLoaiNvNavigation != null ? nv.MaLoaiNvNavigation.TenLoai : "Nhân viên"
                })
                .FirstOrDefaultAsync();

            if (employee == null)
                return NotFound(new { message = "Không tìm thấy thông tin nhân viên." });

            return Ok(employee);
        }

        // 2. API: CẬP NHẬT TỔNG HỢP (HỒ SƠ, TÊN ĐĂNG NHẬP, MẬT KHẨU, FILE ẢNH CLOUDINARY)
        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile(
            [FromForm] string maNv,
            [FromForm] string hoTenNv,
            [FromForm] string sdt,
            [FromForm] string diaChi,
            [FromForm] string gioiTinh,
            [FromForm] string tenDangNhap,
            [FromForm] string? matKhauMoi,
            [FromForm] string? matKhauCu,
            IFormFile? hinhAnhFile)
        {
            if (hinhAnhFile != null && hinhAnhFile.Length > 5 * 1024 * 1024)
            {
                return BadRequest(new { message = "Dung lượng ảnh đại diện không được vượt quá 5MB." });
            }

            var currentMaNv = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentMaNv) || !currentMaNv.Equals(maNv, StringComparison.OrdinalIgnoreCase))
            {
                var isUserAdmin = User.IsInRole("Admin") || User.IsInRole("R01");
                if (!isUserAdmin)
                {
                    return StatusCode(403, new { message = "Bạn không có quyền sửa hồ sơ của nhân viên này." });
                }
            }

            // --- BƯỚC 1: VALIDATE DỮ LIỆU CƠ BẢN ---
            if (string.IsNullOrWhiteSpace(maNv)) return BadRequest(new { message = "Mã nhân viên bắt buộc." });
            if (string.IsNullOrWhiteSpace(hoTenNv)) return BadRequest(new { message = "Họ tên không được để trống." });
            if (string.IsNullOrWhiteSpace(tenDangNhap)) return BadRequest(new { message = "Tên đăng nhập không được để trống." });

            var phoneRegex = @"^(0[3|5|7|8|9])+([0-9]{8})$";
            if (string.IsNullOrWhiteSpace(sdt) || !Regex.IsMatch(sdt, phoneRegex))
            {
                return BadRequest(new { message = "Số điện thoại không hợp lệ hoặc sai định dạng Việt Nam." });
            }

            // --- BƯỚC 2: KIỂM TRA TRÙNG TÊN ĐĂNG NHẬP ---
            bool isUsernameTaken = await _context.Nhanviens
                .AnyAsync(nv => nv.TenDangNhap == tenDangNhap.Trim() && nv.MaNv != maNv);
            if (isUsernameTaken)
            {
                return BadRequest(new { message = "Tên đăng nhập này đã có người khác sử dụng." });
            }

            // --- BƯỚC 3: TÌM NHÂN VIÊN GỐC ĐỂ CẬP NHẬT ---
            var employee = await _context.Nhanviens.FindAsync(maNv);
            if (employee == null) return NotFound(new { message = "Nhân viên không tồn tại." });

            try
            {
                employee.HoTenNv = hoTenNv.Trim();
                employee.Sdt = sdt.Trim();
                employee.DiaChi = diaChi.Trim();
                employee.GioiTinh = gioiTinh;
                employee.TenDangNhap = tenDangNhap.Trim();

                // Đổi mật khẩu nếu có nhập mật khẩu mới
                if (!string.IsNullOrWhiteSpace(matKhauMoi))
                {
                    if (!string.IsNullOrEmpty(employee.MatKhau))
                    {
                        if (string.IsNullOrEmpty(matKhauCu))
                        {
                            return BadRequest(new { message = "Bắt buộc nhập mật khẩu cũ để xác thực thay đổi." });
                        }

                        bool isOldPasswordCorrect = false;
                        if (employee.MatKhau.StartsWith("$2a$") || employee.MatKhau.StartsWith("$2b$") || employee.MatKhau.StartsWith("$2y$"))
                        {
                            isOldPasswordCorrect = BCrypt.Net.BCrypt.Verify(matKhauCu, employee.MatKhau);
                        }
                        else
                        {
                            isOldPasswordCorrect = (employee.MatKhau.Trim() == matKhauCu.Trim());
                        }

                        if (!isOldPasswordCorrect)
                        {
                            return BadRequest(new { message = "Mật khẩu cũ không chính xác." });
                        }
                    }
                    employee.MatKhau = BCrypt.Net.BCrypt.HashPassword(matKhauMoi.Trim());
                }

                // --- BƯỚC 4: TẢI ẢNH LÊN CLOUDINARY ---
                if (hinhAnhFile != null && hinhAnhFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                    var extension = Path.GetExtension(hinhAnhFile.FileName).ToLower();
                    if (!allowedExtensions.Contains(extension))
                    {
                        return BadRequest(new { message = "Định dạng file không hợp lệ. Chỉ chấp nhận JPG, JPEG, PNG, WEBP." });
                    }

                    // 📌 Tải ảnh trực tiếp lên Cloudinary bằng IUploadIMG
                    var uploadResult = await _uploadImg.AddIMG(hinhAnhFile);
                    if (uploadResult?.Error != null || string.IsNullOrEmpty(uploadResult?.SecureUrl?.ToString()))
                    {
                        return StatusCode(500, new { message = "Lỗi khi tải ảnh lên Cloudinary: " + uploadResult?.Error?.Message });
                    }

                    // Lưu URL Cloudinary vào CSDL
                    employee.HinhAnh = uploadResult.SecureUrl.ToString();
                }

                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Cập nhật tài khoản và hồ sơ thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }
}

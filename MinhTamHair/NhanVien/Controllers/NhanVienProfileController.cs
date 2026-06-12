using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System.Text.RegularExpressions;

namespace MinhTamHair.Controllers
{
    [Route("api/NhanVien")]
    [ApiController]
    public class NhanVienProfileController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IWebHostEnvironment _env; // Thêm môi trường để lưu file vật lý

        public NhanVienProfileController(MinhTamHairContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
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

        // 2. API: CẬP NHẬT TỔNG HỢP (HỒ SƠ, TÊN ĐĂNG NHẬP, MẬT KHẨU, FILE ẢNH VẬT LÝ)
        // Dùng [FromForm] thay vì [FromBody] để nhận được File dữ liệu đính kèm
        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile(
            [FromForm] string maNv,
            [FromForm] string hoTenNv,
            [FromForm] string sdt,
            [FromForm] string diaChi,
            [FromForm] string gioiTinh,
            [FromForm] string tenDangNhap,
            [FromForm] string? matKhauMoi,
            IFormFile? hinhAnhFile)
        {
            // --- BƯỚC 1: VALIDATE DỮ LIỆU CƠ BẢN THỦ CÔNG ---
            if (string.IsNullOrWhiteSpace(maNv)) return BadRequest(new { message = "Mã nhân viên bắt buộc." });
            if (string.IsNullOrWhiteSpace(hoTenNv)) return BadRequest(new { message = "Họ tên không được để trống." });
            if (string.IsNullOrWhiteSpace(tenDangNhap)) return BadRequest(new { message = "Tên đăng nhập không được để trống." });

            var phoneRegex = @"^(0[3|5|7|8|9])+([0-9]{8})$";
            if (string.IsNullOrWhiteSpace(sdt) || !Regex.IsMatch(sdt, phoneRegex))
            {
                return BadRequest(new { message = "Số điện thoại không hợp lệ hoặc sai định dạng Việt Nam." });
            }

            // --- BƯỚC 2: KIỂM TRA TRÙNG TÊN ĐĂNG NHẬP TRÊN HỆ THỐNG ---
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
                // Cập nhật thông tin chữ
                employee.HoTenNv = hoTenNv.Trim();
                employee.Sdt = sdt.Trim();
                employee.DiaChi = diaChi.Trim();
                employee.GioiTinh = gioiTinh;
                employee.TenDangNhap = tenDangNhap.Trim();

                // Nếu người dùng có gõ mật khẩu mới thì tiến hành cập nhật đổi mật khẩu
                if (!string.IsNullOrWhiteSpace(matKhauMoi))
                {
                    employee.MatKhau = matKhauMoi.Trim();
                }

                // --- BƯỚC 4: XỬ LÝ LƯU FILE ẢNH VẬT LÝ ---
                if (hinhAnhFile != null && hinhAnhFile.Length > 0)
                {
                    // Đảm bảo định dạng tệp gửi lên là ảnh
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                    var extension = Path.GetExtension(hinhAnhFile.FileName).ToLower();
                    if (!allowedExtensions.Contains(extension))
                    {
                        return BadRequest(new { message = "Định dạng file không hợp lệ. Chỉ chấp nhận JPG, JPEG, PNG, GIF." });
                    }

                    // Tạo tên file duy nhất tránh trùng lặp: avatar_nv01.png
                    string folderPath = Path.Combine(_env.WebRootPath, "uploads", "avatars");
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath); // Tự động tạo thư mục nếu chưa có
                    }

                    string fileName = $"avatar_{maNv}{extension}";
                    string fullFilePath = Path.Combine(folderPath, fileName);

                    // Ghi đè tệp vật lý vào ổ cứng máy chủ
                    using (var stream = new FileStream(fullFilePath, FileMode.Create))
                    {
                        await hinhAnhFile.CopyToAsync(stream);
                    }

                    // Đường dẫn tương đối lưu vào Database để Frontend gọi hiển thị
                    employee.HinhAnh = $"/uploads/avatars/{fileName}";
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
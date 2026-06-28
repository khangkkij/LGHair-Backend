using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/admin/nha-cung-cap")]
    [ApiController]
    [Authorize(Roles = "Admin,Admin,Quản trị viên,Quản trị viên (Admin),R01,R02")]
    public class NhaCungCapController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        public NhaCungCapController(MinhTamHairContext context) => _context = context;

        [HttpGet("danh-sach")]
        public async Task<IActionResult> GetDanhSach([FromQuery] string? search, [FromQuery] int? trangThai)
        {
            try
            {
                var query = _context.Nhacungcaps.AsQueryable();

                if (trangThai.HasValue) query = query.Where(n => n.TrangThai == trangThai.Value);

                if (!string.IsNullOrEmpty(search))
                {
                    var cleanSearch = search.Trim().ToLower();
                    query = query.Where(n => n.MaNcc.ToLower().Contains(cleanSearch) ||
                                             n.TenNcc!.ToLower().Contains(cleanSearch) ||
                                             n.SoDienThoai!.Contains(cleanSearch));
                }

                var data = await query.OrderBy(n => n.MaNcc)
                    .Select(n => new {
                        maNcc = n.MaNcc,
                        tenNcc = n.TenNcc ?? "Chưa có tên",
                        soDienThoai = n.SoDienThoai ?? "N/A",
                        diaChi = n.DiaChi ?? "N/A",
                        email = n.Email ?? "N/A",
                        trangThai = n.TrangThai ?? 1
                    }).ToListAsync();

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ: " + ex.Message });
            }
        }

        [HttpPost("them-moi")]
        public async Task<IActionResult> ThemMoi([FromBody] Nhacungcap ncc)
        {
            try
            {
                if (ncc == null) return BadRequest(new { success = false, message = "Dữ liệu gửi lên không hợp lệ." });

                // --- BỘ PHÒNG THỦ VALIDATION BACKEND (CHỐNG SẬP DATABASE) ---
                if (string.IsNullOrEmpty(ncc.MaNcc) || ncc.MaNcc.Trim().Length > 30)
                    return BadRequest(new { success = false, message = "Mã nhà cung cấp không hợp lệ hoặc vượt quá 30 ký tự." });

                if (string.IsNullOrEmpty(ncc.TenNcc) || ncc.TenNcc.Trim().Length > 150)
                    return BadRequest(new { success = false, message = "Tên đối tác không được để trống và không quá 150 ký tự." });

                if (!string.IsNullOrEmpty(ncc.SoDienThoai) && !Regex.IsMatch(ncc.SoDienThoai.Trim(), @"^\d{9,11}$"))
                    return BadRequest(new { success = false, message = "Số điện thoại bắt buộc phải là chuỗi ký số dài từ 9 - 11 ký tự." });

                if (!string.IsNullOrEmpty(ncc.Email) && (ncc.Email.Length > 100 || !Regex.IsMatch(ncc.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")))
                    return BadRequest(new { success = false, message = "Định dạng Email của nhà cung cấp không đúng chuẩn." });

                var exist = await _context.Nhacungcaps.AnyAsync(x => x.MaNcc == ncc.MaNcc.Trim());
                if (exist) return BadRequest(new { success = false, message = "Mã đối tác này đã tồn tại trên cơ sở dữ liệu." });

                ncc.MaNcc = ncc.MaNcc.Trim();
                ncc.TenNcc = ncc.TenNcc.Trim();
                ncc.SoDienThoai = ncc.SoDienThoai?.Trim();
                ncc.Email = ncc.Email?.Trim();
                ncc.DiaChi = ncc.DiaChi?.Trim();
                ncc.TrangThai = ncc.TrangThai ?? 1;

                _context.Nhacungcaps.Add(ncc);
                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Không thể ghi nhận dữ liệu: " + ex.Message });
            }
        }

        [HttpPut("cap-nhat/{maNcc}")]
        public async Task<IActionResult> CapNhat(string maNcc, [FromBody] Nhacungcap nccData)
        {
            try
            {
                if (nccData == null) return BadRequest(new { success = false, message = "Dữ liệu cập nhật trống." });

                var ncc = await _context.Nhacungcaps.FirstOrDefaultAsync(x => x.MaNcc == maNcc);
                if (ncc == null) return NotFound(new { success = false, message = "Không tìm thấy dữ liệu đối tác cần sửa." });

                // --- BỘ PHÒNG THỦ VALIDATION BACKEND KHI CẬP NHẬT ---
                if (string.IsNullOrEmpty(nccData.TenNcc) || nccData.TenNcc.Trim().Length > 150)
                    return BadRequest(new { success = false, message = "Tên nhà cung cấp tối đa 150 ký tự." });

                if (!string.IsNullOrEmpty(nccData.SoDienThoai) && !Regex.IsMatch(nccData.SoDienThoai.Trim(), @"^\d{9,11}$"))
                    return BadRequest(new { success = false, message = "Số điện thoại bắt buộc phải nhập số ký số từ 9 - 11 chữ số." });

                if (!string.IsNullOrEmpty(nccData.Email) && (nccData.Email.Length > 100 || !Regex.IsMatch(nccData.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")))
                    return BadRequest(new { success = false, message = "Định dạng hòm thư điện tử Email không đúng cấu trúc." });

                ncc.TenNcc = nccData.TenNcc.Trim();
                ncc.SoDienThoai = nccData.SoDienThoai?.Trim();
                ncc.Email = nccData.Email?.Trim();
                ncc.DiaChi = nccData.DiaChi?.Trim();
                ncc.TrangThai = nccData.TrangThai ?? 1;

                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi cập nhật thực thể: " + ex.Message });
            }
        }

        [HttpDelete("xoa/{maNcc}")]
        public async Task<IActionResult> Xoa(string maNcc)
        {
            try
            {
                var ncc = await _context.Nhacungcaps.FirstOrDefaultAsync(x => x.MaNcc == maNcc);
                if (ncc == null) return NotFound(new { success = false, message = "Không tìm thấy dữ liệu đối tác." });

                bool CoPhieuNhap = await _context.Nhacungcaps.Include(n => n.Phieunhaps).AnyAsync(n => n.MaNcc == maNcc && n.Phieunhaps.Any());
                if (CoPhieuNhap)
                {
                    ncc.TrangThai = 0;
                    await _context.SaveChangesAsync();
                    return Ok(new { success = true, message = "Đối tác đã có lịch sử chứng từ phiếu nhập, hệ thống tự động chuyển trạng thái sang Ngừng hợp tác." });
                }

                _context.Nhacungcaps.Remove(ncc);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Xóa thông tin nhà cung cấp hoàn toàn thành công!" });
            }
            catch (Exception ex) { return StatusCode(500, new { success = false, message = ex.Message }); }
        }
    }
}
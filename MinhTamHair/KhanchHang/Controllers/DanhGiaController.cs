using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using Microsoft.AspNetCore.Http; 
using System.IO;                
using Microsoft.AspNetCore.Hosting;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DanhGiaController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        public DanhGiaController(MinhTamHairContext context, IConfiguration configuration, IWebHostEnvironment env)
        {
            _context = context;
            _configuration = configuration;
            _env = env;
        }
        [HttpGet("pending-to-rate/{maKh}")]
        public IActionResult GetPendingOrders(string maKh)
        {
            // Tìm các hóa đơn đã hoàn thành của khách hàng này
            var pendingOrders = _context.HoaDons
                .Where(hd => hd.MaKh == maKh && hd.TrangThai == 1) // TrangThai == 1 là đã thanh toán/hoàn thành
                .Select(hd => new PendingOrderResponse
                {
                    Id = hd.MaHd,
                    Date = hd.NgayTao ?? DateTime.Now,
                    ViewMode = "",

                    // 1. TRUY XUẤT DỊCH VỤ 
                    Services = hd.MaLdNavigation.ChitietLichdats
                        .Select(ct => new ServiceItem
                        {
                            Id = ct.MaDv ?? ct.MaCb, // Nếu không có MaDv thì lấy MaCb
                            Name = ct.MaDvNavigation != null ? ct.MaDvNavigation.TenDv :
                                  (ct.MaCbNavigation != null ? ct.MaCbNavigation.TenCombo : "Dịch vụ"),
                            Rating = 0,
                            Comment = "",
                            // Kiểm tra xem dịch vụ này đã được đánh giá chưa
                            // ĐOẠN CODE MỚI ĐÃ FIX LỖI NULL
                            IsAlreadyRated = _context.DanhgiaDichvus.Any(dg =>
                                dg.MaHd == hd.MaHd &&
                                (
                                    (ct.MaDv != null && dg.MaDv == ct.MaDv) ||
                                    (ct.MaCb != null && dg.MaCb == ct.MaCb)
                                )
                            )
                        })
                        .ToList(),

                    // 2. TRUY XUẤT NHÂN VIÊN (Phần bạn đang thiếu)
                    Staffs = hd.MaLdNavigation.ChitietLichdats
                        .Where(ct => ct.MaNv != null) // Chỉ lấy các chi tiết có gán nhân viên thực hiện
                        .Select(ct => ct.MaNvNavigation) // Trỏ tới Object Nhân Viên
                        .Distinct() // Loại bỏ trùng lặp (trường hợp 1 thợ làm 2 dịch vụ cho 1 khách)
                        .Select(nv => new StaffItem
                        {
                            Id = nv.MaNv,
                            Name = nv.HoTenNv,
                            Avatar = nv.HinhAnh,
                            Role = nv.MaLoaiNvNavigation != null ? nv.MaLoaiNvNavigation.TenLoai : "Nhân viên",
                            Rating = 0,
                            Comment = ""
                        })
                        // QUAN TRỌNG: Lọc bỏ những nhân viên đã được đánh giá trong hóa đơn này rồi
                        .Where(staff => !_context.DanhgiaNhanviens.Any(dgnv => dgnv.MaHd == hd.MaHd && dgnv.MaNv == staff.Id))
                        .ToList()
                })
                .ToList(); // Tải dữ liệu vào RAM trước khi xử lý Where tiếp theo

            // 3. LỌC HÓA ĐƠN: Chỉ trả về những hóa đơn CÒN dịch vụ chưa đánh giá HOẶC CÒN nhân viên chưa đánh giá
            var finalResult = pendingOrders
                .Where(hd => hd.Services.Any(s => !s.IsAlreadyRated) || hd.Staffs.Any())
                .OrderByDescending(hd => hd.Date)
                .ToList();

            return Ok(finalResult);
        }

        [HttpGet("rated-history/{maKH}")]
        public async Task<IActionResult> GetRatedHistory(string maKH)
        {
            try
            {
                var rawRatings = await _context.DanhgiaDichvus
                    .Include(dg => dg.MaHdNavigation)
                    .Include(dg => dg.MaDvNavigation)
                    .Include(dg => dg.MaCbNavigation)
                    .Where(dg => dg.MaHdNavigation.MaKh == maKH)
                    .ToListAsync();

                // SỬA ĐOẠN NÀY
                var result = rawRatings
                    .GroupBy(dg => dg.MaHd)
                    .Select(group => new {
                        id = group.Key,
                        date = group.First().NgayDanhGia?.ToString("yyyy-MM-dd") ?? "",
                        services = group.Select(s => new {
                            id = s.MaDv ?? s.MaCb, // ĐÃ BỔ SUNG: Trả về ID dịch vụ hoặc combo để xóa
                            name = s.MaDvNavigation?.TenDv ?? s.MaCbNavigation?.TenCombo ?? "Dịch vụ",
                            rating = s.SoSao,
                            comment = s.NoiDung,
                            image = s.HinhAnh,
                            reply = s.PhanHoi,
                            Ngay = s.NgayDanhGia?.ToString("dd/MM/yyyy HH:mm") ?? ""
                        }).ToList()
                    })
                    .OrderByDescending(x => x.id)
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("submit-multiple-reviews-with-image")]
        public async Task<IActionResult> SubmitReviews([FromForm] string maHd, [FromForm] List<ServiceRatingDTO> reviews)
        {
            // 1. Kiểm tra dữ liệu đầu vào
            if (string.IsNullOrEmpty(maHd) || reviews == null || !reviews.Any())
            {
                return BadRequest("Dữ liệu không hợp lệ hoặc chưa chọn số sao.");
            }

            // Kiểm tra xem hóa đơn có tồn tại không (Tuỳ chọn nhưng nên có)
            // var checkHd = _context.Hoadons.Any(h => h.MaHd == maHd);
            // if (!checkHd) return NotFound("Không tìm thấy hóa đơn!");

            // THÔNG BÁO QUAN TRỌNG: 
            // KHÔNG dùng lệnh _context.DanhGias.RemoveRange(...) ở đây để tránh làm mất các dịch vụ đã đánh giá trước đó.

            try
            {
                foreach (var item in reviews)
                {
                    string imagePath = null;

                    // 2. Xử lý lưu file ảnh nếu khách hàng có đính kèm
                    if (item.HinhAnhFile != null && item.HinhAnhFile.Length > 0)
                    {
                        // Tạo tên file ngẫu nhiên để tránh trùng lặp
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(item.HinhAnhFile.FileName);

                        // Cấu hình đường dẫn lưu ảnh (lưu vào thư mục wwwroot/images/reviews)
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "reviews");

                        // Tạo thư mục nếu chưa tồn tại
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var filePath = Path.Combine(uploadsFolder, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await item.HinhAnhFile.CopyToAsync(stream);
                        }

                        // Đường dẫn trả về để lưu vào Database (có thể truy cập qua URL)
                        imagePath = "/images/reviews/" + fileName;
                    }

                    // 3. TẠO MỚI bản ghi Đánh Giá (Chỉ Add, Không Xóa)
                    var newReview = new DanhgiaDichvu
                    {
                        MaHd = maHd,
                        MaDv = item.MaDv,
                        MaCb = item.MaCb, // Hỗ trợ lưu cả mã combo nếu có
                        SoSao = item.SoSao,
                        NoiDung = item.NoiDung,
                        HinhAnh = imagePath, // Lưu đường dẫn ảnh vừa upload
                        // NgayTao = DateTime.Now // Bỏ comment dòng này nếu bảng DanhGia của bạn có cột lưu ngày
                    };

                    _context.DanhgiaDichvus.Add(newReview);
                }

                // 4. Lưu tất cả thay đổi vào Database
                await _context.SaveChangesAsync();

                return Ok(new { message = "Gửi đánh giá thành công!" });
            }
            catch (Exception ex)
            {
                // Log lỗi ra console của backend để dễ debug
                Console.WriteLine("LỖI KHI LƯU ĐÁNH GIÁ: " + ex.Message);
                return StatusCode(500, new { message = "Lỗi hệ thống khi lưu đánh giá." });
            }
        }
        // 1. Xóa một dịch vụ cụ thể trong hóa đơn
        [HttpDelete("delete-service-rating/{maHd}/{maDvCb}")]
        public async Task<IActionResult> DeleteServiceRating(string maHd, string maDvCb)
        {
            try
            {
                // Tìm bản ghi: mã hóa đơn khớp và (mã dịch vụ khớp HOẶC mã combo khớp)
                var rating = await _context.DanhgiaDichvus
                    .FirstOrDefaultAsync(dg => dg.MaHd == maHd && (dg.MaDv == maDvCb || dg.MaCb == maDvCb));

                if (rating == null) return NotFound("Không tìm thấy dữ liệu đánh giá.");

                // Xóa ảnh vật lý trên server nếu có
                if (!string.IsNullOrEmpty(rating.HinhAnh))
                {
                    var filePath = Path.Combine(_env.WebRootPath, rating.HinhAnh.TrimStart('/'));
                    if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
                }

                _context.DanhgiaDichvus.Remove(rating);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Xóa thành công" });
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        // 2. Xóa toàn bộ đánh giá của một hóa đơn
        [HttpDelete("delete-order-rating/{maHd}")]
        public async Task<IActionResult> DeleteOrderRating(string maHd)
        {
            try
            {
                var ratings = await _context.DanhgiaDichvus.Where(dg => dg.MaHd == maHd).ToListAsync();
                if (!ratings.Any()) return NotFound("Không tìm thấy đánh giá cho hóa đơn này.");

                foreach (var r in ratings)
                {
                    if (!string.IsNullOrEmpty(r.HinhAnh))
                    {
                        var filePath = Path.Combine(_env.WebRootPath, r.HinhAnh.TrimStart('/'));
                        if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
                    }
                }

                _context.DanhgiaDichvus.RemoveRange(ratings);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã xóa toàn bộ đánh giá của hóa đơn." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
        [HttpGet("get-staff-to-rate/{maHd}")]
        public IActionResult GetStaffToRate(string maHd)
        {
            // Tìm hóa đơn và liên kết qua các bảng để lấy danh sách nhân viên
            var staffList = _context.HoaDons
                .Where(hd => hd.MaHd == maHd && hd.TrangThai == 1) // Kiểm tra hóa đơn
                .SelectMany(hd => hd.MaLdNavigation.ChitietLichdats) // Vào chi tiết lịch đặt
                .Where(ct => ct.MaNv != null) // Đảm bảo có nhân viên thực hiện
                .Select(ct => ct.MaNvNavigation) // Lấy ra Object Nhân Viên
                .Distinct() // Loại bỏ trùng lặp (nếu 1 thợ làm 2 dịch vụ cho 1 khách)
                .Select(nv => new
                {
                    Id = nv.MaNv,
                    Name = nv.HoTenNv,
                    Role = nv.MaLoaiNvNavigation.TenLoai, // Lấy tên loại nhân viên (Stylist, Thợ phụ...)
                    Avatar = nv.HinhAnh,
                    Rating = 0, // Trạng thái mặc định cho UI Vue.js
                    Comment = "" // Trạng thái mặc định cho UI Vue.js
                })
                .ToList();

            if (!staffList.Any())
            {
                // Dự phòng: Nếu chi tiết lịch đặt không lưu nhân viên, lấy tạm nhân viên ở bảng Hóa Đơn
                var mainStaff = _context.HoaDons
                    .Where(hd => hd.MaHd == maHd)
                    .Select(hd => hd.MaNvNavigation)
                    .Select(nv => new
                    {
                        Id = nv.MaNv,
                        Name = nv.HoTenNv,
                        Role = nv.MaLoaiNvNavigation.TenLoai,
                        Avatar = nv.HinhAnh,
                        Rating = 0,
                        Comment = ""
                    })
                    .ToList();

                return Ok(mainStaff);
            }

            return Ok(staffList);
        }
        public class StaffRatingDTO
        {
            public string MaHd { get; set; }
            public string MaNv { get; set; }
            public int SoSao { get; set; }
            public string NoiDung { get; set; }
        }

        [HttpPost("submit-staff-reviews")]
        public async Task<IActionResult> SubmitStaffReviews([FromBody] List<StaffRatingDTO> reviews)
        {
            if (reviews == null || !reviews.Any()) return BadRequest();

            try
            {
                foreach (var item in reviews)
                {
                    var newReview = new DanhgiaNhanvien
                    {
                        MaHd = item.MaHd,
                        MaNv = item.MaNv,
                        SoSao = item.SoSao,
                        NoiDung = item.NoiDung,
                        NgayDanhGia = DateTime.Now,
                        TrangThai = 1
                    };
                    _context.DanhgiaNhanviens.Add(newReview);
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "Đánh giá nhân viên thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
        // API Lấy lịch sử đánh giá Nhân viên
        [HttpGet("staff-rated-history/{maKh}")]
        public IActionResult GetStaffRatedHistory(string maKh)
        {
            var history = _context.HoaDons
                .Where(hd => hd.MaKh == maKh)
                .Select(hd => new
                {
                    Id = hd.MaHd,
                    Date = hd.NgayTao.HasValue ? hd.NgayTao.Value.ToString("dd/MM/yyyy HH:mm") : "",
                    Staffs = _context.DanhgiaNhanviens
                        .Where(dg => dg.MaHd == hd.MaHd)
                        .Select(dg => new StaffItem
                        {
                            Id = dg.MaNv,
                            Name = dg.MaNvNavigation.HoTenNv,
                            Role = dg.MaNvNavigation.MaLoaiNvNavigation.TenLoai,
                            Avatar = dg.MaNvNavigation.HinhAnh,
                            Rating = dg.SoSao ?? 0,
                            Comment = dg.NoiDung
                        })
                        .ToList()
                })
                .Where(x => x.Staffs.Any())
                .OrderByDescending(x => x.Id)
                .ToList();

            return Ok(history);
        }
        [HttpDelete("delete-staff-rating/{maHd}")]
        public async Task<IActionResult> DeleteStaffRating(string maHd)
        {
            try
            {
                // Tìm bản ghi đánh giá nhân viên theo Mã hóa đơn
                var rating = await _context.DanhgiaNhanviens.FirstOrDefaultAsync(dg => dg.MaHd == maHd);

                if (rating == null) return NotFound("Không tìm thấy dữ liệu đánh giá.");

                _context.DanhgiaNhanviens.Remove(rating);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Xóa đánh giá thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
        // Đảm bảo chỉ có DUY NHẤT một hàm mang Route này trong toàn bộ Controller
        [HttpPost("submit-order-overall-with-image")]
        public async Task<IActionResult> SubmitOrderOverallWithImage([FromForm] OrderOverallRatingWithImageDTO dto)
        {
            if (string.IsNullOrEmpty(dto.MaHd)) return BadRequest("Mã hóa đơn không hợp lệ.");

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(hd => hd.MaLdNavigation)
                        .ThenInclude(ld => ld.ChitietLichdats)
                    .FirstOrDefaultAsync(hd => hd.MaHd == dto.MaHd);

                if (hoaDon == null) return NotFound("Hóa đơn không tồn tại.");

                string imagePath = null;
                if (dto.HinhAnhFile != null && dto.HinhAnhFile.Length > 0)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.HinhAnhFile.FileName);
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "reviews");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    var filePath = Path.Combine(uploadsFolder, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.HinhAnhFile.CopyToAsync(stream);
                    }
                    imagePath = "/images/reviews/" + fileName;
                }

                var details = hoaDon.MaLdNavigation.ChitietLichdats;
                foreach (var item in details)
                {
                    // Sử dụng đúng DbSet DanhgiaDichvus
                    bool exists = await _context.DanhgiaDichvus.AnyAsync(dg =>
                        dg.MaHd == dto.MaHd && (dg.MaDv == item.MaDv || dg.MaCb == item.MaCb));

                    if (!exists)
                    {
                        var newReview = new DanhgiaDichvu
                        {
                            MaHd = dto.MaHd,
                            MaDv = item.MaDv,
                            MaCb = item.MaCb,
                            SoSao = dto.GeneralRating,
                            NoiDung = dto.GeneralComment,
                            HinhAnh = imagePath,
                            NgayDanhGia = DateTime.Now,
                            TrangThai = 1
                        };
                        _context.DanhgiaDichvus.Add(newReview);
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã cộng dồn đánh giá thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }



        // API lấy tất cả bình luận của 1 Dịch Vụ / Combo cụ thể
        [HttpGet("service-reviews/{maDvCb}")]
        public async Task<IActionResult> GetServiceReviews(string maDvCb)
        {
            try
            {
                var reviews = await _context.DanhgiaDichvus
                    .Include(dg => dg.MaHdNavigation)
                        .ThenInclude(hd => hd.MaKhNavigation) // Lấy thông tin khách hàng
                                                              // 1. ĐÃ BỎ ĐIỀU KIỆN TrangThai == 1 ĐỂ LẤY ĐƯỢC CẢ DỮ LIỆU BỊ NULL
                    .Where(dg => dg.MaDv == maDvCb || dg.MaCb == maDvCb)
                    .Select(dg => new
                    {
                        id = dg.MaHd + "_" + dg.MaDv + dg.MaCb,
                        customerName = (dg.MaHdNavigation != null && dg.MaHdNavigation.MaKhNavigation != null)
                                        ? dg.MaHdNavigation.MaKhNavigation.HoTen : "Khách hàng",
                        customerAvatar = (dg.MaHdNavigation != null && dg.MaHdNavigation.MaKhNavigation != null)
                                        ? dg.MaHdNavigation.MaKhNavigation.HinhAnh : null,
                        rating = dg.SoSao ?? 0,
                        // 2. CHỐNG LỖI BÌNH LUẬN TRỐNG
                        comment = dg.NoiDung ?? "Không có nội dung đánh giá",
                        image = dg.HinhAnh,
                        // 3. CHỐNG LỖI CRASH VÌ NGÀY BỊ NULL (Lấy ngày hiện tại để thay thế nếu DB trống)
                        date = dg.NgayDanhGia ?? DateTime.Now
                    })
                    .OrderByDescending(r => r.date)
                    .ToListAsync();

                return Ok(reviews);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi lấy đánh giá: " + ex.Message);
            }
        }
        // API Lấy số sao trung bình của TẤT CẢ dịch vụ và combo
        [HttpGet("all-average-ratings")]
        public async Task<IActionResult> GetAllAverageRatings()
        {
            try
            {
                // 1. Chỉ lấy ID và Số sao ra (Tối ưu truy vấn, chạy cực nhanh)
                var rawRatings = await _context.DanhgiaDichvus
                    .Where(dg => dg.SoSao != null)
                    .Select(dg => new {
                        Id = dg.MaDv ?? dg.MaCb, // Lấy mã DV, nếu null thì lấy mã CB
                        Sao = dg.SoSao
                    })
                    .Where(x => x.Id != null)
                    .ToListAsync();

                // 2. Gom nhóm theo ID và tính trung bình
                var groupedRatings = rawRatings
                    .GroupBy(x => x.Id)
                    .Select(g => new {
                        id = g.Key,
                        averageRating = Math.Round(g.Average(x => (double)x.Sao), 1), // Làm tròn 1 số thập phân (VD: 4.8)
                        reviewCount = g.Count() // Đếm tổng lượt đánh giá
                    })
                    .ToList();

                return Ok(groupedRatings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi tính sao: " + ex.Message);
            }
        }
    }
}

using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Admin.Models;
using MinhTamHair.Models;
using static MinhTamHair.Admin.Models.CaLamDTO;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/admin/lich-lam-viec")]
    [ApiController]
    [Authorize(Roles = "R01,R02")] // Phân quyền: Chỉ Admin (R01) hoặc Quản lý (R02) được thao tác
    public class LichLamViecController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public LichLamViecController(MinhTamHairContext context)
        {
            _context = context;
        }

        // 1. LẤY DANH SÁCH NHÂN VIÊN ĐƯỢC PHÉP XẾP CA (Thợ cắt, Thợ gội...)
        [HttpGet("nhan-vien-hop-le")]
        public async Task<IActionResult> GetEligibleEmployees()
        {
            var nhanViens = await _context.Nhanviens
                .Include(nv => nv.MaLoaiNvNavigation)
                .Where(nv => nv.TrangThai == 1)
                .Select(nv => new
                {
                    nv.MaNv,
                    nv.HoTenNv,
                    LoaiNhanVien = nv.MaLoaiNvNavigation.TenLoai
                })
                .ToListAsync();

            return Ok(new { success = true, data = nhanViens });
        }

        // 2. XEM LỊCH LÀM VIỆC (Nâng cấp: Lọc linh hoạt + Trạng thái)
        [HttpGet("danh-sach")]
        public async Task<IActionResult> GetLichLamViec([FromQuery] string? tuNgay, [FromQuery] string? denNgay, [FromQuery] int? trangThai)
        {
            var today = DateOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());
            var retentionLimit = today.AddYears(-1).AddDays(-15); // Mốc giới hạn 1 năm 15 ngày

            var query = _context.Lichlamviecs
                .Include(l => l.MaNvNavigation)
                .ThenInclude(nv => nv.MaLoaiNvNavigation)
                .Where(l => l.NgayLamViec >= retentionLimit) // Ràng buộc cứng: Chỉ lấy dữ liệu trong thời gian cho phép
                .AsQueryable();

            // Lọc theo dải ngày người dùng chọn
            if (!string.IsNullOrEmpty(tuNgay) && DateOnly.TryParse(tuNgay, out DateOnly fromDate))
            {
                // Nếu ngày người dùng chọn cũ hơn mốc, ta lấy mốc làm chuẩn
                var actualFromDate = fromDate < retentionLimit ? retentionLimit : fromDate;
                query = query.Where(l => l.NgayLamViec >= actualFromDate);
            }

            if (!string.IsNullOrEmpty(denNgay) && DateOnly.TryParse(denNgay, out DateOnly toDate))
            {
                query = query.Where(l => l.NgayLamViec <= toDate);
            }

            // Lọc theo trạng thái nếu có
            if (trangThai.HasValue)
            {
                query = query.Where(l => l.TrangThai == trangThai.Value);
            }

            var rawLichs = await query.ToListAsync();

            bool isModified = false;
            var currentTime = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().TimeOfDay;

            foreach (var l in rawLichs)
            {
                if (l.TrangThai == 1 && l.CaLam != "Chưa xếp" && l.CaLam != "Nghỉ lễ" && l.NgayLamViec != null)
                {
                    bool isAbsent = false;
                    if (l.NgayLamViec.Value < today)
                    {
                        isAbsent = true; // Đã qua ngày hôm sau
                    }
                    else if (l.NgayLamViec.Value == today)
                    {
                        TimeSpan shiftEndTime;
                        if (l.CaLam.Contains("Ca sáng")) shiftEndTime = new TimeSpan(17, 0, 0);
                        else if (l.CaLam.Contains("Ca chiều")) shiftEndTime = new TimeSpan(18, 0, 0);
                        else if (l.CaLam.Contains("Ca tối")) shiftEndTime = new TimeSpan(23, 0, 0);
                        else shiftEndTime = new TimeSpan(23, 59, 59);

                        if (currentTime >= shiftEndTime)
                        {
                            isAbsent = true;
                        }
                    }

                    if (isAbsent)
                    {
                        l.TrangThai = 5; // Vắng mặt
                        l.GhiChu = string.IsNullOrEmpty(l.GhiChu) ? "Hệ thống tự đánh vắng do quá giờ" : l.GhiChu;
                        isModified = true;
                    }
                }
            }

            if (isModified)
            {
                await _context.SaveChangesAsync();
            }

            var lichs = rawLichs
                .Select(l => new LichLamViecResponse
                {
                    MaLich = l.MaLich,
                    MaNv = l.MaNv!,
                    HoTenNv = l.MaNvNavigation!.HoTenNv ?? "",
                    TenLoaiNv = l.MaNvNavigation.MaLoaiNvNavigation!.TenLoai,
                    NgayLamViec = l.NgayLamViec.Value.ToString("yyyy-MM-dd"),
                    CaLam = l.CaLam ?? "",
                    TrangThai = l.TrangThai,
                    GhiChu = l.GhiChu
                })
                .ToList();

            return Ok(new { success = true, data = lichs });
        }

        [HttpDelete("xoa/{maLich}")]
        public async Task<IActionResult> DeleteLichLamViec(int maLich)
        {
            var lich = await _context.Lichlamviecs.FindAsync(maLich);
            if (lich == null) return NotFound(new { success = false, message = "Không tìm thấy lịch làm việc." });
            DateOnly start = lich.NgayLamViec.Value;

            // 1. Định nghĩa khung giờ cho từng loại Ca làm (Phải khớp với lúc bạn thêm ca)
            TimeOnly caStart, caEnd;
            switch (lich.CaLam)
            {
                case "Ca sáng (7h-17h)":
                    caStart = new TimeOnly(7, 0); caEnd = new TimeOnly(17, 0);
                    break;
                case "Ca tối (13h-20h)":
                    caStart = new TimeOnly(13, 0); caEnd = new TimeOnly(20, 0);
                    break;
                case "Ca sáng (Part-time)":
                    caStart = new TimeOnly(7, 0); caEnd = new TimeOnly(11, 0);
                    break;
                case "Ca chiều (Part-time)":
                    caStart = new TimeOnly(13, 0); caEnd = new TimeOnly(17, 0);
                    break;
                case "Ca tối (Part-time)":
                    caStart = new TimeOnly(16, 0); caEnd = new TimeOnly(20, 0);
                    break;
                default:
                    // Nếu không xác định được ca, mặc định chặn xóa nếu ngày đó có bất kỳ lịch đặt nào
                    caStart = new TimeOnly(0, 0); caEnd = new TimeOnly(23, 59);
                    break;
            }

            // 2. Kiểm tra xem có lịch đặt nào của khách đè lên khung giờ này không
            // Công thức Overlap: (Bắt đầu ca < Kết thúc khách) VÀ (Bắt đầu khách < Kết thúc ca)
            var hasBooking = await _context.ChitietLichdats
        .Include(ct => ct.MaLdNavigation)
        .AnyAsync(ct =>
            ct.MaNv == lich.MaNv &&
            ct.MaLdNavigation!.NgayHen >= start && // Từ ngày bắt đầu dải
            ct.MaLdNavigation.TrangThai != 0 &&
            caStart < ct.MaLdNavigation.GioKetThuc &&
            ct.MaLdNavigation.GioBatDau < caEnd
        );

            if (hasBooking)
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"Không thể xóa ca {lich.CaLam} vì nhân viên đã có lịch khách đặt trong khung giờ này."
                });
            }

            _context.Lichlamviecs.Remove(lich);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa ca làm thành công." });
        }

        [HttpPost("tao-moi")] // Hoặc đổi tên thành "cap-nhat-ca"
        public async Task<IActionResult> UpdateTodayShift([FromBody] CreateLichLamViecRequest request)
        {
            if (!DateOnly.TryParse(request.NgayLamViec, out DateOnly ngaySua))
                return BadRequest(new { message = "Ngày không hợp lệ." });

            if (ngaySua < DateOnly.FromDateTime(DateTime.Today))
                return BadRequest(new { message = "Không thể thiết lập ca làm việc cho ngày trong quá khứ." });

            // 1. Xác định khung giờ của CA MỚI
            TimeOnly newStart, newEnd;
            switch (request.CaLam)
            {
                case "Ca sáng (7h-17h)": newStart = new TimeOnly(7, 0); newEnd = new TimeOnly(17, 0); break;
                case "Ca tối (13h-20h)": newStart = new TimeOnly(13, 0); newEnd = new TimeOnly(20, 0); break;
                case "Ca sáng (Part-time)": newStart = new TimeOnly(7, 0); newEnd = new TimeOnly(11, 0); break;
                case "Ca chiều (Part-time)": newStart = new TimeOnly(13, 0); newEnd = new TimeOnly(17, 0); break;
                case "Ca tối (Part-time)": newStart = new TimeOnly(16, 0); newEnd = new TimeOnly(20, 0); break;
                default: 
                    // Nếu đổi thành "Chưa xếp" hoặc ca không hợp lệ, không cho phép đổi nếu đang có khách đặt trong ngày
                    newStart = TimeOnly.MaxValue; newEnd = TimeOnly.MinValue; 
                    break;
            }

            // 2. Kiểm tra xem có lịch đặt nào nằm ngoài khung giờ của CA MỚI không
            var hasConflictingBooking = await _context.ChitietLichdats
                .Include(ct => ct.MaLdNavigation)
                .AnyAsync(ct =>
                    ct.MaNv == request.MaNv &&
                    ct.MaLdNavigation!.NgayHen == ngaySua &&
                    ct.MaLdNavigation.TrangThai != 0 &&
                    (ct.MaLdNavigation.GioBatDau < newStart || ct.MaLdNavigation.GioKetThuc > newEnd)
                );

            if (hasConflictingBooking)
            {
                return BadRequest(new { message = $"Không thể đổi thành '{request.CaLam}' vì nhân viên đã có lịch khách đặt nằm ngoài khung giờ này." });
            }

            // 3. Tìm xem hôm nay đã có dòng dữ liệu (do tự động tạo hoặc có sẵn) chưa
            var lichHienTai = await _context.Lichlamviecs
                .FirstOrDefaultAsync(l => l.MaNv == request.MaNv && l.NgayLamViec == ngaySua);

            if (lichHienTai != null)
            {
                // Nếu đã điểm danh rồi thì KHÔNG cho sửa ca nữa để tránh gian lận
                if (lichHienTai.TrangThai == 2)
                    return BadRequest(new { message = "Không thể sửa ca khi nhân viên đã điểm danh xong." });

                // Cập nhật ca làm mới
                lichHienTai.CaLam = request.CaLam;
                lichHienTai.TrangThai = 1; // Khi đã xếp ca thì chuyển từ 0 (Chưa xếp) sang 1 (Chờ điểm danh)
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Cập nhật ca làm thành công." });
            }

            // Nếu trường hợp hy hữu chưa có dòng dữ liệu thì mới tạo mới
            var newLich = new Lichlamviec
            {
                MaNv = request.MaNv,
                NgayLamViec = ngaySua,
                CaLam = request.CaLam,
                TrangThai = 1
            };
            _context.Lichlamviecs.Add(newLich);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xếp ca làm mới." });
        }

        // 5. CẬP NHẬT TRẠNG THÁI CA LÀM (Bật/Tắt)
        [HttpPatch("doi-trang-thai/{maLich}")]
        public async Task<IActionResult> ToggleStatus(int maLich)
        {
            var lich = await _context.Lichlamviecs.FindAsync(maLich);
            if (lich == null) return NotFound(new { success = false, message = "Không tìm thấy lịch làm việc." });

            // Đảo trạng thái: 1 (Hoạt động) <-> 0 (Tạm nghỉ)
            lich.TrangThai = (lich.TrangThai == 1) ? 0 : 1;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = lich.TrangThai == 1 ? "Đã kích hoạt ca làm." : "Đã chuyển sang trạng thái tạm nghỉ.",
                newStatus = lich.TrangThai
            });
        }
        // API Điểm danh ca làm
        [HttpPatch("diem-danh/{maLich}")]
        public async Task<IActionResult> CheckIn(int maLich)
        {
            var lich = await _context.Lichlamviecs.FindAsync(maLich);
            if (lich == null) return NotFound();
            if (lich.TrangThai == 2) return BadRequest(new { message = "Ca làm này đã được điểm danh." });

            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            var today = DateOnly.FromDateTime(now);
            var currentTime = TimeOnly.FromDateTime(now);

            // 1. Kiểm tra ngày
            if (lich.NgayLamViec != today)
                return BadRequest(new { message = "Chỉ được điểm danh ca làm của ngày hôm nay." });

            // 2. Xác định khung giờ ca làm
            TimeOnly start = new TimeOnly(0, 0);
            TimeOnly end = new TimeOnly(23, 59);
            bool isFlexibleShift = false;

            switch (lich.CaLam)
            {
                case "Ca sáng (7h-17h)": start = new TimeOnly(7, 0); end = new TimeOnly(17, 0); break;
                case "Ca tối (13h-20h)": start = new TimeOnly(13, 0); end = new TimeOnly(20, 0); break;
                case "Ca sáng (Part-time)": start = new TimeOnly(7, 0); end = new TimeOnly(11, 0); break;
                case "Ca chiều (Part-time)": start = new TimeOnly(13, 0); end = new TimeOnly(17, 0); break;
                case "Ca tối (Part-time)": start = new TimeOnly(16, 0); end = new TimeOnly(20, 0); break;
                default:
                    isFlexibleShift = true; // Ca linh động, Ca tăng cường, Ca tùy chỉnh
                    break;
            }

            if (!isFlexibleShift)
            {
                // 3. Kiểm tra giờ hiện tại với ca cố định
                if (currentTime < start)
                    return BadRequest(new { message = $"Chưa đến giờ làm. Ca {lich.CaLam} bắt đầu lúc {start}." });

                if (currentTime > end)
                    return BadRequest(new { message = $"Đã quá giờ điểm danh của ca {lich.CaLam} ({end})." });
            }

            // Hợp lệ -> Chuyển trạng thái
            lich.TrangThai = 2;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Điểm danh thành công." });
        }

        // API Cập nhật trạng thái điểm danh (Có mặt, Vắng mặt, Rời ca, Muộn)
        [HttpPatch("cap-nhat-diem-danh/{maLich}")]
        public async Task<IActionResult> CapNhatDiemDanh(int maLich, [FromBody] UpdateDiemDanhRequest request)
        {
            var lich = await _context.Lichlamviecs.FindAsync(maLich);
            if (lich == null) return NotFound(new { message = "Không tìm thấy ca làm." });

            // 1. Kiểm tra mã trạng thái điểm danh hợp lệ (Chỉ cho phép 2: Có mặt, 3: Rời ca, 5: Vắng mặt, 6: Muộn)
            var validStatuses = new[] { 2, 3, 5, 6 };
            if (!validStatuses.Contains(request.TrangThai))
            {
                return BadRequest(new { message = "Trạng thái điểm danh không hợp lệ." });
            }

            // 2. Không cho phép đảo ngược ca đã điểm danh về 0 hoặc 1
            if (lich.TrangThai == 2 && (request.TrangThai == 0 || request.TrangThai == 1))
            {
                return BadRequest(new { message = "Nhân viên đã điểm danh có mặt, không thể reset về trạng thái chưa điểm danh." });
            }

            // 3. Khi chọn trạng thái khác Có mặt (2) thì phải nhập lý do
            if (request.TrangThai != 2 && string.IsNullOrWhiteSpace(request.GhiChu))
            {
                return BadRequest(new { message = "Vui lòng nhập lý do (Ghi chú)." });
            }

            lich.TrangThai = request.TrangThai;
            lich.GhiChu = request.GhiChu;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật trạng thái điểm danh thành công." });
        }

        [HttpPost("khoi-tao-ngay")]
        public async Task<IActionResult> InitializeTodayShifts([FromBody] JsonElement body)
        {
            DateOnly targetDate;
            if (body.TryGetProperty("date", out var dateProp) && DateOnly.TryParse(dateProp.GetString(), out var parsedDate))
            {
                targetDate = parsedDate;
            }
            else
            {
                targetDate = DateOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());
            }

            // --- BẮT ĐẦU LOGIC CLEAR TỰ ĐỘNG ---
            try
            {
                var retentionLimit = targetDate.AddYears(-1).AddDays(-15); // Mốc 1 năm 15 ngày trước

                // Tìm những bản ghi cũ hơn giới hạn này
                var oldRecords = _context.Lichlamviecs.Where(l => l.NgayLamViec < retentionLimit);

                if (oldRecords.Any())
                {
                    _context.Lichlamviecs.RemoveRange(oldRecords); // Xóa hàng loạt
                                                                   // Không cần SaveChanges ngay đây, nó sẽ được lưu cùng lúc với phần khởi tạo ở dưới
                }
            }
            catch (Exception ex)
            {
                // Log lỗi nếu cần, nhưng không chặn quá trình khởi tạo ngày mới
                Console.WriteLine($"Lỗi dọn dẹp DB: {ex.Message}");
            }

            var yesterday = targetDate.AddDays(-1);

            // 2. Lấy danh sách thợ hợp lệ
            var eligibleStaff = await _context.Nhanviens
                .Where(nv => nv.TrangThai == 1)
                .ToListAsync();

            // 3. Kiểm tra xem ngày targetDate đã có lịch chưa
            var existingMaNv = await _context.Lichlamviecs
                .Where(l => l.NgayLamViec == targetDate)
                .Select(l => l.MaNv)
                .ToListAsync();

            var staffToInit = eligibleStaff.Where(nv => !existingMaNv.Contains(nv.MaNv)).ToList();

            if (staffToInit.Any())
            {
                // 2. SỬA LỖI TẠI ĐÂY: Lấy lịch ngày hôm trước nhưng xử lý trùng lặp nhân viên
                var yesterdayShifts = await _context.Lichlamviecs
                    .Where(l => l.NgayLamViec == yesterday && l.MaNv != null)
                    .GroupBy(l => l.MaNv) // Nhóm tất cả các ca của cùng 1 người lại
                    .Select(g => new {
                        MaNv = g.Key,
                        CaLam = g.FirstOrDefault()!.CaLam // Chỉ lấy ca đầu tiên làm mẫu để tránh lỗi Duplicate Key
                    })
                    .ToDictionaryAsync(x => x.MaNv!, x => x.CaLam);

                foreach (var nv in staffToInit)
                {
                    var inheritedCaLam = yesterdayShifts.GetValueOrDefault(nv.MaNv) ?? "Chưa xếp";
                    if (targetDate.DayOfWeek == DayOfWeek.Sunday) {
                        inheritedCaLam = "Chưa xếp";
                    }
                    _context.Lichlamviecs.Add(new Lichlamviec
                    {
                        MaNv = nv.MaNv,
                        NgayLamViec = targetDate,
                        // Sử dụng GetValueOrDefault để an toàn hơn
                        CaLam = inheritedCaLam,
                        TrangThai = inheritedCaLam == "Chưa xếp" ? 0 : 1
                    });
                }
            }
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Đã khởi tạo cho ngày {targetDate}" });
        }

        [HttpPatch("roi-ca-som/{maLich}")]
        public async Task<IActionResult> MarkAsLeftEarly(int maLich, [FromBody] RoiCaSomRequest request)
        {
            var lich = await _context.Lichlamviecs.FindAsync(maLich);
            if (lich == null) return NotFound();

            // Chỉ cho phép chuyển từ "Đã có mặt" (2) sang "Rời ca sớm" (3)
            if (lich.TrangThai != 2)
            {
                return BadRequest(new { message = "Chỉ có thể đánh dấu rời ca cho nhân viên đã điểm danh có mặt." });
            }

            if (string.IsNullOrWhiteSpace(request.GhiChu))
            {
                return BadRequest(new { message = "Vui lòng nhập lý do rời ca sớm." });
            }

            lich.TrangThai = 3; // Trạng thái mới: Rời ca sớm
            lich.GhiChu = request.GhiChu;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã ghi nhận nhân viên rời ca sớm." });
        }
		[HttpPost("tao-lich-30-ngay")]
		public async Task<IActionResult> Generate30DaysShifts()
		{
			try
			{
				// 1. Xác định mốc thời gian: Từ hôm nay đến 30 ngày sau
				DateOnly today = DateOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());
				DateOnly endDate = today.AddDays(30);

				// 2. Lấy danh sách thợ hợp lệ (Được phép đặt lịch)
				var danhSachTho = await _context.Nhanviens
					.Include(nv => nv.MaLoaiNvNavigation)
					.Where(nv => nv.TrangThai == 1)
					.ToListAsync();

				// 3. Lấy ca làm gần nhất của từng thợ để làm "Mẫu"
				var caLamGiaoDien = await _context.Lichlamviecs
					.Where(l => l.NgayLamViec <= today && l.MaNv != null)
					.OrderByDescending(l => l.NgayLamViec)
					.ToListAsync();

				var mauCaLam = caLamGiaoDien
					.GroupBy(l => l.MaNv)
					.ToDictionary(g => g.Key!, g => g.FirstOrDefault(l => l.CaLam != "Chưa xếp" && l.CaLam != "Nghỉ lễ")?.CaLam ?? "Chưa xếp"); // Sửa fallback mặc định thành "Chưa xếp"

				// --- BƯỚC KIỂM TRA MỚI ---
				// Lọc ra danh sách mã nhân viên đang bị "Chưa xếp" ca
				var dsMaNvChuaXep = mauCaLam
					.Where(x => x.Value == "Chưa xếp" || string.IsNullOrWhiteSpace(x.Value))
					.Select(x => x.Key)
					.ToList();

				// Kiểm tra xem những người chưa xếp ca này có nằm trong danh sách thợ hợp lệ không
				var thoChuaXepLich = danhSachTho
					.Where(nv => dsMaNvChuaXep.Contains(nv.MaNv))
					.ToList();

				if (thoChuaXepLich.Any())
				{
					// Lấy tên những thợ chưa được xếp lịch để báo lỗi cụ thể
					// (Lưu ý: Thay 'HoTenNv' bằng đúng tên cột trong DB của bạn nếu khác)
					string tenCacTho = string.Join(", ", thoChuaXepLich.Select(nv => nv.HoTenNv));

					return BadRequest(new
					{
						success = false,
						message = $"Không thể tự động tạo lịch! Vui lòng xếp ca cụ thể cho các nhân viên sau trước khi tạo 30 ngày"
					});
				}
				// -------------------------

				int soCaMoi = 0;

				// 4. Vòng lặp chạy từ hôm nay đến 30 ngày sau
				for (var date = today; date <= endDate; date = date.AddDays(1))
				{
					// Lấy các ca ĐÃ CÓ trong ngày này để KHÔNG bị tạo trùng lặp
					var caDaCo = await _context.Lichlamviecs
						.Where(l => l.NgayLamViec == date)
						.Select(l => l.MaNv)
						.ToListAsync();

					foreach (var nv in danhSachTho)
					{
						// Nếu thợ này chưa có lịch trong ngày hôm đó -> Tạo mới
						if (!caDaCo.Contains(nv.MaNv))
						{
							var caMacDinh = mauCaLam.GetValueOrDefault(nv.MaNv, "Ca sáng (7h-17h)");
							if (date.DayOfWeek == DayOfWeek.Sunday) {
								caMacDinh = "Chưa xếp";
							}
							_context.Lichlamviecs.Add(new Lichlamviec
							{
								MaNv = nv.MaNv,
								NgayLamViec = date,
								CaLam = caMacDinh,
								TrangThai = caMacDinh == "Chưa xếp" ? 0 : 1
							});
							soCaMoi++;
						}
					}
				}

				await _context.SaveChangesAsync();
				return Ok(new { success = true, message = $"Tuyệt vời! Đã tự động tạo {soCaMoi} ca làm việc mới cho 30 ngày tới." });
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { success = false, message = "Lỗi khi tạo lịch: " + ex.Message });
			}
		}

		[HttpPost("nghi-le-chung")]
		public async Task<IActionResult> SetCompanyWideHoliday([FromBody] NghiLeChungRequest request)
		{
			if (!DateOnly.TryParse(request.TuNgay, out DateOnly fromDate) || 
			    !DateOnly.TryParse(request.DenNgay, out DateOnly toDate))
			{
				return BadRequest(new { message = "Định dạng ngày không hợp lệ." });
			}

			if (fromDate < DateOnly.FromDateTime(DateTime.Today))
			{
				return BadRequest(new { message = "Không thể thiết lập ngày nghỉ lễ trong quá khứ." });
			}

			if (fromDate > toDate)
			{
				return BadRequest(new { message = "Từ ngày không được lớn hơn Đến ngày." });
			}

			try
			{
				// 1. Lấy danh sách thợ hợp lệ
				var danhSachTho = await _context.Nhanviens
					.Include(nv => nv.MaLoaiNvNavigation)
					.Where(nv => nv.TrangThai == 1)
					.ToListAsync();

				int countUpdated = 0;
				int countCreated = 0;

				// 2. Lấy tất cả ca làm của các thợ trong khoảng thời gian này
				var existingShifts = await _context.Lichlamviecs
					.Where(l => l.NgayLamViec >= fromDate && l.NgayLamViec <= toDate)
					.ToListAsync();

				// 3. Xử lý từng ngày
				for (var date = fromDate; date <= toDate; date = date.AddDays(1))
				{
					foreach (var nv in danhSachTho)
					{
						var shift = existingShifts.FirstOrDefault(l => l.MaNv == nv.MaNv && l.NgayLamViec == date);
						if (shift != null)
						{
							if (shift.CaLam != "Nghỉ lễ")
							{
								shift.CaLam = "Nghỉ lễ";
								shift.TrangThai = 7; // Trạng thái 7: Nghỉ lễ
								shift.GhiChu = request.LyDo;
								countUpdated++;
							}
						}
						else
						{
							_context.Lichlamviecs.Add(new Lichlamviec
							{
								MaNv = nv.MaNv,
								NgayLamViec = date,
								CaLam = "Nghỉ lễ",
								TrangThai = 7, // Trạng thái 7: Nghỉ lễ
								GhiChu = request.LyDo
							});
							countCreated++;
						}
					}
				}

				await _context.SaveChangesAsync();
				return Ok(new { success = true, message = $"Đã áp dụng nghỉ lễ chung. Tạo mới {countCreated} ca nghỉ, cập nhật {countUpdated} ca." });
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { success = false, message = "Lỗi khi thiết lập nghỉ chung: " + ex.Message });
			}
		}

		[HttpGet("qr-diem-danh")]
		public IActionResult GetDailyQRCode()
		{
			var dateStr = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().ToString("yyyyMMdd");
			var qrCode = $"MinhTamHair-DiemDanh-{dateStr}";
			return Ok(new { success = true, qrCode = qrCode });
		}
	}
}
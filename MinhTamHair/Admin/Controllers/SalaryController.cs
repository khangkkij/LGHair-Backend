using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MinhTamHair.Admin.Services;

namespace MinhTamHair.Admin.Controllers
{
    // =====================================================
    // SALARY CONTROLLER - API TÍNH LƯƠNG ĐỘNG REAL-TIME
    // =====================================================
    // Route: /api/salary
    //
    // API 1 (Admin): GET /api/salary/summary?month=6&year=2025
    //   - Phân quyền: Chỉ Admin (R01)
    //   - Trả về: Bảng lương toàn bộ nhân viên
    //
    // API 2 (Nhân viên): GET /api/salary/my-salary?month=6&year=2025
    //   - Phân quyền: Admin + Nhân viên (R01, R02, Staff...)
    //   - Bảo mật: EmployeeId lấy từ JWT Claims, không nhận từ URL
    //   - Trả về: Phiếu lương cá nhân
    // =====================================================

    [Route("api/salary")]
    [ApiController]
    [Authorize] // Yêu cầu đăng nhập cho tất cả API trong controller này
    public class SalaryController : ControllerBase
    {
        private readonly ISalaryService _salaryService;
        private readonly ILogger<SalaryController> _logger;

        public SalaryController(ISalaryService salaryService, ILogger<SalaryController> logger)
        {
            _salaryService = salaryService;
            _logger = logger;
        }

        // =====================================================
        // API 1: ADMIN - XEM BẢNG LƯƠNG TOÀN BỘ NHÂN VIÊN
        // =====================================================
        /// <summary>
        /// [ADMIN ONLY] Lấy bảng lương tổng hợp tất cả nhân viên theo tháng/năm.
        /// Chỉ Admin (Role = R01) mới được phép gọi API này.
        /// </summary>
        /// <param name="month">Tháng cần xem (1-12), không được để trống</param>
        /// <param name="year">Năm cần xem (VD: 2025), không được để trống</param>
        [HttpGet("summary")]
        [Authorize(Roles = "R01")] // Chỉ Admin mới xem được bảng lương toàn bộ
        public async Task<IActionResult> GetSalarySummary([FromQuery] int month, [FromQuery] int year)
        {
            try
            {
                // --- VALIDATION THAM SỐ ĐẦU VÀO ---
                if (month < 1 || month > 12)
                    return BadRequest(new
                    {
                        success = false,
                        message = "Tháng không hợp lệ. Vui lòng nhập giá trị từ 1 đến 12."
                    });

                if (year < 2020 || year > MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Year + 1)
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Năm không hợp lệ. Vui lòng nhập năm từ 2020 đến {MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Year + 1}."
                    });

                // --- GỌI SERVICE TÍNH LƯƠNG ---
                // Mọi logic phức tạp đã được tách sang SalaryService để tái sử dụng
                var result = await _salaryService.GetSalarySummaryAsync(month, year);

                _logger.LogInformation(
                    "Admin {AdminId} đã xem bảng lương tháng {Month}/{Year}. Tổng {Total} nhân viên.",
                    User.FindFirstValue(ClaimTypes.NameIdentifier), month, year, result.TongNhanVien);

                return Ok(new
                {
                    success = true,
                    message = $"Bảng lương tháng {month}/{year}",
                    data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tính bảng lương tháng {Month}/{Year}", month, year);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Đã xảy ra lỗi khi tính lương. Vui lòng thử lại sau.",
                    // Chi tiết lỗi chỉ hiển thị ở môi trường development
                    detail = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
                        ? ex.Message : null
                });
            }
        }

        // =====================================================
        // API 2: NHÂN VIÊN - XEM PHIẾU LƯƠNG CÁ NHÂN
        // =====================================================
        /// <summary>
        /// [NHÂN VIÊN] Lấy phiếu lương cá nhân theo tháng/năm.
        /// EmployeeId được lấy tự động từ JWT Token Cookie (ClaimTypes.NameIdentifier = MaNv).
        /// Nhân viên CHỈ được xem lương của chính mình - không thể truy cập dữ liệu người khác.
        /// </summary>
        /// <param name="month">Tháng cần xem (1-12)</param>
        /// <param name="year">Năm cần xem (VD: 2025)</param>
        [HttpGet("my-salary")]
        // Cho phép cả Admin (R01) và Nhân viên (R02, Staff...) xem phiếu lương của mình
        // Không giới hạn role ở đây vì đã có [Authorize] ở cấp controller
        public async Task<IActionResult> GetMySalary([FromQuery] int month, [FromQuery] int year)
        {
            try
            {
                // -----------------------------------------------
                // BẢO MẬT QUAN TRỌNG: LẤY EMPLOYEE ID TỪ JWT CLAIMS
                // -----------------------------------------------
                // KHÔNG bao giờ nhận employeeId từ URL/QueryString vì nhân viên
                // có thể tự thay đổi để xem lương người khác (IDOR Attack).
                //
                // JWT Token được lưu trong HttpOnly Cookie (adminAccessToken).
                // ASP.NET Core tự động đọc Cookie → xác thực → parse Claims.
                // ClaimTypes.NameIdentifier = MaNv (được gán lúc Login trong AccountController)
                var employeeId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(employeeId))
                {
                    // Trường hợp này không nên xảy ra nếu [Authorize] đã hoạt động đúng
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Không xác định được tài khoản. Vui lòng đăng nhập lại."
                    });
                }

                // --- VALIDATION THAM SỐ ---
                if (month < 1 || month > 12)
                    return BadRequest(new
                    {
                        success = false,
                        message = "Tháng không hợp lệ. Vui lòng nhập giá trị từ 1 đến 12."
                    });

                if (year < 2020 || year > MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Year + 1)
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Năm không hợp lệ."
                    });

                // --- GỌI SERVICE TÍNH LƯƠNG CHO NV NÀY ---
                var result = await _salaryService.GetMySalaryAsync(employeeId, month, year);

                if (result == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy thông tin nhân viên hoặc tài khoản đã bị khóa."
                    });
                }

                _logger.LogInformation(
                    "Nhân viên {EmployeeId} xem phiếu lương tháng {Month}/{Year}.",
                    employeeId, month, year);

                return Ok(new
                {
                    success = true,
                    message = $"Phiếu lương tháng {month}/{year}",
                    data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy phiếu lương nhân viên tháng {Month}/{Year}", month, year);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Đã xảy ra lỗi khi lấy phiếu lương. Vui lòng thử lại sau."
                });
            }
        }
    }
}

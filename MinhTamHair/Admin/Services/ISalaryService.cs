using MinhTamHair.Admin.Models;

namespace MinhTamHair.Admin.Services
{
    // =====================================================
    // INTERFACE - HỢP ĐỒNG DỊCH VỤ TÍNH LƯƠNG
    // =====================================================
    // Tách biệt Interface và Implementation theo Clean Architecture:
    //   - Controller chỉ biết Interface này → dễ Unit Test, dễ Mock
    //   - Implementation có thể thay đổi mà không ảnh hưởng Controller
    // =====================================================

    public interface ISalaryService
    {
        /// <summary>
        /// [ADMIN] Tính lương toàn bộ nhân viên trong tháng/năm chỉ định.
        /// Trả về danh sách đầy đủ để Admin xem bảng lương tổng.
        /// </summary>
        /// <param name="month">Tháng cần tính (1-12)</param>
        /// <param name="year">Năm cần tính (VD: 2025)</param>
        Task<BangLuongSummaryResponse> GetSalarySummaryAsync(int month, int year);

        /// <summary>
        /// [NHÂN VIÊN] Tính lương của một nhân viên cụ thể trong tháng/năm.
        /// EmployeeId lấy từ JWT Claims để đảm bảo bảo mật - NV chỉ xem lương của chính mình.
        /// </summary>
        /// <param name="employeeId">Mã nhân viên (MaNv) - lấy từ ClaimTypes.NameIdentifier</param>
        /// <param name="month">Tháng cần xem (1-12)</param>
        /// <param name="year">Năm cần xem (VD: 2025)</param>
        Task<NhanVienLuongDto?> GetMySalaryAsync(string employeeId, int month, int year);
    }
}

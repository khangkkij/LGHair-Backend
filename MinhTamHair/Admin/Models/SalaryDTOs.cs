namespace MinhTamHair.Admin.Models
{
    // =====================================================
    // DTOs - DATA TRANSFER OBJECTS CHO MODULE TÍNH LƯƠNG
    // =====================================================
    // File này chứa tất cả các lớp DTO dùng để:
    //   1. Nhận dữ liệu từ truy vấn LINQ nội bộ (intermediate projection)
    //   2. Trả về dữ liệu chuẩn hóa cho Frontend Vue.js
    //
    // NGUYÊN TẮC: KHÔNG tạo bảng mới - chỉ tổng hợp từ bảng hiện có
    // =====================================================

    /// <summary>
    /// DTO trả về thông tin lương của 1 nhân viên trong 1 tháng cụ thể.
    /// Dùng cho cả API Admin (danh sách) và API Nhân viên (phiếu lương cá nhân).
    /// </summary>
    public class NhanVienLuongDto
    {
        /// <summary>Mã nhân viên (Primary Key)</summary>
        public string MaNv { get; set; } = "";

        /// <summary>Họ và tên đầy đủ của nhân viên</summary>
        public string HoTenNv { get; set; } = "";

        /// <summary>Tên loại nhân viên (ví dụ: Thợ cắt, Thợ nhuộm...)</summary>
        public string TenLoaiNv { get; set; } = "";

        /// <summary>Hình ảnh đại diện (đường dẫn ảnh)</summary>
        public string? HinhAnh { get; set; }

        /// <summary>
        /// Lương cơ bản theo loại nhân viên (từ bảng Loainhanvien.Luong).
        /// Đây là mức lương THÁNG CHUẨN (quy định), trước khi tính theo số ca thực tế.
        /// </summary>
        public decimal LuongCoBan { get; set; }

        /// <summary>
        /// Số ngày công quy định trong tháng (mặc định 26 ngày = ~2 ca/ngày với lịch salon).
        /// Admin có thể cấu hình. Dùng làm mẫu số trong công thức tính lương theo ca.
        /// </summary>
        public int SoNgayCongQuyDinh { get; set; } = 26;

        /// <summary>
        /// Số ca làm việc đã hoàn thành thực tế trong tháng.
        /// Điều kiện: Lichlamviec.TrangThai = 2 (Đã điểm danh có mặt).
        /// </summary>
        public int SoCaLamThucTe { get; set; }

        /// <summary>
        /// Tổng tiền hoa hồng dịch vụ tích lũy trong tháng.
        /// Lấy từ: ChitietLichdat.TienHoaHongDv của các lịch đặt thuộc hóa đơn
        /// đã được thanh toán thành công (Lichdat.TrangThai = 2 hoặc 3).
        /// </summary>
        public decimal TongHoaHong { get; set; }

        /// <summary>
        /// Phần lương cơ bản được tính dựa trên số ca thực làm.
        /// Công thức: LuongCoBan / SoNgayCongQuyDinh * SoCaLamThucTe
        /// </summary>
        public decimal LuongTheoNgayCong { get; set; }

        /// <summary>
        /// TỔNG THU NHẬP THỰC NHẬN = LuongTheoNgayCong + TongHoaHong
        /// </summary>
        public decimal TongThuNhap { get; set; }

        /// <summary>Tháng tính lương (1-12)</summary>
        public int Thang { get; set; }

        /// <summary>Năm tính lương</summary>
        public int Nam { get; set; }
    }

    /// <summary>
    /// DTO cho response của API Admin - bao gồm danh sách lương toàn bộ nhân viên
    /// </summary>
    public class BangLuongSummaryResponse
    {
        /// <summary>Tháng tính lương</summary>
        public int Thang { get; set; }

        /// <summary>Năm tính lương</summary>
        public int Nam { get; set; }

        /// <summary>Tổng số nhân viên trong bảng lương</summary>
        public int TongNhanVien { get; set; }

        /// <summary>Tổng quỹ lương phải trả trong tháng</summary>
        public decimal TongQuyLuong { get; set; }

        /// <summary>Danh sách chi tiết lương từng nhân viên</summary>
        public List<NhanVienLuongDto> DanhSachLuong { get; set; } = new();
    }

    /// <summary>
    /// DTO nội bộ - dùng để gộp kết quả LINQ trước khi tính toán.
    /// Không trả về cho Frontend.
    /// </summary>
    internal class NhanVienRawData
    {
        public string MaNv { get; set; } = "";
        public string HoTenNv { get; set; } = "";
        public string TenLoaiNv { get; set; } = "";
        public string? HinhAnh { get; set; }
        public decimal LuongCoBan { get; set; }

        /// <summary>Số ca đã điểm danh (TrangThai = 2) trong tháng</summary>
        public int SoCaDaDiemDanh { get; set; }

        /// <summary>Tổng hoa hồng từ các ChitietLichdat thuộc hóa đơn đã thanh toán</summary>
        public decimal TongHoaHong { get; set; }
    }
}

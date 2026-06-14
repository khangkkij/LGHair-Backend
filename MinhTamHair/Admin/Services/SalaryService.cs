using Microsoft.EntityFrameworkCore;
using MinhTamHair.Admin.Models;
using MinhTamHair.Models;

namespace MinhTamHair.Admin.Services
{
    // =====================================================
    // IMPLEMENTATION - TÍNH LƯƠNG ĐỘNG REAL-TIME
    // =====================================================
    // NGUYÊN TẮC QUAN TRỌNG:
    //   ✅ TUYỆT ĐỐI KHÔNG tạo bảng mới, không lưu kết quả vào DB
    //   ✅ Mọi số liệu đều truy vấn TRỰC TIẾP từ bảng hiện có và tính toán runtime
    //   ✅ Dùng LINQ .Select() để PROJECT thẳng xuống SQL → tránh lỗi N+1 Query
    //   ✅ Mọi JOIN chỉ chạy 1 lần duy nhất per API call (batch query)
    //
    // CÁC BẢNG ĐƯỢC SỬ DỤNG (KHÔNG THAY ĐỔI CẤU TRÚC):
    //   - Nhanvien         → Danh sách nhân viên, thông tin cơ bản
    //   - Loainhanvien     → Loại nhân viên, lương cơ bản (Loainhanvien.Luong)
    //   - Lichlamviec      → Bảng chấm công (TrangThai=2 = đã điểm danh)
    //   - HoaDon           → Hóa đơn dịch vụ (link tới Lichdat)
    //   - Lichdat          → Lịch đặt dịch vụ (TrangThai=2/3 = đã hoàn thành)
    //   - ChitietLichdat   → Chi tiết từng dịch vụ + hoa hồng (TienHoaHongDv)
    // =====================================================

    public class SalaryService : ISalaryService
    {
        private readonly MinhTamHairContext _context;

        // Số ngày công quy định trong 1 tháng (tiêu chuẩn salon)
        // Đây là mẫu số trong công thức: Lương/Ca = LuongCoBan / SO_NGAY_CONG_QUY_DINH
        // Salon thường tính 26 ngày (tính luôn thứ 7, trừ Chủ nhật)
        private const int SO_NGAY_CONG_QUY_DINH = 26;

        public SalaryService(MinhTamHairContext context)
        {
            _context = context;
        }

        // =====================================================
        // API 1: ADMIN - BẢNG LƯƠNG TOÀN BỘ NHÂN VIÊN
        // =====================================================
        public async Task<BangLuongSummaryResponse> GetSalarySummaryAsync(int month, int year)
        {
            // -----------------------------------------------
            // BƯỚC 1: XÁC ĐỊNH KHOẢNG THỜI GIAN CẦN TÍNH
            // -----------------------------------------------
            // Tính ngày đầu và cuối tháng để lọc dữ liệu chính xác
            var ngayDau = new DateTime(year, month, 1);
            var ngayCuoi = ngayDau.AddMonths(1).AddSeconds(-1); // 23:59:59 ngày cuối tháng

            // Tương đương DateOnly cho bảng Lichlamviec (dùng DateOnly)
            var ngayDauDate = DateOnly.FromDateTime(ngayDau);
            var ngayCuoiDate = DateOnly.FromDateTime(ngayCuoi);

            // -----------------------------------------------
            // BƯỚC 2: TRUY VẤN SỐ CA LÀM THỰC TẾ (BATCH QUERY #1)
            // -----------------------------------------------
            // Mục tiêu: Đếm số ca đã điểm danh thành công của TỪNG nhân viên trong tháng
            // JOIN: Lichlamviec → GROUP BY MaNv → COUNT
            //
            // Trạng thái Lichlamviec:
            //   0 = Chưa xếp ca
            //   1 = Chờ điểm danh
            //   2 = Đã điểm danh (Có mặt) ← ĐÂY LÀ CA ĐƯỢC TÍNH LƯƠNG
            //   3 = Rời ca sớm (vẫn tính - đã có mặt)
            //   5 = Vắng mặt (KHÔNG tính)
            //   6 = Muộn (vẫn tính - đã có mặt)
            //
            // Điều kiện TrangThai: Tính lương cho ca có mặt (2) + rời sớm (3) + muộn (6)
            // Nhưng theo thực tế, chỉ TrangThai = 2 (Đã điểm danh) là ca hoàn thành chắc chắn
            var soCaLamDict = await _context.Lichlamviecs
                .Where(l =>
                    l.NgayLamViec >= ngayDauDate &&     // Từ đầu tháng
                    l.NgayLamViec <= ngayCuoiDate &&    // Đến cuối tháng
                    l.TrangThai == 2 &&                 // CHỈ tính ca đã điểm danh thành công
                    l.MaNv != null)                     // Có mã NV hợp lệ
                .GroupBy(l => l.MaNv!)                  // Nhóm theo từng NV
                .Select(g => new
                {
                    MaNv = g.Key,
                    SoCa = g.Count()                    // Đếm số ca hoàn thành
                })
                .ToDictionaryAsync(x => x.MaNv, x => x.SoCa);
                // Kết quả: { "NV001": 22, "NV002": 18, ... }
                // Sau đó dùng Dictionary để lookup O(1) thay vì N+1 query

            // -----------------------------------------------
            // BƯỚC 3: TRUY VẤN HOA HỒNG DỊCH VỤ (BATCH QUERY #2)
            // -----------------------------------------------
            // Mục tiêu: Tính tổng hoa hồng của từng nhân viên từ các DV đã thực hiện
            //           trong tháng và thuộc hóa đơn ĐÃ THANH TOÁN THÀNH CÔNG
            //
            // LUỒNG DỮ LIỆU:
            //   ChitietLichdat (ct) 
            //     → ct.MaLdNavigation (Lichdat ld) → ld.TrangThai = 2 (hoàn thành) hoặc 3
            //     → HoaDon (hd) phải tồn tại (lịch đặt đã được lập hóa đơn)
            //     → Ngày của Lichdat phải nằm trong tháng cần tính
            //
            // TRẠNG THÁI LICHDAT:
            //   0 = Đã hủy
            //   1 = Chờ xác nhận
            //   2 = Đã hoàn thành / Đã thanh toán ← TÍNH HOA HỒNG
            //   3 = Đang thực hiện (có thể tính tùy business rule)
            //
            // ct.TienHoaHongDv: Đây là số tiền hoa hồng đã được tính sẵn tại thời điểm
            //                   booking (= GiaLucDat × PhanTramHoaHong / 100)
            //                   Ta dùng giá trị này trực tiếp, không cần tính lại %
            var hoaHongDict = await _context.ChitietLichdats
                .Where(ct =>
                    ct.MaNv != null &&                              // Chi tiết phải gán cho NV
                    ct.TienHoaHongDv != null &&                     // Phải có giá trị hoa hồng
                    ct.MaLdNavigation != null &&                    // Phải có lịch đặt
                    ct.MaLdNavigation.TrangThai == 2 &&             // Lịch đặt đã HOÀN THÀNH
                    ct.MaLdNavigation.NgayHen >= ngayDauDate &&     // Trong tháng cần tính
                    ct.MaLdNavigation.NgayHen <= ngayCuoiDate &&

                    // QUAN TRỌNG: Chỉ tính hoa hồng từ lịch đặt có hóa đơn thực tế
                    // Kiểm tra qua navigation: Lịch đặt → HoaDon
                    _context.HoaDons.Any(hd =>
                        hd.MaLd == ct.MaLd &&                       // HĐ thuộc cùng lịch đặt
                        hd.TrangThai == 1))                          // HĐ đã thanh toán (TrangThai=1 trong HoaDon)
                .GroupBy(ct => ct.MaNv!)                             // Nhóm theo NV thực hiện
                .Select(g => new
                {
                    MaNv = g.Key,
                    // SUM tất cả hoa hồng dịch vụ của NV này trong tháng
                    TongHoaHong = g.Sum(ct => ct.TienHoaHongDv ?? 0)
                })
                .ToDictionaryAsync(x => x.MaNv, x => x.TongHoaHong);
                // Kết quả: { "NV001": 500000m, "NV002": 320000m, ... }

            // -----------------------------------------------
            // BƯỚC 4: TRUY VẤN DANH SÁCH NHÂN VIÊN + LƯƠNG CƠ BẢN (BATCH QUERY #3)
            // -----------------------------------------------
            // Mục tiêu: Lấy info cơ bản + lương tháng chuẩn từ Loainhanvien
            // Chỉ lấy NV đang hoạt động (TrangThai = 1)
            var danhSachNhanVien = await _context.Nhanviens
                .Where(nv => nv.TrangThai == 1)     // Chỉ NV đang làm việc
                .Select(nv => new
                {
                    nv.MaNv,
                    HoTenNv = nv.HoTenNv ?? "Chưa cập nhật",
                    nv.HinhAnh,
                    // Lương cơ bản lấy từ bảng Loainhanvien - đây là lương THÁNG chuẩn
                    // Nếu chưa gán loại NV thì lương = 0 (sẽ hiện cảnh báo ở Frontend)
                    LuongCoBan = nv.MaLoaiNvNavigation != null
                        ? (nv.MaLoaiNvNavigation.Luong ?? 0)
                        : 0,
                    TenLoaiNv = nv.MaLoaiNvNavigation != null
                        ? (nv.MaLoaiNvNavigation.TenLoai ?? "Chưa phân loại")
                        : "Chưa phân loại"
                })
                .OrderBy(nv => nv.HoTenNv)         // Sắp xếp A-Z cho dễ đọc
                .ToListAsync();

            // -----------------------------------------------
            // BƯỚC 5: GHÉP DỮ LIỆU VÀ TÍNH TOÁN (IN-MEMORY)
            // -----------------------------------------------
            // Lúc này ta đã có đủ 3 nguồn dữ liệu trong bộ nhớ,
            // thực hiện phép tính KHÔNG gọi thêm query nào nữa → tránh N+1
            var ketQua = danhSachNhanVien.Select(nv =>
            {
                // Lookup O(1) từ Dictionary đã chuẩn bị
                var soCa = soCaLamDict.GetValueOrDefault(nv.MaNv, 0);
                var hoaHong = hoaHongDict.GetValueOrDefault(nv.MaNv, 0);

                // ============================================================
                // CÔNG THỨC TÍNH LƯƠNG THEO CA LÀM VIỆC THỰC TẾ:
                //
                //   LuongTheoNgayCong = (LuongCoBan / SoNgayCongQuyDinh) × SoCaLamThucTe
                //
                // Ý nghĩa: 
                //   - LuongCoBan: Lương tháng chuẩn theo loại NV (ví dụ: 8,000,000đ)
                //   - SoNgayCongQuyDinh: Số ca chuẩn/tháng (ví dụ: 26 ca)
                //   - Lương/ca: 8,000,000 / 26 ≈ 307,692đ
                //   - Nếu NV làm 22 ca: 307,692 × 22 = 6,769,230đ
                //
                // TỔNG THU NHẬP = LuongTheoNgayCong + TongHoaHong
                // ============================================================
                var luongMoiCa = SO_NGAY_CONG_QUY_DINH > 0
                    ? nv.LuongCoBan / SO_NGAY_CONG_QUY_DINH
                    : 0;

                var luongTheoNgayCong = Math.Round(luongMoiCa * soCa, 0); // Làm tròn đơn vị đồng
                var tongThuNhap = luongTheoNgayCong + hoaHong;

                return new NhanVienLuongDto
                {
                    MaNv = nv.MaNv,
                    HoTenNv = nv.HoTenNv,
                    TenLoaiNv = nv.TenLoaiNv,
                    HinhAnh = nv.HinhAnh,
                    LuongCoBan = nv.LuongCoBan,
                    SoNgayCongQuyDinh = SO_NGAY_CONG_QUY_DINH,
                    SoCaLamThucTe = soCa,
                    TongHoaHong = hoaHong,
                    LuongTheoNgayCong = luongTheoNgayCong,
                    TongThuNhap = tongThuNhap,
                    Thang = month,
                    Nam = year
                };
            }).ToList();

            // Tổng quỹ lương = sum tổng thu nhập của tất cả NV
            var tongQuyLuong = ketQua.Sum(x => x.TongThuNhap);

            return new BangLuongSummaryResponse
            {
                Thang = month,
                Nam = year,
                TongNhanVien = ketQua.Count,
                TongQuyLuong = tongQuyLuong,
                DanhSachLuong = ketQua
            };
        }

        // =====================================================
        // API 2: NHÂN VIÊN - PHIẾU LƯƠNG CÁ NHÂN
        // =====================================================
        public async Task<NhanVienLuongDto?> GetMySalaryAsync(string employeeId, int month, int year)
        {
            // -----------------------------------------------
            // BƯỚC 1: KIỂM TRA NV TỒN TẠI + LẤY THÔNG TIN CƠ BẢN
            // -----------------------------------------------
            var nhanVien = await _context.Nhanviens
                .Where(nv => nv.MaNv == employeeId && nv.TrangThai == 1)
                .Select(nv => new
                {
                    nv.MaNv,
                    HoTenNv = nv.HoTenNv ?? "Chưa cập nhật",
                    nv.HinhAnh,
                    LuongCoBan = nv.MaLoaiNvNavigation != null
                        ? (nv.MaLoaiNvNavigation.Luong ?? 0)
                        : 0,
                    TenLoaiNv = nv.MaLoaiNvNavigation != null
                        ? (nv.MaLoaiNvNavigation.TenLoai ?? "Chưa phân loại")
                        : "Chưa phân loại"
                })
                .FirstOrDefaultAsync();

            // NV không tồn tại hoặc đã nghỉ việc
            if (nhanVien == null) return null;

            // -----------------------------------------------
            // BƯỚC 2: XÁC ĐỊNH KHOẢNG THỜI GIAN
            // -----------------------------------------------
            var ngayDauDate = new DateOnly(year, month, 1);
            var ngayCuoiDate = ngayDauDate.AddMonths(1).AddDays(-1);

            // -----------------------------------------------
            // BƯỚC 3: ĐẾM SỐ CA ĐÃ LÀM CỦA NV NÀY
            // -----------------------------------------------
            // Chỉ đếm của 1 NV cụ thể → truy vấn nhanh hơn API Admin
            var soCaLam = await _context.Lichlamviecs
                .CountAsync(l =>
                    l.MaNv == employeeId &&
                    l.NgayLamViec >= ngayDauDate &&
                    l.NgayLamViec <= ngayCuoiDate &&
                    l.TrangThai == 2); // Chỉ tính ca đã điểm danh

            // -----------------------------------------------
            // BƯỚC 4: TÍNH TỔNG HOA HỒNG CỦA NV NÀY
            // -----------------------------------------------
            // Logic giống API Admin nhưng lọc theo 1 NV cụ thể
            var tongHoaHong = await _context.ChitietLichdats
                .Where(ct =>
                    ct.MaNv == employeeId &&                         // Chỉ DV của NV này
                    ct.TienHoaHongDv != null &&
                    ct.MaLdNavigation != null &&
                    ct.MaLdNavigation.TrangThai == 2 &&              // Lịch đặt hoàn thành
                    ct.MaLdNavigation.NgayHen >= ngayDauDate &&
                    ct.MaLdNavigation.NgayHen <= ngayCuoiDate &&
                    _context.HoaDons.Any(hd =>
                        hd.MaLd == ct.MaLd &&
                        hd.TrangThai == 1))                          // HĐ đã thanh toán
                .SumAsync(ct => ct.TienHoaHongDv ?? 0);

            // -----------------------------------------------
            // BƯỚC 5: TÍNH TOÁN VÀ TRẢ VỀ KẾT QUẢ
            // -----------------------------------------------
            var luongMoiCa = SO_NGAY_CONG_QUY_DINH > 0
                ? nhanVien.LuongCoBan / SO_NGAY_CONG_QUY_DINH
                : 0;

            var luongTheoNgayCong = Math.Round(luongMoiCa * soCaLam, 0);
            var tongThuNhap = luongTheoNgayCong + tongHoaHong;

            return new NhanVienLuongDto
            {
                MaNv = nhanVien.MaNv,
                HoTenNv = nhanVien.HoTenNv,
                TenLoaiNv = nhanVien.TenLoaiNv,
                HinhAnh = nhanVien.HinhAnh,
                LuongCoBan = nhanVien.LuongCoBan,
                SoNgayCongQuyDinh = SO_NGAY_CONG_QUY_DINH,
                SoCaLamThucTe = soCaLam,
                TongHoaHong = tongHoaHong,
                LuongTheoNgayCong = luongTheoNgayCong,
                TongThuNhap = tongThuNhap,
                Thang = month,
                Nam = year
            };
        }
    }
}

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
        private int soCa;

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
            // Lấy danh sách ca làm trong tháng để phân loại và thống kê
            var caLamRaw = await _context.Lichlamviecs
                .Where(l =>
                    l.NgayLamViec >= ngayDauDate &&     // Từ đầu tháng
                    l.NgayLamViec <= ngayCuoiDate &&    // Đến cuối tháng
                    l.MaNv != null)                     // Có mã NV hợp lệ
                .Select(l => new { l.MaNv, l.CaLam, l.TrangThai })  // Lấy các cột cần thiết
                .ToListAsync();

            // Phân loại Fulltime / Part-time và tính công quy đổi cùng các loại ca nghỉ (in-memory)
            var soCaLamDict = caLamRaw
                .GroupBy(x => x.MaNv!)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        SoCaFT         = g.Count(x => x.TrangThai == 2 && !(x.CaLam ?? "").Contains("Part-time")),
                        SoCaPT         = g.Count(x => x.TrangThai == 2 &&  (x.CaLam ?? "").Contains("Part-time")),
                        SoCaQuyDoi     = (decimal)g.Count(x => x.TrangThai == 2 && !(x.CaLam ?? "").Contains("Part-time")) * 1.0m
                                       + (decimal)g.Count(x => x.TrangThai == 2 &&  (x.CaLam ?? "").Contains("Part-time")) * 0.5m,
                        SoCaNghiPhep   = g.Count(x => x.TrangThai == 4 || x.CaLam == "Nghỉ phép"),
                        SoCaNghiLe     = g.Count(x => x.TrangThai == 7 || x.CaLam == "Nghỉ lễ"),
                        SoCaVangMat    = g.Count(x => x.TrangThai == 5)
                    });
                // Kết quả: { "NV001": { SoCaFT=20, SoCaPT=4, SoCaQuyDoi=22.0 }, ... }

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

            // -----------------------------------------------
            // BƯỚC 4: TRUY VẤN DANH SÁCH NHÂN VIÊN + LƯƠNG CƠ BẢN (BATCH QUERY #3)
            // -----------------------------------------------
            var danhSachNhanVien = await _context.Nhanviens
                .Where(nv => nv.TrangThai == 1)
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
                        : "Chưa phân loại",
                    // Cờ phân biệt THỢ CHÍNH (DuocDatLich=true) vs THỢ PHỤ
                    LaThoChinh = nv.MaLoaiNvNavigation != null
                        && (nv.MaLoaiNvNavigation.DuocDatLich ?? false)
                })
                .OrderBy(nv => nv.HoTenNv)
                .ToListAsync();

            // -----------------------------------------------
            // BƯỚC 4b: TRUY VẤN HOA HỒNG SẢN PHẨM (BATCH QUERY #4)
            // -----------------------------------------------
            // CHỈ THỢ CHÍNH (DuocDatLich=true) mới nhận hoa hồng SP
            // Nguồn: ChitietHoadonSp.TienHoaHongSp → HoaDon đã thanh toán (TrangThai=1)
            var hoaHongSpDict = await _context.ChitietHoadonSps
                .Where(ct =>
                    ct.MaNv != null &&
                    ct.TienHoaHongSp != null &&
                    ct.MaHdNavigation != null &&
                    ct.MaHdNavigation.TrangThai == 1 &&
                    ct.MaHdNavigation.NgayTao >= ngayDau &&
                    ct.MaHdNavigation.NgayTao <= ngayCuoi)
                .GroupBy(ct => ct.MaNv!)
                .Select(g => new
                {
                    MaNv = g.Key,
                    TongHoaHongSp = g.Sum(ct => ct.TienHoaHongSp ?? 0)
                })
                .ToDictionaryAsync(x => x.MaNv, x => x.TongHoaHongSp);
                // Kết quả: { "NV001": 200000m, ... } – chỉ thợ chính có giá trị > 0


            // -----------------------------------------------
            // BƯỚC 5: GHÉP DỮ LIỆU VÀ TÍNH TOÁN (IN-MEMORY)
            // -----------------------------------------------
            // Lúc này ta đã có đủ 3 nguồn dữ liệu trong bộ nhớ,
            // thực hiện phép tính KHÔNG gọi thêm query nào nữa → tránh N+1
            var ketQua = danhSachNhanVien.Select(nv =>
            {
                // Lookup O(1) từ Dictionary đã chuẩn bị
                var caData     = soCaLamDict.GetValueOrDefault(nv.MaNv);
                var soCaFT     = caData?.SoCaFT     ?? 0;
                var soCaPT     = caData?.SoCaPT     ?? 0;
                var soCaQuyDoi = caData?.SoCaQuyDoi ?? 0m;

                // HOA HỒNG DỊCH VỤ: cả thợ chính lẫn thợ phụ đều nhận
                var hoaHongDv  = hoaHongDict.GetValueOrDefault(nv.MaNv, 0);

                // HOA HỒNG SẢN PHẨM: chỉ thợ chính (DuocDatLich=true) mới nhận
                var hoaHongSp  = nv.LaThoChinh
                    ? hoaHongSpDict.GetValueOrDefault(nv.MaNv, 0)
                    : 0m;

 
                var luongMoiCa = SO_NGAY_CONG_QUY_DINH > 0
                    ? nv.LuongCoBan / SO_NGAY_CONG_QUY_DINH
                    : 0;

                var luongTheoNgayCong = Math.Round(luongMoiCa * soCaQuyDoi, 0); // Tính theo công QUY ĐỔI
                var tongThuNhap = luongTheoNgayCong + hoaHongDv + hoaHongSp;

                return new NhanVienLuongDto
                {
                    MaNv = nv.MaNv,
                    HoTenNv = nv.HoTenNv,
                    TenLoaiNv = nv.TenLoaiNv,
                    HinhAnh = nv.HinhAnh,
                    LuongCoBan = nv.LuongCoBan,
                    LaThoChinh = nv.LaThoChinh,
                    SoNgayCongQuyDinh = SO_NGAY_CONG_QUY_DINH,
                    SoCaLamThucTe  = soCaFT + soCaPT,
                    SoCaFulltime   = soCaFT,
                    SoCaParttime   = soCaPT,
                    SoCaChuanQuyDoi = soCaQuyDoi,
                    SoCaNghiPhep   = caData?.SoCaNghiPhep ?? 0,
                    SoCaNghiLe     = caData?.SoCaNghiLe ?? 0,
                    SoCaVangMat    = caData?.SoCaVangMat ?? 0,
                    TongHoaHongDv = hoaHongDv,
                    TongHoaHongSp = hoaHongSp,
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
                        : "Chưa phân loại",
                    LaThoChinh = nv.MaLoaiNvNavigation != null
                        && (nv.MaLoaiNvNavigation.DuocDatLich ?? false)
                })
                .FirstOrDefaultAsync();

            if (nhanVien == null) return null;

            
            var ngayDau = new DateTime(year, month, 1, 0, 0, 0);
            var ngayCuoi = ngayDau.AddMonths(1).AddSeconds(-1);
            var ngayDauDate = DateOnly.FromDateTime(ngayDau);
            var ngayCuoiDate = DateOnly.FromDateTime(ngayCuoi);

         
            var caLamRawMy = await _context.Lichlamviecs
                .Where(l =>
                    l.MaNv == employeeId &&
                    l.NgayLamViec >= ngayDauDate &&
                    l.NgayLamViec <= ngayCuoiDate)
                .Select(l => new { l.CaLam, l.TrangThai })
                .ToListAsync();

            // Part-time: tên ca chứa "Part-time" → 0.5 công; còn lại → 1.0 công
            var soCaFTMy        = caLamRawMy.Count(x => x.TrangThai == 2 && !(x.CaLam ?? "").Contains("Part-time"));
            var soCaPTMy        = caLamRawMy.Count(x => x.TrangThai == 2 &&  (x.CaLam ?? "").Contains("Part-time"));
            var soCaQuyDoiMy    = soCaFTMy * 1.0m + soCaPTMy * 0.5m;
            var soCaNghiPhepMy  = caLamRawMy.Count(x => x.TrangThai == 4 || x.CaLam == "Nghỉ phép");
            var soCaNghiLeMy    = caLamRawMy.Count(x => x.TrangThai == 7 || x.CaLam == "Nghỉ lễ");
            var soCaVangMatMy   = caLamRawMy.Count(x => x.TrangThai == 5);

           
            var tongHoaHongDv = await _context.ChitietLichdats
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

            // 4b. Hoa hồng sản phẩm (chỉ thợ chính có DuocDatLich=true)
            var tongHoaHongSp = 0m;
            if (nhanVien.LaThoChinh)
            {
                tongHoaHongSp = await _context.ChitietHoadonSps
                    .Where(ct =>
                        ct.MaNv == employeeId &&
                        ct.TienHoaHongSp != null &&
                        ct.MaHdNavigation != null &&
                        ct.MaHdNavigation.TrangThai == 1 &&
                        ct.MaHdNavigation.NgayTao >= ngayDau &&
                        ct.MaHdNavigation.NgayTao <= ngayCuoi)
                    .SumAsync(ct => ct.TienHoaHongSp ?? 0);
            }

            // -----------------------------------------------
            // BƯỚC 5: TÍNH TOÁN VÀ TRẢ VỀ KẾT QUẢ
            // -----------------------------------------------
            var luongMoiCa = SO_NGAY_CONG_QUY_DINH > 0
                ? nhanVien.LuongCoBan / SO_NGAY_CONG_QUY_DINH
                : 0;

            // Tính theo công QUY ĐỔI (FT=1.0, PT=0.5)
            var luongTheoNgayCong = Math.Round(luongMoiCa * soCaQuyDoiMy, 0);
            var tongThuNhap = luongTheoNgayCong + tongHoaHongDv + tongHoaHongSp;

            return new NhanVienLuongDto
            {
                MaNv = nhanVien.MaNv,
                HoTenNv = nhanVien.HoTenNv,
                TenLoaiNv = nhanVien.TenLoaiNv,
                HinhAnh = nhanVien.HinhAnh,
                LuongCoBan = nhanVien.LuongCoBan,
                LaThoChinh = nhanVien.LaThoChinh,
                SoNgayCongQuyDinh = SO_NGAY_CONG_QUY_DINH,
                SoCaLamThucTe   = soCaFTMy + soCaPTMy,
                SoCaFulltime    = soCaFTMy,
                SoCaParttime    = soCaPTMy,
                SoCaChuanQuyDoi = soCaQuyDoiMy,
                SoCaNghiPhep    = soCaNghiPhepMy,
                SoCaNghiLe      = soCaNghiLeMy,
                SoCaVangMat     = soCaVangMatMy,
                TongHoaHongDv = tongHoaHongDv,
                TongHoaHongSp = tongHoaHongSp,
                LuongTheoNgayCong = luongTheoNgayCong,
                TongThuNhap = tongThuNhap,
                Thang = month,
                Nam = year
            };
        }
    }
}

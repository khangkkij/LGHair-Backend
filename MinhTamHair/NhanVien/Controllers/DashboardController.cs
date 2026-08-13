using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers
{
    // Cấu hình đường dẫn gốc cho Controller. VD: DomainCuaBan.com/api/ThongKeThuNhap
    [Route("api/[controller]")]
    // Đánh dấu class này là một API Controller để ASP.NET Core tự động xử lý các phản hồi HTTP
    [ApiController]
    public class ThongKeThuNhapController : ControllerBase
    {
        // Khai báo biến _context (chỉ đọc) để thao tác với Database
        private readonly MinhTamHairContext _context;

        // Constructor: Nhận _context được tiêm (inject) tự động từ hệ thống (Dependency Injection)
        public ThongKeThuNhapController(MinhTamHairContext context)
        {
            _context = context;
        }

        // ====================================================================
        // API 1: LẤY THU NHẬP THÁNG HIỆN TẠI
        // Phương thức: GET
        // Đường dẫn: /api/ThongKeThuNhap/thang-hien-tai/{maNV}
        // ====================================================================
        [HttpGet("thang-hien-tai/{maNV}")]
        public async Task<IActionResult> GetCurrentMonthIncome(string maNV)
        {
            // Lấy ra ngày giờ hiện tại của hệ thống máy chủ
            var ngayHienTai = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();

            // Gọi hàm xử lý logic dùng chung ở bên dưới, truyền vào tháng và năm hiện tại
            var result = await TinhToanThuNhapHieuNangCao(maNV, ngayHienTai.Month, ngayHienTai.Year);

            // Kiểm tra: Nếu hàm trả về null (nghĩa là không tìm thấy nhân viên trong DB)
            if (result == null)
                return NotFound(new { Message = $"Không tìm thấy nhân viên có mã: {maNV}" }); // Trả về lỗi 404

            // Nếu tìm thấy và tính toán thành công, trả về dữ liệu (Mã 200 OK)
            return Ok(result);
        }

        // ====================================================================
        // API 2: LỌC THU NHẬP THEO THÁNG & NĂM BẤT KỲ DO NGƯỜI DÙNG CHỌN
        // Phương thức: GET
        // Đường dẫn: /api/ThongKeThuNhap/loc-theo-thang/{maNV}?month=...&year=...
        // ====================================================================
        [HttpGet("loc-theo-thang/{maNV}")]
        public async Task<IActionResult> GetIncomeByChosenMonth(string maNV, [FromQuery] int month, [FromQuery] int year)
        {
            // Xác thực dữ liệu đầu vào: Tháng phải từ 1 đến 12, năm phải từ 2000 trở đi
            if (month < 1 || month > 12 || year < 2000)
            {
                // Trả về lỗi 400 (Bad Request) nếu client gửi sai ngày tháng
                return BadRequest(new { Message = "Tháng hoặc năm chọn không hợp lệ." });
            }

            // Gọi hàm xử lý logic dùng chung, truyền vào tháng/năm lấy từ URL
            var result = await TinhToanThuNhapHieuNangCao(maNV, month, year);

            // Kiểm tra tồn tại nhân viên
            if (result == null)
                return NotFound(new { Message = $"Không tìm thấy nhân viên có mã: {maNV}" });

            // Trả về kết quả thành công
            return Ok(result);
        }

        // ====================================================================
        // HÀM XỬ LÝ LOGIC CHÍNH (PRIVATE)
        // Đây là "trái tim" của Controller, 2 API trên chỉ làm nhiệm vụ tiếp nhận rồi ném xuống đây xử lý
        // ====================================================================
        private async Task<IncomeDashboardDto> TinhToanThuNhapHieuNangCao(string maNV, int month, int year)
        {
            // --- BƯỚC 1: LẤY THÔNG TIN CƠ BẢN CỦA NHÂN VIÊN ---

            // Truy vấn bảng Nhanviens, dùng .Include (giống JOIN trong SQL) để lấy thêm Lương và Tên chức vụ
            var nhanVien = await _context.Nhanviens
                .Include(n => n.MaLoaiNvNavigation) // Lấy thông tin loại nhân viên
                .Include(n => n.MaRoleNavigation)   // Lấy thông tin quyền/chức vụ
                .FirstOrDefaultAsync(n => n.MaNv == maNV); // Tìm người đầu tiên khớp mã

            // Chốt chặn an toàn: Trả về null nếu mã NV không tồn tại
            if (nhanVien == null) return null;

            // Lấy mức lương cơ bản từ bảng LoaiNv. Nếu dữ liệu bị Null thì cho mặc định bằng 0
            decimal luongCoBan = (decimal?)(nhanVien.MaLoaiNvNavigation?.Luong) ?? 0;


            // --- BƯỚC 2: THIẾT LẬP CÁC MỐC THỜI GIAN ĐỂ QUÉT DATABASE ---

            // Xác định ngày đầu tiên của tháng cần tính (Ví dụ: 01/05/2026 00:00:00)
            var ngayBatDauDT = new DateTime(year, month, 1);

            // Xác định ngày cuối cùng của tháng (dùng DaysInMonth để tự biết là ngày 28, 30 hay 31)
            var ngayKetThucDT = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            // Tối ưu hóa: Nếu người dùng đang tra cứu trúng tháng hiện tại, 
            // thì mốc kết thúc chỉ cần lấy tới thời điểm hiện tại (MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()) thay vì chạy tới cuối tháng
            if (year == MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Year && month == MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Month)
            {
                ngayKetThucDT = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            }

            // Ép kiểu từ DateTime sang DateOnly (cắt bỏ Giờ/Phút/Giây) 
            // Bước này BẮT BUỘC vì cột trong Database của Entity Framework đang mang kiểu DateOnly
            var ngayBatDauDO = DateOnly.FromDateTime(ngayBatDauDT);
            var ngayKetThucDO = DateOnly.FromDateTime(ngayKetThucDT);


            // --- BƯỚC 3: GỌI XUỐNG DATABASE ĐỂ LẤY DỮ LIỆU ---
            // (Lưu ý: Chúng ta chỉ Select những cột cần thiết để tiết kiệm RAM máy chủ)

            // 3.1: Lấy danh sách lịch làm việc (Dùng biến DateOnly DO)
            var lichLamViecRaw = await _context.Lichlamviecs
                .Where(l => l.MaNv == maNV && l.NgayLamViec >= ngayBatDauDO && l.NgayLamViec <= ngayKetThucDO)
                .ToListAsync();

            // 3.2: Lấy dữ liệu Hoa hồng từ Hóa đơn SP (NgayTao là DateTime nên dùng biến DT)
            var hoaHongSPRaw = await _context.ChitietHoadonSps
                .Include(ct => ct.MaHdNavigation) // Join qua bảng Hóa đơn cha
                .Where(ct => ct.MaNv == maNV
                             && ct.MaHdNavigation != null // Loại bỏ dữ liệu rác không có hóa đơn cha
                             && ct.MaHdNavigation.NgayTao >= ngayBatDauDT
                             && ct.MaHdNavigation.NgayTao <= ngayKetThucDT)
                .Select(ct => new { TrangThai = ct.MaHdNavigation.TrangThai, ct.TienHoaHongSp }) // Lấy 2 cột
                .ToListAsync();

            // 3.3: Lấy dữ liệu Hoa hồng từ Lịch đặt Dịch vụ (NgayHen là DateOnly nên dùng biến DO)
            var hoaHongDVRaw = await _context.ChitietLichdats
                .Include(ct => ct.MaLdNavigation) // Join qua bảng Lịch đặt cha
                .Where(ct => ct.MaNv == maNV
                             && ct.MaLdNavigation != null // Loại bỏ dữ liệu rác
                             && ct.MaLdNavigation.NgayHen >= ngayBatDauDO
                             && ct.MaLdNavigation.NgayHen <= ngayKetThucDO)
                .Select(ct => new { TrangThai = ct.MaLdNavigation.TrangThai, ct.TienHoaHongDv }) // Lấy 2 cột
                .ToListAsync();


            // --- BƯỚC 4: TÍNH TOÁN CÁC CON SỐ TRÊN RAM ---

            // Đếm tổng số ngày nhân viên có đi làm (Trạng thái == 1 hoặc == 3)
            int soNgayDiLam = lichLamViecRaw
                .Count(l => Convert.ToInt32(l.TrangThai) == 1 || Convert.ToInt32(l.TrangThai) == 3);

            // Tìm ra tháng đang xét có tổng cộng bao nhiêu ngày
            int tongSoNgayTrongThang = DateTime.DaysInMonth(year, month);

            // Số ngày nghỉ = Tổng ngày trong tháng - Số ngày đi làm
            int soNgayNghi = tongSoNgayTrongThang - soNgayDiLam;

            // Tính lương chuẩn 1 ngày (Lấy lương cứng chia cho tổng số ngày thực tế của tháng đó)
            decimal luongMotNgay = luongCoBan / tongSoNgayTrongThang;

            // Lương cứng thực lãnh = Lương 1 ngày * Số ngày đi làm (Math.Round để làm tròn bỏ số thập phân)
            decimal luongThucTeTheoNgay = Math.Round(soNgayDiLam * luongMotNgay, 0);

            // Tính tổng tiền Hoa hồng Sản phẩm (Chỉ cộng dồn những hóa đơn có Trạng thái == 1: Đã thanh toán)
            // (?? 0) giúp tránh lỗi khi giá trị trong DB bị rỗng (Null)
            decimal tongHoaHongSP = hoaHongSPRaw
                .Where(h => Convert.ToInt32(h.TrangThai) == 1)
                .Sum(h => h.TienHoaHongSp ?? 0);

            // Tính tổng tiền Hoa hồng Dịch vụ (Chỉ cộng dồn những lịch đặt có Trạng thái == 3: Hoàn thành)
            decimal tongHoaHongDV = hoaHongDVRaw
                .Where(h => Convert.ToInt32(h.TrangThai) == 3)
                .Sum(h => h.TienHoaHongDv ?? 0);

            // TỔNG THU NHẬP = Lương cứng + Hoa hồng SP + Hoa hồng DV
            decimal tongThuNhapThang = luongThucTeTheoNgay + tongHoaHongSP + tongHoaHongDV;


            // --- BƯỚC 5: ĐÓNG GÓI DỮ LIỆU CHUẨN BỊ GỬI VỀ FRONTEND ---

            // Khởi tạo Object DTO và gán các giá trị đã tính toán vào
            int xepHangTrongThang = await TinhXepHangNhanVienTrongThang(maNV, month, year);
            return new IncomeDashboardDto
            {

                MaNV = nhanVien.MaNv,
                HoTenNV = nhanVien.HoTenNv,
                ChucVu = nhanVien.MaRoleNavigation?.TenChucVu ?? "Nhân viên", // Nếu không có chức vụ thì hiện "Nhân viên"
                LuongCoBan = luongCoBan,
                XepHangTrongThang = xepHangTrongThang,

                ThangHienTai = new ChiTietThuNhapThangDto
                {
                    ThangNam = $"{month:D2}/{year}", // Format chuỗi hiển thị gọn gàng (VD: "06/2026")
                    SoNgayDiLam = soNgayDiLam,
                    SoNgayNghi = soNgayNghi,
                    LuongThucTeTheoNgay = luongThucTeTheoNgay,
                    HoaHongSanPham = Math.Round(tongHoaHongSP, 0),
                    HoaHongDichVu = Math.Round(tongHoaHongDV, 0),
                    TongThuNhap = Math.Round(tongThuNhapThang, 0),

                }
            };
        }
        private async Task<int> TinhXepHangNhanVienTrongThang(string maNVCanTim, int month, int year)
        {
            // Xác định ngày đầu tháng và cuối tháng
            var ngayBatDauDT = new DateTime(year, month, 1);
            var ngayKetThucDT = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            // Nếu là tháng hiện tại thì chỉ tính tới thời điểm hiện tại
            if (year == MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Year && month == MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Month)
            {
                ngayKetThucDT = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            }

            var ngayBatDauDO = DateOnly.FromDateTime(ngayBatDauDT);
            var ngayKetThucDO = DateOnly.FromDateTime(ngayKetThucDT);

            // Lấy toàn bộ nhân viên còn hoạt động để đem ra xếp hạng
            var danhSachNhanVien = await _context.Nhanviens
                .Include(nv => nv.MaLoaiNvNavigation)
                .Where(nv => nv.TrangThai == 1)
                .ToListAsync();

            var bangXepHang = new List<(string MaNV, decimal TongThuNhap)>();

            foreach (var nv in danhSachNhanVien)
            {
                decimal luongCoBan = (decimal?)(nv.MaLoaiNvNavigation?.Luong) ?? 0;

                // Lấy lịch làm việc của nhân viên trong tháng
                var lichLamViecRaw = await _context.Lichlamviecs
                    .Where(l => l.MaNv == nv.MaNv
                             && l.NgayLamViec >= ngayBatDauDO
                             && l.NgayLamViec <= ngayKetThucDO)
                    .ToListAsync();

                // Đếm số ngày đi làm
                int soNgayDiLam = lichLamViecRaw
                    .Count(l => Convert.ToInt32(l.TrangThai) == 1 || Convert.ToInt32(l.TrangThai) == 3);

                int tongSoNgayTrongThang = DateTime.DaysInMonth(year, month);

                decimal luongMotNgay = luongCoBan / tongSoNgayTrongThang;
                decimal luongThucTeTheoNgay = Math.Round(soNgayDiLam * luongMotNgay, 0);

                // Hoa hồng sản phẩm
                var hoaHongSPRaw = await _context.ChitietHoadonSps
                    .Include(ct => ct.MaHdNavigation)
                    .Where(ct => ct.MaNv == nv.MaNv
                             && ct.MaHdNavigation != null
                             && ct.MaHdNavigation.NgayTao >= ngayBatDauDT
                             && ct.MaHdNavigation.NgayTao <= ngayKetThucDT)
                    .Select(ct => new
                     {
                            TrangThai = ct.MaHdNavigation.TrangThai,
                            ct.TienHoaHongSp
                    })
                    .ToListAsync();

                decimal tongHoaHongSP = hoaHongSPRaw
                    .Where(h => Convert.ToInt32(h.TrangThai) == 1)
                    .Sum(h => h.TienHoaHongSp ?? 0);

                // Hoa hồng dịch vụ
                var hoaHongDVRaw = await _context.ChitietLichdats
                    .Include(ct => ct.MaLdNavigation)
                    .Where(ct => ct.MaNv == nv.MaNv
                            && ct.MaLdNavigation != null
                            && ct.MaLdNavigation.NgayHen >= ngayBatDauDO
                            && ct.MaLdNavigation.NgayHen <= ngayKetThucDO)
                    .Select(ct => new
                     {
                            TrangThai = ct.MaLdNavigation.TrangThai,
                            ct.TienHoaHongDv
                    })
                    .ToListAsync();

                decimal tongHoaHongDV = hoaHongDVRaw
                    .Where(h => Convert.ToInt32(h.TrangThai) == 3)
                    .Sum(h => h.TienHoaHongDv ?? 0);

                decimal tongThuNhap = luongThucTeTheoNgay + tongHoaHongSP + tongHoaHongDV;

                bangXepHang.Add((nv.MaNv, Math.Round(tongThuNhap, 0)));
            }

            // Sắp xếp thu nhập giảm dần
            var bangDaXep = bangXepHang
                .OrderByDescending(x => x.TongThuNhap)
                .ToList();

            // Tìm vị trí của nhân viên hiện tại
            var viTri = bangDaXep.FindIndex(x => x.MaNV == maNVCanTim);

            // FindIndex trả về 0 cho hạng 1, nên +1
            return viTri >= 0 ? viTri + 1 : 0;
        }
    }

    #region CÁC LỚP ĐỐI TƯỢNG ĐÓNG GÓI DỮ LIỆU (DTO)
    // DTO (Data Transfer Object) dùng để chứa dữ liệu định dạng JSON gửi về cho Frontend

    // Lớp chứa thông tin tổng quan của Dashboard
    public class IncomeDashboardDto
    {
        public string MaNV { get; set; }
        public string HoTenNV { get; set; }
        public string ChucVu { get; set; }
        public decimal LuongCoBan { get; set; }

        // Vị trí xếp hạng thu nhập của nhân viên này trong tháng đang xem
        public int XepHangTrongThang { get; set; }

        public ChiTietThuNhapThangDto ThangHienTai { get; set; }
    }

    // Lớp chứa chi tiết các khoản tiền của tháng đang xét
    public class ChiTietThuNhapThangDto
    {
        public string ThangNam { get; set; }
        public int SoNgayDiLam { get; set; }
        public int SoNgayNghi { get; set; }
        public decimal LuongThucTeTheoNgay { get; set; }
        public decimal HoaHongSanPham { get; set; }
        public decimal HoaHongDichVu { get; set; }
        public decimal TongThuNhap { get; set; }
    }
    #endregion
    public class TopNhanVienThuNhapDto
    {
        public int XepHang { get; set; }
        public string MaNV { get; set; }
        public string HoTenNV { get; set; }
        public string ChucVu { get; set; }
        public decimal LuongCoBan { get; set; }
        public decimal HoaHongSanPham { get; set; }
        public decimal HoaHongDichVu { get; set; }
        public decimal TongThuNhap { get; set; }
    }
}
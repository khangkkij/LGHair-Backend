using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using MinhTamHair.Services;
using System.Security.Cryptography;
using System.Text;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThanhToanTongController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IQrEmailService _qrEmailService;
        private readonly IServiceScopeFactory _scopeFactory;

        public ThanhToanTongController(MinhTamHairContext context, IConfiguration configuration, IQrEmailService qrEmailService, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _configuration = configuration;
            _qrEmailService = qrEmailService;
            _scopeFactory = scopeFactory;
        }

        // TẢI DANH SÁCH DỊCH VỤ 
        [HttpGet("get-services")]
        public async Task<IActionResult> GetServices()
        {
            try
            {
                var services = await _context.Dichvus
                    .Where(s => s.TrangThai == 1)
                    .Select(s => new
                    {
                        id = s.MaDv,
                        name = s.TenDv,
                        price = s.Gia,
                        originalPrice = s.Gia,
                        duration = s.ThoiGianLam ?? 30,
                        hinhAnh = s.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault(),
                        icon = s.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault()
                    })
                    .ToListAsync();
                var combos = await _context.Combos.Where(c => c.TrangThai == 1)
                    .Select(c => new {
                        id = c.MaCb,
                        name = c.TenCombo,
                        price = c.Gia,
                        originalPrice = c.Gia,
                        duration = c.Combo_DichVus.Sum(cd => cd.DichVu.ThoiGianLam ?? 30),
                        hinhAnh = c.HinhAnh, // Hoặc đường dẫn ảnh nếu có
                        icon = "fas fa-layer-group",
                    }).ToListAsync();

                return Ok(new { success = true, services = services, combos = combos });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi lấy dịch vụ: " + ex.Message });
            }
        }


        //TẢI DANH SÁCH SẢN PHẨM & BIẾN THỂ (CÓ LỌC)
        [HttpGet("get-products")]
        public async Task<IActionResult> GetProducts([FromQuery] string? keyword = "", [FromQuery] string? categoryId = "")
        {
            try
            {
                var query = _context.SanPhams.Where(sp => sp.TrangThai == 1);

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    query = query.Where(sp => sp.TenSp.Contains(keyword));
                }

                if (!string.IsNullOrWhiteSpace(categoryId))
                {
                    query = query.Where(sp => sp.MaDanhMuc == categoryId);
                }

                query = query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1));

                var result = await query.SelectMany(sp => sp.BienTheSanphams
                    .Where(bt => bt.TrangThai == 1)
                    .Select(bt => new
                    {
                        maSp = sp.MaSp,
                        maBienThe = bt.MaBienThe,
                        tenSp = sp.TenSp,
                        tenBienThe = bt.TenBienThe,
                        giaBan = bt.GiaBan,
                        soLuongTon = bt.SoLuongTon,
                        hinhAnhDaiDien = !string.IsNullOrEmpty(bt.HinhAnhDaiDien) ? bt.HinhAnhDaiDien : sp.HinhAnhDaiDien
                    }))
                    .Take(30)
                    .ToListAsync();

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi lấy sản phẩm: " + ex.Message });
            }
        }

        // TẢI DANH SÁCH VOUCHER 
        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal, [FromQuery] string? itemIds)
        {
            await VoucherCartLogic.AutoDisableExpiredVouchersAsync(_context, DateTime.Now);
            var kh = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);

            var now = DateTime.Now;
            var cartIds = string.IsNullOrWhiteSpace(itemIds) ? new List<string>() : itemIds.Split(',').ToList();

            string? maRank = kh?.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                rankCap = rank?.PhanTramGiamGia;
            }

            var publicVouchers = await _context.Vouchers.AsNoTracking()
                .Where(v => (v.TrangThai == null || v.TrangThai == 1)
                         && (!v.NgayBatDau.HasValue || v.NgayBatDau <= now)
                         && (!v.NgayKetThuc.HasValue || v.NgayKetThuc >= now)
                         && (!v.SoLuong.HasValue || (v.SoLuongDaDung ?? 0) < v.SoLuong))
                .ToListAsync();

            var myCodes = kh != null ? kh.MaCodes : new List<Voucher>();
            var allVouchers = myCodes.Concat(publicVouchers)
                .GroupBy(v => v.MaCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var usageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in allVouchers.Select(v => v.MaCode))
            {
                usageMap[c] = await _context.HoaDons.AsNoTracking().CountAsync(h => h.MaKh == maKH && h.MaCode == c);
            }

            var result = new List<object>();
            foreach (var v in allVouchers)
            {
                if (v.TrangThai != null && v.TrangThai != 1) continue;
                if (v.NgayBatDau.HasValue && now < v.NgayBatDau.Value) continue;
                if (v.NgayKetThuc.HasValue && now > v.NgayKetThuc.Value) continue;
                if (v.SoLuong.HasValue && (v.SoLuongDaDung ?? 0) >= v.SoLuong.Value) continue;

                var uses = usageMap.GetValueOrDefault(v.MaCode, 0);
                var err = VoucherCartLogic.ValidateAndCalculate(v, uses, subTotal, cartIds, now, now, maRank, rankCap, out var tienGiam, out _);

                decimal displayDiscount = tienGiam;
                if (displayDiscount == 0)
                {
                    if (v.SoTienGiam.HasValue && v.SoTienGiam > 0)
                        displayDiscount = v.SoTienGiam.Value;
                    else if (v.PhanTramGiam.HasValue && v.PhanTramGiam > 0 && subTotal > 0)
                        displayDiscount = Math.Round(subTotal * v.PhanTramGiam.Value / 100m, 0);
                }

                string desc = v.MoTa ?? "";
                if (v.GiaTriToiThieu.HasValue && v.GiaTriToiThieu > 0 && subTotal < v.GiaTriToiThieu.Value)
                {
                    desc += $" (Đơn tối thiểu {v.GiaTriToiThieu.Value:N0}đ)";
                }

                result.Add(new
                {
                    id = v.MaCode,
                    code = v.MaCode,
                    discount = displayDiscount,
                    desc = desc,
                    expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : "Vĩnh viễn"
                });
            }
            return Ok(result);
        }

        [HttpPost("get-available-stylists")]
        public async Task<IActionResult> GetAvailableStylists([FromBody] ThanhToanCheckStylistRequest request)
        {
            try
            {
                DateOnly ngayHen = DateOnly.Parse(request.NgayHen);
                TimeOnly start = TimeOnly.Parse(request.GioBatDau);
                TimeOnly end = start.AddMinutes(request.TongThoiGian);

                // --- 1. LẤY DANH SÁCH THỢ YÊU THÍCH TỪ DB ---
                var favoriteStylistIds = new List<string>();
                if (!string.IsNullOrEmpty(request.MaKh) && request.MaKh != "GUEST")
                {
                    favoriteStylistIds = await _context.Set<YeuThichNhanVien>()
                        .Where(y => y.MaKH == request.MaKh)
                        .Select(y => y.MaNV.Trim())
                        .ToListAsync();
                }

                // LẤY TẤT CẢ THỢ ĐI LÀM TRONG NGÀY
                var lichLamViecs = await _context.Lichlamviecs
                    .Include(l => l.MaNvNavigation)
                    .ThenInclude(nv => nv.MaLoaiNvNavigation)
                    .Where(l => l.NgayLamViec == ngayHen && l.TrangThai != 0 && l.TrangThai != 5 && l.MaNvNavigation.TrangThai == 1)
                    .ToListAsync();

                // LẤY CHI TIẾT CÁC LỊCH ĐANG BẬN
                var cacLichBan = await _context.ChitietLichdats
                    .Where(ct => ct.MaLdNavigation.NgayHen == ngayHen
                                 && ct.MaLdNavigation.TrangThai != 0
                                 && ct.MaLdNavigation.GioBatDau < end
                                 && ct.MaLdNavigation.GioKetThuc > start)
                    .Select(ct => new {
                        MaNv = ct.MaNv,
                        GioBatDau = ct.MaLdNavigation.GioBatDau,
                        GioKetThuc = ct.MaLdNavigation.GioKetThuc
                    })
                    .ToListAsync();

                var result = new List<object>();

                // XỬ LÝ TỪNG THỢ
                foreach (var lich in lichLamViecs)
                {
                    var nv = lich.MaNvNavigation;
                    bool isAvailable = true;
                    string reason = "";

                    bool dungCaLam = false;
                    string ca = lich.CaLam ?? "";
                    string gioLamThucTe = ca;

                    // Kiểm tra khớp ca
                    if ((ca == "Ca sáng (7h-17h)" || ca == "Ca ngày" || ca == "Ca sáng") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(17, 0)) dungCaLam = true;
                    else if ((ca == "Ca tối (13h-20h)" || ca == "Ca tối") && start >= new TimeOnly(13, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;
                    else if ((ca == "Cả ngày" || ca == "Full time") && start >= new TimeOnly(7, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;
                    else if (ca == "Ca sáng (Part-time)" && start >= new TimeOnly(7, 0) && end <= new TimeOnly(11, 0)) dungCaLam = true;
                    else if (ca == "Ca chiều (Part-time)" && start >= new TimeOnly(13, 0) && end <= new TimeOnly(17, 0)) dungCaLam = true;
                    else if (ca == "Ca tối (Part-time)" && start >= new TimeOnly(16, 0) && end <= new TimeOnly(20, 0)) dungCaLam = true;

                    // Đổi tên ca
                    if (ca == "Ca sáng (7h-17h)" || ca == "Ca ngày" || ca == "Ca sáng") gioLamThucTe = "07:00 - 17:00";
                    else if (ca == "Ca tối (13h-20h)" || ca == "Ca tối") gioLamThucTe = "13:00 - 20:00";
                    else if (ca == "Cả ngày" || ca == "Full time") gioLamThucTe = "07:00 - 20:00";
                    else if (ca == "Ca sáng (Part-time)") gioLamThucTe = "07:00 - 11:00";
                    else if (ca == "Ca chiều (Part-time)") gioLamThucTe = "13:00 - 17:00";
                    else if (ca == "Ca tối (Part-time)") gioLamThucTe = "16:00 - 20:00";

                    var lichKhet = cacLichBan.Where(b => b.MaNv == nv.MaNv).OrderByDescending(b => b.GioKetThuc).FirstOrDefault();

                    if (!dungCaLam)
                    {
                        isAvailable = false;
                        reason = $"Trái ca (Làm {gioLamThucTe})";
                    }
                    else if (lichKhet != null)
                    {
                        isAvailable = false;
                        reason = $"Bận khách ({lichKhet.GioBatDau?.ToString(@"hh\:mm")} - {lichKhet.GioKetThuc?.ToString(@"hh\:mm")})";
                    }

                    var ratingVal = _context.DanhgiaDichvus.Where(dg => _context.HoaDons.Any(hd => dg.MaHd == hd.MaHd && _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))).Average(dg => (double?)dg.SoSao);

                    // --- 2. GẮN CỜ YÊU THÍCH VÀO KẾT QUẢ ---
                    bool isFav = favoriteStylistIds.Contains(nv.MaNv.Trim());

                    result.Add(new
                    {
                        id = nv.MaNv.Trim(),
                        name = nv.HoTenNv,
                        role = nv.MaLoaiNvNavigation?.TenLoai ?? "Stylist",
                        img = nv.HinhAnh ?? "/images/no-image.png",
                        rating = ratingVal != null ? Math.Round(ratingVal.Value, 1) : 5.0,
                        isAvailable = isAvailable,
                        unAvailableReason = reason,
                        caLam = lich.CaLam,
                        isFavorite = isFav // <--- TRẢ VỀ CHO VUE
                    });
                }

                // --- 3. SẮP XẾP ƯU TIÊN LÊN ĐẦU ---
                var finalResult = result
                    .OrderByDescending(r => (bool)((dynamic)r).isAvailable)
                    .ThenByDescending(r => (bool)((dynamic)r).isFavorite)
                    .ToList();

                return Ok(finalResult);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi Backend: " + ex.Message });
            }
        }

        // TÍNH TOÁN TRƯỚC SỐ TIỀN ĐƯỢC GIẢM
        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(string maKH, [FromQuery] string? codes, [FromQuery] decimal subTotal, [FromQuery] string? itemIds)
        {
            var parts = string.IsNullOrWhiteSpace(codes) ? new List<string>() : codes.Split(',').Select(VoucherCartLogic.NormalizeMaCode).Take(2).ToList();
            if (parts.Count == 0) return Ok(new { totalDiscount = 0m, finalAmount = subTotal });

            var kh = await _context.Khachhangs.Include(k => k.MaCodes).FirstOrDefaultAsync(k => k.MaKh == maKH);
            if (kh == null) return Ok(new { totalDiscount = 0m, finalAmount = subTotal });

            string? maRank = kh.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking().FirstOrDefaultAsync(r => r.MaRank == maRank);
                rankCap = rank?.PhanTramGiamGia;
            }

            var cartIds = string.IsNullOrWhiteSpace(itemIds) ? new List<string>() : itemIds.Split(',').ToList();

            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(_context, parts, maKH, subTotal, cartIds, DateTime.Now, DateTime.Now, maRank, rankCap);

            if (err != null) return BadRequest(new { message = err });
            return Ok(new { totalDiscount = totalGiam, finalAmount = sau, codes = parts });
        }

        // LƯU TẤT CẢ VÀO CƠ SỞ DỮ LIỆU
        [HttpPost("submit-checkout")]
        public async Task<IActionResult> SubmitCheckout([FromBody] CheckoutSummaryRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ!" });

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    string maHd = "TH" + DateTime.Now.ToString("HHmmssff");

                    // BƯỚC 1: TẠO KHÁCH HÀNG (Nếu chưa có)
                    string safeKhachHang = req.MaKh;
                    if (string.IsNullOrWhiteSpace(safeKhachHang) || safeKhachHang == "GUEST")
                    {
                        if (!string.IsNullOrEmpty(req.Phone))
                        {
                            var existingKhach = await _context.Khachhangs.FirstOrDefaultAsync(x => x.Sdt == req.Phone);
                            if (existingKhach != null) { safeKhachHang = existingKhach.MaKh; }
                            else
                            {
                                string prefixSdt = req.Phone.Length >= 4 ? req.Phone.Substring(0, 4) : "0000";
                                safeKhachHang = "KH" + prefixSdt + new Random().Next(100, 999).ToString();
                                if (!string.IsNullOrWhiteSpace(req.Email) && existingKhach.Email != req.Email)
                                {
                                    existingKhach.Email = req.Email;
                                }
                                _context.Khachhangs.Add(new Khachhang
                                {
                                    MaKh = safeKhachHang,
                                    HoTen = string.IsNullOrWhiteSpace(req.CustomerName) ? "Khách vãng lai" : req.CustomerName,
                                    Sdt = req.Phone,
                                    Email = req.Email ?? "",
                                    TrangThai = 1,
                                    NgayTao = DateTime.Now,
                                    DiemTichLuy = 0,
                                    MaRank = "RANK01"
                                });
                                await _context.SaveChangesAsync();
                            }
                        }
                        else { safeKhachHang = null; }
                    }

                    // BƯỚC 2: TẠO LỊCH ĐẶT & CHI TIẾT
                    string maLd = null;
                    if (req.Services != null && req.Services.Any())
                    {
                        // Sinh 10 số random liên tục từ thời gian + mili-giây
                        maLd = DateTime.Now.ToString("yyHHmmssff");

                        int tongThoiGianPhut = req.Services.Sum(s => s.Duration);
                        var localTime = req.BookingDate ?? DateTime.Now;
                        var gioBatDau = TimeOnly.FromDateTime(localTime);
                        var gioKetThuc = gioBatDau.AddMinutes(tongThoiGianPhut);

                        _context.Lichdats.Add(new Lichdat
                        {
                            MaLd = maLd,
                            MaKh = safeKhachHang,
                            NgayHen = DateOnly.FromDateTime(localTime),
                            GioBatDau = gioBatDau,
                            GioKetThuc = gioKetThuc,
                            TrangThai = 0, // Trạng thái ban đầu luôn là 0 (Chưa xác nhận/chưa thanh toán)
                        });
                        await _context.SaveChangesAsync();

                        foreach (var sv in req.Services)
                        {
                            var chiTiet = new ChitietLichdat
                            {
                                MaLd = maLd,
                                MaNv = string.IsNullOrEmpty(req.StylistId) ? null : req.StylistId,
                                GiaLucDat = sv.Price
                            };

                            if (sv.Id.StartsWith("DV")) { chiTiet.MaDv = sv.Id; }
                            else if (sv.Id.StartsWith("CB")) { chiTiet.MaCb = sv.Id; }

                            _context.ChitietLichdats.Add(chiTiet);
                        }
                        await _context.SaveChangesAsync();
                    }

                    // BƯỚC 5 (Chuyển lên trước để xác định trạng thái thanh toán online)
                    string phuongThuc = "COD";
                    if (req.PaidAmount > 0)
                    {
                        phuongThuc = (req.CheckoutType == "CHI_MUA_SAN_PHAM") ? req.ProductPaymentMethod : req.ServicePaymentMethod;
                    }

                    // Xác định đơn hàng có phải thanh toán online qua cổng VNPay/MoMo hay không
                    bool isOnlinePayment = req.PaidAmount > 0 &&
                        (phuongThuc.ToLower() == "vnpay" || phuongThuc.ToLower() == "momo");

                    // BƯỚC 3: TẠO HÓA ĐƠN
                    string? maCodeChinh = req.VoucherIds != null && req.VoucherIds.Any(v => !string.IsNullOrWhiteSpace(v))
                    ? req.VoucherIds.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                    : null;
                    decimal totalOriginal = (req.Services?.Sum(s => s.Price) ?? 0) + (req.Products?.Sum(p => p.Gia * p.SoLuong) ?? 0);
                    var hoaDon = new HoaDon
                    {
                        MaHd = maHd,
                        MaKh = safeKhachHang,
                        MaLd = maLd,
                        NgayTao = DateTime.Now,
                        TongTien = totalOriginal,
                        ThanhTien = req.GrandTotal,
                        TienDaTra = req.PaidAmount,
                        // Nếu là thanh toán online thì khởi tạo là 0 (Chưa thanh toán), ngược lại (COD/Free) thì là 1
                        TrangThai = isOnlinePayment ? 0 : 1,
                        MaCode = maCodeChinh
                    };
                    _context.HoaDons.Add(hoaDon);
                    await _context.SaveChangesAsync();

                    // BƯỚC 4: TẠO CHI TIẾT SẢN PHẨM & GIAO HÀNG
                    if (req.Products != null && req.Products.Any())
                    {
                        foreach (var p in req.Products)
                        {
                            _context.ChitietHoadonSps.Add(new ChitietHoadonSp
                            {
                                MaHd = maHd,
                                MaBienThe = p.Id,
                                SoLuong = p.SoLuong,
                                DonGiaLucBan = p.Gia,
                                ThanhTien = p.Gia * p.SoLuong
                            });
                            //Phần lịch sử tồn kho liên quan đến bán hàng
                            var bienthe = await _context.BienTheSanphams.FindAsync(p.Id);
                            if (bienthe != null && bienthe.SoLuongTon >= p.SoLuong)
                            {
                                var tonTruoc = bienthe.SoLuongTon ?? 0;
                                bienthe.SoLuongTon -= p.SoLuong;

                                _context.LichSuTonKhos.Add(new LichSuTonKho
                                {
                                    MaBienThe = bienthe.MaBienThe,
                                    LoaiBienDong = "BAN",
                                    SoLuongThayDoi = -p.SoLuong,
                                    TonTruoc = tonTruoc,
                                    TonSau = bienthe.SoLuongTon.Value,
                                    MaChungTu = maHd,
                                    MaNv = null,
                                    GhiChu = $"Bán hàng theo đơn {maHd}",
                                    ThoiGian = DateTime.Now
                                });
                            }
                        }

                        if (req.ReceiveType == "home" && req.DeliveryInfo != null)
                        {
                            _context.ThongtinGiaohangs.Add(new ThongtinGiaohang
                            {
                                MaHd = maHd,
                                TenNguoiNhan = req.DeliveryInfo.HoTen ?? "Khách hàng",
                                SdtNhan = req.DeliveryInfo.SoDienThoai ?? "",
                                DiaChiNhan = req.DeliveryInfo.DiaChi ?? "",
                                Email = req.DeliveryInfo.Email ?? "",
                                PhiShip = req.ShippingFee,
                                TrangThaiGiaoHang = 0
                            });
                        }
                    }

                    // LƯU BẢN GHI THANH TOÁN
                    _context.Thanhtoans.Add(new Thanhtoan
                    {
                        MaHd = maHd,
                        PhuongThuc = (phuongThuc ?? "COD").ToUpper(),
                        SoTien = req.PaidAmount,
                        // Nếu là thanh toán online thì bắt đầu với trạng thái 0 (Chưa thanh toán)
                        TrangThai = isOnlinePayment ? 0 : 1
                    });

                    if (req.VoucherIds != null)
                    {
                        foreach (var code in req.VoucherIds)
                        {
                            await VoucherCartLogic.TryIncrementUsageAsync(_context, code);
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // PHẦN XUẤT HÓA ĐƠN ĐIỆN TỬ
                    // Luật yêu cầu MỌI giao dịch bán hàng đều phải xuất hóa đơn, không phụ thuộc khách có yêu cầu hay không.
                    // req.InvoiceInfo chỉ dùng để xác định đây là hóa đơn cá nhân hay doanh nghiệp (có MST) và thông tin xuất hóa đơn riêng (nếu khác thông tin giao hàng).
                    {
                        bool laDoanhNghiep = req.InvoiceInfo?.LaDoanhNghiep ?? false;
                        var maHddt = "HD" + DateTime.Now.ToString("yyMMddHHmmss");
                        _context.HoaDonDienTus.Add(new HoaDonDienTu
                        {
                            MaHddt = maHddt,
                            MaHd = maHd,
                            LoaiKhach = laDoanhNghiep ? (byte)1 : (byte)0,
                            TenNguoiMua = laDoanhNghiep ? req.InvoiceInfo?.TenCongTy : (req.CustomerName ?? "Khach hang"),
                            Mst = laDoanhNghiep ? req.InvoiceInfo?.Mst : null,
                            DiaChiXuatHd = req.InvoiceInfo?.DiaChiXuatHD ?? req.DeliveryInfo?.DiaChi,
                            EmailNhanHd = req.InvoiceInfo?.EmailNhanHD ?? req.Email,
                            TrangThai = 0
                        });
                        await _context.SaveChangesAsync();

                        _ = Task.Run(async () =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var eInvoiceService = scope.ServiceProvider.GetRequiredService<IEInvoiceService>();
                            await eInvoiceService.PhatHanhHoaDonAsync(maHddt);
                        }); // fire-and-forget với scope riêng, không dùng _context của request đã kết thúc

                    }


                    // BƯỚC 6: TẠO LINK VNPAY HOẶC MOMO
                    string paymentUrl = null;
                    if (req.PaidAmount > 0)
                    {
                        if (phuongThuc.ToLower() == "vnpay")
                        {
                            var vnpay = new VnPayLibrary();
                            vnpay.AddRequestData("vnp_Version", "2.1.0");
                            vnpay.AddRequestData("vnp_Command", "pay");
                            vnpay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"] ?? "");
                            vnpay.AddRequestData("vnp_Amount", ((long)(req.PaidAmount * 100)).ToString());
                            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                            vnpay.AddRequestData("vnp_CurrCode", "VND");
                            vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                            vnpay.AddRequestData("vnp_Locale", "vn");
                            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan hoa don " + maHd);
                            vnpay.AddRequestData("vnp_OrderType", "other");
                            vnpay.AddRequestData("vnp_ReturnUrl", _configuration["Vnpay:ReturnUrlTong"]);
                            vnpay.AddRequestData("vnp_TxnRef", maHd);

                            paymentUrl = vnpay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"] ?? "", _configuration["Vnpay:HashSecret"] ?? "");
                        }
                        else if (phuongThuc.ToLower() == "momo")
                        {
                            var partnerCode = _configuration["Momo:PartnerCode"];
                            var accessKey = _configuration["Momo:AccessKey"];
                            var secretKey = _configuration["Momo:SecretKey"];
                            var endpoint = _configuration["Momo:ApiEndpoint"];
                            var redirectUrl = _configuration["Momo:ReturnUrlTong"];
                            var ipnUrl = _configuration["Momo:NotifyUrl"];

                            long amount = (long)req.PaidAmount;
                            string orderInfo = "Thanh toan hoa don tong " + maHd;

                            string rawHash =
                                $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={maHd}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={maHd}&requestType=captureWallet";

                            string signature = SignHmacSHA256(rawHash, secretKey);

                            var requestData = new
                            {
                                partnerCode,
                                requestId = maHd,
                                amount,
                                orderId = maHd,
                                orderInfo,
                                redirectUrl,
                                ipnUrl,
                                lang = "vi",
                                extraData = "",
                                requestType = "captureWallet",
                                signature
                            };

                            using var client = new HttpClient();
                            var response = await client.PostAsJsonAsync(endpoint, requestData);
                            var result = await response.Content.ReadFromJsonAsync<MomoResponse>();

                            if (result != null && result.resultCode == 0)
                            {
                                paymentUrl = result.payUrl;
                            }
                            else
                            {
                                return BadRequest(new
                                {
                                    success = false,
                                    message = "Lỗi tạo link MoMo: " + (result?.message ?? "Không xác định")
                                });
                            }
                        }
                    }

                    return Ok(new { success = true, isOnlinePayment = !string.IsNullOrEmpty(paymentUrl), url = paymentUrl, maHd = maHd });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine("====== LỖI LƯU HÓA ĐƠN ======\n" + ex.Message);
                    if (ex.InnerException != null) Console.WriteLine("CHI TIẾT: " + ex.InnerException.Message);
                    return BadRequest(new { success = false, message = "Lỗi khi lưu DB: " + (ex.InnerException?.Message ?? ex.Message) });
                }
            });
        }

        // ===============================================
        // API 6: CẬP NHẬT TRẠNG THÁI & LẤY CHI TIẾT BIÊN LAI
        // ===============================================
        [HttpGet("get-receipt")]
        public async Task<IActionResult> GetReceipt(
            [FromQuery] string maHd,
            [FromQuery] string? vnp_ResponseCode = null,
            [FromQuery] string? resultCode = null)
        {
            if (string.IsNullOrEmpty(maHd)) return BadRequest(new { success = false, message = "Thiếu mã hóa đơn." });

            try
            {
                var hoaDon = await _context.HoaDons
                    .Include(h => h.ThongtinGiaohangs)
                    .Include(h => h.Thanhtoans)
                    .FirstOrDefaultAsync(h => h.MaHd == maHd);

                if (hoaDon == null) return NotFound(new { success = false, message = "Không tìm thấy hóa đơn" });

                // 1. CẬP NHẬT TRẠNG THÁI NẾU THANH TOÁN ONLINE THÀNH CÔNG (VNPay hoặc MoMo)
                bool isPaymentSuccess = (vnp_ResponseCode == "00") || (resultCode == "0");

                if (isPaymentSuccess && hoaDon.TrangThai == 0)
                {
                    hoaDon.TrangThai = 1;
                    var thanhToan = hoaDon.Thanhtoans.FirstOrDefault();
                    if (thanhToan != null) thanhToan.TrangThai = 1;
                    await _context.SaveChangesAsync();
                }

                // 2. NẾU HÓA ĐƠN ĐÃ THANH TOÁN (TrangThai == 1) VÀ LỊCH ĐẶT CHƯA XÁC NHẬN (TrangThai == 0)
                // THÌ TIẾN HÀNH XÁC NHẬN LỊCH VÀ KÍCH HOẠT GỬI EMAIL QR (Chỉ chạy đúng 1 lần đầu tiên)
                bool shouldSendEmail = false;
                if (hoaDon.TrangThai == 1 && !string.IsNullOrEmpty(hoaDon.MaLd))
                {
                    var lichDat = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                    if (lichDat != null && lichDat.TrangThai == 0)
                    {
                        lichDat.TrangThai = 1;
                        await _context.SaveChangesAsync();
                        shouldSendEmail = true;
                    }
                }

                // 3. THỰC HIỆN GỬI EMAIL QR XÁC NHẬN LỊCH HẸN
                var delivery = hoaDon.ThongtinGiaohangs.FirstOrDefault();
                if (shouldSendEmail)
                {
                    try
                    {
                        var khachHang = await _context.Khachhangs.FindAsync(hoaDon.MaKh);

                        // Ưu tiên lấy thông tin email & tên từ Khachhang, nếu không có/rỗng thì lấy từ ThongtinGiaohang
                        string tenKhach = khachHang?.HoTen ?? "Khách hàng";
                        string emailNhan = khachHang?.Email;

                        if (string.IsNullOrWhiteSpace(emailNhan) && delivery != null)
                        {
                            emailNhan = delivery.Email;
                            tenKhach = delivery.TenNguoiNhan;
                        }

                        if (!string.IsNullOrWhiteSpace(emailNhan))
                        {
                            var ld = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                            DateTime ngayHenDateTime = DateTime.Now;

                            if (ld != null && ld.NgayHen.HasValue && ld.GioBatDau.HasValue)
                            {
                                // Chuyển đổi DateOnly và TimeOnly thành DateTime
                                ngayHenDateTime = ld.NgayHen.Value.ToDateTime(ld.GioBatDau.Value);
                            }

                            // Gọi service gửi Email
                            await _qrEmailService.SendBookingEmailAsync(
                                emailNhan,
                                tenKhach,
                                hoaDon.MaLd,
                                ngayHenDateTime,
                                hoaDon.ThanhTien ?? 0m
                            );
                            Console.WriteLine("Đã gửi mã QR thành công tới email: " + emailNhan);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("====== LỖI GỬI EMAIL QR TRONG GET-RECEIPT ======\n" + ex.Message);
                    }
                }

                // 4. GOM DỮ LIỆU ĐỂ TRẢ VỀ FRONTEND
                var khachHangInfo = await _context.Khachhangs.FindAsync(hoaDon.MaKh);

                object bookingInfo = null;
                if (!string.IsNullOrEmpty(hoaDon.MaLd))
                {
                    var ld = await _context.Lichdats.FirstOrDefaultAsync(l => l.MaLd == hoaDon.MaLd);
                    var chiTietLd = await _context.ChitietLichdats.Where(c => c.MaLd == hoaDon.MaLd).ToListAsync();

                    var servicesList = new List<object>();
                    foreach (var ct in chiTietLd)
                    {
                        string name = "";
                        if (!string.IsNullOrEmpty(ct.MaDv)) { var dv = await _context.Dichvus.FindAsync(ct.MaDv); name = dv?.TenDv; }
                        else if (!string.IsNullOrEmpty(ct.MaCb)) { var cb = await _context.Combos.FindAsync(ct.MaCb); name = cb?.TenCombo; }

                        string stylistName = "Chưa chọn thợ";
                        if (!string.IsNullOrEmpty(ct.MaNv)) { var nv = await _context.Nhanviens.FindAsync(ct.MaNv); stylistName = nv?.HoTenNv; }

                        servicesList.Add(new { name = name ?? "Dịch vụ", price = ct.GiaLucDat, stylist = stylistName });
                    }

                    if (ld != null) bookingInfo = new
                    {
                        ngayHen = ld.NgayHen?.ToString("dd/MM/yyyy"),
                        gioBatDau = ld.GioBatDau?.ToString(@"HH\:mm"),
                        services = servicesList
                    };
                }

                var chiTietSp = await _context.ChitietHoadonSps.Where(c => c.MaHd == maHd).ToListAsync();
                var productsList = new List<object>();
                foreach (var ct in chiTietSp)
                {
                    var bt = await _context.BienTheSanphams.FindAsync(ct.MaBienThe);
                    var sp = bt != null ? await _context.SanPhams.FindAsync(bt.MaSanPham) : null;
                    productsList.Add(new { name = $"{sp?.TenSp} - {bt?.TenBienThe}", qty = ct.SoLuong, price = ct.DonGiaLucBan, total = ct.ThanhTien });
                }

                var receipt = new
                {
                    maHd = hoaDon.MaHd,
                    ngayTao = hoaDon.NgayTao?.ToString("dd/MM/yyyy HH:mm"),
                    khachHang = khachHangInfo?.HoTen ?? "Khách vãng lai",
                    sdt = khachHangInfo?.Sdt ?? "",
                    tongTien = hoaDon.TongTien,
                    thanhTien = hoaDon.ThanhTien,
                    tienDaTra = hoaDon.TienDaTra,
                    trangThai = hoaDon.TrangThai == 1 ? "Đã thanh toán" : "Chưa thanh toán",
                    booking = bookingInfo,
                    products = productsList,
                    delivery = delivery != null ? new { nguoiNhan = delivery.TenNguoiNhan, sdt = delivery.SdtNhan, diaChi = delivery.DiaChiNhan, phiShip = delivery.PhiShip } : null
                };

                return Ok(new { success = true, data = receipt });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        private string SignHmacSHA256(string message, string key)
        {
            byte[] keyByte = Encoding.UTF8.GetBytes(key);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmacsha256 = new HMACSHA256(keyByte);
            byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);

            return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class CheckoutSummaryRequest
    {
        public string? CheckoutType { get; set; }
        public string? MaKh { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Note { get; set; }
        public DateTime? BookingDate { get; set; }
        public string? StylistId { get; set; }

        public List<CheckoutServiceItemDto>? Services { get; set; }

        public List<ProductItemDto2>? Products { get; set; }
        public string? ReceiveType { get; set; }
        public DeliveryInfoDto? DeliveryInfo { get; set; } // Cho phép null nếu không giao hàng
        public List<string>? VoucherIds { get; set; }

        public decimal GrandTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ShippingFee { get; set; }

        public string? PaymentMode { get; set; }
        public string? ServicePaymentMethod { get; set; }
        public string? ProductPaymentMethod { get; set; }

        // Phần hóa đơn điện tử
        public InvoiceInfoDto? InvoiceInfo { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class CheckoutServiceItemDto
    {
        public string? Id { get; set; }
        public decimal Price { get; set; }
        public int Duration { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class ProductItemDto2
    {
        public string? Id { get; set; }
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class DeliveryInfoDto
    {
        public string? HoTen { get; set; }
        public string? SoDienThoai { get; set; }
        public string? DiaChi { get; set; }
        public string? Email { get; set; }
    }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public class ThanhToanCheckStylistRequest
    {
        public string NgayHen { get; set; }
        public string GioBatDau { get; set; }
        public int TongThoiGian { get; set; }
        public string MaKh { get; set; }
    }
}
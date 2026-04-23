using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.KhanchHang.Models;
using MinhTamHair.Models;
using System.Text; // Để dùng Encoding
using System.Security.Cryptography; // Để dùng HMACSHA256
using System.Net.Http.Json; // Để dùng PostAsJsonAsync cho MoMo
using MinhTamHair.Services;
using MinhTamHair;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DatDichVuController : Controller
    {
        private readonly MinhTamHairContext _context;
        private readonly IConfiguration _configuration;
        private readonly IQrEmailService _qrEmailService;
        public DatDichVuController(MinhTamHairContext context, IConfiguration configuration, IQrEmailService qrEmailService)
        {
            _context = context;
            _configuration = configuration;
            _qrEmailService = qrEmailService;
        }
        [HttpGet("data-booking")]
        public IActionResult GetAll()
        {
            var services = _context.Dichvus
                .Where(s => s.TrangThai == 1)
                .Select(s => new {
                    id = s.MaDv,
                    name = s.TenDv,
                    price = s.Gia,
                    duration = s.ThoiGianLam ?? 30, // Lấy thời gian làm từ bảng DICHVU
                    icon = "fas fa-cut"
                }).ToList();

            var combos = _context.Combos
                .Where(c => c.TrangThai == 1)
                .Select(c => new {
                    id = c.MaCb,
                    name = c.TenCombo,
                    price = c.Gia,
                    // Dùng Combo_DichVus theo đúng class C# của bạn
                    duration = c.Combo_DichVus.Sum(ct => ct.DichVu.ThoiGianLam ?? 30),
                    icon = "fas fa-layer-group"
                }).ToList();

            return Ok(new { services, combos });
        }
        [HttpGet("get-stylists")]
        public IActionResult GetStylists()
        {
            var stylists = _context.Nhanviens
                .Include(nv => nv.MaLoaiNvNavigation)
                .Where(nv => nv.TrangThai == 1 && nv.MaLoaiNv == "LNV01")
                .Select(nv => new {
                    id = nv.MaNv,
                    name = nv.HoTenNv,
                    role = nv.MaLoaiNvNavigation.TenLoai,
                    img = nv.HinhAnh ?? "https://i.pravatar.cc/150",
                    // Lọc số sao của thợ này từ bảng DanhGia -> HoaDon -> ChiTiet
                    rating = Math.Round(_context.DanhgiaDichvus
                        .Where(dg => _context.HoaDons
                            .Where(hd => _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))
                            .Select(hd => hd.MaHd)
                            .Contains(dg.MaHd))
                        .Average(dg => (double?)dg.SoSao) ?? 5.0, 1) // Mặc định 5 sao nếu chưa có đánh giá
                }).ToList();
            return Ok(stylists);
        }

        [HttpGet("get-my-vouchers/{maKH}")]
        public async Task<IActionResult> GetMyVouchers(string maKH, [FromQuery] decimal subTotal,
            [FromQuery] DateTime? ngayGioHen, [FromQuery] string? serviceIds)
        {
            var kh = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);
            if (kh == null)
                return Ok(Array.Empty<object>());

            var now = DateTime.Now;
            var ngayDichVu = ngayGioHen ?? now;
            var cartIds = string.IsNullOrWhiteSpace(serviceIds)
                ? new List<string>()
                : serviceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            string? maRank = kh.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                rankCap = rank?.PhanTramGiamGia;
            }

            var codeList = kh.MaCodes.Select(v => v.MaCode).ToList();
            var usageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in codeList)
            {
                var n = await _context.HoaDons.AsNoTracking()
                    .CountAsync(h => h.MaKh == maKH && h.MaCode == c);
                usageMap[c] = n;
            }

            var result = new List<object>();
            foreach (var v in kh.MaCodes)
            {
                var uses = usageMap.GetValueOrDefault(v.MaCode, 0);
                var err = VoucherCartLogic.ValidateAndCalculate(v, uses, subTotal, cartIds, ngayDichVu, now, maRank, rankCap,
                    out var tienGiam, out _);
                if (err != null)
                    continue;

                result.Add(new
                {
                    id = v.MaCode,
                    code = v.MaCode,
                    discount = tienGiam,
                    desc = v.MoTa,
                    expiry = v.NgayKetThuc != null ? v.NgayKetThuc.Value.ToString("dd/MM/yyyy") : ""
                });
            }

            return Ok(result);
        }

        /// <summary>Xem trước tổng giảm với tối đa 1 mã voucher.</summary>
        [HttpGet("preview-voucher-stack")]
        public async Task<IActionResult> PreviewVoucherStack(
            string maKH,
            [FromQuery] string? codes,
            [FromQuery] decimal subTotal,
            [FromQuery] DateTime? ngayGioHen,
            [FromQuery] string? serviceIds)
        {
            if (string.IsNullOrWhiteSpace(maKH))
                return BadRequest(new { message = "Thiếu mã khách hàng." });

            var parts = string.IsNullOrWhiteSpace(codes)
                ? new List<string>()
                : codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(VoucherCartLogic.NormalizeMaCode)
                    .Where(s => s.Length > 0)
                    .Take(1)
                    .ToList();

            if (parts.Count == 0)
                return Ok(new { totalDiscount = 0m, finalAmount = subTotal, codes = Array.Empty<string>() });

            var kh = await _context.Khachhangs.AsNoTracking()
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKH);
            if (kh == null)
                return Ok(new { totalDiscount = 0m, finalAmount = subTotal, codes = parts });

            var owned = new HashSet<string>(kh.MaCodes.Select(x => x.MaCode), StringComparer.OrdinalIgnoreCase);
            foreach (var c in parts)
            {
                if (!owned.Contains(c))
                    return BadRequest(new { message = $"Bạn không sở hữu mã: {c}" });
            }

            var now = DateTime.Now;
            var ngayDichVu = ngayGioHen ?? now;
            var cartIds = string.IsNullOrWhiteSpace(serviceIds)
                ? new List<string>()
                : serviceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            string? maRank = kh.MaRank;
            int? rankCap = null;
            if (!string.IsNullOrWhiteSpace(maRank))
            {
                var rank = await _context.Hangthanhviens.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                rankCap = rank?.PhanTramGiamGia;
            }

            var (err, totalGiam, sau) = await VoucherCartLogic.ValidateStackedAsync(
                _context,
                parts,
                maKH,
                subTotal,
                cartIds,
                ngayDichVu,
                now,
                maRank,
                rankCap);

            if (err != null)
                return BadRequest(new { message = err });

            return Ok(new { totalDiscount = totalGiam, finalAmount = sau, codes = parts });
        }

        [HttpPost("create-payment")]
        public async Task<IActionResult> CreatePayment([FromBody] PaymentInformationRequest model)
        {
            // 1. NẾU LÀ THANH TOÁN VNPAY
            if (model.OrderDescription.ToLower().Contains("vnpay"))
            {
                var vnpay = new VnPayLibrary();
                var vnp_TmnCode = _configuration["Vnpay:TmnCode"];
                var vnp_HashSecret = _configuration["Vnpay:HashSecret"];
                var vnp_Url = _configuration["Vnpay:BaseUrl"];
                var vnp_ReturnUrl = _configuration["Vnpay:ReturnUrl"];

                vnpay.AddRequestData("vnp_Version", "2.1.0");
                vnpay.AddRequestData("vnp_Command", "pay");
                vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
                vnpay.AddRequestData("vnp_Amount", ((long)(model.Amount * 100)).ToString());
                vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                vnpay.AddRequestData("vnp_CurrCode", "VND");
                vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                vnpay.AddRequestData("vnp_Locale", "vn");
                vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan lich hen " + DateTime.Now.Ticks);
                vnpay.AddRequestData("vnp_OrderType", "other");
                vnpay.AddRequestData("vnp_ReturnUrl", vnp_ReturnUrl);
                vnpay.AddRequestData("vnp_TxnRef", DateTime.Now.ToString("yyyyMMddHHmmss"));

                var paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
                return Ok(new { url = paymentUrl });
            }

            // 2. NẾU LÀ THANH TOÁN MOMO
            var partnerCode = _configuration["Momo:PartnerCode"];
            var accessKey = _configuration["Momo:AccessKey"];
            var secretKey = _configuration["Momo:SecretKey"];
            var endpoint = _configuration["Momo:ApiEndpoint"];
            var redirectUrl = _configuration["Momo:ReturnUrl"];
            var ipnUrl = _configuration["Momo:NotifyUrl"];

            string orderId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string requestId = Guid.NewGuid().ToString();
            long amount = (long)model.Amount;
            string orderInfo = "Thanh toan lich hen Minh Tam Hair";

            // Tạo chữ ký MoMo
            string rawHash = $"accessKey={accessKey}&amount={amount}&extraData=&ipnUrl={ipnUrl}&orderId={orderId}&orderInfo={orderInfo}&partnerCode={partnerCode}&redirectUrl={redirectUrl}&requestId={requestId}&requestType=captureWallet";
            string signature = SignHmacSHA256(rawHash, secretKey);

            var requestData = new
            {
                partnerCode,
                requestId,
                amount,
                orderId,
                orderInfo,
                redirectUrl,
                ipnUrl,
                lang = "vi",
                extraData = "",
                requestType = "captureWallet",
                signature
            };

            using var client = new HttpClient();
            // LỖI CS4032 ĐƯỢC SỬA TẠI ĐÂY (Sử dụng await trong async Task)
            var response = await client.PostAsJsonAsync(endpoint, requestData);
            var result = await response.Content.ReadFromJsonAsync<MomoResponse>();

            if (result != null && result.resultCode == 0) return Ok(new { url = result.payUrl });
            return BadRequest(result?.message ?? "Lỗi MoMo");
        }
        [HttpPost("save-booking-final")]
        public async Task<IActionResult> SaveBooking([FromBody] BookingRequest model, [FromQuery] string paymentMode, [FromQuery] string maStylist) // Nhận thêm mã Stylist
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                string currentMaKh = model.MaKh;

                // Nếu người đặt là khách vãng lai (GUEST)
                if (currentMaKh == "GUEST" && !string.IsNullOrEmpty(model.Phone))
                {
                    // Kiểm tra xem SĐT này đã từng đặt lịch hoặc có trong DB chưa
                    var existingKhach = await _context.Khachhangs.FirstOrDefaultAsync(x => x.Sdt == model.Phone);

                    if (existingKhach != null)
                    {
                        currentMaKh = existingKhach.MaKh; // Đã có thì dùng lại mã cũ
                    }
                    else
                    {
                        // Tạo mã KH mới: Lấy chữ "KH" ghép với 4 số đầu SĐT + Random 3 số để tránh trùng lặp nếu 2 người chung đầu số
                        string prefixSdt = model.Phone.Length >= 4 ? model.Phone.Substring(0, 4) : "0000";
                        string newMaKh = "KH" + prefixSdt + new Random().Next(100, 999).ToString();

                        var newKhachHang = new Khachhang
                        {
                            MaKh = newMaKh,
                            HoTen = model.HoTen,
                            Sdt = model.Phone,
                            Email = model.Email,
                            MatKhau = null, // Mật khẩu null theo đúng ý bạn
                            TrangThai = 1,
                            NgayTao = DateTime.Now,
                            DiemTichLuy = 0,
                            MaRank = "RANK01" // Gán rank Đồng mặc định
                        };

                        _context.Khachhangs.Add(newKhachHang);
                        await _context.SaveChangesAsync(); // Lưu ngay để có khách hàng trước khi tạo Hóa đơn

                        currentMaKh = newMaKh; // Gán lại mã mới cho quy trình chạy phía dưới
                    }
                }
                string shortMaLd = model.BookingId.Length > 10
                    ? model.BookingId.Substring(model.BookingId.Length - 10)
                    : model.BookingId;

                decimal subtotal = model.Services.Sum(s => s.Price);

                var codesList = new List<string>();
                if (model.MaCodes != null && model.MaCodes.Count > 0)
                {
                    foreach (var c in model.MaCodes.Take(1))
                    {
                        if (!string.IsNullOrWhiteSpace(c))
                            codesList.Add(VoucherCartLogic.NormalizeMaCode(c));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(model.MaCode))
                {
                    codesList.Add(VoucherCartLogic.NormalizeMaCode(model.MaCode));
                }

                // Hóa đơn FK: cột maCode = mã voucher áp dụng (tối đa 1 mã)
                string? maCodeNorm = codesList.Count > 0 ? codesList[0] : null;
                decimal tongSauGiam = subtotal;

                if (codesList.Count > 0)
                {
                    if (codesList.Count != codesList.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Không được dùng trùng một mã voucher." });
                    }

                    var khForRank = await _context.Khachhangs.AsNoTracking()
                        .Include(k => k.MaCodes)
                        .FirstOrDefaultAsync(k => k.MaKh == currentMaKh);
                    if (khForRank == null)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Không tìm thấy khách hàng." });
                    }

                    var ownedCodes = new HashSet<string>(khForRank.MaCodes.Select(x => x.MaCode), StringComparer.OrdinalIgnoreCase);
                    foreach (var c in codesList)
                    {
                        if (!ownedCodes.Contains(c))
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = $"Bạn không sở hữu mã voucher: {c}" });
                        }
                    }

                    string? maRank = khForRank.MaRank;
                    int? rankCap = null;
                    if (!string.IsNullOrWhiteSpace(maRank))
                    {
                        var rank = await _context.Hangthanhviens.AsNoTracking()
                            .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));
                        rankCap = rank?.PhanTramGiamGia;
                    }

                    var cartIds = model.Services.Select(s => s.Id).ToList();
                    var (vErr, _, sau) = await VoucherCartLogic.ValidateStackedAsync(
                        _context,
                        codesList,
                        currentMaKh,
                        subtotal,
                        cartIds,
                        model.NgayHen,
                        DateTime.Now,
                        maRank,
                        rankCap);
                    if (vErr != null)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = vErr });
                    }
                    tongSauGiam = sau;
                }

                decimal total = tongSauGiam;
                decimal daThanhToan = (paymentMode == "10") ? (total * 0.1m) : total;
                int trangThaiLichDat = (paymentMode == "10") ? 1 : 2;

                // 1. LƯU BẢNG LICHDAT
                var lichDat = new Lichdat
                {
                    MaLd = shortMaLd,
                    MaKh = currentMaKh,
                    NgayHen = DateOnly.FromDateTime(model.NgayHen),
                    GioBatDau = TimeOnly.FromDateTime(model.NgayHen), // Lấy giờ từ NgayHen
                    TongTienDuKien = total,
                    TrangThai = trangThaiLichDat
                };
                _context.Lichdats.Add(lichDat);

                // 2. TẠO HÓA ĐƠN TẠM (Để tránh lỗi khóa ngoại bảng THANHTOAN)
                string maHdTạm = shortMaLd;
                var hoaDon = new HoaDon
                {
                    MaHd = shortMaLd,
                    MaLd = shortMaLd,
                    MaKh = currentMaKh,
                    NgayTao = DateTime.Now,
                    TongTien = subtotal,
                    ThanhTien = tongSauGiam,
                    TienDaTra = daThanhToan,
                    TrangThai = 0, // 0: Hóa đơn chưa hoàn tất (đang thực hiện)
                    MaCode = maCodeNorm
                };
                _context.HoaDons.Add(hoaDon);

                // 3. LƯU CHI TIẾT DỊCH VỤ
                foreach (var item in model.Services)
                {
                    var chiTiet = new ChitietLichdat
                    {
                        MaLd = shortMaLd,
                        MaNv = maStylist, // Gán mã thợ đã chọn
                        GiaLucDat = item.Price
                    };

                    if (item.Id.StartsWith("DV"))
                    {
                        chiTiet.MaDv = item.Id;
                        chiTiet.MaCb = null;
                    }
                    else if (item.Id.StartsWith("CB"))
                    {
                        chiTiet.MaCb = item.Id;
                        chiTiet.MaDv = null;
                    }
                    _context.ChitietLichdats.Add(chiTiet);
                }

                // 4. LƯU BẢNG THANHTOAN
                var tt = new Thanhtoan
                {
                    MaHd = maHdTạm, // Gán mã hóa đơn tạm vừa tạo
                    PhuongThuc = (paymentMode == "10") ? "Coc 10%" : "Full 100%",
                    SoTien = daThanhToan,
                    TrangThai = 1
                };
                _context.Thanhtoans.Add(tt);

                await _context.SaveChangesAsync();

                foreach (var code in codesList)
                {
                    var (incOk, incErr) = await VoucherCartLogic.TryIncrementUsageAsync(_context, code);
                    if (!incOk)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = incErr ?? "Không thể ghi nhận lượt dùng voucher." });
                    }
                }

                await transaction.CommitAsync();
                try
                {
                    var khachHang = await _context.Khachhangs.FindAsync(model.MaKh);
                    string tenKhach = khachHang?.HoTen ?? "Khách hàng";

                    // LOGIC VÀNG: Ưu tiên lấy Email từ Vue gửi lên (model.Email). 
                    // Nếu khách không nhập gì trên web thì mới moi từ Database ra (khachHang?.Email)
                    string emailNhan = !string.IsNullOrEmpty(model.Email) ? model.Email : khachHang?.Email;

                    if (!string.IsNullOrEmpty(emailNhan))
                    {
                        // Gọi qua _qrEmailService và truyền đúng emailNhan vào
                        await _qrEmailService.SendBookingEmailAsync(emailNhan, tenKhach, shortMaLd, model.NgayHen, total);
                        Console.WriteLine("Đã gửi mã QR thành công tới email: " + emailNhan);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("LỖI GỬI EMAIL: " + ex.Message);
                }
                return Ok(new { message = "Thành công", maLd = shortMaLd });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest("Lỗi: " + ex.InnerException?.Message ?? ex.Message);
            }
        }
        private string SignHmacSHA256(string message, string key)
        {
            byte[] keyByte = Encoding.UTF8.GetBytes(key);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
                return BitConverter.ToString(hashmessage).Replace("-", "").ToLower();
            }
        }
        // Class hứng dữ liệu gửi lên từ Vue
        public class CheckStylistRequest
        {
            public string NgayHen { get; set; }
            public string GioBatDau { get; set; }
            public int TongThoiGian { get; set; } // Tổng số phút
        }

        [HttpPost("get-available-stylists")]
        public async Task<IActionResult> GetAvailableStylists([FromBody] CheckStylistRequest request)
        {
            try
            {
                // 1. CHUẨN HÓA THỜI GIAN
                DateOnly ngayHen = DateOnly.Parse(request.NgayHen);
                TimeOnly start = TimeOnly.Parse(request.GioBatDau);
                TimeOnly end = start.AddMinutes(request.TongThoiGian);

                var caCanThiet = new List<string> { "Cả ngày" };
                if (start >= new TimeOnly(8, 0) && end <= new TimeOnly(12, 0)) caCanThiet.Add("Sáng");
                else if (start >= new TimeOnly(13, 0) && end <= new TimeOnly(17, 0)) caCanThiet.Add("Chiều");
                else if (start >= new TimeOnly(17, 0) && end <= new TimeOnly(21, 0)) caCanThiet.Add("Tối");

                // 2. LẤY THỢ ĐI LÀM (Bỏ Include để EF Core không bị ngáo)
                var thoDiLam = await _context.Lichlamviecs
                    .Where(l => l.NgayLamViec == ngayHen && caCanThiet.Contains(l.CaLam))
                    .Select(l => l.MaNv)
                    .Distinct()
                    .ToListAsync();

                // 3. LẤY THỢ ĐANG KẸT LỊCH (Bỏ Include, chọc thẳng vào MaLdNavigation)
                var thoBan = await _context.ChitietLichdats
                    .Where(ct => ct.MaLdNavigation.NgayHen == ngayHen
                                 && ct.MaLdNavigation.TrangThai != 0
                                 && ct.MaLdNavigation.GioBatDau < end
                                 && ct.MaLdNavigation.GioKetThuc > start)
                    .Select(ct => ct.MaNv)
                    .Distinct()
                    .ToListAsync();

                // 4. LỌC THỢ RẢNH
                var thoRanhIds = thoDiLam.Except(thoBan).ToList();

                // 5. TRẢ DATA (Kiểm tra trạng thái nhân viên ở bước cuối cùng này cho an toàn)

                var availableStylists = await _context.Nhanviens
                    .Include(nv => nv.MaRoleNavigation)
                    .Where(nv => thoRanhIds.Contains(nv.MaNv) && nv.TrangThai == 1)
                    .Select(nv => new {
                        id = nv.MaNv,
                        name = nv.HoTenNv,
                        role = nv.MaRoleNavigation.TenChucVu ?? "Stylist",
                        img = nv.HinhAnh ?? "/assets/images/default.jpg",
                        // Lọc số sao của thợ này từ bảng DanhGia -> HoaDon -> ChiTiet
                        rating = Math.Round(_context.DanhgiaDichvus
                            .Where(dg => _context.HoaDons
                                .Where(hd => _context.ChitietLichdats.Any(ct => ct.MaLd == hd.MaLd && ct.MaNv == nv.MaNv))
                                .Select(hd => hd.MaHd)
                                .Contains(dg.MaHd))
                            .Average(dg => (double?)dg.SoSao) ?? 5.0, 1)
                    }).ToListAsync();

                return Ok(availableStylists);
            }
            catch (Exception ex)
            {
                // GHI LOG RA MÀN HÌNH ĐEN VISUAL STUDIO
                Console.WriteLine("============= LỖI API TÌM THỢ =============");
                Console.WriteLine(ex.Message);
                if (ex.InnerException != null) Console.WriteLine("Chi tiết: " + ex.InnerException.Message);
                Console.WriteLine("===========================================");

                // TRẢ LỖI VỀ VUE ĐỂ HIỂN THỊ
                return StatusCode(500, new { message = "Lỗi Backend: " + ex.Message, detail = ex.InnerException?.Message });
            }
        }
    }
}

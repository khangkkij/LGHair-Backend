using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MinhTamHair.Admin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckInController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly MinhTamHair.Services.IEmailService _emailService;

        public CheckInController(MinhTamHairContext context, MinhTamHair.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }


        [HttpGet("get-appointments")]
        public async Task<IActionResult> GetAppointments(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;
            var targetDateOnly = DateOnly.FromDateTime(targetDate);

            var data = await _context.Lichdats
                .Include(ld => ld.MaKhNavigation)
                .Where(ld => ld.NgayHen == targetDateOnly && (ld.TrangThai == 1 || ld.TrangThai == 2))
                .Select(ld => new
                {
                    maLd = ld.MaLd,
                    tenKhach = ld.MaKhNavigation.HoTen,
                    sdt = ld.MaKhNavigation.Sdt,
                    email = ld.MaKhNavigation.Email,
                    ngayHen = $"{ld.NgayHen:yyyy-MM-dd}T{(ld.GioBatDau.HasValue ? ld.GioBatDau.Value.ToString("HH:mm") : "00:00")} - {(ld.GioKetThuc.HasValue ? ld.GioKetThuc.Value.ToString("HH:mm") : "??:??")}",


                    tongTien = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.TongTien ?? 0).FirstOrDefault(),
                    daThanhToan = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.TienDaTra ?? 0).FirstOrDefault(),

                    voucher = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.MaCode).FirstOrDefault(),
                    phanTramGiamVip = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.PhanTramGiamVip ?? 0).FirstOrDefault(),
                    tienGiam = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => (hd.TongTien ?? 0) - (hd.ThanhTien ?? 0)).FirstOrDefault(),

                    maNv = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => ct.MaNv)
                        .FirstOrDefault(),

                    dichVus = _context.ChitietLichdats
                        .Where(ct => ct.MaLd == ld.MaLd)
                        .Select(ct => new {
                            id = ct.MaDv ?? ct.MaCb,
                            ten = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                            gia = ct.GiaLucDat ?? 0,
                            originalPrice = ct.MaDv != null ? ct.MaDvNavigation.Gia : ct.MaCbNavigation.Gia
                        }).ToList(),

                    sanPhams = _context.ChitietHoadonSps
                        .Where(ct => _context.HoaDons.Any(hd => hd.MaHd == ct.MaHd && hd.MaLd == ld.MaLd))
                        .Join(_context.BienTheSanphams, ct => ct.MaBienThe, bt => bt.MaBienThe, (ct, bt) => new { ct, bt })
                        .Join(_context.SanPhams, temp => temp.bt.MaSanPham, sp => sp.MaSp, (temp, sp) => new {
                            id = temp.ct.MaBienThe,
                            ten = sp.TenSp + " - " + temp.bt.TenBienThe,
                            soLuong = temp.ct.SoLuong,
                            gia = temp.ct.DonGiaLucBan
                        }).ToList()
                })
                .ToListAsync();

            return Ok(data);
        }

        // 2. Xác nhận Check-in
        [HttpPost("confirm-checkin")]
        public async Task<IActionResult> ConfirmCheckIn([FromBody] CheckInConfirmRequest request)
        {
            var cleanLdId = request.MaLd?.Trim();
            var lichDat = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanLdId);
            if (lichDat == null) return NotFound("Không tìm thấy lịch đặt");

            lichDat.TrangThai = 3;

            var chiTietLichDats = await _context.ChitietLichdats
                .Where(ct => ct.MaLd == lichDat.MaLd)
                .ToListAsync();

            foreach (var item in chiTietLichDats)
            {
                item.MaNv = request.MaStylist;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Check-in thành công và đã gán Stylist!" });
        }

        // 3. Lấy thông tin bằng mã QR
        [HttpGet("get-by-qrcode/{maLd}")]
        public async Task<IActionResult> GetByQrCode(string maLd)
        {
            var cleanId = maLd.Trim();
            var ld = await _context.Lichdats
                .Include(x => x.MaKhNavigation)
                .FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanId);

            if (ld == null) return NotFound($"Mã ID '{cleanId}' không tồn tại.");

            var result = new
            {
                maLd = ld.MaLd,
                tenKhach = ld.MaKhNavigation.HoTen,
                sdt = ld.MaKhNavigation.Sdt,
                email = ld.MaKhNavigation.Email,
                ngayHen = $"{ld.NgayHen:yyyy-MM-dd}T{ld.GioBatDau:HH\\:mm} - {ld.GioKetThuc:HH\\:mm}",


                tongTien = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.TongTien ?? 0).FirstOrDefault(),
                daThanhToan = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.TienDaTra ?? 0).FirstOrDefault(),

                voucher = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.MaCode).FirstOrDefault(),
                phanTramGiamVip = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => hd.PhanTramGiamVip ?? 0).FirstOrDefault(),
                tienGiam = _context.HoaDons.Where(hd => hd.MaLd == ld.MaLd).Select(hd => (hd.TongTien ?? 0) - (hd.ThanhTien ?? 0)).FirstOrDefault(),

                maNv = _context.ChitietLichdats
                    .Where(ct => ct.MaLd == ld.MaLd)
                    .Select(ct => ct.MaNv)
                    .FirstOrDefault(),
                dichVus = _context.ChitietLichdats
                    .Where(ct => ct.MaLd == ld.MaLd)
                    .Select(ct => new
                    {
                        id = ct.MaDv ?? ct.MaCb,
                        ten = ct.MaDvNavigation.TenDv ?? ct.MaCbNavigation.TenCombo,
                        gia = ct.GiaLucDat ?? 0,
                        originalPrice = ct.MaDv != null ? ct.MaDvNavigation.Gia : ct.MaCbNavigation.Gia
                    }).ToList(),

                sanPhams = _context.ChitietHoadonSps
                    .Where(ct => _context.HoaDons.Any(hd => hd.MaHd == ct.MaHd && hd.MaLd == ld.MaLd))
                    .Join(_context.BienTheSanphams, ct => ct.MaBienThe, bt => bt.MaBienThe, (ct, bt) => new { ct, bt })
                    .Join(_context.SanPhams, temp => temp.bt.MaSanPham, sp => sp.MaSp, (temp, sp) => new {
                        id = temp.ct.MaBienThe,
                        ten = sp.TenSp + " - " + temp.bt.TenBienThe,
                        soLuong = temp.ct.SoLuong,
                        gia = temp.ct.DonGiaLucBan
                    }).ToList()
            };

            return Ok(result);
        }

        // 4. Lấy danh mục dịch vụ
        [HttpGet("all-services-combos")]
        public async Task<IActionResult> GetAllServicesAndCombos()
        {
            try
            {
                var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();

                // 1. Lấy danh sách dịch vụ
                var servicesRaw = await _context.Dichvus
                    .Where(s => s.TrangThai == 1)
                    .Include(x => x.ChitietFlashsales)
                        .ThenInclude(fs => fs.MaFlNavigation)
                    .ToListAsync();

                var services = servicesRaw.Select(s => {
                    // KIỂM TRA AN TOÀN: fs.MaFlNavigation != null
                    var flash = s.ChitietFlashsales
                        .FirstOrDefault(fs =>
                            fs.MaFlNavigation != null &&
                            fs.MaFlNavigation.TrangThai == 1 &&
                            fs.MaFlNavigation.NgayBatDau <= now &&
                            fs.MaFlNavigation.NgayKetThuc >= now);

                    // Dùng ?? 0 để tránh lỗi Nullable cast
                    decimal originalPrice = (decimal)(s.Gia ?? 0);
                    decimal finalPrice = originalPrice;

                    if (flash != null && flash.PhanTramGiam.HasValue && flash.PhanTramGiam.Value > 0)
                    {
                        finalPrice = originalPrice - (originalPrice * (decimal)flash.PhanTramGiam.Value / 100m);
                    }

                    return new
                    {
                        Id = s.MaDv,
                        Name = s.TenDv ?? "Dịch vụ chưa đặt tên",
                        Price = finalPrice,
                        OriginalPrice = originalPrice,
                        Type = "DV",
                        // Bỏ thuộc tính viết hoa PhanTramHoaHong, chỉ giữ lại phanTramHoaHong
                        phanTramHoaHong = (s.PhanTramHoaHong ?? 0) > 0 ? s.PhanTramHoaHong.Value : 10m
                    };
                }).ToList();

                // 2. Lấy danh sách Combo (xử lý an toàn null cho c.Gia)
                var combos = await _context.Combos
                    .Where(c => c.TrangThai == 1)
                    .Select(c => new {
                        Id = c.MaCb,
                        Name = c.TenCombo ?? "Combo chưa đặt tên",
                        Price = (decimal)(c.Gia ?? 0),
                        OriginalPrice = (decimal)(c.Gia ?? 0),
                        Type = "CB",
                        // Bỏ thuộc tính viết hoa PhanTramHoaHong, chỉ giữ lại phanTramHoaHong
                        phanTramHoaHong = 10m
                    }).ToListAsync();

                var result = services.Cast<object>().Concat(combos.Cast<object>()).ToList();
                return Ok(result);
            }
            catch (Exception ex)
            {
                // Trả về lỗi rõ ràng để dễ debug nếu có vấn đề khác về DB
                return StatusCode(500, $"Lỗi máy chủ: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // 5. Chốt dịch vụ order thêm tại Check-in
        [HttpPost("create-payment-checkin")]
        public async Task<IActionResult> CreatePaymentCheckIn([FromBody] PaymentCheckInRequest request)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // 1. So khớp cắt bỏ khoảng trắng (Trim) để tìm đúng Lịch đặt
                    var cleanLdId = request.MaLd?.Trim();
                    var ld = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanLdId);
                    if (ld == null) return NotFound("Không tìm thấy lịch đặt");

                    ld.TrangThai = 3;

                    // 2. Lấy chi tiết dịch vụ cũ theo mã MaLd chuẩn của database để tránh lệch khoảng trắng
                    var oldDetails = await _context.ChitietLichdats.Where(ct => ct.MaLd == ld.MaLd).ToListAsync();
                    _context.ChitietLichdats.RemoveRange(oldDetails);

                    if (request.UpdatedServices != null)
                    {
                        foreach (var item in request.UpdatedServices)
                        {
                            // 3. Kiểm tra an toàn: Bỏ qua nếu mã dịch vụ bị null hoặc mang chuỗi lỗi "undefined"/"null"
                            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.ToLower() == "undefined" || item.Id.ToLower() == "null")
                            {
                                continue;
                            }

                            var newDetail = new ChitietLichdat
                            {
                                MaLd = ld.MaLd, // Dùng MaLd của db đã load để khớp foreign key 100% (gồm cả khoảng trắng đệm)
                                MaNv = request.MaStylist,
                                GiaLucDat = item.Gia
                            };

                            if (item.Id.StartsWith("DV"))
                                newDetail.MaDv = item.Id;
                            else
                                newDetail.MaCb = item.Id;

                            _context.ChitietLichdats.Add(newDetail);
                        }
                    }

                    // TÍNH TOÁN LẠI TỔNG TIỀN MỚI
                    decimal tongDichVuMoi = request.UpdatedServices?.Sum(s => s.Gia) ?? 0;

                    var cleanHdId = ld.MaLd.Trim();
                    var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(hd => hd.MaLd.Trim() == cleanHdId);
                    if (hoaDon != null)
                    {
                        decimal tongSanPhamDaDat = await _context.ChitietHoadonSps
                            .Where(ct => ct.MaHd == hoaDon.MaHd)
                            .SumAsync(ct => ct.ThanhTien ?? 0);
                        decimal tongTienMoi = tongDichVuMoi + tongSanPhamDaDat;

                        hoaDon.TongTien = tongTienMoi;
                        ld.TongTienDuKien = tongTienMoi; // Cập nhật đồng bộ lịch đặt

                        decimal thanhTienMoi;
                        if (!string.IsNullOrEmpty(hoaDon.MaCode))
                        {
                            var vc = await _context.Vouchers.FirstOrDefaultAsync(v => v.MaCode == hoaDon.MaCode);
                            if (vc != null)
                            {
                                if (vc.SoTienGiam.HasValue && vc.SoTienGiam > 0)
                                    thanhTienMoi = tongTienMoi - vc.SoTienGiam.Value;
                                else if (vc.PhanTramGiam.HasValue && vc.PhanTramGiam > 0)
                                    thanhTienMoi = tongTienMoi - (tongTienMoi * vc.PhanTramGiam.Value / 100m);
                                else
                                    thanhTienMoi = tongTienMoi;

                                if (thanhTienMoi < 0) thanhTienMoi = 0;
                            }
                            else
                            {
                                thanhTienMoi = tongTienMoi;
                            }
                        }
                        else
                        {
                            thanhTienMoi = tongTienMoi;
                        }

                        decimal phiShipHienTai = await _context.ThongtinGiaohangs
                            .Where(t => t.MaHd == hoaDon.MaHd)
                            .Select(t => t.PhiShip ?? 0m)
                            .FirstOrDefaultAsync();
                        hoaDon.ThanhTien = thanhTienMoi + phiShipHienTai;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { message = "Đã chốt dịch vụ thành công! Đơn đã chuyển sang trạng thái Đang phục vụ." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    // In ra InnerException nếu có lỗi ràng buộc dữ liệu sâu hơn trong SQL Server
                    return BadRequest("Lỗi xử lý: " + (ex.InnerException?.Message ?? ex.Message));
                }
            });
        }

        // 6. Tạo lịch cho khách vãng lai
        [HttpPost("create-walkin")]
        public async Task<IActionResult> CreateWalkIn([FromBody] WalkInRequest request)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    string maKh = "";

                    var walkInAccounts = await _context.Khachhangs
                        .Where(kh => kh.HoTen.Contains("Khách vãng lai") || kh.HoTen.Contains("Khách vãn lai"))
                        .Select(kh => kh.MaKh)
                        .ToListAsync();

                    if (walkInAccounts.Count == 0)
                    {
                        maKh = "KVL" + MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().ToString("ddHHmm");
                        var newKh = new Khachhang
                        {
                            MaKh = maKh,
                            HoTen = "Khách vãng lai",
                            Sdt = "0000000000",
                            Email = "khachvanglai@gmail.com",
                            TrangThai = 1,
                            NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                            MaRank = "RANK01",
                            DiemTichLuy = 0
                        };
                        _context.Khachhangs.Add(newKh);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        Random rnd = new Random();
                        int index = rnd.Next(walkInAccounts.Count);
                        maKh = walkInAccounts[index];
                    }

                    string maLd = "WLK" + MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().ToString("HHmmss");
                    decimal tongTien = request.Services.Sum(s => s.Gia);

                    var ld = new Lichdat
                    {
                        MaLd = maLd,
                        MaKh = maKh,
                        NgayHen = DateOnly.FromDateTime(DateTime.Today),
                        GioBatDau = TimeOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()),
                        GioKetThuc = TimeOnly.FromDateTime(MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()).AddMinutes(60),
                        TongTienDuKien = tongTien,
                        TrangThai = 3
                    };
                    _context.Lichdats.Add(ld);

                    string thongTinKhachThat = $"Tên thật: {request.TenKhach} - SĐT: {request.Sdt}";

                    foreach (var s in request.Services)
                    {
                        var ct = new ChitietLichdat
                        {
                            MaLd = maLd,
                            MaNv = request.MaStylist,
                            GiaLucDat = s.Gia,
                            GhiChu = thongTinKhachThat
                        };
                        if (s.Id.StartsWith("DV")) ct.MaDv = s.Id;
                        else ct.MaCb = s.Id;
                        _context.ChitietLichdats.Add(ct);
                    }

                    var hd = new HoaDon
                    {
                        MaHd = maLd,
                        MaLd = maLd,
                        MaKh = maKh,
                        NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
                        TongTien = tongTien,
                        TienDaTra = 0,
                        TrangThai = 0
                    };
                    _context.HoaDons.Add(hd);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { message = $"Đã thêm khách vãng lai thành công! (Lưu vào mã: {maKh})", maLd });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return BadRequest("Lỗi: " + (ex.InnerException?.Message ?? ex.Message));
                }
            });
        }
    
        // 7. Quét & Gửi Email thông báo Tạm Hủy cho các đơn trễ hẹn
        [HttpPost("gui-mail-don-tre")]
        [HttpPatch("gui-mail-don-tre")]
        public async Task<IActionResult> GuiMailDonTre([FromBody] GuiMailDonTreRequest? request)
        {
            try
            {
                var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
                var today = DateOnly.FromDateTime(now);
                var currentTime = TimeOnly.FromDateTime(now);

                List<Lichdat> dsDonTre = new();

                if (request != null && !string.IsNullOrEmpty(request.MaLd))
                {
                    var cleanId = request.MaLd.Trim();
                    var ld = await _context.Lichdats
                        .Include(x => x.MaKhNavigation)
                        .FirstOrDefaultAsync(x => x.MaLd.Trim() == cleanId);
                    if (ld != null) dsDonTre.Add(ld);
                }
                else
                {
                    var query = await _context.Lichdats
                        .Include(x => x.MaKhNavigation)
                        .Where(x => x.NgayHen <= today && (x.TrangThai == 1 || x.TrangThai == 2) && x.GioBatDau.HasValue)
                        .ToListAsync();

                    foreach (var ld in query)
                    {
                        if (ld.NgayHen < today || (ld.NgayHen == today && currentTime > ld.GioBatDau.Value.AddMinutes(30)))
                        {
                            dsDonTre.Add(ld);
                        }
                    }
                }

                if (!dsDonTre.Any())
                {
                    return Ok(new { success = true, count = 0, message = "Hiện không có đơn nào bị quá giờ hẹn trên 30 phút." });
                }

                int countSuccess = 0;
                List<string> sentEmails = new();

                foreach (var ld in dsDonTre)
                {
                    ld.TrangThai = 4;
                    var hd = await _context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == ld.MaLd);
                    if (hd != null) hd.TrangThai = 4;

                    var email = ld.MaKhNavigation?.Email;
                    var tenKh = ld.MaKhNavigation?.HoTen ?? "Quý khách";
                    var maDon = hd?.MaHd ?? ld.MaLd;

                    if (!string.IsNullOrEmpty(email) && _emailService != null)
                    {
                        try
                        {
                            var subject = $"[LG Hair Salon] Thông báo tạm hủy lịch hẹn #{maDon} & Mở khóa quyền Dời lịch / Hoàn tiền";
                            var htmlContent = $@"
                            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 14px; background-color: #ffffff;'>
                                <div style='text-align: center; padding-bottom: 18px; border-bottom: 2px solid #f1f5f9;'>
                                    <h1 style='color: #0f172a; margin: 0; font-size: 24px; letter-spacing: 2px; font-weight: 800; text-transform: uppercase;'>LG HAIR SALON</h1>
                                    <p style='color: #64748b; font-size: 13px; margin: 6px 0 0; text-transform: uppercase; letter-spacing: 1px;'>Thông báo tình trạng lịch hẹn phục vụ</p>
                                </div>
                                <div style='padding: 24px 0;'>
                                    <p style='font-size: 15px; color: #1e293b;'>Kính gửi <b>{tenKh}</b>,</p>
                                    <p style='font-size: 14px; color: #334155; line-height: 1.6;'>
                                        Lịch hẹn của Quý khách (Mã đơn: <b style='color: #be123c;'>#{maDon}</b>) đã được chuyển sang trạng thái <b>Tạm hủy</b> do đã quá thời gian hẹn phục vụ hơn 30 phút mà Salon chưa tiếp nhận thủ tục check-in.
                                    </p>
                                    <div style='background-color: #fffbeb; border: 1px solid #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                                        <p style='margin: 0 0 10px; font-size: 14px; font-weight: bold; color: #92400e;'>📋 Hệ thống đã Mở Khóa Quyền Hạn cho đơn hàng #{maDon}:</p>
                                        <ul style='margin: 0; padding-left: 20px; font-size: 13.5px; color: #78350f; line-height: 1.7;'>
                                            <li><b>Lựa chọn 1 (Khuyên dùng):</b> Quý khách vào website <b>LG Hair Salon &gt; Tài khoản &gt; Lịch sử hóa đơn</b> và bấm nút <b>[🔄 Dời lịch]</b> để chọn lại Ngày &amp; Giờ hẹn mới hoàn toàn <b>MIỄN PHÍ (Bảo lưu 100% tiền cọc)</b>.</li>
                                            <li><b>Lựa chọn 2:</b> Bấm nút <b>[💳 Hoàn tiền]</b> để điền thông tin Số tài khoản ngân hàng nhận tiền hoàn cọc qua VietQR.</li>
                                            <li><b>Thời hạn xử lý:</b> Trong vòng <b>3 ngày</b> kể từ hôm nay. Sau 3 ngày, phí thủ tục bảo lưu đơn là <b>5.000đ/ngày</b> quá hạn.</li>
                                        </ul>
                                    </div>
                                    <div style='background: #f8fafc; border: 1px dashed #cbd5e1; padding: 16px; border-radius: 8px; text-align: center; margin-top: 24px;'>
                                        <p style='margin: 0; font-size: 14px; color: #0f172a; font-weight: 600;'>
                                            👉 Quý khách chỉ cần mở website LG Hair Salon, đăng nhập tài khoản và vào mục <span style='color: #be123c;'>""Lịch sử hóa đơn""</span> để thao tác ngay!
                                        </p>
                                    </div>
                                    <p style='font-size: 13px; color: #64748b; text-align: center; margin-top: 24px; margin-bottom: 0;'>
                                        Hotline hỗ trợ trực tiếp 24/7: <b style='color: #0f172a;'>0901.234.567</b>
                                    </p>
                                </div>
                                <div style='border-top: 1px solid #f1f5f9; padding-top: 16px; text-align: center; font-size: 12px; color: #94a3b8;'>
                                    © 2026 LG Hair Salon. Trân trọng cảm ơn Quý khách!
                                </div>
                            </div>
                            ";
                            await _emailService.SendEmailAsync(email, subject, htmlContent);
                            sentEmails.Add(email);
                            countSuccess++;
                        }
                        catch (Exception exMail)
                        {
                            Console.WriteLine($"[EMAIL_ERROR] {exMail.Message}");
                        }
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    count = countSuccess,
                    message = $"Đã tạm hủy và gửi Gmail thành công cho {countSuccess} đơn trễ hẹn!",
                    emails = sentEmails
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xử lý gửi mail: " + ex.Message });
            }
        }
    }

    public class GuiMailDonTreRequest
    {
        public string? MaLd { get; set; }
    }

    public class CheckInConfirmRequest
    {
        public string MaLd { get; set; }
        public string MaStylist { get; set; }
    }

    public class PaymentCheckInRequest
    {
        public string MaLd { get; set; }
        public string MaStylist { get; set; }
        public string? PaymentMethod { get; set; }
        public List<ServiceItemDTO> UpdatedServices { get; set; }
    }

    public class WalkInRequest
    {
        public string TenKhach { get; set; }
        public string Sdt { get; set; }
        public string MaStylist { get; set; }
        public List<ServiceItemDTO> Services { get; set; }
    }

    public class ServiceItemDTO
    {
        public string Id { get; set; }
        public decimal Gia { get; set; }
    }
}
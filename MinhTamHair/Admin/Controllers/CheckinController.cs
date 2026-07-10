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

        public CheckInController(MinhTamHairContext context)
        {
            _context = context;
        }

        // 1. Lấy danh sách khách hàng chờ check-in theo ngày
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
            var lichDat = await _context.Lichdats.FindAsync(request.MaLd);
            if (lichDat == null) return NotFound("Không tìm thấy lịch đặt");

            lichDat.TrangThai = 3;

            var chiTietLichDats = await _context.ChitietLichdats
                .Where(ct => ct.MaLd == request.MaLd)
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
            var now = DateTime.Now;

            var servicesRaw = await _context.Dichvus
                .Where(s => s.TrangThai == 1)
                .Include(x => x.ChitietFlashsales)
                    .ThenInclude(fs => fs.MaFlNavigation)
                .ToListAsync();

            var services = servicesRaw.Select(s => {
                var flash = s.ChitietFlashsales
                    .FirstOrDefault(fs =>
                        fs.MaFlNavigation.TrangThai == 1 &&
                        fs.MaFlNavigation.NgayBatDau <= now &&
                        fs.MaFlNavigation.NgayKetThuc >= now);

                decimal finalPrice = (decimal)s.Gia;
                if (flash != null && flash.PhanTramGiam.HasValue)
                    finalPrice = (decimal)s.Gia - ((decimal)s.Gia * (decimal)flash.PhanTramGiam.Value / 100m);

                return new
                {
                    Id = s.MaDv,
                    Name = s.TenDv,
                    Price = finalPrice,
                    OriginalPrice = s.Gia,
                    Type = "DV"
                };
            }).ToList();

            var combos = await _context.Combos
                .Where(c => c.TrangThai == 1)
                .Select(c => new {
                    Id = c.MaCb,
                    Name = c.TenCombo,
                    Price = c.Gia,
                    OriginalPrice = c.Gia,
                    Type = "CB"
                }).ToListAsync();

            var result = services.Cast<object>().Concat(combos.Cast<object>()).ToList();
            return Ok(result);
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
                    var ld = await _context.Lichdats.FirstOrDefaultAsync(x => x.MaLd == request.MaLd);
                    if (ld == null) return NotFound("Không tìm thấy lịch đặt");

                    ld.TrangThai = 3;

                    var oldDetails = await _context.ChitietLichdats.Where(ct => ct.MaLd == request.MaLd).ToListAsync();
                    _context.ChitietLichdats.RemoveRange(oldDetails);

                    foreach (var item in request.UpdatedServices)
                    {
                        var newDetail = new ChitietLichdat
                        {
                            MaLd = request.MaLd,
                            MaNv = request.MaStylist,
                            GiaLucDat = item.Gia
                        };
                        if (item.Id.StartsWith("DV")) newDetail.MaDv = item.Id;
                        else newDetail.MaCb = item.Id;
                        _context.ChitietLichdats.Add(newDetail);
                    }

                    // TÍNH TOÁN LẠI TỔNG TIỀN MỚI
                    decimal tongDichVuMoi = request.UpdatedServices.Sum(s => s.Gia);

                    var hoaDon = await _context.HoaDons.FirstOrDefaultAsync(hd => hd.MaLd == request.MaLd);
                    if (hoaDon != null)
                    {
                        // Lấy tiền sản phẩm cũ từ CSDL để cộng dồn vào tổng mới
                        decimal tongSanPhamDaDat = await _context.ChitietHoadonSps
                            .Where(ct => ct.MaHd == hoaDon.MaHd)
                            .SumAsync(ct => ct.ThanhTien ?? 0);

                        // Tổng tiền mới = Tổng dịch vụ mới + Tổng sản phẩm cũ
                        decimal tongTienMoi = tongDichVuMoi + tongSanPhamDaDat;

                        hoaDon.TongTien = tongTienMoi;
                        ld.TongTienDuKien = tongTienMoi; // Cập nhật đồng bộ lịch đặt
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { message = "Đã chốt dịch vụ thành công! Đơn đã chuyển sang trạng thái Đang phục vụ." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return BadRequest("Lỗi xử lý: " + ex.Message);
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
                        maKh = "KVL" + DateTime.Now.ToString("ddHHmm");
                        var newKh = new Khachhang
                        {
                            MaKh = maKh,
                            HoTen = "Khách vãng lai",
                            Sdt = "0000000000",
                            Email = "khachvanglai@gmail.com",
                            TrangThai = 1,
                            NgayTao = DateTime.Now,
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

                    string maLd = "WLK" + DateTime.Now.ToString("HHmmss");
                    decimal tongTien = request.Services.Sum(s => s.Gia);

                    var ld = new Lichdat
                    {
                        MaLd = maLd,
                        MaKh = maKh,
                        NgayHen = DateOnly.FromDateTime(DateTime.Today),
                        GioBatDau = TimeOnly.FromDateTime(DateTime.Now),
                        GioKetThuc = TimeOnly.FromDateTime(DateTime.Now).AddMinutes(60),
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
                        NgayTao = DateTime.Now,
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
        public string PaymentMethod { get; set; }
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
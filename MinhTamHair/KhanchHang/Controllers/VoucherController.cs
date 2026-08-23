using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.KhanchHang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VoucherController : ControllerBase
    {
        private readonly MinhTamHairContext _context;

        public VoucherController(MinhTamHairContext context)
        {
            _context = context;
        }

        [HttpGet("available")]
        public async Task<IActionResult> GetAvailable([FromQuery] string? maKh)
        {
            Khachhang? khachHang = null;
            var collectedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int? rankDiscountPercent = null;
            string? maRank = null;

            if (!string.IsNullOrWhiteSpace(maKh))
            {
                khachHang = await _context.Khachhangs
                    .Include(k => k.MaCodes)
                    .FirstOrDefaultAsync(k => k.MaKh == maKh);

                if (khachHang != null)
                {
                    collectedCodes = khachHang.MaCodes
                        .Select(v => v.MaCode)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    maRank = khachHang.MaRank;

                    if (!string.IsNullOrWhiteSpace(maRank))
                    {
                        var rank = await _context.Hangthanhviens
                            .FirstOrDefaultAsync(r => r.MaRank == maRank && (r.TrangThai == null || r.TrangThai == 1));

                        rankDiscountPercent = rank?.PhanTramGiamGia;
                    }
                }
            }



            var query = _context.Vouchers.AsQueryable();

            // Không lọc theo rank ở bước hiển thị (ưu đãi rank xử lý khi Collect).

            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            // Chỉ đưa vào danh sách công khai: còn hiệu lực + trong thời gian.
            // Ẩn hoàn toàn các voucher đã hết hạn hoặc bị vô hiệu khỏi trang chủ/khách hàng.
            query = query.Where(v =>
                (v.TrangThai == null || v.TrangThai == 1)
                && (!v.NgayBatDau.HasValue || v.NgayBatDau <= now)
                && (!v.NgayKetThuc.HasValue || v.NgayKetThuc >= now)
                && (
                    (!v.SoLuong.HasValue || (v.SoLuongDaDung ?? 0) < v.SoLuong)
                    || (collectedCodes.Count > 0 && collectedCodes.Contains(v.MaCode))
                ));

            var vouchers = await query
                .OrderByDescending(v => v.NgayBatDau ?? DateTime.MinValue)
                .ToListAsync();

            var result = vouchers.Select(v => new
            {
                maCode = v.MaCode,
                moTa = v.MoTa,
                phanTramGiam = v.PhanTramGiam,
                soTienGiam = v.SoTienGiam,
                giaTriToiThieu = v.GiaTriToiThieu,
                ngayBatDau = v.NgayBatDau,
                ngayKetThuc = v.NgayKetThuc,
                soLuong = v.SoLuong,
                soLuongDaDung = v.SoLuongDaDung,
                trangThai = v.TrangThai,
                isCollected = collectedCodes.Contains(v.MaCode),
                userRank = maRank,
                rankDiscountPercent
            });

            return Ok(result);
        }

        [HttpGet("kho")]
        public async Task<IActionResult> GetKho([FromQuery] string maKh)
        {
            if (string.IsNullOrWhiteSpace(maKh))
            {
                return BadRequest(new { message = "Mã khách hàng không hợp lệ." });
            }

            var khachHang = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == maKh);

            if (khachHang == null)
            {
                return NotFound(new { message = "Không tìm thấy khách hàng." });
            }

            var usedCodes = await _context.HoaDons
                .Where(h => h.MaKh == maKh && h.MaCode != null)
                .Select(h => h.MaCode!)
                .ToListAsync();

            var usedCodeSet = usedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();


            var result = khachHang.MaCodes
                .Where(v => (v.TrangThai == null || v.TrangThai == 1) && (!v.NgayKetThuc.HasValue || v.NgayKetThuc >= now))
                .Select(v => new
                {
                    maCode = v.MaCode,
                    moTa = v.MoTa,
                    phanTramGiam = v.PhanTramGiam,
                    soTienGiam = v.SoTienGiam,
                    giaTriToiThieu = v.GiaTriToiThieu,
                    ngayBatDau = v.NgayBatDau,
                    ngayKetThuc = v.NgayKetThuc,
                    soLuong = v.SoLuong,
                    soLuongDaDung = v.SoLuongDaDung,
                    trangThai = v.TrangThai,
                    isUsed = usedCodeSet.Contains(v.MaCode),
                    isExpired = v.NgayKetThuc.HasValue && v.NgayKetThuc < now
                });

            return Ok(result);
        }

        [HttpPost("collect")]
        public async Task<IActionResult> Collect([FromBody] CollectVoucherRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MaKh) || string.IsNullOrWhiteSpace(request.MaCode))
            {
                return BadRequest(new { message = "Thiếu mã khách hàng hoặc mã voucher." });
            }

            var khachHang = await _context.Khachhangs
                .Include(k => k.MaCodes)
                .FirstOrDefaultAsync(k => k.MaKh == request.MaKh);

            if (khachHang == null)
            {
                return NotFound(new { message = "Không tìm thấy khách hàng." });
            }

            var voucher = await _context.Vouchers.FirstOrDefaultAsync(v => v.MaCode == request.MaCode);
            if (voucher == null)
            {
                return NotFound(new { message = "Không tìm thấy voucher." });
            }

            if (khachHang.MaCodes.Any(v => v.MaCode == request.MaCode))
            {
                return Ok(new { message = "Voucher đã có trong kho." });
            }

            var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
            if (voucher.TrangThai.HasValue && voucher.TrangThai != 1)
            {
                return BadRequest(new { message = "Voucher không còn hiệu lực." });
            }

            if ((voucher.NgayBatDau.HasValue && voucher.NgayBatDau > now) ||
                (voucher.NgayKetThuc.HasValue && voucher.NgayKetThuc < now))
            {
                return BadRequest(new { message = "Voucher đã hết hạn hoặc chưa đến thời gian áp dụng." });
            }

            if (voucher.SoLuong.HasValue && voucher.SoLuongDaDung.HasValue &&
                voucher.SoLuongDaDung >= voucher.SoLuong)
            {
                return BadRequest(new { message = "Voucher đã hết lượt." });
            }

            // Không chặn nhận mã theo trần % hạng — trần chỉ áp khi áp dụng đơn (VoucherCartLogic).

            khachHang.MaCodes.Add(voucher);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã thêm voucher vào kho." });
        }
    }

    public class CollectVoucherRequest
    {
        public string? MaKh { get; set; }
        public string? MaCode { get; set; }
    }
}

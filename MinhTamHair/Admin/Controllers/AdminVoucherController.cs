using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;


[Route("api/admin/vouchers")]
[ApiController]
public class AdminVoucherController : ControllerBase
{
    private readonly MinhTamHairContext _context;

    public AdminVoucherController(MinhTamHairContext context)
    {
        _context = context;
    }

    /// <param name="lifecycle">
    /// Lọc vòng đời: <c>dangChay</c> (đang chạy), <c>sapChay</c> (sắp chạy), <c>hetHan</c> (đã hết hạn),
    /// <c>hetLuot</c> (đã hết lượt), để trống hoặc <c>tatCa</c> = tất cả.
    /// </param>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? lifecycle = null)
    {
        var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
        await MinhTamHair.VoucherCartLogic.AutoDisableExpiredVouchersAsync(_context, now);
        var key = NormalizeLifecycleKey(lifecycle);

        try
        {
            var query = ApplyLifecycleFilter(_context.Vouchers.AsNoTracking(), key, now)
                .OrderByDescending(v => v.NgayBatDau);

            var list = await query
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
                   
                    soKhachDaLuu = v.MaKhs.Count()
                })
                .ToListAsync();

            return Ok(list);
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            var rows = await _context.Database
                .SqlQuery<AdminVoucherDbRow>($@"
                    SELECT maCode, moTa, phanTramGiam, soTienGiam, giaTriToiThieu,
                           ngayBatDau, ngayKetThuc, soLuong, soLuongDaDung, trangThai
                    FROM VOUCHER
                    ORDER BY ngayBatDau DESC")
                .ToListAsync();

            IEnumerable<AdminVoucherDbRow> filtered = rows;
            if (key != LifecycleFilterKey.TatCa)
                filtered = rows.Where(r => MatchesLifecycleRow(r, key, now));

            var codes = filtered.Select(r => r.MaCode).Distinct().ToList();
            var countMap = await LoadKhachVoucherCountsByCodeAsync(codes);

            var list = filtered
                .Select(r => new
                {
                    maCode = r.MaCode,
                    moTa = r.MoTa,
                    phanTramGiam = r.PhanTramGiam,
                    soTienGiam = r.SoTienGiam,
                    giaTriToiThieu = r.GiaTriToiThieu,
                    ngayBatDau = r.NgayBatDau,
                    ngayKetThuc = r.NgayKetThuc,
                    soLuong = r.SoLuong,
                    soLuongDaDung = r.SoLuongDaDung,
                    trangThai = r.TrangThai,
                    apDungMaRank = (string?)null,
                    soKhachDaLuu = countMap.GetValueOrDefault(r.MaCode, 0)
                })
                .ToList();

            return Ok(list);
        }
    }

    /// <summary>Chi tiết voucher + khách đã thu thập (KHACHHANG_VOUCHER) + hóa đơn đã áp mã (HOA_DON).</summary>
    [HttpGet("{maCode}/detail")]
    public async Task<IActionResult> GetDetail(string maCode)
    {
        if (string.IsNullOrWhiteSpace(maCode))
            return BadRequest(new { message = "Thiếu mã voucher." });

        try
        {
            await MinhTamHair.VoucherCartLogic.AutoDisableExpiredVouchersAsync(_context, MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());
            var head = await _context.Vouchers
                .AsNoTracking()
                .Where(x => x.MaCode == maCode)
                .Select(x => new
                {
                    maCode = x.MaCode,
                    moTa = x.MoTa,
                    phanTramGiam = x.PhanTramGiam,
                    soTienGiam = x.SoTienGiam,
                    giaTriToiThieu = x.GiaTriToiThieu,
                    ngayBatDau = x.NgayBatDau,
                    ngayKetThuc = x.NgayKetThuc,
                    soLuong = x.SoLuong,
                    soLuongDaDung = x.SoLuongDaDung,
                    trangThai = x.TrangThai,
                    
                    soKhachDaLuu = x.MaKhs.Count()
                })
                .FirstOrDefaultAsync();

            if (head == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var khachDaThuThap = await _context.Khachhangs
                .AsNoTracking()
                .Where(k => k.MaCodes.Any(v => v.MaCode == maCode))
                .OrderBy(k => k.MaKh)
                .Select(k => new
                {
                    maKh = k.MaKh,
                    hoTen = k.HoTen,
                    sdt = k.Sdt,
                    email = k.Email
                })
                .ToListAsync();

            var hoaDonApDung = await _context.HoaDons
                .AsNoTracking()
                .Where(h => h.MaCode == maCode)
                .OrderByDescending(h => h.NgayTao)
                .Select(h => new
                {
                    maHd = h.MaHd,
                    maKh = h.MaKh,
                    ngayTao = h.NgayTao,
                    tongTien = h.TongTien,
                    thanhTien = h.ThanhTien,
                    trangThai = h.TrangThai
                })
                .ToListAsync();

            return Ok(new
            {
                head.maCode,
                head.moTa,
                head.phanTramGiam,
                head.soTienGiam,
                head.giaTriToiThieu,
                head.ngayBatDau,
                head.ngayKetThuc,
                head.soLuong,
                head.soLuongDaDung,
                head.trangThai,
                
                head.soKhachDaLuu,
                khachDaThuThap,
                hoaDonApDung
            });
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            var r = await _context.Database
                .SqlQuery<AdminVoucherDbRow>($@"
                    SELECT maCode, moTa, phanTramGiam, soTienGiam, giaTriToiThieu,
                           ngayBatDau, ngayKetThuc, soLuong, soLuongDaDung, trangThai
                    FROM VOUCHER
                    WHERE maCode = {maCode}")
                .FirstOrDefaultAsync();

            if (r == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var khCount = await _context.Database
                .SqlQuery<CountRow>($"SELECT CAST(COUNT(*) AS int) AS Value FROM KHACHHANG_VOUCHER WHERE maCode = {maCode}")
                .SingleAsync();

            var khachDaThuThap = await _context.Database
                .SqlQuery<KhachThuThapRow>($@"
                    SELECT k.maKH AS MaKh, k.hoTen AS HoTen, k.SDT AS Sdt, k.email AS Email
                    FROM KHACHHANG_VOUCHER j
                    INNER JOIN KHACHHANG k ON k.maKH = j.maKH
                    WHERE j.maCode = {maCode}
                    ORDER BY k.maKH")
                .ToListAsync();

            var hoaDonApDung = await _context.Database
                .SqlQuery<HoaDonVoucherRow>($@"
                    SELECT maHD AS MaHd, maKH AS MaKh, ngayTao AS NgayTao,
                           tongTien AS TongTien, thanhTien AS ThanhTien, trangThai AS TrangThai
                    FROM HOA_DON
                    WHERE maCode = {maCode}
                    ORDER BY ngayTao DESC")
                .ToListAsync();

            return Ok(new
            {
                maCode = r.MaCode,
                moTa = r.MoTa,
                phanTramGiam = r.PhanTramGiam,
                soTienGiam = r.SoTienGiam,
                giaTriToiThieu = r.GiaTriToiThieu,
                ngayBatDau = r.NgayBatDau,
                ngayKetThuc = r.NgayKetThuc,
                soLuong = r.SoLuong,
                soLuongDaDung = r.SoLuongDaDung,
                trangThai = r.TrangThai,
                apDungMaRank = (string?)null,
                soKhachDaLuu = khCount.Value,
                khachDaThuThap = khachDaThuThap.Select(k => new
                {
                    maKh = k.MaKh,
                    hoTen = k.HoTen,
                    sdt = k.Sdt,
                    email = k.Email
                }),
                hoaDonApDung = hoaDonApDung.Select(h => new
                {
                    maHd = h.MaHd,
                    maKh = h.MaKh,
                    ngayTao = h.NgayTao,
                    tongTien = h.TongTien,
                    thanhTien = h.ThanhTien,
                    trangThai = h.TrangThai
                })
            });
        }
    }

    [HttpGet("{maCode}")]
    public async Task<IActionResult> GetOne(string maCode)
    {
        try
        {
            await MinhTamHair.VoucherCartLogic.AutoDisableExpiredVouchersAsync(_context, MinhTamHair.Services.TimeHelper.VnTimeHelper.Now());
            var v = await _context.Vouchers
                .AsNoTracking()
                .Where(x => x.MaCode == maCode)
                .Select(x => new
                {
                    maCode = x.MaCode,
                    moTa = x.MoTa,
                    phanTramGiam = x.PhanTramGiam,
                    soTienGiam = x.SoTienGiam,
                    giaTriToiThieu = x.GiaTriToiThieu,
                    ngayBatDau = x.NgayBatDau,
                    ngayKetThuc = x.NgayKetThuc,
                    soLuong = x.SoLuong,
                    soLuongDaDung = x.SoLuongDaDung,
                    trangThai = x.TrangThai,
                    
                    soKhachDaLuu = x.MaKhs.Count()
                })
                .FirstOrDefaultAsync();

            if (v == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            return Ok(v);
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            var r = await _context.Database
                .SqlQuery<AdminVoucherDbRow>($@"
                    SELECT maCode, moTa, phanTramGiam, soTienGiam, giaTriToiThieu,
                           ngayBatDau, ngayKetThuc, soLuong, soLuongDaDung, trangThai
                    FROM VOUCHER
                    WHERE maCode = {maCode}")
                .FirstOrDefaultAsync();

            if (r == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var khCount = await _context.Database
                .SqlQuery<CountRow>($"SELECT CAST(COUNT(*) AS int) AS Value FROM KHACHHANG_VOUCHER WHERE maCode = {maCode}")
                .SingleAsync();

            return Ok(new
            {
                maCode = r.MaCode,
                moTa = r.MoTa,
                phanTramGiam = r.PhanTramGiam,
                soTienGiam = r.SoTienGiam,
                giaTriToiThieu = r.GiaTriToiThieu,
                ngayBatDau = r.NgayBatDau,
                ngayKetThuc = r.NgayKetThuc,
                soLuong = r.SoLuong,
                soLuongDaDung = r.SoLuongDaDung,
                trangThai = r.TrangThai,
                apDungMaRank = (string?)null,
                soKhachDaLuu = khCount.Value
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminVoucherWriteDto dto)
    {
        var err = ValidateCreate(dto);
        if (err != null)
            return BadRequest(new { message = err });

        var code = VoucherCartLogic.NormalizeMaCode(dto.MaCode!);
        if (!VoucherCartLogic.MaCodePattern.IsMatch(code))
            return BadRequest(new { message = "Mã voucher chỉ gồm chữ in hoa và số, không dấu cách, không ký tự đặc biệt." });
        if (code.Length > 20)
            return BadRequest(new { message = "Mã voucher tối đa 20 ký tự." });

        if (await VoucherCodeExistsAsync(code))
            return Conflict(new { message = "Mã voucher đã tồn tại — không thể trùng mã." });

        var (pt, money, discountErr) = NormalizeDiscountPair(dto.PhanTramGiam, dto.SoTienGiam);
        if (discountErr != null)
            return BadRequest(new { message = discountErr });

        var entity = new Voucher
        {
            MaCode = code,
            MoTa = string.IsNullOrWhiteSpace(dto.MoTa) ? null : dto.MoTa.Trim(),
            PhanTramGiam = pt,
            SoTienGiam = money,
            GiaTriToiThieu = dto.GiaTriToiThieu,
            NgayBatDau = MinhTamHair.Services.TimeHelper.VnTimeHelper.ToVnTime(dto.NgayBatDau),
            NgayKetThuc = MinhTamHair.Services.TimeHelper.VnTimeHelper.ToVnTime(dto.NgayKetThuc),
            SoLuong = dto.SoLuong,
            SoLuongDaDung = 0,
            TrangThai = 1,
            
        };

        try
        {
            _context.Vouchers.Add(entity);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            DetachAllTracked();
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO VOUCHER (maCode, moTa, phanTramGiam, soTienGiam, giaTriToiThieu, ngayBatDau, ngayKetThuc, soLuong, soLuongDaDung, trangThai)
                VALUES (
                    {entity.MaCode},
                    {entity.MoTa},
                    {entity.PhanTramGiam},
                    {entity.SoTienGiam},
                    {entity.GiaTriToiThieu},
                    {entity.NgayBatDau},
                    {entity.NgayKetThuc},
                    {entity.SoLuong},
                    {entity.SoLuongDaDung},
                    {entity.TrangThai})");
        }

        return CreatedAtAction(nameof(GetOne), new { maCode = entity.MaCode }, new { maCode = entity.MaCode });
    }

    [HttpPut("{maCode}")]
    public async Task<IActionResult> Update(string maCode, [FromBody] AdminVoucherWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(maCode))
            return BadRequest(new { message = "Thiếu mã voucher." });

        try
        {
            var entity = await _context.Vouchers
                .Include(v => v.MaKhs)
                .FirstOrDefaultAsync(v => v.MaCode == maCode);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var savedCount = entity.MaKhs.Count;
            var patchErr = BuildUpdatePatch(entity, dto, savedCount, out var patch);
            if (patchErr != null)
                return BadRequest(new { message = patchErr });

            entity.MoTa = patch.MoTa;
            entity.PhanTramGiam = patch.PhanTramGiam;
            entity.SoTienGiam = patch.SoTienGiam;
            entity.GiaTriToiThieu = patch.GiaTriToiThieu;
            entity.NgayBatDau = patch.NgayBatDau;
            entity.NgayKetThuc = patch.NgayKetThuc;
            entity.SoLuong = patch.SoLuong;
            entity.TrangThai = patch.TrangThai;
           

            await _context.SaveChangesAsync();
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            DetachAllTracked();
            var cur = await _context.Database
                .SqlQuery<AdminVoucherDbRow>($@"
                    SELECT maCode, moTa, phanTramGiam, soTienGiam, giaTriToiThieu,
                           ngayBatDau, ngayKetThuc, soLuong, soLuongDaDung, trangThai
                    FROM VOUCHER WHERE maCode = {maCode}")
                .FirstOrDefaultAsync();
            if (cur == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var temp = new Voucher
            {
                MaCode = cur.MaCode,
                MoTa = cur.MoTa,
                PhanTramGiam = cur.PhanTramGiam,
                SoTienGiam = cur.SoTienGiam,
                GiaTriToiThieu = cur.GiaTriToiThieu,
                NgayBatDau = cur.NgayBatDau,
                NgayKetThuc = cur.NgayKetThuc,
                SoLuong = cur.SoLuong,
                SoLuongDaDung = cur.SoLuongDaDung,
                TrangThai = cur.TrangThai,
             
            };

            var savedCt = await _context.Database
                .SqlQuery<CountRow>($"SELECT CAST(COUNT(*) AS int) AS Value FROM KHACHHANG_VOUCHER WHERE maCode = {maCode}")
                .SingleAsync();

            var patchErr = BuildUpdatePatch(temp, dto, savedCt.Value, out var patch);
            if (patchErr != null)
                return BadRequest(new { message = patchErr });

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE VOUCHER SET
                    moTa = {patch.MoTa},
                    phanTramGiam = {patch.PhanTramGiam},
                    soTienGiam = {patch.SoTienGiam},
                    giaTriToiThieu = {patch.GiaTriToiThieu},
                    ngayBatDau = {patch.NgayBatDau},
                    ngayKetThuc = {patch.NgayKetThuc},
                    soLuong = {patch.SoLuong},
                    trangThai = {patch.TrangThai}
                WHERE maCode = {maCode}");
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Xóa mềm: luôn chỉ cập nhật <c>trangThai = 0</c> (ngừng hoạt động). Không dùng DELETE để tránh lỗi FK và mất lịch sử.
    /// </summary>
    [HttpDelete("{maCode}")]
    public async Task<IActionResult> Delete(string maCode)
    {
        const string msg = "Đã đặt voucher ở trạng thái ngừng hoạt động (giữ nguyên lịch sử hóa đơn và khách đã lưu mã).";

        try
        {
            var entity = await _context.Vouchers.FirstOrDefaultAsync(v => v.MaCode == maCode);

            if (entity == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            entity.TrangThai = 0;
            await _context.SaveChangesAsync();
            return Ok(new { message = msg, softDeleted = true });
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            DetachAllTracked();

            var exists = await _context.Database
                .SqlQuery<MaCodeOnlyRow>($"SELECT maCode FROM VOUCHER WHERE maCode = {maCode}")
                .FirstOrDefaultAsync();
            if (exists == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE VOUCHER SET trangThai = 0 WHERE maCode = {maCode}");
            return Ok(new { message = msg, softDeleted = true });
        }
    }

    private async Task<bool> VoucherCodeExistsAsync(string code)
    {
        try
        {
            return await _context.Vouchers.AnyAsync(v => v.MaCode == code);
        }
        catch (Exception ex) when (IsMissingApDungMaRankColumn(ex))
        {
            var dup = await _context.Database
                .SqlQuery<MaCodeOnlyRow>($"SELECT maCode FROM VOUCHER WHERE maCode = {code}")
                .FirstOrDefaultAsync();
            return dup != null;
        }
    }

    private void DetachAllTracked()
    {
        foreach (var e in _context.ChangeTracker.Entries().ToList())
            e.State = EntityState.Detached;
    }

    private enum LifecycleFilterKey
    {
        TatCa,
        DangChay,
        SapChay,
        HetHan,
        HetLuot
    }

    private static LifecycleFilterKey NormalizeLifecycleKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return LifecycleFilterKey.TatCa;

        return raw.Trim().ToLowerInvariant() switch
        {
            "dangchay" or "dang-chay" or "running" => LifecycleFilterKey.DangChay,
            "sapchay" or "sap-chay" or "upcoming" => LifecycleFilterKey.SapChay,
            "hethan" or "het-han" or "expired" => LifecycleFilterKey.HetHan,
            "hetluot" or "het-luot" or "depleted" => LifecycleFilterKey.HetLuot,
            "tatca" or "tat-ca" or "all" => LifecycleFilterKey.TatCa,
            _ => LifecycleFilterKey.TatCa
        };
    }

    private static IQueryable<Voucher> ApplyLifecycleFilter(IQueryable<Voucher> q, LifecycleFilterKey key, DateTime now)
    {
        return key switch
        {
            LifecycleFilterKey.DangChay => q.Where(v => v.TrangThai == 1
                && (v.NgayBatDau == null || v.NgayBatDau <= now)
                && (v.NgayKetThuc == null || v.NgayKetThuc >= now)
                && (v.SoLuong == null || (v.SoLuongDaDung ?? 0) < v.SoLuong)),
            LifecycleFilterKey.SapChay => q.Where(v => v.TrangThai == 1 && v.NgayBatDau != null && v.NgayBatDau > now),
            LifecycleFilterKey.HetHan => q.Where(v => v.NgayKetThuc != null && v.NgayKetThuc < now),
            LifecycleFilterKey.HetLuot => q.Where(v => v.SoLuong != null && (v.SoLuongDaDung ?? 0) >= v.SoLuong),
            _ => q
        };
    }

    private static bool MatchesLifecycleRow(AdminVoucherDbRow r, LifecycleFilterKey key, DateTime now)
    {
        return key switch
        {
            LifecycleFilterKey.DangChay => r.TrangThai == 1
                && (r.NgayBatDau == null || r.NgayBatDau <= now)
                && (r.NgayKetThuc == null || r.NgayKetThuc >= now)
                && (r.SoLuong == null || (r.SoLuongDaDung ?? 0) < r.SoLuong),
            LifecycleFilterKey.SapChay => r.TrangThai == 1 && r.NgayBatDau != null && r.NgayBatDau > now,
            LifecycleFilterKey.HetHan => r.NgayKetThuc != null && r.NgayKetThuc < now,
            LifecycleFilterKey.HetLuot => r.SoLuong != null && (r.SoLuongDaDung ?? 0) >= r.SoLuong,
            _ => true
        };
    }

    private async Task<Dictionary<string, int>> LoadKhachVoucherCountsByCodeAsync(IReadOnlyList<string> codes)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in codes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var n = await _context.Database
                .SqlQuery<CountRow>($"SELECT CAST(COUNT(*) AS int) AS Value FROM KHACHHANG_VOUCHER WHERE maCode = {c}")
                .SingleAsync();
            map[c] = n.Value;
        }

        return map;
    }

    private static bool IsMissingApDungMaRankColumn(Exception? ex)
    {
        while (ex != null)
        {
            if (ex is SqlException sql && sql.Number == 207 &&
                sql.Message.Contains("apDungMaRank", StringComparison.OrdinalIgnoreCase))
                return true;
            ex = ex.InnerException;
        }

        return false;
    }

    private static string? ValidateWrite(AdminVoucherWriteDto dto, bool isCreate)
    {
        if (isCreate)
        {
            if (string.IsNullOrWhiteSpace(dto.MaCode))
                return "Thiếu mã voucher.";
            if (dto.MaCode.Trim().Length > 20)
                return "Mã voucher tối đa 20 ký tự.";
        }

        if (!string.IsNullOrEmpty(dto.MoTa) && dto.MoTa.Length > 255)
            return "Mô tả tối đa 255 ký tự.";

        if (dto.PhanTramGiam is int pt && (pt < 0 || pt > 100))
            return "Phần trăm giảm phải từ 0 đến 100.";

        if (dto.SoTienGiam.HasValue && dto.SoTienGiam < 0)
            return "Số tiền giảm không hợp lệ.";

        if (dto.NgayBatDau.HasValue && dto.NgayKetThuc.HasValue && dto.NgayKetThuc <= dto.NgayBatDau)
            return "Ngày kết thúc phải sau ngày bắt đầu.";

        if (!string.IsNullOrWhiteSpace(dto.ApDungMaRank) && dto.ApDungMaRank.Length > 200)
            return "Danh sách hạng áp dụng tối đa 200 ký tự.";

        return null;
    }

    private static string? ValidateCreate(AdminVoucherWriteDto dto)
    {
        var err = ValidateWrite(dto, true);
        if (err != null)
            return err;

        if (!dto.SoLuong.HasValue || dto.SoLuong <= 0)
            return "Tổng số lượng phát hành phải lớn hơn 0.";

        var vBatDau = MinhTamHair.Services.TimeHelper.VnTimeHelper.ToVnTime(dto.NgayBatDau);
        var vKetThuc = MinhTamHair.Services.TimeHelper.VnTimeHelper.ToVnTime(dto.NgayKetThuc);

        if (!vKetThuc.HasValue)
            return "Ngày kết thúc là bắt buộc.";

        var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
        if (vKetThuc <= now)
            return "Ngày kết thúc phải ở tương lai.";

        if (vBatDau.HasValue && vKetThuc.HasValue && vKetThuc <= vBatDau)
            return "Ngày kết thúc phải sau ngày bắt đầu.";

        return null;
    }

    /// <summary>Chỉ một hình thức giảm: % hoặc tiền; cái còn lại về 0.</summary>
    private static (int? PhanTramGiam, decimal? SoTienGiam, string? Error) NormalizeDiscountPair(int? phanTramGiam, decimal? soTienGiam)
    {
        var pt = phanTramGiam ?? 0;
        var money = soTienGiam ?? 0m;

        if (pt > 0 && money > 0m)
            return (null, null, "Chỉ được chọn một hình thức giảm: phần trăm hoặc tiền mặt, không dùng cả hai.");

        if (pt <= 0 && money <= 0m)
            return (null, null, "Vui lòng nhập giảm theo % hoặc theo số tiền.");

        if (pt > 0)
            return (pt, 0m, null);

        return (0, money, null);
    }

    private sealed class VoucherUpdatePatch
    {
        public string? MoTa { get; set; }
        public int? PhanTramGiam { get; set; }
        public decimal? SoTienGiam { get; set; }
        public decimal? GiaTriToiThieu { get; set; }
        public DateTime? NgayBatDau { get; set; }
        public DateTime? NgayKetThuc { get; set; }
        public int? SoLuong { get; set; }
        public int? TrangThai { get; set; }
        public string? ApDungMaRank { get; set; }
    }

    /// <param name="savedCustomerCount">Số khách đã thu thập mã (KHACHHANG_VOUCHER).</param>
    private static string? BuildUpdatePatch(Voucher cur, AdminVoucherWriteDto dto, int savedCustomerCount, out VoucherUpdatePatch patch)
    {
        patch = new VoucherUpdatePatch();

        var err = ValidateWrite(dto, false);
        if (err != null)
            return err;

        if (!dto.SoLuong.HasValue || dto.SoLuong <= 0)
            return "Tổng số lượng phát hành phải lớn hơn 0.";

        var daDung = cur.SoLuongDaDung ?? 0;
        if (dto.SoLuong < daDung)
            return $"Số lượng phát hành không được nhỏ hơn số đã dùng ({daDung}).";

        if (dto.TrangThai is int tt && tt != 0 && tt != 1)
            return "Trạng thái không hợp lệ (chỉ 0 hoặc 1).";

        var now = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
        var chuaBatDau = cur.NgayBatDau.HasValue && now < cur.NgayBatDau.Value;
        var coTuongTac = daDung > 0 || savedCustomerCount > 0;
        var cheDoHanChe = !chuaBatDau && coTuongTac;

        var vBatDau = MinhTamHair.Services.TimeHelper.VnTimeHelper.ToVnTime(dto.NgayBatDau);
        var vKetThuc = MinhTamHair.Services.TimeHelper.VnTimeHelper.ToVnTime(dto.NgayKetThuc);

        if (cheDoHanChe)
        {
            if (!NullableEquals(dto.PhanTramGiam, cur.PhanTramGiam) ||
                !NullableDecimalEquals(dto.SoTienGiam, cur.SoTienGiam) ||
                !NullableDecimalEquals(dto.GiaTriToiThieu, cur.GiaTriToiThieu))
                return "Voucher đã bắt đầu và đã có khách lưu mã hoặc đã phát sinh lượt dùng — không được sửa mức giảm hoặc đơn tối thiểu.";

            if (!NullableEqualsDate(vBatDau, cur.NgayBatDau))
                return "Voucher đã bắt đầu và đã có khách lưu mã hoặc đã phát sinh lượt dùng — không được đổi ngày bắt đầu.";

            var dtoRank = NormalizeRankCsv(dto.ApDungMaRank);
           

            if (!vKetThuc.HasValue)
                return "Ngày kết thúc là bắt buộc.";

            if (cur.NgayKetThuc.HasValue && vKetThuc < cur.NgayKetThuc)
                return "Chỉ được gia hạn ngày kết thúc, không được rút ngắn.";

            if (vKetThuc <= now)
                return "Ngày kết thúc phải lớn hơn thời điểm hiện tại.";

            var startRef = cur.NgayBatDau;
            if (startRef.HasValue && vKetThuc <= startRef)
                return "Ngày kết thúc phải sau ngày bắt đầu.";

            patch.MoTa = string.IsNullOrWhiteSpace(dto.MoTa) ? null : dto.MoTa.Trim();
            patch.PhanTramGiam = cur.PhanTramGiam;
            patch.SoTienGiam = cur.SoTienGiam;
            patch.GiaTriToiThieu = cur.GiaTriToiThieu;
            patch.NgayBatDau = cur.NgayBatDau;
            patch.NgayKetThuc = vKetThuc;
            patch.SoLuong = dto.SoLuong;
            patch.TrangThai = dto.TrangThai ?? cur.TrangThai;
           
            return null;
        }

        var (pt, money, discoErr) = NormalizeDiscountPair(dto.PhanTramGiam, dto.SoTienGiam);
        if (discoErr != null)
            return discoErr;

        if (!vKetThuc.HasValue)
            return "Ngày kết thúc là bắt buộc.";

        if (vKetThuc <= now)
            return "Ngày kết thúc phải ở tương lai.";

        if (vBatDau.HasValue && vKetThuc.HasValue && vKetThuc <= vBatDau)
            return "Ngày kết thúc phải sau ngày bắt đầu.";

        patch.MoTa = string.IsNullOrWhiteSpace(dto.MoTa) ? null : dto.MoTa.Trim();
        patch.PhanTramGiam = pt;
        patch.SoTienGiam = money;
        patch.GiaTriToiThieu = dto.GiaTriToiThieu;
        patch.NgayBatDau = vBatDau;
        patch.NgayKetThuc = vKetThuc;
        patch.SoLuong = dto.SoLuong;
        patch.TrangThai = dto.TrangThai ?? cur.TrangThai ?? 1;
        patch.ApDungMaRank = NormalizeRankCsv(dto.ApDungMaRank);
        return null;
    }

    private static bool NullableEquals(int? a, int? b) => a == b;

    private static bool NullableDecimalEquals(decimal? a, decimal? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return decimal.Round(a.Value, 2, MidpointRounding.AwayFromZero) ==
               decimal.Round(b.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static bool NullableEqualsDate(DateTime? a, DateTime? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return Math.Abs((a.Value - b.Value).TotalSeconds) < 1.5;
    }

    private static string? NormalizeRankCsv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var parts = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(",", parts);
    }

    private sealed class AdminVoucherDbRow
    {
        public string MaCode { get; set; } = null!;
        public string? MoTa { get; set; }
        public int? PhanTramGiam { get; set; }
        public decimal? SoTienGiam { get; set; }
        public decimal? GiaTriToiThieu { get; set; }
        public DateTime? NgayBatDau { get; set; }
        public DateTime? NgayKetThuc { get; set; }
        public int? SoLuong { get; set; }
        public int? SoLuongDaDung { get; set; }
        public int? TrangThai { get; set; }
    }

    private sealed class MaCodeOnlyRow
    {
        public string MaCode { get; set; } = null!;
    }

    private sealed class CountRow
    {
        public int Value { get; set; }
    }

    private sealed class KhachThuThapRow
    {
        public string MaKh { get; set; } = null!;
        public string? HoTen { get; set; }
        public string? Sdt { get; set; }
        public string? Email { get; set; }
    }

    private sealed class HoaDonVoucherRow
    {
        public string MaHd { get; set; } = null!;
        public string? MaKh { get; set; }
        public DateTime? NgayTao { get; set; }
        public decimal? TongTien { get; set; }
        public decimal? ThanhTien { get; set; }
        public int? TrangThai { get; set; }
    }
}

/// <summary>Chuẩn hóa mã, máy quét 4 chốt + tính giảm giá; cộng <c>soLuongDaDung</c> khi chốt đơn thành công (gọi từ luồng tạo HOA_DON).</summary>
public static class VoucherCartLogic
{
    private static readonly Regex MaCodeRegex = new("^[A-Z0-9]{1,20}$", RegexOptions.Compiled);

    public static Regex MaCodePattern => MaCodeRegex;

    public static string NormalizeMaCode(string raw) => raw.Trim().ToUpperInvariant();

    /// <param name="rankCapPercent">Trần % theo hạng (null hoặc 0 = không giới hạn).</param>
    /// <param name="rankCsvAllows">Khách thỏa danh sách hạng áp dụng của voucher.</param>
    public static string? TryApplyToCart(
        Voucher? v,
        decimal tongTien,
        DateTime now,
        int? rankCapPercent,
        bool rankCsvAllows,
        out decimal tienDuocGiam,
        out decimal tongPhaiTra)
    {
        tienDuocGiam = 0m;
        tongPhaiTra = tongTien;

        if (v == null)
            return "Mã voucher không tồn tại.";

        if (v.TrangThai != 1)
            return "Voucher không còn hiệu lực.";

        if (v.NgayBatDau.HasValue && now < v.NgayBatDau.Value)
            return "Voucher chưa đến thời gian áp dụng.";

        if (v.NgayKetThuc.HasValue && now > v.NgayKetThuc.Value)
            return "Voucher đã hết hạn.";

        var daDung = v.SoLuongDaDung ?? 0;
        if (v.SoLuong.HasValue && daDung >= v.SoLuong.Value)
            return "Mã đã hết lượt sử dụng.";

        var minOrder = v.GiaTriToiThieu ?? 0m;
        if (tongTien < minOrder)
            return $"Đơn hàng chưa đạt giá trị tối thiểu ({minOrder:N0} đ).";

        if (!rankCsvAllows)
            return "Voucher không áp dụng cho hạng thành viên của bạn.";

        if (v.PhanTramGiam is int pctp && pctp > 0 &&
            rankCapPercent is int capPct && capPct > 0 && pctp > capPct)
            return "Voucher vượt mức ưu đãi cho hạng thành viên hiện tại.";

        if (v.SoTienGiam is decimal st && st > 0m)
            tienDuocGiam = st >= tongTien ? tongTien : st;
        else if (v.PhanTramGiam is int pct && pct > 0)
        {
            tienDuocGiam = Math.Round(tongTien * pct / 100m, 0, MidpointRounding.AwayFromZero);
            if (tienDuocGiam > tongTien)
                tienDuocGiam = tongTien;
        }
        else
            return "Voucher chưa cấu hình hình thức giảm giá.";

        tongPhaiTra = tongTien - tienDuocGiam;
        if (tongPhaiTra < 0m)
            tongPhaiTra = 0m;

        return null;
    }

    /// <summary>Gọi sau khi đã ghi <c>HOA_DON</c> với <c>maCode</c>; tăng <c>soLuongDaDung</c> có kiểm tra tồn kho.</summary>
    public static async Task<(bool Ok, string? Error)> TryIncrementUsageAsync(
        MinhTamHairContext ctx,
        string maCode,
        CancellationToken cancellationToken = default)
    {
        var n = await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"
            UPDATE VOUCHER SET soLuongDaDung = ISNULL(soLuongDaDung, 0) + 1
            WHERE maCode = {maCode}
              AND trangThai = 1
              AND (soLuong IS NULL OR ISNULL(soLuongDaDung, 0) < soLuong)",
            cancellationToken);

        if (n == 0)
            return (false, "Không cộng được lượt đã dùng — kiểm tra tồn kho mã hoặc trạng thái.");

        return (true, null);
    }
}

public class AdminVoucherWriteDto
{
    public string? MaCode { get; set; }
    public string? MoTa { get; set; }
    public int? PhanTramGiam { get; set; }
    public decimal? SoTienGiam { get; set; }
    public decimal? GiaTriToiThieu { get; set; }
    public DateTime? NgayBatDau { get; set; }
    public DateTime? NgayKetThuc { get; set; }
    public int? SoLuong { get; set; }
    public int? SoLuongDaDung { get; set; }
    public int? TrangThai { get; set; }
    /// <summary>Mã hạng cách nhau bởi dấu phẩy; null/empty = mọi hạng.</summary>
    public string? ApDungMaRank { get; set; }
}

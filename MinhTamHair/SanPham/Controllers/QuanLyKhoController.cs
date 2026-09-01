using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MinhTamHair.Models;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuanLyKhoController : ControllerBase
{
    private readonly MinhTamHairContext _db;
    public QuanLyKhoController(MinhTamHairContext db) => _db = db;

    [HttpGet("nhacungcap")]
    public async Task<IActionResult> GetNhaCungCap()
    {
        var list = await _db.Nhacungcaps
            .AsNoTracking()
            .Select(n => new
            {
                n.MaNcc,
                n.TenNcc,
                n.SoDienThoai,
                n.DiaChi,
                n.Email,
                n.TrangThai,
                SoPhieuNhap = n.Phieunhaps.Count
            })
            .OrderBy(n => n.MaNcc)
            .ToListAsync();
        return Ok(list);
    }

    private static readonly System.Text.RegularExpressions.Regex MaNccRegex =
        new(@"^[A-Za-z0-9]{2,10}$");
    private static readonly System.Text.RegularExpressions.Regex SoDienThoaiRegex =
        new(@"^0\d{9}$");
    private static readonly System.Text.RegularExpressions.Regex EmailRegex =
        new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$");

    // Validate các trường chung của nhà cung cấp (dùng cho cả tạo mới & cập nhật).
    // Trả về message lỗi đầu tiên gặp phải, hoặc null nếu hợp lệ.
    private static string? ValidateNhaCungCapDto(NhaCungCapDto dto, bool isCreate)
    {
        if (isCreate)
        {
            if (string.IsNullOrWhiteSpace(dto.MaNcc))
                return "Mã nhà cung cấp không được để trống.";
            if (!MaNccRegex.IsMatch(dto.MaNcc.Trim()))
                return "Mã nhà cung cấp chỉ gồm chữ và số, 2–10 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(dto.TenNcc))
            return "Tên nhà cung cấp không được để trống.";
        if (dto.TenNcc.Trim().Length > 100)
            return "Tên nhà cung cấp không được vượt quá 100 ký tự.";

        if (!string.IsNullOrWhiteSpace(dto.SoDienThoai) && !SoDienThoaiRegex.IsMatch(dto.SoDienThoai.Trim()))
            return "Số điện thoại không hợp lệ (10 số, bắt đầu bằng 0).";

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            if (dto.Email.Trim().Length > 100)
                return "Email không được vượt quá 100 ký tự.";
            if (!EmailRegex.IsMatch(dto.Email.Trim()))
                return "Email không hợp lệ.";
        }

        if (!string.IsNullOrWhiteSpace(dto.DiaChi) && dto.DiaChi.Trim().Length > 200)
            return "Địa chỉ không được vượt quá 200 ký tự.";

        if (!isCreate && dto.TrangThai != 0 && dto.TrangThai != 1)
            return "Trạng thái không hợp lệ.";

        return null;
    }

    [HttpPost("nhacungcap")]
    public async Task<IActionResult> CreateNhaCungCap([FromBody] NhaCungCapDto dto)
    {
        var loi = ValidateNhaCungCapDto(dto, isCreate: true);
        if (loi != null) return BadRequest(new { message = loi });

        var maNcc = dto.MaNcc.Trim();
        var daTonTai = await _db.Nhacungcaps.AnyAsync(n => n.MaNcc == maNcc);
        if (daTonTai)
            return BadRequest(new { message = $"Mã nhà cung cấp '{maNcc}' đã tồn tại." });

        var ncc = new Nhacungcap
        {
            MaNcc = maNcc,
            TenNcc = dto.TenNcc.Trim(),
            SoDienThoai = dto.SoDienThoai?.Trim(),
            DiaChi = dto.DiaChi?.Trim(),
            Email = dto.Email?.Trim(),
            TrangThai = 1
        };

        _db.Nhacungcaps.Add(ncc);
        await _db.SaveChangesAsync();

        return Ok(ncc);
    }

    [HttpPut("nhacungcap/{id}")]
    public async Task<IActionResult> UpdateNhaCungCap(string id, [FromBody] NhaCungCapDto dto)
    {
        var entity = await _db.Nhacungcaps.FindAsync(id);
        if (entity == null) return NotFound();

        var loi = ValidateNhaCungCapDto(dto, isCreate: false);
        if (loi != null) return BadRequest(new { message = loi });

        entity.TenNcc = dto.TenNcc.Trim();
        entity.SoDienThoai = dto.SoDienThoai?.Trim();
        entity.DiaChi = dto.DiaChi?.Trim();
        entity.Email = dto.Email?.Trim();
        entity.TrangThai = dto.TrangThai;

        await _db.SaveChangesAsync();

        return Ok(entity);
    }

    [HttpDelete("nhacungcap/{id}")]
    public async Task<IActionResult> DeleteNhaCungCap(string id)
    {
        var entity = await _db.Nhacungcaps.Include(n => n.Phieunhaps).FirstOrDefaultAsync(n => n.MaNcc == id);
        if (entity == null) return NotFound();
        if (entity.Phieunhaps.Any())
            return BadRequest(new { message = "Không thể xóa: nhà cung cấp đã có phiếu nhập." });

        _db.Nhacungcaps.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("phieunhap")]
    public async Task<IActionResult> GetPhieuNhap(
        [FromQuery] string? maNcc,
        [FromQuery] int? trangThai,
        [FromQuery] DateTime? tuNgay,
        [FromQuery] DateTime? denNgay)
    {
        var q = _db.Phieunhaps
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(maNcc)) q = q.Where(p => p.MaNcc == maNcc);
        if (trangThai.HasValue) q = q.Where(p => p.TrangThai == trangThai.Value);
        if (tuNgay.HasValue) q = q.Where(p => p.NgayNhap >= tuNgay.Value);
        if (denNgay.HasValue) q = q.Where(p => p.NgayNhap <= denNgay.Value.AddDays(1));

        var list = await q
            .OrderByDescending(p => p.MaPn)
            .Select(p => new
            {
                p.MaPn,
                p.MaNcc,
                TenNcc = p.MaNccNavigation != null ? p.MaNccNavigation.TenNcc : null,
                p.MaNv,
                TenNv = p.MaNvNavigation != null ? p.MaNvNavigation.HoTenNv : null,
                p.NgayNhap,
                p.TongTien,
                p.GhiChu,
                p.TrangThai,
                SoLuongDong = p.ChitietPhieunhaps.Count,
                ChiTiet = p.ChitietPhieunhaps.Select(c => new
                {
                    c.MaPn,
                    c.MaBienThe,
                    TenBienThe = c.MaBienTheNavigation.TenBienThe,
                    TenSanPham = c.MaBienTheNavigation.MaSpNavigation != null
                        ? c.MaBienTheNavigation.MaSpNavigation.TenSp : null,
                    HinhAnh = c.MaBienTheNavigation.HinhAnhDaiDien,
                    c.SoLuong,
                    c.GiaNhap,
                    ThanhTien = c.SoLuong * c.GiaNhap
                }).ToList()
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("phieunhap/{id}")]
    public async Task<IActionResult> GetPhieuNhapDetail(string id)
    {
        var p = await _db.Phieunhaps
            .AsNoTracking()
            .Where(x => x.MaPn == id)
            .Select(p => new
            {
                p.MaPn,
                p.MaNcc,
                TenNcc = p.MaNccNavigation != null ? p.MaNccNavigation.TenNcc : null,
                p.MaNv,
                TenNv = p.MaNvNavigation != null ? p.MaNvNavigation.HoTenNv : null,
                p.NgayNhap,
                p.TongTien,
                p.GhiChu,
                p.TrangThai,
                ChiTiet = p.ChitietPhieunhaps.Select(c => new
                {
                    c.MaBienThe,
                    TenBienThe = c.MaBienTheNavigation.TenBienThe,
                    TenSanPham = c.MaBienTheNavigation.MaSpNavigation != null ? c.MaBienTheNavigation.MaSpNavigation.TenSp : null,
                    HinhAnh = c.MaBienTheNavigation.HinhAnhDaiDien,
                    GiaBanHienTai = c.MaBienTheNavigation.GiaBan,
                    c.SoLuong,
                    c.GiaNhap,
                    ThanhTien = c.SoLuong * c.GiaNhap
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (p == null) return NotFound();

        return Ok(p);
    }

    // Tự sinh mã phiếu nhập dạng PN01, PN02, PN03...
    private async Task<string> GenerateMaPnAsync()
    {
        var maxSo = await _db.Phieunhaps
            .Where(p => p.MaPn.StartsWith("PN"))
            .Select(p => p.MaPn.Substring(2))
            .ToListAsync();

        int max = 0;
        foreach (var s in maxSo)
        {
            if (int.TryParse(s, out var so) && so > max)
            {
                max = so;
            }
        }

        return "PN" + (max + 1).ToString("D2");
    }

    [HttpPost("phieunhap")]
    public async Task<IActionResult> CreatePhieuNhap([FromBody] CreatePhieuNhapRequest req)
    {
        var maPn = await GenerateMaPnAsync();
        req.MaPn = maPn;

        if (req.MaNv != null && req.MaNv.Trim().Length > 20)
            return BadRequest(new { message = "Mã nhân viên không hợp lệ." });

        if (req.ChiTiet == null || !req.ChiTiet.Any())
            return BadRequest(new { message = "Phiếu nhập phải có ít nhất một dòng chi tiết." });

        // Validate số lượng / đơn giá hợp lệ (kèm chặn trên để tránh tràn số khi tính tổng tiền)
        var dongLoi = req.ChiTiet
            .Where(c => string.IsNullOrWhiteSpace(c.MaBienThe)
                     || (c.SoLuong ?? 0) <= 0 || (c.SoLuong ?? 0) > 1_000_000
                     || (c.GiaNhap ?? -1) < 0 || (c.GiaNhap ?? 0) > 1_000_000_000)
            .ToList();
        if (dongLoi.Any())
            return BadRequest(new { message = "Mỗi dòng chi tiết phải có biến thể hợp lệ, số lượng từ 1–1.000.000 và giá nhập từ 0–1.000.000.000." });

        // Không cho trùng biến thể trong cùng 1 phiếu (gộp số lượng thay vì để 2 dòng riêng gây nhầm)
        var trungLap = req.ChiTiet.GroupBy(c => c.MaBienThe).Any(g => g.Count() > 1);
        if (trungLap)
            return BadRequest(new { message = "Một biến thể chỉ được xuất hiện một lần trong phiếu nhập." });

        // Validate mã phiếu chưa tồn tại
        var daTonTai = await _db.Phieunhaps.AnyAsync(p => p.MaPn == req.MaPn);
        if (daTonTai)
            return BadRequest(new { message = $"Mã phiếu nhập '{req.MaPn}' đã tồn tại." });

        // Validate nhà cung cấp tồn tại (nếu có chọn)
        if (!string.IsNullOrWhiteSpace(req.MaNcc))
        {
            var nccTonTai = await _db.Nhacungcaps.AnyAsync(n => n.MaNcc == req.MaNcc);
            if (!nccTonTai)
                return BadRequest(new { message = $"Nhà cung cấp '{req.MaNcc}' không tồn tại." });
        }

        // Validate biến thể tồn tại
        var maBTs = req.ChiTiet.Select(c => c.MaBienThe).Distinct().ToList();
        var existing = await _db.BienTheSanphams
            .Where(b => maBTs.Contains(b.MaBienThe))
            .Select(b => b.MaBienThe)
            .ToListAsync();
        var missing = maBTs.Except(existing).ToList();
        if (missing.Any())
            return BadRequest(new { message = $"Biến thể không tồn tại: {string.Join(", ", missing)}" });

        var tongTien = req.ChiTiet.Sum(c => (c.SoLuong ?? 0) * (c.GiaNhap ?? 0));

        var phieu = new Phieunhap
        {
            MaPn = req.MaPn,
            MaNcc = req.MaNcc,
            MaNv = req.MaNv,
            NgayNhap = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
            TongTien = tongTien,
            GhiChu = req.GhiChu,
            TrangThai = 0,
        };

        foreach (var ct in req.ChiTiet)
        {
            phieu.ChitietPhieunhaps.Add(new ChitietPhieunhap
            {
                MaPn = req.MaPn,
                MaBienThe = ct.MaBienThe,
                SoLuong = ct.SoLuong,
                GiaNhap = ct.GiaNhap
            });
        }

        _db.Phieunhaps.Add(phieu);
        await _db.SaveChangesAsync();
        return Ok(new { phieu.MaPn, phieu.TongTien, message = "Tạo phiếu nhập thành công." });
    }

    [HttpPost("phieunhap/{id}/duyet")]
    public async Task<IActionResult> DuyetPhieuNhap(string id)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var phieu = await _db.Phieunhaps
                    .Include(p => p.ChitietPhieunhaps)
                    .FirstOrDefaultAsync(p => p.MaPn == id);

                if (phieu == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }
                if (phieu.TrangThai == 1)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Phiếu đã được duyệt trước đó." });
                }
                if (phieu.TrangThai == 2)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Phiếu đã bị hủy, không thể duyệt." });
                }

                var maBTs = phieu.ChitietPhieunhaps.Select(c => c.MaBienThe).Distinct().ToList();
                var bienThes = await _db.BienTheSanphams
                    .Where(b => maBTs.Contains(b.MaBienThe))
                    .ToListAsync();

                foreach (var ct in phieu.ChitietPhieunhaps)
                {
                    var bt = bienThes.FirstOrDefault(b => b.MaBienThe == ct.MaBienThe);
                    if (bt == null) continue;

                    var tonTruoc = bt.SoLuongTon ?? 0;
                    var soLuongCong = ct.SoLuong ?? 0;
                    bt.SoLuongTon = tonTruoc + soLuongCong;

                    _db.LichSuTonKhos.Add(new LichSuTonKho
                    {
                        MaBienThe = bt.MaBienThe,
                        LoaiBienDong = "NHAP",
                        SoLuongThayDoi = soLuongCong,
                        TonTruoc = tonTruoc,
                        TonSau = bt.SoLuongTon.Value,
                        MaChungTu = phieu.MaPn,
                        MaNv = phieu.MaNv,
                        GhiChu = $"Duyệt phiếu nhập {phieu.MaPn}",
                        ThoiGian = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()
                    });
                }

                phieu.TrangThai = 1;
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "Duyệt phiếu nhập thành công. Tồn kho đã được cập nhật." });
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                // 1205 = deadlock victim do SQL Server tự phát hiện và hủy 1 trong các transaction xung đột
                await transaction.RollbackAsync();
                return Conflict(new { message = "Hệ thống đang xử lý phiếu này, vui lòng thử duyệt lại." });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    [HttpPost("phieunhap/{id}/huy")]
    public async Task<IActionResult> HuyPhieuNhap(string id)
    {
        var phieu = await _db.Phieunhaps.FindAsync(id);
        if (phieu == null) return NotFound();
        if (phieu.TrangThai == 1)
            return BadRequest(new { message = "Không thể hủy phiếu đã duyệt." });

        phieu.TrangThai = 2;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Phiếu nhập đã bị hủy." });
    }

    [HttpDelete("phieunhap/{id}")]
    public async Task<IActionResult> DeletePhieuNhap(string id)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var phieu = await _db.Phieunhaps
                    .Include(p => p.ChitietPhieunhaps)
                    .FirstOrDefaultAsync(p => p.MaPn == id);

                if (phieu == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }
                if (phieu.TrangThai == 1)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Không thể xóa phiếu đã duyệt." });
                }

                _db.ChitietPhieunhaps.RemoveRange(phieu.ChitietPhieunhaps);
                _db.Phieunhaps.Remove(phieu);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return NoContent();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    [HttpGet("tonkho")]
    public async Task<IActionResult> GetTonKho(
        [FromQuery] string? search,
        [FromQuery] string? maDanhMuc,
        [FromQuery] int? trangThai)
    {
        var q = _db.BienTheSanphams
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(b =>
                (b.TenBienThe != null && b.TenBienThe.Contains(search)) ||
                (b.MaSpNavigation != null && b.MaSpNavigation.TenSp != null
                    && b.MaSpNavigation.TenSp.Contains(search)));

        if (!string.IsNullOrEmpty(maDanhMuc))
            q = q.Where(b => b.MaSpNavigation != null && b.MaSpNavigation.MaDanhMuc == maDanhMuc);

        if (trangThai.HasValue)
            q = q.Where(b => b.TrangThai == trangThai.Value);

        var list = await q
            .OrderBy(b => b.MaSpNavigation!.TenSp)
            .ThenBy(b => b.TenBienThe)
            .Select(b => new
            {
                b.MaBienThe,
                b.TenBienThe,
                MaSanPham = b.MaSanPham,
                TenSanPham = b.MaSpNavigation != null ? b.MaSpNavigation.TenSp : null,
                DanhMuc = b.MaSpNavigation != null && b.MaSpNavigation.MaDanhMucTheNavigation != null
                    ? b.MaSpNavigation.MaDanhMucTheNavigation.TenDanhMuc : null,
                HinhAnh = b.HinhAnhDaiDien,
                b.SoLuongTon,
                b.GiaBan,
                b.TrangThai,
                MucTon = b.SoLuongTon == null ? "unknown"
                       : b.SoLuongTon == 0 ? "het"
                       : b.SoLuongTon < 10 ? "thap"
                       : "on"
            })
            .ToListAsync();

        return Ok(list);
    }
    [HttpGet("timkiembienthe")]
    public async Task<IActionResult> GetBienTheLookup([FromQuery] string? q)
    {
        var query = _db.BienTheSanphams
            .AsNoTracking()
            .Where(b => b.TrangThai == 1)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(b =>
                (b.MaBienThe != null && b.MaBienThe.Contains(q)) ||
                (b.TenBienThe != null && b.TenBienThe.Contains(q)) ||
                (b.MaSpNavigation != null &&
                 b.MaSpNavigation.TenSp != null &&
                 b.MaSpNavigation.TenSp.Contains(q)));

        var list = await query
            .Take(30)
            .Select(b => new
            {
                b.MaBienThe,
                Label = (b.MaSpNavigation != null
                            ? b.MaSpNavigation.TenSp + " — "
                            : "") + b.TenBienThe,
                b.GiaBan,
                b.SoLuongTon,
                HinhAnh = b.HinhAnhDaiDien
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("lichsutonkho")]
    public async Task<IActionResult> GetLichSuTonKho(
        [FromQuery] string? maBienThe,
        [FromQuery] string? loaiBienDong,
        [FromQuery] DateTime? tuNgay,
        [FromQuery] DateTime? denNgay,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var q = _db.LichSuTonKhos
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(maBienThe)) q = q.Where(l => l.MaBienThe == maBienThe);
        if (!string.IsNullOrWhiteSpace(loaiBienDong)) q = q.Where(l => l.LoaiBienDong == loaiBienDong);
        if (tuNgay.HasValue) q = q.Where(l => l.ThoiGian >= tuNgay.Value);
        if (denNgay.HasValue) q = q.Where(l => l.ThoiGian <= denNgay.Value.AddDays(1));

        var total = await q.CountAsync();

        var list = await q
            .OrderByDescending(l => l.ThoiGian)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.MaLich,
                l.MaBienThe,
                TenBienThe = l.MaBienTheNavigation != null ? l.MaBienTheNavigation.TenBienThe : null,
                TenSanPham = l.MaBienTheNavigation != null && l.MaBienTheNavigation.MaSpNavigation != null
                    ? l.MaBienTheNavigation.MaSpNavigation.TenSp : null,
                l.LoaiBienDong,
                l.SoLuongThayDoi,
                l.TonTruoc,
                l.TonSau,
                l.MaChungTu,
                l.MaNv,
                l.GhiChu,
                l.ThoiGian
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = list });
    }

    [HttpGet("doisoathoadon")]
    public async Task<IActionResult> GetDoiSoatHoaDon(
        [FromQuery] DateTime? tuNgay,
        [FromQuery] DateTime? denNgay,
        [FromQuery] string? trangThai, // "", "khop", "thieu"
        [FromQuery] string? search, // tìm theo mã đơn hàng, mã biến thể, tên sản phẩm/biến thể
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var q = _db.LichSuTonKhos
            .AsNoTracking()
            .Where(l => l.LoaiBienDong == "BAN")
            .AsQueryable();

        if (tuNgay.HasValue) q = q.Where(l => l.ThoiGian >= tuNgay.Value);
        if (denNgay.HasValue) q = q.Where(l => l.ThoiGian <= denNgay.Value.AddDays(1));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim();
            q = q.Where(l =>
                (l.MaChungTu != null && l.MaChungTu.Contains(kw)) ||
                (l.MaBienThe != null && l.MaBienThe.Contains(kw)) ||
                (l.MaBienTheNavigation != null && l.MaBienTheNavigation.TenBienThe != null && l.MaBienTheNavigation.TenBienThe.Contains(kw)) ||
                (l.MaBienTheNavigation != null && l.MaBienTheNavigation.MaSpNavigation != null
                    && l.MaBienTheNavigation.MaSpNavigation.TenSp != null
                    && l.MaBienTheNavigation.MaSpNavigation.TenSp.Contains(kw))
            );
        }

        var hddtSanPhamOnly = _db.HoaDonDienTus.AsNoTracking().Where(h => h.MaHddt != null && !h.MaHddt.EndsWith("D"));

        var joined = from l in q
                     join hddt in hddtSanPhamOnly on l.MaChungTu equals hddt.MaHd into hddtGroup
                     from hddt in hddtGroup.DefaultIfEmpty()
                     select new
                     {
                         l.MaLich,
                         l.MaBienThe,
                         TenBienThe = l.MaBienTheNavigation != null ? l.MaBienTheNavigation.TenBienThe : null,
                         TenSanPham = l.MaBienTheNavigation != null && l.MaBienTheNavigation.MaSpNavigation != null
                             ? l.MaBienTheNavigation.MaSpNavigation.TenSp : null,
                         l.SoLuongThayDoi,
                         l.ThoiGian,
                         MaDonHang = l.MaChungTu,
                         CoHoaDon = hddt != null,
                         TrangThaiHoaDon = hddt != null ? (int?)hddt.TrangThai : null,
                         SoHoaDon = hddt != null ? hddt.SoHoaDon : null,
                         LinkPdf = hddt != null ? hddt.LinkPdf : null,
                         GhiChuLoi = hddt != null ? hddt.GhiChuLoi : null
                     };

        if (trangThai == "khop") joined = joined.Where(x => x.TrangThaiHoaDon == 1);
        else if (trangThai == "thieu") joined = joined.Where(x => x.TrangThaiHoaDon == null || x.TrangThaiHoaDon != 1);

        var total = await joined.CountAsync();

        var tongLuot = await q.CountAsync();
        var statsQuery = from l in q
                         join hddt in hddtSanPhamOnly on l.MaChungTu equals hddt.MaHd into hddtGroup2
                         from hddt in hddtGroup2.DefaultIfEmpty()
                         where hddt != null && hddt.TrangThai == 1
                         select l.MaLich;
        var daKhop = await statsQuery.CountAsync();

        var list = await joined
            .OrderByDescending(x => x.ThoiGian)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            stats = new { tongLuot, daKhop, chuaKhop = tongLuot - daKhop },
            items = list
        });
    }

    [HttpGet("canhbaoton")]
    public async Task<IActionResult> GetCanhBaoTon([FromQuery] int nguong = 10)
    {
        if (nguong < 0 || nguong > 100_000) nguong = 10;

        var list = await _db.BienTheSanphams
            .AsNoTracking()
            .Where(b => b.TrangThai == 1 && (b.SoLuongTon ?? 0) < nguong)
            .OrderBy(b => b.SoLuongTon)
            .Select(b => new
            {
                b.MaBienThe,
                b.TenBienThe,
                TenSanPham = b.MaSpNavigation != null ? b.MaSpNavigation.TenSp : null,
                HinhAnh = b.HinhAnhDaiDien,
                b.SoLuongTon,
                b.GiaBan,
                MucTon = (b.SoLuongTon ?? 0) == 0 ? "het" : "thap"
            })
            .ToListAsync();

        return Ok(new { nguong, soLuongCanhBao = list.Count, items = list });
    }

    [HttpGet("thongkenhapkho")]
    public async Task<IActionResult> GetThongKeNhapKho([FromQuery] int? nam)
    {
        var namXet = nam ?? MinhTamHair.Services.TimeHelper.VnTimeHelper.Now().Year;

        var raw = await _db.Phieunhaps
            .AsNoTracking()
            .Where(p => p.TrangThai == 1 && p.NgayNhap.HasValue && p.NgayNhap.Value.Year == namXet)
            .Select(p => new
            {
                Thang = p.NgayNhap!.Value.Month,
                p.TongTien,
                TongSoLuong = p.ChitietPhieunhaps.Sum(c => c.SoLuong ?? 0)
            })
            .ToListAsync();

        var theoThang = Enumerable.Range(1, 12)
            .Select(thang =>
            {
                var nhomThang = raw.Where(x => x.Thang == thang).ToList();
                return new
                {
                    Thang = thang,
                    SoPhieuNhap = nhomThang.Count,
                    TongTien = nhomThang.Sum(x => x.TongTien ?? 0),
                    TongSoLuongNhap = nhomThang.Sum(x => x.TongSoLuong)
                };
            })
            .ToList();

        return Ok(new
        {
            Nam = namXet,
            TongSoPhieuCaNam = theoThang.Sum(t => t.SoPhieuNhap),
            TongTienCaNam = theoThang.Sum(t => t.TongTien),
            TheoThang = theoThang
        });
    }
}

public class CreatePhieuNhapRequest
{
    public string? MaPn { get; set; } // Không cần gửi khi tạo mới — server tự sinh mã (PN01, PN02...)
    public string? MaNcc { get; set; }
    public string? MaNv { get; set; }
    public DateTime? NgayNhap { get; set; }
    public string? GhiChu { get; set; }
    public List<ChiTietPhieuNhapDto> ChiTiet { get; set; } = new();
}

public class ChiTietPhieuNhapDto
{
    public string MaBienThe { get; set; } = null!;
    public int? SoLuong { get; set; }
    public decimal? GiaNhap { get; set; }
}
public class NhaCungCapDto
{
    public string MaNcc { get; set; }
    public string TenNcc { get; set; }
    public string SoDienThoai { get; set; }
    public string DiaChi { get; set; }
    public string Email { get; set; }
    public int TrangThai { get; set; }
}
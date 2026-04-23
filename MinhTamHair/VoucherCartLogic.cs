using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;

namespace MinhTamHair;

/// <summary>Chuẩn hóa mã, kiểm tra theo thứ tự nghiệp vụ, tính giảm; cộng <c>soLuongDaDung</c> khi chốt đơn (atomic UPDATE).</summary>
public static class VoucherCartLogic
{
    private static readonly Regex MaCodeRegex = new("^[A-Z0-9]{1,20}$", RegexOptions.Compiled);

    public static Regex MaCodePattern => MaCodeRegex;

    public static string NormalizeMaCode(string raw) => raw.Trim().ToUpperInvariant();

    public static HashSet<string> ParseDichVuCsv(string? csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var p in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (p.Length > 0) set.Add(p);
        }
        return set;
    }

    public static bool ServicesMatchWhitelist(string? dichVuCsv, IReadOnlyList<string> cartIds)
    {
        var allow = ParseDichVuCsv(dichVuCsv);
        if (allow.Count == 0) return true;
        if (cartIds.Count == 0) return false;
        foreach (var id in cartIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !allow.Contains(id.Trim()))
                return false;
        }
        return true;
    }

    /// <summary>Kiểm tra và tính tiền giảm (schema VOUCHER cơ bản, không cột mở rộng).</summary>
    public static string? ValidateAndCalculate(
        Voucher v,
        int soLanKhachDaDung,
        decimal tongTienDon,
        IReadOnlyList<string> maDichVuTrongGio,
        DateTime ngayGioDichVu,
        DateTime now,
        string? maRankKhach,
        int? rankCapPercent,
        out decimal tienDuocGiam,
        out decimal tongPhaiTra)
    {
        _ = soLanKhachDaDung;
        _ = ngayGioDichVu;
        _ = maRankKhach;

        tienDuocGiam = 0m;
        tongPhaiTra = tongTienDon;

        if (v.TrangThai != 1)
            return "Voucher không còn hiệu lực hoặc đã đóng.";

        if (v.NgayBatDau.HasValue && now < v.NgayBatDau.Value)
            return "Voucher chưa đến thời gian áp dụng.";

        if (v.NgayKetThuc.HasValue && now > v.NgayKetThuc.Value)
            return "Voucher đã hết hạn.";

        var daDung = v.SoLuongDaDung ?? 0;
        if (v.SoLuong.HasValue && daDung >= v.SoLuong.Value)
            return "Mã đã hết số lượng.";

        var minOrder = v.GiaTriToiThieu ?? 0m;
        if (tongTienDon < minOrder)
            return $"Đơn hàng chưa đạt giá trị tối thiểu ({minOrder:N0} đ).";

        if (v.PhanTramGiam is int pctp && pctp > 0 &&
            rankCapPercent is int capPct && capPct > 0 && pctp > capPct)
            return "Voucher vượt mức ưu đãi cho hạng thành viên hiện tại.";

        if (!ServicesMatchWhitelist(null, maDichVuTrongGio))
            return "Giỏ hàng có dịch vụ không thuộc phạm vi áp dụng của mã này.";

        if (v.SoTienGiam is decimal st && st > 0m)
        {
            tienDuocGiam = st >= tongTienDon ? tongTienDon : st;
        }
        else if (v.PhanTramGiam is int pct && pct > 0)
        {
            var trial = Math.Round(tongTienDon * pct / 100m, 0, MidpointRounding.AwayFromZero);
            tienDuocGiam = trial > tongTienDon ? tongTienDon : trial;
        }
        else
            return "Voucher chưa cấu hình hình thức giảm giá (tiền mặt hoặc %).";

        tongPhaiTra = tongTienDon - tienDuocGiam;
        if (tongPhaiTra < 0m) tongPhaiTra = 0m;
        return null;
    }

    public static async Task<(string? Error, decimal TienGiam, decimal TongSauGiam)> ValidateAndCalculateAsync(
        MinhTamHairContext ctx,
        Voucher v,
        string? maKh,
        decimal tongTienDon,
        IReadOnlyList<string> maDichVuTrongGio,
        DateTime ngayGioDichVu,
        DateTime now,
        string? maRankKhach,
        int? rankCapPercent,
        CancellationToken ct = default)
    {
        var userUses = -1;
        if (!string.IsNullOrWhiteSpace(maKh))
        {
            userUses = await ctx.HoaDons.AsNoTracking()
                .CountAsync(h => h.MaKh == maKh && h.MaCode == v.MaCode, ct);
        }

        var err = ValidateAndCalculate(v, userUses, tongTienDon, maDichVuTrongGio, ngayGioDichVu, now, maRankKhach, rankCapPercent,
            out var giam, out var sau);
        return (err, giam, sau);
    }

    /// <summary>Áp dụng tối đa 1 mã voucher trên đơn.</summary>
    public static async Task<(string? Error, decimal TotalGiam, decimal TongSauGiam)> ValidateStackedAsync(
        MinhTamHairContext ctx,
        IReadOnlyList<string> codesNormalized,
        string? maKh,
        decimal subtotal,
        IReadOnlyList<string> maDichVuTrongGio,
        DateTime ngayGioDichVu,
        DateTime now,
        string? maRankKhach,
        int? rankCapPercent,
        CancellationToken ct = default)
    {
        if (codesNormalized == null || codesNormalized.Count == 0)
            return (null, 0m, subtotal);
        if (codesNormalized.Count > 1)
            return ("Chỉ áp dụng một mã voucher trên mỗi đơn.", 0m, subtotal);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in codesNormalized)
        {
            if (!seen.Add(c))
                return ("Không được dùng trùng một mã voucher.", 0m, subtotal);
        }

        decimal running = subtotal;
        decimal totalGiam = 0m;
        foreach (var code in codesNormalized)
        {
            var v = await ctx.Vouchers.FirstOrDefaultAsync(x => x.MaCode == code, ct);
            if (v == null)
                return ($"Mã voucher không tồn tại: {code}", 0m, subtotal);

            var (err, giam, sau) = await ValidateAndCalculateAsync(
                ctx, v, maKh, running, maDichVuTrongGio, ngayGioDichVu, now, maRankKhach, rankCapPercent, ct);
            if (err != null)
                return (err, 0m, subtotal);
            totalGiam += giam;
            running = sau;
        }

        return (null, totalGiam, running);
    }

    /// <summary>Gọi trong transaction sau khi ghi <c>HOA_DON</c>; tăng <c>soLuongDaDung</c> khi vẫn còn lượt (chống race).</summary>
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
            return (false, "Mã đã hết số lượng hoặc không còn hiệu lực — không thể ghi nhận lượt dùng.");

        return (true, null);
    }
}

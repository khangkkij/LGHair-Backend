using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using MinhTamHair.Models.DTOs;
using MinhTamHair.Services;

namespace MinhTamHair.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ThongBaoController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public ThongBaoController(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Kiểm tra nhanh Zalo (đặc biệt bridge Personal) trước khi gửi — dùng cho Admin UI.</summary>
    [HttpGet("zalo-trang-thai")]
    public async Task<ActionResult<ZaloGuiTrangThaiDto>> GetZaloTrangThai(CancellationToken cancellationToken)
    {
        var mode = (_configuration["Zalo:Mode"] ?? ZaloSendDefaults.ModeWhenUnset).Trim();
        var dto = new ZaloGuiTrangThaiDto
        {
            Mode = mode,
            IsPersonal = IsZaloPersonalModeStatic(mode)
        };

        if (!dto.IsPersonal)
        {
            dto.Hint = string.Equals(mode, "Zns", StringComparison.OrdinalIgnoreCase)
                ? "Chế độ ZNS: cần AccessToken OA + Zns:TemplateId trong cấu hình."
                : string.Equals(mode, "HttpTest", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "HttpBin", StringComparison.OrdinalIgnoreCase)
                ? "Chế độ HttpTest: chỉ POST thử (httpbin), không có tin trên app Zalo — chỉ dùng khi dev."
                : $"Chế độ Zalo «{mode}»: kiểm tra Zalo:Mode (Personal / Zns / HttpTest).";
            return Ok(dto);
        }

        var baseUrl = _configuration["Zalo:Personal:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            dto.Hint = "Chưa cấu Zalo:Personal:BaseUrl (vd. http://127.0.0.1:3847).";
            return Ok(dto);
        }

        var client = _httpClientFactory.CreateClient("ZaloPersonalBridgeProbe");
        var healthUri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "health");
        using var req = new HttpRequestMessage(HttpMethod.Get, healthUri);
        var apiKey = _configuration["Zalo:Personal:ApiKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        try
        {
            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
            dto.PersonalBridgeReachable = resp.IsSuccessStatusCode;
            if (!resp.IsSuccessStatusCode)
            {
                dto.Hint = $"Bridge trả HTTP {(int)resp.StatusCode}. Kiểm tra BaseUrl và bridge.";
                return Ok(dto);
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var root = doc.RootElement;
            dto.PersonalLoggedIn = root.TryGetProperty("loggedIn", out var li) && li.ValueKind == JsonValueKind.True;
            dto.CredentialsFileExists = root.TryGetProperty("credentialsFileExists", out var cf) && cf.ValueKind == JsonValueKind.True;
            dto.CredentialsValidShape = root.TryGetProperty("credentialsValidShape", out var cv) && cv.ValueKind == JsonValueKind.True;
            dto.WarmingUp = root.TryGetProperty("warmingUp", out var w) && w.ValueKind == JsonValueKind.True;
            dto.PersonalLastError = root.TryGetProperty("lastLoginError", out var le) && le.ValueKind == JsonValueKind.String
                ? le.GetString()
                : null;

            if (dto.CredentialsFileExists != true)
                dto.Hint = "Bridge đang chạy nhưng chưa có credentials.json → trong thư mục MinhTamHair-ZaloPersonalBridge chạy: npm run login";
            else if (dto.CredentialsValidShape != true)
                dto.Hint = "credentials.json chưa đúng (cookie rỗng?) → chạy: npm run login:force";
            else if (dto.WarmingUp == true)
                dto.Hint = "Đang đăng nhập Zalo nền… Đợi vài giây rồi bấm «Kiểm tra lại» hoặc gửi thử.";
            else if (dto.PersonalLoggedIn == true)
                dto.Hint = "Zalo Personal: sẵn sàng gửi tin chat.";
            else if (!string.IsNullOrWhiteSpace(dto.PersonalLastError))
                dto.Hint = dto.PersonalLastError;
            else
                dto.Hint = "Đợi bridge đăng nhập xong hoặc xem log terminal bridge.";
        }
        catch (OperationCanceledException)
        {
            dto.PersonalBridgeReachable = false;
            dto.Hint = "Không phản hồi kịp (timeout). Bridge có đang npm start không?";
        }
        catch (HttpRequestException)
        {
            dto.PersonalBridgeReachable = false;
            dto.Hint =
                "Không kết nối được bridge. Mở thư mục MinhTamHair-ZaloPersonalBridge → chay-bridge.cmd hoặc npm start (mặc định cổng 3847).";
        }
        catch (Exception ex)
        {
            dto.PersonalBridgeReachable = false;
            dto.Hint = ex.Message;
        }

        return Ok(dto);
    }

    private static bool IsZaloPersonalModeStatic(string mode) =>
        string.Equals(mode, "Personal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "Zca", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "ZcaBridge", StringComparison.OrdinalIgnoreCase);

    /// <summary>Danh sách khách hàng có lịch đặt — hiển thị thời gian hẹn để admin gửi thông báo nhắc lịch.</summary>
    [HttpGet("khach-hang-dat-lich")]
    public async Task<ActionResult<List<KhachHangDatLichDto>>> GetKhachHangDatLich(
        [FromQuery] string? ngay,
        [FromQuery] int? trangThai,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();

        // Mặc định lấy hôm nay + 7 ngày tới nếu không truyền ngày
        DateOnly targetDate;
        if (!string.IsNullOrWhiteSpace(ngay) && DateOnly.TryParse(ngay, out var parsedDate))
            targetDate = parsedDate;
        else
            targetDate = DateOnly.FromDateTime(DateTime.Today);

        var query = db.Lichdats
            .AsNoTracking()
            .Where(ld => ld.NgayHen >= targetDate && ld.NgayHen <= targetDate.AddDays(7))
            .AsQueryable();

        if (trangThai.HasValue)
            query = query.Where(ld => ld.TrangThai == trangThai.Value);

        var list = await query
            .OrderBy(ld => ld.NgayHen)
            .ThenBy(ld => ld.GioBatDau)
            .Select(ld => new KhachHangDatLichDto
            {
                MaLd = ld.MaLd,
                MaKh = ld.MaKh ?? "",
                HoTen = ld.MaKhNavigation != null ? ld.MaKhNavigation.HoTen : null,
                Sdt = ld.MaKhNavigation != null ? ld.MaKhNavigation.Sdt : null,
                Email = ld.MaKhNavigation != null ? ld.MaKhNavigation.Email : null,
                NgayHen = ld.NgayHen.HasValue ? ld.NgayHen.Value.ToString("yyyy-MM-dd") : null,
                GioBatDau = ld.GioBatDau.HasValue ? ld.GioBatDau.Value.ToString("HH:mm") : null,
                GioKetThuc = ld.GioKetThuc.HasValue ? ld.GioKetThuc.Value.ToString("HH:mm") : null,
                TrangThai = ld.TrangThai,
                TongTienDuKien = ld.TongTienDuKien
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    /// <summary>Danh sách khách hàng cho dropdown Admin (maKH, hoTen, SDT, email).</summary>
    [HttpGet("khach-hang")]
    public async Task<ActionResult<List<KhachHangListItemDto>>> GetKhachHangForSelect(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();

        var list = await db.Khachhangs.AsNoTracking()
            .OrderBy(k => k.HoTen)
            .Select(k => new KhachHangListItemDto
            {
                MaKh = k.MaKh,
                HoTen = k.HoTen,
                Sdt = k.Sdt,
                Email = k.Email
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpPost("Send")]
    public async Task<ActionResult<SendThongBaoResponse>> Send([FromBody] SendThongBaoRequest dto, CancellationToken cancellationToken)
    {
        if (!dto.SendViaGmail && !dto.SendViaZalo)
        {
            return BadRequest(new SendThongBaoResponse
            {
                Success = false,
                Message = "Vui lòng chọn ít nhất một kênh (Gmail hoặc Zalo)."
            });
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();

        var kh = await db.Khachhangs.AsNoTracking()
            .FirstOrDefaultAsync(k => k.MaKh == dto.MaKh, cancellationToken);

        if (kh == null)
            return NotFound(new SendThongBaoResponse { Success = false, Message = "Không tìm thấy khách hàng." });

        var tasks = new List<Task<ChannelSendResultDto>>();

        if (dto.SendViaGmail)
            tasks.Add(SendGmailInNewScopeAsync(kh.MaKh, kh.Email, dto.TieuDe, dto.NoiDung ?? "", cancellationToken));

        if (dto.SendViaZalo)
            tasks.Add(SendZaloInNewScopeAsync(kh.MaKh, kh.Sdt, dto.TieuDe, dto.NoiDung ?? "", cancellationToken));

        var results = tasks.Count == 0
            ? new List<ChannelSendResultDto>()
            : (await Task.WhenAll(tasks)).ToList();

        var allOk = results.All(r => r.TrangThai == 1);
        var anyOk = results.Any(r => r.TrangThai == 1);

        return Ok(new SendThongBaoResponse
        {
            Success = anyOk,
            Message = allOk
                ? "Đã gửi thành công qua các kênh đã chọn."
                : anyOk
                    ? "Một số kênh gửi thất bại — xem chi tiết từng kênh."
                    : "Gửi thất bại trên các kênh đã chọn.",
            Results = results
        });
    }

    private async Task<ChannelSendResultDto> SendGmailInNewScopeAsync(
        string maKh, string? email, string? tieuDe, string noiDung, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var tb = new Thongbao
        {
            MaKh = maKh,
            TieuDe = tieuDe,
            NoiDung = noiDung,
            KenhGui = "GMAIL",
            LoaiTin = "THU_CONG",
            NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
            TrangThai = 0
        };
        db.Thongbaos.Add(tb);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("Khách hàng chưa có email.");

            var html = $"<div style=\"font-family:sans-serif\"><p><strong>{WebUtility.HtmlEncode(tieuDe ?? "Thông báo")}</strong></p><pre style=\"white-space:pre-wrap\">{WebUtility.HtmlEncode(noiDung)}</pre></div>";
            await emailService.SendEmailAsync(email, tieuDe ?? "Thông báo từ Minh Tâm Hair", html);
            tb.TrangThai = 1;
            await db.SaveChangesAsync(cancellationToken);
            return new ChannelSendResultDto { KenhGui = "GMAIL", TrangThai = 1, ChiTiet = "Đã gửi email." };
        }
        catch (Exception ex)
        {
            tb.TrangThai = 2;
            await db.SaveChangesAsync(cancellationToken);
            return new ChannelSendResultDto { KenhGui = "GMAIL", TrangThai = 2, ChiTiet = ex.Message };
        }
    }

    private async Task<ChannelSendResultDto> SendZaloInNewScopeAsync(
        string maKh, string? sdt, string? tieuDe, string noiDung, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();
        var zalo = scope.ServiceProvider.GetRequiredService<IZaloService>();

        var tb = new Thongbao
        {
            MaKh = maKh,
            TieuDe = tieuDe,
            NoiDung = noiDung,
            KenhGui = "ZALO",
            LoaiTin = "THU_CONG",
            NgayTao = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now(),
            TrangThai = 0
        };
        db.Thongbaos.Add(tb);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(sdt))
                throw new InvalidOperationException("Khách hàng chưa có số điện thoại.");

            await zalo.SendAsync(sdt.Trim(), tieuDe, noiDung, cancellationToken);
            tb.TrangThai = 1;
            await db.SaveChangesAsync(cancellationToken);
            var zaloMode = (_configuration["Zalo:Mode"] ?? ZaloSendDefaults.ModeWhenUnset).Trim();
            string chiTiet;
            if (IsZaloPersonalMode(zaloMode))
            {
                chiTiet =
                    "Personal (zca-js bridge): đã gửi tin chat từ tài khoản Zalo trên bridge — người nhận xem trên app Zalo (cần SĐT khớp Zalo, thường phải là bạn bè / có thể nhận tin).";
            }
            else if (zalo.LastSendWasZnsDegradedToHttpTest)
            {
                chiTiet =
                    "HttpTest: Mode=Zns nhưng chưa có AccessToken hoặc TemplateId — đã gửi thử (httpbin), không tin trên app Zalo. " +
                    "Thêm token + Zns:TemplateId (hoặc đặt Zalo:Zns:StrictToken=true nếu muốn báo lỗi thay vì fallback).";
            }
            else if (IsZaloRealSend(zaloMode))
                chiTiet = "ZNS: đã gọi API Zalo. Kiểm tra thông báo trên app Zalo (số đã đăng ký Zalo).";
            else
                chiTiet =
                    "HttpTest: chỉ thử API (httpbin), không có tin trên app Zalo. Gửi thật: Zalo:Mode=Zns + OA token + TemplateId, hoặc Mode=Personal + bridge Node (MinhTamHair-ZaloPersonalBridge).";
            return new ChannelSendResultDto { KenhGui = "ZALO", TrangThai = 1, ChiTiet = chiTiet };
        }
        catch (Exception ex)
        {
            tb.TrangThai = 2;
            await db.SaveChangesAsync(cancellationToken);
            return new ChannelSendResultDto { KenhGui = "ZALO", TrangThai = 2, ChiTiet = ex.Message };
        }
    }

    private static bool IsZaloRealSend(string mode) =>
        string.Equals(mode, "Zns", StringComparison.OrdinalIgnoreCase)
        || IsZaloPersonalMode(mode);

    private static bool IsZaloPersonalMode(string mode) =>
        string.Equals(mode, "Personal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "Zca", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "ZcaBridge", StringComparison.OrdinalIgnoreCase);
}

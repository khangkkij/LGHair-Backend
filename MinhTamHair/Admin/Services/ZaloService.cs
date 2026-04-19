using System.Globalization;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;

namespace MinhTamHair.Services;

/// <summary>
/// Gửi tin Zalo: <c>Zns</c> = Zalo Notification Service (OA, SĐT + mẫu tin đã duyệt).
/// <c>Personal</c> = tin chat lên app qua dịch vụ Node (zca-js bridge: findUser + sendMessage).
/// <c>HttpTest</c> = POST tới URL tùy chỉnh (vd. httpbin) để dev.
/// Token/TemplateId: biến môi trường → IConfiguration (user-secrets, appsettings) → file appsettings.Zalo.local.json (gitignore).
/// </summary>
public class ZaloService : IZaloService
{
    private const string ZaloLocalFileName = "appsettings.Zalo.local.json";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ZaloService> _logger;
    private readonly IWebHostEnvironment _environment;

    private static readonly JsonSerializerOptions ZnsJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public bool LastSendWasZnsDegradedToHttpTest { get; private set; }

    public ZaloService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ZaloService> logger,
        IWebHostEnvironment environment)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    public async Task SendAsync(string phone, string? title, string content, CancellationToken cancellationToken = default)
    {
        LastSendWasZnsDegradedToHttpTest = false;
        var mode = (_configuration["Zalo:Mode"] ?? ZaloSendDefaults.ModeWhenUnset).Trim();
        if (string.Equals(mode, "Zns", StringComparison.OrdinalIgnoreCase))
        {
            var local = TryReadZaloLocalFile();
            var token = ResolveAccessToken(local);
            var templateId = ResolveTemplateId(local);
            var strict = IsStrictZns();

            if (IsUnsetOrPlaceholder(token) || IsUnsetOrPlaceholder(templateId))
            {
                if (strict)
                {
                    if (IsUnsetOrPlaceholder(token))
                        throw new InvalidOperationException("Zalo ZNS: thiếu AccessToken OA.");
                    throw new InvalidOperationException("Zalo ZNS: thiếu TemplateId.");
                }

                LastSendWasZnsDegradedToHttpTest = true;
                _logger.LogWarning(
                    "Zalo Mode=Zns nhưng thiếu AccessToken hoặc TemplateId — gửi HttpTest (httpbin), không có tin trên app Zalo.");
                await SendHttpTestAsync(phone, title, content, cancellationToken);
                return;
            }

            await SendZnsAsync(phone, title, content, cancellationToken);
            return;
        }

        if (string.Equals(mode, "Personal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Zca", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "ZcaBridge", StringComparison.OrdinalIgnoreCase))
        {
            await SendPersonalBridgeAsync(phone, title, content, cancellationToken);
            return;
        }

        if (string.Equals(mode, "HttpTest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "HttpBin", StringComparison.OrdinalIgnoreCase))
        {
            await SendHttpTestAsync(phone, title, content, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Zalo:Mode = '{mode}' không hợp lệ. Dùng Zns, Personal (bridge zca-js), hoặc HttpTest. " +
            "Xem appsettings.json mục Zalo.");
    }

    /// <summary>
    /// ZNS: cần OA Access Token + template_id đã duyệt; template_data phải khớp biến trên Zalo Cloud.
    /// Tài liệu: https://developers.zalo.me/docs/api/zalo-notification-service-api/gui-zns/gui-zns-post-5208
    /// </summary>
    private async Task SendZnsAsync(string phone, string? title, string content, CancellationToken cancellationToken)
    {
        var local = TryReadZaloLocalFile();

        var token = ResolveAccessToken(local);
        if (IsUnsetOrPlaceholder(token))
            throw new InvalidOperationException("Zalo ZNS: thiếu AccessToken OA.");

        var templateId = ResolveTemplateId(local);
        if (IsUnsetOrPlaceholder(templateId))
            throw new InvalidOperationException("Zalo ZNS: thiếu TemplateId.");

        var apiUrl = _configuration["Zalo:Zns:ApiUrl"]?.Trim();
        if (string.IsNullOrEmpty(apiUrl))
            apiUrl = "https://business.openapi.zalo.me/message/template";

        var phoneNorm = NormalizeVietnamPhone(phone);
        if (string.IsNullOrEmpty(phoneNorm))
            throw new InvalidOperationException("Zalo ZNS: số điện thoại không hợp lệ.");

        var titleKey = _configuration["Zalo:Zns:DataKeyTitle"]?.Trim();
        var contentKey = _configuration["Zalo:Zns:DataKeyContent"]?.Trim() ?? "noi_dung";

        var templateData = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(titleKey))
            templateData[titleKey] = title ?? string.Empty;
        templateData[contentKey] = string.IsNullOrEmpty(title)
            ? content
            : $"{title}{Environment.NewLine}{content}";

        var extraPrefix = "Zalo:Zns:ExtraData:";
        foreach (var kv in _configuration.AsEnumerable())
        {
            if (!kv.Key.StartsWith(extraPrefix, StringComparison.OrdinalIgnoreCase) || kv.Value == null)
                continue;
            var k = kv.Key[extraPrefix.Length..];
            if (!string.IsNullOrEmpty(k))
                templateData[k] = kv.Value;
        }

        var trackingPrefix = _configuration["Zalo:Zns:TrackingIdPrefix"]?.Trim();
        var trackingId = string.IsNullOrEmpty(trackingPrefix)
            ? $"MTH_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"
            : $"{trackingPrefix}_{Guid.NewGuid():N}";

        var oaId = ResolveOaId(local);
        var body = new ZnsSendBody
        {
            OaId = IsUnsetOrPlaceholder(oaId) ? null : oaId,
            Phone = phoneNorm,
            TemplateId = templateId!,
            TemplateData = templateData,
            TrackingId = trackingId.Length > 80 ? trackingId[..80] : trackingId
        };

        var uri = $"{apiUrl.TrimEnd('/')}?access_token={Uri.EscapeDataString(token!)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: ZnsJson)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Zalo ZNS HTTP {Status}: {Body}", (int)response.StatusCode, raw);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var errEl))
        {
            var code = errEl.ValueKind == JsonValueKind.Number
                ? errEl.GetInt32()
                : int.TryParse(errEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)
                    ? c
                    : -1;
            if (code != 0)
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : raw;
                throw new InvalidOperationException($"Zalo ZNS từ chối (error={code}): {msg}");
            }
        }

        var logBody = raw.Length > 500 ? raw[..500] + "…" : raw;
        _logger.LogInformation("Zalo ZNS gửi thành công tới {Phone}. Phản hồi: {Body}", phoneNorm, logBody);
    }

    private async Task SendHttpTestAsync(string phone, string? title, string content, CancellationToken cancellationToken)
    {
        var path = _configuration["Zalo:SendPath"] ?? "/post";
        var baseUrl = _configuration["Zalo:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("HttpTest: thiếu Zalo:ApiBaseUrl (vd. https://httpbin.org).");

        var uri = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(path)
            : new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

        var payload = new Dictionary<string, string?>
        {
            ["phone"] = phone,
            ["title"] = title,
            ["content"] = content
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };

        var accessToken = _configuration["Zalo:AccessToken"];
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Zalo HttpTest HTTP {Status}: {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogWarning(
            "Zalo HttpTest: POST tới {Uri} thành công nhưng KHÔNG gửi tin lên app Zalo. Đổi Zalo:Mode=Zns + token + TemplateId, hoặc Mode=Personal + chạy MinhTamHair-ZaloPersonalBridge.",
            uri);
    }

    /// <summary>
    /// Gửi tin chat cá nhân qua HTTP bridge chạy zca-js (POST /send: phone, title, content).
    /// Thư mục: MinhTamHair-ZaloPersonalBridge — cần credentials Zalo đã đăng nhập.
    /// </summary>
    private async Task SendPersonalBridgeAsync(string phone, string? title, string content, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Zalo:Personal:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(
                "Zalo Personal: thiếu Zalo:Personal:BaseUrl (vd. http://127.0.0.1:3847). Chạy npm start trong MinhTamHair-ZaloPersonalBridge.");

        var phoneNorm = NormalizeVietnamPhone(phone);
        if (string.IsNullOrEmpty(phoneNorm))
            throw new InvalidOperationException("Zalo Personal: số điện thoại không hợp lệ.");

        var sendPath = _configuration["Zalo:Personal:SendPath"]?.Trim();
        if (string.IsNullOrEmpty(sendPath))
            sendPath = "send";

        var uri = sendPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(sendPath)
            : new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), sendPath.TrimStart('/'));

        var payload = new Dictionary<string, string?>
        {
            ["phone"] = phoneNorm,
            ["title"] = title,
            ["content"] = content
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };

        var apiKey = _configuration["Zalo:Personal:ApiKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        {
            throw new InvalidOperationException(
                "Zalo Personal: bridge chưa chạy hoặc sai cổng (kết nối bị từ chối). Mở thư mục MinhTamHair-ZaloPersonalBridge → chạy «npm start» hoặc chay-bridge.cmd, rồi thử lại.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Zalo Personal: không gọi được bridge. Kiểm tra Zalo:Personal:BaseUrl và bridge đang bật.",
                ex);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Zalo Personal: hết thời gian chờ bridge. Bridge có đang treo hoặc chưa khởi động?");
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var hint = TryParseBridgeError(raw);
                _logger.LogWarning("Zalo Personal bridge HTTP {Status}: {Body}", (int)response.StatusCode, raw);
                throw new InvalidOperationException(
                    hint ?? $"Zalo Personal bridge HTTP {(int)response.StatusCode}: {TruncateForMessage(raw)}");
            }

            using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                {
                    var msg = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : raw;
                    throw new InvalidOperationException($"Zalo Personal: {msg ?? raw}");
                }
            }
        }

        _logger.LogInformation("Zalo Personal bridge gửi thành công tới {Phone}.", phoneNorm);
    }

    private static string? TryParseBridgeError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            if (!doc.RootElement.TryGetProperty("error", out var errEl)) return null;
            return errEl.ValueKind == JsonValueKind.String ? $"Zalo Personal: {errEl.GetString()}" : null;
        }
        catch
        {
            return null;
        }
    }

    private static string TruncateForMessage(string s, int max = 400) =>
        s.Length <= max ? s : s[..max] + "…";

    /// <summary>Ưu tiên: env → IConfiguration → file local (chỉ khi giá trị hợp lệ).</summary>
    private string? ResolveAccessToken(ZaloLocalSnapshot? local)
    {
        var t = Environment.GetEnvironmentVariable("Zalo__AccessToken")?.Trim();
        if (!IsUnsetOrPlaceholder(t)) return t;

        t = _configuration["Zalo:AccessToken"]?.Trim();
        if (!IsUnsetOrPlaceholder(t)) return t;

        t = local?.AccessToken?.Trim();
        return IsUnsetOrPlaceholder(t) ? null : t;
    }

    private string? ResolveTemplateId(ZaloLocalSnapshot? local)
    {
        var t = Environment.GetEnvironmentVariable("Zalo__Zns__TemplateId")?.Trim();
        if (!IsUnsetOrPlaceholder(t)) return t;

        t = _configuration["Zalo:Zns:TemplateId"]?.Trim();
        if (!IsUnsetOrPlaceholder(t)) return t;

        t = local?.TemplateId?.Trim();
        return IsUnsetOrPlaceholder(t) ? null : t;
    }

    private string? ResolveOaId(ZaloLocalSnapshot? local)
    {
        var t = Environment.GetEnvironmentVariable("Zalo__Zns__OaId")?.Trim();
        if (!IsUnsetOrPlaceholder(t)) return t;

        t = _configuration["Zalo:Zns:OaId"]?.Trim();
        if (!IsUnsetOrPlaceholder(t)) return t;

        t = local?.OaId?.Trim();
        return IsUnsetOrPlaceholder(t) ? null : t;
    }

    private ZaloLocalSnapshot? TryReadZaloLocalFile()
    {
        try
        {
            var path = Path.Combine(_environment.ContentRootPath, ZaloLocalFileName);
            if (!File.Exists(path))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Zalo", out var zalo))
                return null;

            var snap = new ZaloLocalSnapshot();
            if (zalo.TryGetProperty("AccessToken", out var at))
                snap.AccessToken = at.GetString();

            if (zalo.TryGetProperty("Zns", out var zns))
            {
                if (zns.TryGetProperty("TemplateId", out var tid))
                    snap.TemplateId = tid.ValueKind == JsonValueKind.Number
                        ? tid.GetInt64().ToString(CultureInfo.InvariantCulture)
                        : tid.GetString();
                if (zns.TryGetProperty("OaId", out var oid))
                    snap.OaId = oid.ValueKind == JsonValueKind.Number
                        ? oid.GetInt64().ToString(CultureInfo.InvariantCulture)
                        : oid.GetString();
            }

            return snap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được {File}; bỏ qua.", ZaloLocalFileName);
            return null;
        }
    }

    /// <summary>
    /// true = thiếu token/template sẽ ném lỗi (appsettings mặc định). false = tự fallback HttpTest (httpbin).
    /// </summary>
    private bool IsStrictZns() =>
        string.Equals(_configuration["Zalo:Zns:StrictToken"]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsetOrPlaceholder(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return true;
        var t = v.Trim();
        if (t.StartsWith("DÁN_", StringComparison.Ordinal)) return true;
        if (t.StartsWith("TÙY_CHỌN_", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Chuẩn hóa SĐT VN cho ZNS: 84xxxxxxxxx (không dấu +).</summary>
    internal static string NormalizeVietnamPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;
        if (digits.StartsWith("84", StringComparison.Ordinal))
            return digits;
        if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length >= 10)
            return "84" + digits[1..];
        if (digits.Length == 9)
            return "84" + digits;
        return digits;
    }

    private sealed class ZaloLocalSnapshot
    {
        public string? AccessToken { get; set; }
        public string? TemplateId { get; set; }
        public string? OaId { get; set; }
    }

    private sealed class ZnsSendBody
    {
        [JsonPropertyName("oa_id")]
        public string? OaId { get; set; }

        [JsonPropertyName("phone")]
        public string Phone { get; set; } = null!;

        [JsonPropertyName("template_id")]
        public string TemplateId { get; set; } = null!;

        [JsonPropertyName("template_data")]
        public Dictionary<string, string> TemplateData { get; set; } = new();

        [JsonPropertyName("tracking_id")]
        public string? TrackingId { get; set; }
    }
}

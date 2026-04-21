using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MinhTamHair.Services.LightX
{
    public class LightXService(HttpClient httpClient, ILogger<LightXService> logger) : ILightXService
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly ILogger<LightXService> _logger = logger;

        private const string UploadApiUrl = "https://api.lightxeditor.com/external/api/v2/uploadImageUrl";
        private const string Image2ImageApiUrl = "https://api.lightxeditor.com/external/api/v2/image2image";
        private const string HaircolorRgbApiUrl = "https://api.lightxeditor.com/external/api/v2/haircolor-rgb";
        private const string OrderStatusApiUrl = "https://api.lightxeditor.com/external/api/v2/order-status";

        public async Task<string> ProcessHairAndColorAsync(string apiKey, IFormFile userImageInput, string styleImageUrl, string textPrompt, double strength, string? colorHex)
        {
            try
            {
                _logger.LogInformation("Đang upload ảnh khách hàng lên LightX...");
                string uploadedUserImageUrl = await UploadAndGetUrlAsync(apiKey, userImageInput);

          
                _logger.LogInformation("Khởi tạo Job Image2Image V2 với Prompt từ Gemini...");

                // Mẹo rẽ nhánh: Nếu khách không chọn màu, ép thẳng màu đen ngay từ bước vẽ tóc để ra màu tự nhiên nhất
                string baseColor = string.IsNullOrWhiteSpace(colorHex) ? "black hair, " : "";
                string safePrompt = $"a person with {baseColor}{textPrompt}, exact hairstyle from reference image, photorealistic hair texture, keep original face, keep original background";

                var i2iPayload = new
                {
                    imageUrl = uploadedUserImageUrl,
                    textPrompt = safePrompt,
                    strength = strength,
                    styleImageUrl = styleImageUrl,
                    styleStrength = 0.8
                };

                string styleOrderId = await StartJob(apiKey, Image2ImageApiUrl, i2iPayload);
                string currentImageUrl = await PollJob(apiKey, styleOrderId, "Ghép Tóc Image2Image");

                // ==========================================
                // BƯỚC 2: NHUỘM MÀU MÃ HEX (Chỉ chạy khi có chọn màu)
                // ==========================================
                if (!string.IsNullOrWhiteSpace(colorHex))
                {
                    _logger.LogInformation("Khách có chọn màu. Gọi API haircolor-rgb với mã: {Hex}", colorHex);

                    // Chuẩn hóa mã Hex để chống lỗi mất dấu #
                    if (!colorHex.StartsWith("#")) colorHex = "#" + colorHex;

                    var colorPayload = new
                    {
                        imageUrl = currentImageUrl, // Lấy ảnh vừa ghép xong làm phôi
                        hairHexColor = colorHex,    // Đổ mã Hex vào
                        colorStrength = 0.85        // Độ bám màu (từ 0.1 đến 1.0)
                    };

                    string colorOrderId = await StartJob(apiKey, HaircolorRgbApiUrl, colorPayload);
                    currentImageUrl = await PollJob(apiKey, colorOrderId, "Nhuộm Màu RGB");
                }
                else
                {
                    _logger.LogInformation("Khách không chọn màu -> Bỏ qua bước nhuộm RGB, tiết kiệm chi phí API.");
                }

                return currentImageUrl; // Trả về ảnh final cho Controller
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong chuỗi xử lý LightX TryOn.");
                throw new Exception(ex.Message);
            }
        }

        private async Task<string> UploadAndGetUrlAsync(string apiKey, IFormFile imageFile)
        {
            using var memoryStream = new MemoryStream();
            await imageFile.CopyToAsync(memoryStream);
            byte[] imageBytes = memoryStream.ToArray();

            string contentType = string.IsNullOrEmpty(imageFile.ContentType) ? "image/jpeg" : imageFile.ContentType;

            var urlPayload = new { uploadType = "imageUrl", size = imageBytes.Length, contentType = contentType };

            var urlRequest = new HttpRequestMessage(HttpMethod.Post, UploadApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(urlPayload), Encoding.UTF8, "application/json")
            };
            urlRequest.Headers.Add("x-api-key", apiKey);

            var urlResponse = await _httpClient.SendAsync(urlRequest);
            var urlResponseString = await urlResponse.Content.ReadAsStringAsync();

            if (!urlResponse.IsSuccessStatusCode)
                throw new Exception($"Lỗi xin URL Upload: {urlResponseString}");

            var urlData = JsonSerializer.Deserialize<JsonElement>(urlResponseString);
            if (urlData.GetProperty("statusCode").GetInt32() != 2000)
                throw new Exception($"LightX từ chối cấp URL: {urlData.GetProperty("message").GetString()}");

            string uploadUrl = urlData.GetProperty("body").GetProperty("uploadImage").GetString()!;
            string finalImageUrl = urlData.GetProperty("body").GetProperty("imageUrl").GetString()!;

            var uploadContent = new ByteArrayContent(imageBytes);
            uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            var putResponse = await _httpClient.PutAsync(uploadUrl, uploadContent);
            if (!putResponse.IsSuccessStatusCode)
                throw new Exception("Lỗi khi PUT dữ liệu ảnh lên máy chủ LightX.");

            return finalImageUrl;
        }

        private async Task<string> StartJob(string apiKey, string endpoint, object payload)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", apiKey);

            var response = await _httpClient.SendAsync(request);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Lỗi gọi API {endpoint}: {responseString}");

            var data = JsonSerializer.Deserialize<JsonElement>(responseString);
            if (data.GetProperty("statusCode").GetInt32() != 2000)
                throw new Exception($"LightX từ chối Job: {data.GetProperty("message").GetString()}");

            return data.GetProperty("body").GetProperty("orderId").GetString()!;
        }

        private async Task<string> PollJob(string apiKey, string orderId, string jobName)
        {
            const int maxRetries = 15;
            const int delayMs = 3000;

            var payload = new { orderId = orderId };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            for (int i = 0; i < maxRetries; i++)
            {
                await Task.Delay(delayMs);

                var request = new HttpRequestMessage(HttpMethod.Post, OrderStatusApiUrl) { Content = content };
                request.Headers.Add("x-api-key", apiKey);

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Lỗi kết nối kiểm tra [{jobName}]: {responseString}");

                var data = JsonSerializer.Deserialize<JsonElement>(responseString);
                var status = data.GetProperty("body").GetProperty("status").GetString();

                if (status == "active") return data.GetProperty("body").GetProperty("output").GetString()!;
                if (status == "failed") throw new Exception($"Tiến trình [{jobName}] THẤT BẠI.");
            }

            throw new TimeoutException($"Hết thời gian chờ [{jobName}].");
        }
    }
}
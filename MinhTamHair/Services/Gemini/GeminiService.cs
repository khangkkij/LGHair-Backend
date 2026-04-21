//string geminiApiKey = "AIzaSyCqVETCVunX-qGWfrfC_7mwykJho7ZBY8M";
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MinhTamHair.Services.Gemini
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public GeminiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string> AnalyzeHairstyleFromUrlAsync(string imageUrl,string? moTa)
        {
            string? geminiApiKey = "AIzaSyCqVETCVunX-qGWfrfC_7mwykJho7ZBY8M";



            if (string.IsNullOrEmpty(geminiApiKey))
                throw new Exception("Lỗi: Chưa cấu hình Gemini API Key trong appsettings.json.");

            byte[] imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
            string base64Image = Convert.ToBase64String(imageBytes);
            string mimeType = imageUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

            //            string systemPrompt = @"Hãy đóng vai một hair stylist chuyên nghiệp. Quan sát kiểu tóc trong ảnh và mô tả lại THẬT NGHIÊM TÚC bằng tiếng Anh trong đúng 1 câu duy nhất, ngắn gọn và rõ ràng để dùng làm prompt cho AI chỉnh sửa ảnh.

            //Chỉ mô tả: độ dài tổng thể, kết cấu (straight, wavy, curly), phần tóc mái (bangs), và hai bên.

            //QUY TẮC BẮT BUỘC:
            //- Chỉ trả về 1 câu duy nhất bằng tiếng Anh, không xuống dòng.
            //- Không thêm giải thích hoặc bất kỳ nội dung nào khác.
            //- Không dùng dấu ngoặc kép.
            //- Không nhắc đến giới tính dưới bất kỳ hình thức nào.
            //- Không mô tả khuôn mặt, cơ thể, quần áo hoặc nền.
            //- Tránh các từ: skin, cut, fade, shave, naked, blood.
            //- Ưu tiên các từ mô tả an toàn: ""short sides"", ""tapered sides"", ""layered"", ""textured"", ""soft fringe"", ""full bangs"".
            //- Câu mô tả phải rõ ràng để AI có thể tái tạo lại kiểu tóc trên một khuôn mặt khác.";
            string systemPrompt = @"You are an expert hair stylist. Analyze the hairstyle in the image and describe it in EXACTLY ONE short English sentence.
Focus ONLY on: overall length, hair texture (straight, wavy, curly), bangs/fringe, and the sides.

STRICT RULES (CRITICAL):
1. OUTPUT EXACTLY ONE SENTENCE. No conversational filler, no greetings, no explanations, no quotes.
2. DO NOT mention gender (never use words like man, woman, boy, girl, male, female, he, she).
3. DO NOT describe the face, body, clothing, or background.
4. BANNED WORDS: skin, cut, fade, shave, naked, blood. DO NOT USE THEM UNDER ANY CIRCUMSTANCES.
5. PREFERRED VOCABULARY: use safe terms like 'short sides', 'tapered sides', 'layered', 'textured', 'soft fringe', 'full bangs'.";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = systemPrompt },
                            new { inline_data = new { mime_type = mimeType, data = base64Image } }
                        }
                    }
                }
            };


            string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={geminiApiKey}";
          
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(apiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini/Gemma AI bị lỗi: {error}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);

            string generatedPrompt = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text").GetString()!;

            generatedPrompt = generatedPrompt.Replace("\n", " ").Trim();

            if (generatedPrompt.Contains("INVALID_IMAGE"))
            {
            
                throw new Exception("Ảnh không hợp lệ. Vui lòng tải lên ảnh khác!");
            }

            return generatedPrompt;
        }
    }
}
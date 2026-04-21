using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MinhTamHair.DTO.Chatbot;
using MinhTamHair.Models;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly MinhTamHairContext _dbContext;
        public ChatbotController( HttpClient httpClient, MinhTamHairContext dbContext)
        {

            _httpClient = httpClient;
            _dbContext = dbContext;
        }


        [HttpPost]
        public async Task<IActionResult> PostMessage([FromBody] ChatbotRequest mes)
        {

            try
            {
                var productDataList = await _dbContext.Dichvus.Select(d => new
                {
                    d.TenDv,
                    d.MoTa,
                    d.Gia,
                    HinhAnh = d.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault() ?? ""
                }).ToListAsync();


                var formattedProducts = string.Join("\n", productDataList.Select(d =>
    $"- Tên dịch vụ: {d.TenDv} | Mô tả: {d.MoTa} | Giá: {d.Gia} | Hình ảnh: {d.HinhAnh}"
));

                // 3. Tạo Payload
                var ollamaPayload = new
            {
                model = "qwen2.5",
                messages = new[]
                {
                new {
                    role = "system",
                    content = $@"Bạn là nhân viên chăm sóc khách hàng nhiệt tình của salon tóc Minh Tâm. 
                        Nhiệm vụ của bạn là tư vấn và giới thiệu các dịch vụ làm tóc phù hợp với nhu cầu của khách hàng.
                        Chỉ tư vấn dựa trên danh sách dịch vụ sau:
                        {formattedProducts}

                        CHÚ Ý QUAN TRỌNG: 
                        - Chỉ trả lời các dịch vụ có trong danh sách trên.
                        - Trả lời ngắn gọn, lịch sự, dùng tiếng Việt tự nhiên.
                        - CHỈ tư vấn các dịch vụ có trong danh sách trên.
                        - KHI GIỚI THIỆU DỊCH VỤ, BẮT BUỘC PHẢI ĐÍNH KÈM HÌNH ẢNH bằng cú pháp Markdown: ![Tên dịch vụ](Hình ảnh).
                        - Trả lời ngắn gọn, lịch sự, dùng tiếng Việt tự nhiên.
                        - Tuyệt đối không điều hướng khách hàng đến đối thủ khác. Không vòng vo."
                                        },
                new {
                    role = "user",
                    content = mes.Message
                }
            },
                stream = false,
                option = new {
                    num_predict = 2048,
                    temperature = 0.5,
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(ollamaPayload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("http://localhost:11434/api/chat", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(500, "Không thể kết nối đến AI. Hãy chắc chắn bạn đã bật Ollama.");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseString);

            string aiReply = jsonDoc.RootElement
                                    .GetProperty("message")
                                    .GetProperty("content")
                                    .GetString() ?? "Xin lỗi, tôi không hiểu.";
            return Ok(new
            {
                reply = aiReply
            });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Đã xảy ra lỗi: {ex.Message}");
            }
        }
    }
}
    

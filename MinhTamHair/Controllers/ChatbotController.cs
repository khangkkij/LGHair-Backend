using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MinhTamHair.DTO.Chatbot;
using MinhTamHair.Models;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer; 

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly MinhTamHairContext _dbContext;

        public ChatbotController(HttpClient httpClient, MinhTamHairContext dbContext)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
        }

        [HttpPost]
        public async Task<IActionResult> PostMessage([FromBody] ChatbotRequest mes)
        {
            try
            {

                var serviceDataList = await _dbContext.Dichvus.Select(d => new
                {
                    d.TenDv,
                    d.MoTa,
                    d.Gia,
                    HinhAnh = d.Hinhanhs.Select(h => h.DuongDan).FirstOrDefault() ?? ""
                }).ToListAsync();

                var formattedProducts = string.Join("\n", serviceDataList.Select(d =>
                    $"- Tên dịch vụ: {d.TenDv} | Mô tả: {d.MoTa} | Giá: {d.Gia} | Hình ảnh: {d.HinhAnh}"
                ));
                //var productDataList = await _dbContext.SanPhams.Where(sp => sp.TrangThai == 1).Include(x => x.BienTheSanphams).Where(bt => bt.TrangThai == 1).Include(sp => sp.Thuonghieu).Select(sp => new
                //{

                //    sp.TenSp,
                //    sp.Thuonghieu.TenThuongHieu,
                //    BienTheSanphams = sp.BienTheSanphams.Select(bt => new
                //    {

                //        bt.GiaBan,
                //        bt.SoLuongTon,
                //        bt.HinhAnhDaiDien

                //    }).ToList()
                //}).ToListAsync();
            


                string systemPrompt = $@"Bạn là nhân viên chăm sóc khách hàng nhiệt tình của salon tóc LGHair. 
                                    Nhiệm vụ của bạn là tư vấn và giới thiệu các dịch vụ làm tóc phù hợp với nhu cầu của khách hàng.
                                    Chỉ tư vấn dựa trên danh sách dịch vụ sau:
                                    {formattedProducts}

                                    CHÚ Ý QUAN TRỌNG: 
                                    - Chỉ trả lời các dịch vụ có trong danh sách trên.
                                    - Trả lời ngắn gọn, lịch sự, dùng tiếng Việt tự nhiên.
                                    - KHI GIỚI THIỆU DỊCH VỤ, BẮT BUỘC PHẢI ĐÍNH KÈM HÌNH ẢNH bằng cú pháp Markdown: ![Tên dịch vụ](Hình ảnh).
                                    - Tuyệt đối không điều hướng khách hàng đến đối thủ khác. Không vòng vo.";


                string? userId = null;


                var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

                if (authResult.Succeeded && authResult.Principal != null)
                {
                    var principal = authResult.Principal;

                    userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? principal.FindFirstValue("Id")
                          ?? principal.FindFirstValue("id")
                          ?? principal.FindFirstValue("MaKh")
                          ?? principal.FindFirstValue("UserId");
                }

                // Nếu có Token hợp lệ -> Tiến hành lấy lịch sử
                if (!string.IsNullOrEmpty(userId))
                {
                    var khachHang = await _dbContext.Khachhangs.FirstOrDefaultAsync(k => k.MaKh == userId);

                    if (khachHang != null)
                    {
                        // Lấy 3 dịch vụ gần nhất khách đã làm thành công
                        var lichSu = await _dbContext.Lichdats
                            .Where(ld => ld.MaKh == userId)
                            .Where(ld => ld.TrangThai == 3)
                            .OrderByDescending(ld => ld.NgayHen)
                            .Take(3)
                            .SelectMany(ld => ld.ChitietLichdats)
                            .Select(ct => ct.MaDvNavigation.TenDv)
                            .Distinct()
                            .ToListAsync();

                        if (lichSu.Any())
                        {
                            var dsLichSu = string.Join(", ", lichSu);

                            systemPrompt += $@"

                                            THÔNG TIN KHÁCH HÀNG HIỆN TẠI (ĐÃ ĐĂNG NHẬP):
                                            - Tên khách hàng: {khachHang.HoTen}
                                            - Các dịch vụ khách đã từng sử dụng gần đây: {dsLichSu}

                                            NHIỆM VỤ BỔ SUNG DÀNH CHO KHÁCH QUEN NÀY: 
                                            1. Mở đầu câu trả lời, hãy chào tên khách hàng một cách thân thiện (Ví dụ: Chào anh/chị {khachHang.HoTen}...).
                                            2. Chủ động hỏi thăm xem khách hàng cảm thấy thế nào về các dịch vụ '{dsLichSu}' đã làm lần trước.
                                            3. Khéo léo hỏi xem khách có muốn đặt lại dịch vụ đó hôm nay không, hoặc tư vấn thêm dịch vụ mới phù hợp.";
                        }
                    }
                }


                var ollamaPayload = new
                {
                    model = "qwen2.5",
                    messages = new[]
                    {
                        new {
                            role = "system",
                            content = systemPrompt
                        },
                        new {
                            role = "user",
                            content = mes.Message
                        }
                    },
                    stream = false,
                    options = new
                    {
                        num_predict = 2048,
                        temperature = 0.5,
                    }
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(ollamaPayload), Encoding.UTF8, "application/json");

                // Gọi API của Ollama chạy ở localhost
                var response = await _httpClient.PostAsync("http://localhost:11434/api/chat", jsonContent);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(500, new { message = "Không thể kết nối đến AI. Hãy chắc chắn bạn đã bật Ollama." });
                }

                // Đọc chuỗi JSON trả về
                var responseString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseString);

                string aiReply = jsonDoc.RootElement
                                        .GetProperty("message")
                                        .GetProperty("content")
                                        .GetString() ?? "Xin lỗi, tôi không hiểu.";

                return Ok(new { reply = aiReply });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Đã xảy ra lỗi hệ thống: {ex.Message}" });
            }
        }
    }
}
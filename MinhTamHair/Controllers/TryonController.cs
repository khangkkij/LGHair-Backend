using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.DTO.Tryon;
using MinhTamHair.Models;
using MinhTamHair.Services.LightX;
using MinhTamHair.Services.Gemini; // Sửa Using
using System.Text.RegularExpressions;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // Bổ sung GeminiService vào Constructor
    public class TryonController(MinhTamHairContext context, ILightXService lightXHairService, GeminiService geminiService) : ControllerBase
    {
        private readonly MinhTamHairContext _context = context;
        private readonly ILightXService _lightXHairService = lightXHairService;
        private readonly GeminiService _geminiService = geminiService;

        [HttpPost("process")]
        public async Task<IActionResult> TryOnHairAndColor([FromForm] HairTryOnRequest request)
        {
            if (string.IsNullOrEmpty(request.ApiKey))
                return BadRequest(new { success = false, message = "Thiếu API Key." });

            try
            {
                // 1. LẤY LINK ẢNH TỪ DATABASE
                var kieuToc = await _context.Kifutocs
                    .Where(x => x.MaKt == request.MaKT)
                    .Select(x => new { x.HinhAnh, x.TenKieuToc, x.MoTa })
                    .FirstOrDefaultAsync();

                if (kieuToc == null || string.IsNullOrEmpty(kieuToc.HinhAnh))
                    return NotFound(new { success = false, message = "Không tìm thấy kiểu tóc hoặc chưa có ảnh mẫu." });

                string enPrompt = await _geminiService.AnalyzeHairstyleFromUrlAsync(kieuToc.HinhAnh,kieuToc.MoTa);

                // Lọc sạch Regex tránh lỗi Invalid Prompts
                string finalPrompt = Regex.Replace(enPrompt + " hairstyle", @"[^a-zA-Z0-9\s,\.-]", "").Trim();

                // 3. LẤY MÃ MÀU HEX 
                string? colorHex = null;
                if (!string.IsNullOrEmpty(request.MaMau))
                {
                    colorHex = await _context.Bangmaus
                        .Where(x => x.MaMau == request.MaMau)
                        .Select(x => x.MaMau) 
                        .FirstOrDefaultAsync();
                }

                // 4. GỌI LIGHTX (Giao cho thợ cắt)
                string resultUrl = await _lightXHairService.ProcessHairAndColorAsync(
                    request.ApiKey,
                    request.UserImage,
                    kieuToc.HinhAnh, 
                    finalPrompt,     
                    0.45,            
                    colorHex
                );

                // Trả về kèm câu prompt Gemini nó đọc được để bạn xem cho dễ debug
                return Ok(new { success = true, finalImageUrl = resultUrl, aiPromptUsed = finalPrompt });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
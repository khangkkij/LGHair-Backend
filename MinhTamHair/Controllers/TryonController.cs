using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.DTO.Tryon;
using MinhTamHair.Models;
using MinhTamHair.Services.LightX;
using System.Text.RegularExpressions;
// ĐÃ XÓA: using MinhTamHair.Services.Gemini;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // ĐÃ XÓA: GeminiService khỏi constructor
    public class TryonController(MinhTamHairContext context, ILightXService lightXHairService) : ControllerBase
    {
        private readonly MinhTamHairContext _context = context;
        private readonly ILightXService _lightXHairService = lightXHairService;

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

                // ĐÃ SỬA: Loại bỏ Gemini, dùng trực tiếp MoTa làm Prompt
                string finalPrompt = Regex.Replace(
                    kieuToc.MoTa ?? "",
                    @"[^a-zA-Z0-9\s,\.-]",
                    ""
                ).Trim();

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

                // Trả về kèm câu prompt đã dùng để dễ debug ở phía Frontend
                return Ok(new { success = true, finalImageUrl = resultUrl, aiPromptUsed = finalPrompt });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
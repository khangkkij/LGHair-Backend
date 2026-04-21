using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace MinhTamHair.Services.LightX
{
    public interface ILightXService
    {
        Task<string> ProcessHairAndColorAsync(string apiKey, IFormFile userImageInput, string styleImageUrl, string textPrompt, double strength, string? colorHex);
    }
}
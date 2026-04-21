using System.Text.Json;
using System.Web;

namespace MinhTamHair.Services.Translate
{
    public class TranslateService
    {
        public static async Task<string> TranslateVietnameseToEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            try
            {
                
                string encodedText = HttpUtility.UrlEncode(text);
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl=en&dt=t&q={encodedText}";

                using var client = new HttpClient();
                var response = await client.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(response);
                string translatedText = doc.RootElement[0][0][0].GetString()!;

                return translatedText;
            }
            catch
            {
                return text;
            }
        }
    }
}

namespace MinhTamHair.DTO.Tryon
{
    public class HairTryOnRequest
    {
        public string ApiKey { get; set; } 
        public string? MaKH { get; set; }
        public string MaKT { get; set; }
        public string? MaMau { get; set; }
        public IFormFile UserImage { get; set; }
    }
}

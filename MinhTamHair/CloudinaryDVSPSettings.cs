namespace MinhTamHair.Services
{
    // Cấu hình Cloudinary riêng cho module Dịch Vụ / Sản Phẩm (DVSP).
    // Tách khỏi CloudinarySetings (dùng chung cho các module khác như avatar user...)
    // để sau này có thể đổi sang 1 account Cloudinary khác cho DVSP mà không ảnh hưởng
    // tới các module còn lại.
    public class CloudinaryDVSPSettings
    {
        public string CloudName { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
    }
}

namespace MinhTamHair.KhanchHang.Models
{
    public class PendingOrderResponse
    {
        public string Id { get; set; }
        public DateTime Date { get; set; }
        public string ViewMode { get; set; } = "";
        public List<ServiceItem> Services { get; set; }
        public List<StaffItem> Staffs { get; set; }
    }

    public class ServiceItem
    { 
        public string Id { get; set; }
        public string Name { get; set; }
        public int Rating { get; set; } = 0;
        public string Comment { get; set; } = "";
        public bool IsAlreadyRated { get; set; }
    }

    public class StaffItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
        public string? Avatar { get; set; }
        public int Rating { get; set; } = 0;
        public string Comment { get; set; } = "";
    }

    public class ServiceRatingDTO
    {
        public string? MaDv { get; set; }
        public string? MaCb { get; set; }
        public int SoSao { get; set; }
        public string? NoiDung { get; set; }
        // Nhận file hình ảnh từ FormData
        public IFormFile? HinhAnhFile { get; set; }
    }
    public class OrderOverallRatingDTO
    {
        public string MaHd { get; set; }
        public int GeneralRating { get; set; }
        public string? GeneralComment { get; set; }
    }
    public class OrderOverallRatingWithImageDTO
    {
        public string MaHd { get; set; }
        public int GeneralRating { get; set; }
        public string? GeneralComment { get; set; }
        public IFormFile? HinhAnhFile { get; set; } // Thuộc tính để nhận file ảnh
    }
    // Thêm vào file DTO hiện tại của bạn
    public class ProductRatingDTO
    {
        public string MaBienThe { get; set; }
        public int SoSao { get; set; }
        public string? NoiDung { get; set; }
        public IFormFile? HinhAnhFile { get; set; }
    }
}

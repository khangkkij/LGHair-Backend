namespace MinhTamHair.KhanchHang.Models
{
    // Model dùng riêng cho API thanh toán để tránh nhầm lẫn
    public class PaymentInformationRequest
    {
        public double Amount { get; set; }
        public string OrderDescription { get; set; } = string.Empty;
        public string PaymentMode { get; set; } = string.Empty;
    }

    public class BookingRequest
    {
        public string BookingId { get; set; } = string.Empty;
        public string MaKh { get; set; } = string.Empty;
        public DateTime NgayHen { get; set; }
        public decimal TotalAmount { get; set; }
        public List<ServiceItemRequest> Services { get; set; } = new();
    }

    public class ServiceItemRequest
    {
        public string Id { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
    public class MomoResponse
    {
        public string partnerCode { get; set; } = string.Empty;
        public string orderId { get; set; } = string.Empty;
        public string requestId { get; set; } = string.Empty;
        public long amount { get; set; }
        public string payUrl { get; set; } = string.Empty; // Đây là link để khách thanh toán
        public string message { get; set; } = string.Empty;
        public int resultCode { get; set; }
    }
}
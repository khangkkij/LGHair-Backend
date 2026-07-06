namespace MinhTamHair.KhanchHang.Models
{
    // Model nhận dữ liệu từ Vue gửi lên
    public class ProductCheckoutRequest
    {
        public string MaKh { get; set; } = string.Empty;
        public string HoTen { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;

        // "cod", "vnpay", "momo"
        public string PaymentMethod { get; set; } = "cod";
        public decimal ShippingFee { get; set; }
        public List<string>? VoucherIds { get; set; } = new();

        // Danh sách sản phẩm mua
        public List<ProductItemRequest> Products { get; set; } = new();
        // Hóa đơn điện tử
        public InvoiceInfoDto? InvoiceInfo { get; set; }
    }

    // Quan trọng: Phải dùng Mã Biến Thể thay vì Mã Sản Phẩm chung chung
    public class ProductItemRequest
    {
        public string MaBienThe { get; set; } = string.Empty;
        public decimal GiaBan { get; set; }
        public int SoLuong { get; set; }
    }
    //Hoá đơn điện tử
    public class InvoiceInfoDto
    {
        public bool XuatHoaDon { get; set; }
        public bool LaDoanhNghiep { get; set; }
        public string? TenCongTy { get; set; }
        public string? Mst { get; set; }
        public string? DiaChiXuatHD { get; set; }
        public string? EmailNhanHD { get; set; }
    }
}  
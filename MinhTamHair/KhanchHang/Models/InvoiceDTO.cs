namespace MinhTamHair.KhanchHang.Models
{
    public class InvoiceDTO
    {
        public string MaLd { get; set; }
        public string TenKhach { get; set; }
        public string SoDienThoai { get; set; }
        public string ThoiGian { get; set; } // Ngày + Giờ hẹn
        public List<InvoiceServiceItem> DanhSachDichVu { get; set; }
        public decimal TongTien { get; set; }
        public decimal DaThanhToan { get; set; }
        public decimal ConLai { get; set; }
    }

    public class InvoiceServiceItem
    {
        public string Ten { get; set; }
        public decimal Gia { get; set; }
    }
}

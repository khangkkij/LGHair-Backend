namespace MinhTamHair.KhanchHang.Models
{
    public class ServiceItemDto
    {
        public string Id { get; set; }
        public string TenDv { get; set; }
        public decimal Gia { get; set; }
        public int ThoiGianLam { get; set; }
        public string MoTa { get; set; }
        public string HinhAnh { get; set; }
        public decimal GiaGoc { get; set; }
        public int? PhanTramGiam { get; set; }
        public DateTime? NgayKetThuc { get; set; }
    }
}

namespace MinhTamHair.Admin.Models
{
    public class CheckInViewDTO
    {
        public string MaLd { get; set; }
        public string TenKhach { get; set; }
        public string Sdt { get; set; }
        public string NgayHen { get; set; } // Định dạng ISO để Vue dễ lọc
        public decimal TongTien { get; set; }
        public decimal DaThanhToan { get; set; }
        public string MaNv { get; set; }
        public List<ServiceItemDTO> DichVus { get; set; }
    }

    public class ServiceItemDTO
    {
        public string Id { get; set; }
        public string Ten { get; set; }
        public decimal Gia { get; set; }
    }
}

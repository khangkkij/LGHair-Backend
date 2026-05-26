namespace MinhTamHair.Models
{
    public class YeuThichDichVu
    {
        public string MaKH { get; set; }
        public Khachhang KhachHang { get; set; }

        public string MaDV { get; set; }
        public Dichvu Dichvu { get; set; }

        public DateTime NgayThem { get; set; } = DateTime.Now;
    }
}

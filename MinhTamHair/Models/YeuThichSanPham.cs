namespace MinhTamHair.Models
{
    public class YeuThichSanPham
    {
        public string MaKH { get; set; }
        public Khachhang KhachHang { get; set; }

        public string MaSP { get; set; }
        public Sanpham SanPham { get; set; }

        public DateTime NgayThem { get; set; } = DateTime.Now;
    }
}

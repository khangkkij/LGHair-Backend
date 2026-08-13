namespace MinhTamHair.Models
{
    public class YeuThichSanPham
    {
        public string MaKH { get; set; }
        public Khachhang KhachHang { get; set; }

        public string MaSP { get; set; }
        public SanPham SanPham { get; set; }

        public DateTime NgayThem { get; set; } = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
    }
}

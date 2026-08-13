namespace MinhTamHair.Models
{
    public class YeuThichNhanVien
    {
        public string MaKH { get; set; }
        public Khachhang KhachHang { get; set; }

        public string MaNV { get; set; }
        public Nhanvien nhanVien { get; set; }

        public DateTime NgayThem { get; set; } = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now();
    }
}

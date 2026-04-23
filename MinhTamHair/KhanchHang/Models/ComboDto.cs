namespace MinhTamHair.KhanchHang.Models
{
    public class ComboDto
    {
        public string Id { get; set; }
        public string TenCombo { get; set; }
        public decimal Gia { get; set; }
        public string MoTa { get; set; }
        public string HinhAnh {  get; set; }
        public List<string> Items { get; set; }
        public decimal GiaGoc { get; set; }
        public int? PhanTramGiam { get; set; }
        public DateTime? NgayKetThuc { get; set; }
    }
}

namespace MinhTamHair.KhanchHang.Models
{
    public class ComboDto
    {
        public string Id { get; set; }
        public string TenCombo { get; set; }
        public decimal Gia { get; set; }
        public string MoTa { get; set; }

        public List<string> Items { get; set; }
    }
}

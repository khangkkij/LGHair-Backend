namespace MinhTamHair.KhanchHang.Models
{
    public class ServiceDetailDto
    {
        public string MaDv { get; set; }
        public string TenDv { get; set; }
        public decimal Gia { get; set; }
        public int ThoiGianLam { get; set; }
        public string MoTa { get; set; }

        public List<string> Images { get; set; }

        public List<ComboDto> RelatedCombos { get; set; }

        public List<ServiceItemDto> SuggestedServices { get; set; }
    }
}

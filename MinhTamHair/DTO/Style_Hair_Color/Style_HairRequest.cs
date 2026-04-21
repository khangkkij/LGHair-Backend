namespace MinhTamHair.DTO.Style_Hair_Color
{
    public class Style_HairRequest
    {


        public string? TenKieuToc { get; set; }

        public IFormFile? HinhAnh { get; set; }

        public string? MoTa { get; set; }

        public int? TrangThai { get; set; }
    }
}

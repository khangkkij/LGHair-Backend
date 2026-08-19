namespace MinhTamHair.Models.DTOs
{
    public class GioHangItemDto
    {
        public string Id { get; set; } = null!;
        public string Loai { get; set; } = null!; // Bổ sung hỗ trợ: "BIENTHE", "DICHVU", "COMBO"[cite: 9]
        public string Ten { get; set; } = null!;
        public string? HinhAnh { get; set; }
        public decimal Gia { get; set; }
        // Giá gốc trước khi áp Flash Sale — dùng để Frontend hiển thị gạch ngang.
        // Nếu không có khuyến mãi thì GiaGoc = Gia.
        public decimal GiaGoc { get; set; }
        public int SoLuong { get; set; }
        public int SoLuongTon { get; set; }
    }

    public class GioHangDto
    {
        public string CartOwner { get; set; } = null!;
        public List<GioHangItemDto> Items { get; set; } = new List<GioHangItemDto>();
        public decimal TongTien => Items.Sum(x => x.Gia * x.SoLuong);
    }

    public class ThemBienTheRequest
    {
        public string MaBienThe { get; set; } = null!;
        public int SoLuong { get; set; } = 1;
    }

    public class ThemDichVuRequest
    {
        public string MaDv { get; set; } = null!;
        public int SoLuong { get; set; } = 1;
    }

    // BỔ SUNG: Request cho Combo
    public class ThemComboRequest
    {
        public string MaCb { get; set; } = null!;
        public int SoLuong { get; set; } = 1;
    }


    public class CapNhatSoLuongRequest
    {
        public string Id { get; set; } = null!;
        public string Loai { get; set; } = null!;
        public int SoLuong { get; set; }
    }

    public class XoaItemRequest
    {
        public string Id { get; set; } = null!;
        public string Loai { get; set; } = null!;
    }
}
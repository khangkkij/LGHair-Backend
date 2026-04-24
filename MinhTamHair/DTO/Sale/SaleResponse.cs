namespace MinhTamHair.DTO.Sale
{
    public class SaleResponse
    {
        public string MaFL { get; set; }
        public DateTime NgayBatDau { get; set; }
        public DateTime NgayKetThuc { get; set; }
        public string? TongDichVu { get; set; }
        public int TrangThai { get; set; }

        public List<SaleResponseDetails> ChiTietFlashSales { get; set; }
    }

    public class SaleResponseDetails
    {
        public int Id { get; set; }
        public string? MaDV { get; set; } 
        public string? MaCB { get; set; }
        public string? TenDv { get; set; }
        public string? TenCb { get; set; }
        public decimal Gia { get; set; }
        public decimal GiaSauGiam { get; set; }
        public int PhanTramGiam { get; set; }
    }
}
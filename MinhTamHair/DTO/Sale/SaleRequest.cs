namespace MinhTamHair.DTO.Sale
{
    public class SaleRequest
    {
        public DateTime NgayBatDau { get; set; }
        public DateTime NgayKetThuc { get; set; }
        public int TrangThai { get; set; }
    }
    public class SaleRequestDetails
    {
        public string? MaDV { get; set; }
        public string? MaCB { get; set; }
        public float PhanTramGiam { get; set; }
    }
}

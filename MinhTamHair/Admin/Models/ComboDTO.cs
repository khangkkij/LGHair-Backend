public class ComboDTO
{
    public string? MaCB { get; set; }
    public string TenCombo { get; set; }
    public decimal Gia { get; set; }
    public decimal GiaGoc { get; set; }
    public string MoTa { get; set; }
    public int TrangThai { get; set; }

    public List<string> DichVus { get; set; }
    public IFormFile? File { get; set; }
}
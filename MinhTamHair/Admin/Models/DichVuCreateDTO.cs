public class DichVuCreateDTO
{
    public string MaDv { get; set; }
    public string TenDv { get; set; }
    public string MaDm { get; set; }
    public decimal Gia { get; set; }
    public int ThoiGianLam { get; set; }
    public string MoTa { get; set; }
    public int TrangThai { get; set; }
    public List<string>? DeletedImages { get; set; }

    public List<IFormFile>? Files { get; set; }
}
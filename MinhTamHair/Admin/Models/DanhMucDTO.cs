public class DanhMucDTO
{
    public string? MaDM { get; set; }
    public string? TenDanhMuc { get; set; }
    public int TrangThai { get; set; } = 1;
    public IFormFile? File { get; set; }
    public bool? XoaAnh { get; set; }
}
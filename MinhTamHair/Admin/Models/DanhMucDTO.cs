using Microsoft.AspNetCore.Http;

namespace MinhTamHair.Models
{
    public class DanhMucDTO
    {
        public string? MaDM { get; set; }
        public string? TenDanhMuc { get; set; }
        public int? TrangThai { get; set; }
        public IFormFile? HinhAnh { get; set; }
        public bool XoaHinh { get; set; } = false;
    }

}
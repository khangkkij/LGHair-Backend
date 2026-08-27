using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MinhTamHair.Models
{
    [Table("YeuCauHoanHang")]
    public partial class YeuCauHoanHang
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Required]
        [Column("maHD")]
        [StringLength(50)]
        public string MaHd { get; set; } = null!;

        [Column("lyDoHoan")]
        [StringLength(500)]
        public string? LyDoHoan { get; set; }

        [Column("ghiChuKhach")]
        [StringLength(1000)]
        public string? GhiChuKhach { get; set; }

        [Column("soTienHoan", TypeName = "decimal(18, 2)")]
        public decimal SoTienHoan { get; set; }

        [Column("maNganHang")]
        [StringLength(50)]
        public string? MaNganHang { get; set; }

        [Column("tenNganHang")]
        [StringLength(150)]
        public string? TenNganHang { get; set; }

        [Column("soTaiKhoan")]
        [StringLength(50)]
        public string? SoTaiKhoan { get; set; }

        [Column("tenChuTaiKhoan")]
        [StringLength(150)]
        public string? TenChuTaiKhoan { get; set; }

        [Column("donViVanChuyen")]
        [StringLength(100)]
        public string? DonViVanChuyen { get; set; }

        [Column("maVanDon")]
        [StringLength(100)]
        public string? MaVanDon { get; set; }

        [Column("hinhAnhBienLai")]
        public string? HinhAnhBienLai { get; set; }

        [Column("trangThai")]
        public int TrangThai { get; set; } = 1;

        [Column("lyDoTuChoi")]
        [StringLength(500)]
        public string? LyDoTuChoi { get; set; }

        [Column("ngayYeuCau")]
        public DateTime? NgayYeuCau { get; set; } = DateTime.Now;

        [Column("ngayDuyet")]
        public DateTime? NgayDuyet { get; set; }

        [Column("ngayGuiHang")]
        public DateTime? NgayGuiHang { get; set; }

        [Column("ngayNhanHang")]
        public DateTime? NgayNhanHang { get; set; }

        [Column("ngayHoanTien")]
        public DateTime? NgayHoanTien { get; set; }

        [Column("maNV")]
        [StringLength(10)]
        public string? MaNv { get; set; }

        [ForeignKey("MaHd")]
        public virtual HoaDon? MaHdNavigation { get; set; }
    }
}

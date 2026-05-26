using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace MinhTamHair.Models;

public partial class MinhTamHairContext : DbContext
{
    public MinhTamHairContext()
    {
    }

    public MinhTamHairContext(DbContextOptions<MinhTamHairContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Bangmau> Bangmaus { get; set; }

    public virtual DbSet<ChitietFlashsale> ChitietFlashsales { get; set; }

    public virtual DbSet<ChitietLichdat> ChitietLichdats { get; set; }

    public virtual DbSet<ChitietTieude> ChitietTieudes { get; set; }

    public virtual DbSet<Combo> Combos { get; set; }

    public virtual DbSet<DanhMuc> DanhMucs { get; set; }

    public virtual DbSet<DanhgiaDichvu> DanhgiaDichvus { get; set; }

    public virtual DbSet<DanhgiaNhanvien> DanhgiaNhanviens { get; set; }

    public virtual DbSet<Dichvu> Dichvus { get; set; }

    public virtual DbSet<Flashsale> Flashsales { get; set; }

    public virtual DbSet<Hangthanhvien> Hangthanhviens { get; set; }

    public virtual DbSet<Hinhanh> Hinhanhs { get; set; }

    public virtual DbSet<HoaDon> HoaDons { get; set; }

    public virtual DbSet<Khachhang> Khachhangs { get; set; }

    public virtual DbSet<Kifutoc> Kifutocs { get; set; }

    public virtual DbSet<Lichdat> Lichdats { get; set; }

    public virtual DbSet<Lichlamviec> Lichlamviecs { get; set; }

    public virtual DbSet<Lichsuthutoc> Lichsuthutocs { get; set; }

    public virtual DbSet<Loainhanvien> Loainhanviens { get; set; }

    public virtual DbSet<Nhanvien> Nhanviens { get; set; }

    public virtual DbSet<Refreshtoken> Refreshtokens { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Thanhtoan> Thanhtoans { get; set; }

    public virtual DbSet<Thongbao> Thongbaos { get; set; }

    public virtual DbSet<TieudeDichvu> TieudeDichvus { get; set; }

    public virtual DbSet<Voucher> Vouchers { get; set; }
    public DbSet<Combo_DichVu> Combo_DichVus { get; set; }
    public virtual DbSet<ChitietGiohang> ChitietGiohangs { get; set; }
    public virtual DbSet<ChitietHoadonSp> ChitietHoadonSps { get; set; }
    public virtual DbSet<ChitietPhieunhap> ChitietPhieunhaps { get; set; }
    public virtual DbSet<DanhgiaSanpham> DanhgiaSanphams { get; set; }
    public virtual DbSet<Giohang> Giohangs { get; set; }
    public virtual DbSet<Hinhanhsp> Hinhanhsps { get; set; }
    public virtual DbSet<Loaisanphamcha> Loaisanphamchas { get; set; }
    public virtual DbSet<Loaisanphamcon> Loaisanphamcons { get; set; }
    public virtual DbSet<Nhacungcap> Nhacungcaps { get; set; }
    public virtual DbSet<Phieunhap> Phieunhaps { get; set; }
    public virtual DbSet<Sanpham> Sanphams { get; set; }
    public virtual DbSet<ThongtinGiaohang> ThongtinGiaohangs { get; set; }
    public virtual DbSet<Thuonghieu> Thuonghieus { get; set; }
    public  DbSet<YeuThichNhanVien> YeuThichNhanViens { get; set; }
    public  DbSet<YeuThichDichVu> YeuThichDichVus { get; set; }
    public  DbSet<YeuThichSanPham> YeuThichSanPhams { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<YeuThichSanPham>(entity =>
        {
            entity.HasKey(yt => new { yt.MaKH, yt.MaSP });
            entity.HasOne(yt => yt.KhachHang)
                  .WithMany()
                  .HasForeignKey(yt => yt.MaKH)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(yt => yt.SanPham)
                  .WithMany()
                  .HasForeignKey(yt => yt.MaSP)
                  .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<YeuThichNhanVien>(entity =>
        {
            entity.HasKey(yt => new { yt.MaKH, yt.MaNV });
            entity.HasOne(yt => yt.KhachHang)
                  .WithMany()
                  .HasForeignKey(yt => yt.MaKH)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(yt => yt.nhanVien)
                  .WithMany()
                  .HasForeignKey(yt => yt.MaNV)
                  .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<YeuThichDichVu>(entity =>
        {
  
            entity.HasKey(yt => new { yt.MaKH, yt.MaDV });

           
            entity.HasOne(yt => yt.KhachHang)
                  .WithMany()
                  .HasForeignKey(yt => yt.MaKH)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(yt => yt.Dichvu)
                  .WithMany()
                  .HasForeignKey(yt => yt.MaDV)
                  .OnDelete(DeleteBehavior.Cascade);
        });



        modelBuilder.Entity<Bangmau>(entity =>
        {
            entity.HasKey(e => e.MaMau).HasName("PK__BANGMAU__27572EAE77605C70");

            entity.ToTable("BANGMAU");

            entity.Property(e => e.MaMau)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maMau");
            entity.Property(e => e.TenMau)
                .HasMaxLength(50)
                .HasColumnName("tenMau");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });
        modelBuilder.Entity<ChitietFlashsale>(entity =>
        {
            entity.ToTable("CHITIET_FLASHSALE");

            // 1. CHỖ QUAN TRỌNG NHẤT: Đổi sang Id làm Khóa chính
            entity.HasKey(e => e.Id).HasName("PK_CHITIET_FLASHSALE");

            // 2. Cấu hình Id tự tăng (Identity)
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("Id");

            entity.Property(e => e.MaFl)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maFL")
                .IsRequired(); 

            entity.Property(e => e.MaCb)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maCB")
                .IsRequired(false);

            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV")
                .IsRequired(false);

            entity.Property(e => e.PhanTramGiam).HasColumnName("phanTramGiam");

      
            entity.HasOne(d => d.MaCbNavigation).WithMany(p => p.ChitietFlashsales)
                .HasForeignKey(d => d.MaCb)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_FL__maCB__571DF1D5");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.ChitietFlashsales)
                .HasForeignKey(d => d.MaDv)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_FL__maDV__5812160E");

            entity.HasOne(d => d.MaFlNavigation).WithMany(p => p.ChitietFlashsales)
                .HasForeignKey(d => d.MaFl)
                .OnDelete(DeleteBehavior.Cascade) 
                .HasConstraintName("FK__CHITIET_FL__maFL__5629CD9C");
        });
        modelBuilder.Entity<ChitietLichdat>(entity =>
        {
            entity.HasKey(e => e.MaCtld).HasName("PK__CHITIET___FD279DF0AAB0864A");

            entity.ToTable("CHITIET_LICHDAT");

            entity.Property(e => e.MaCtld).HasColumnName("maCTLD");
            entity.Property(e => e.GhiChu)
                .HasMaxLength(255)
                .HasColumnName("ghiChu");
            entity.Property(e => e.GiaLucDat)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("giaLucDat");
            entity.Property(e => e.MaCb)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maCB");
            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");
            entity.Property(e => e.MaLd)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLD");
            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");

            entity.HasOne(d => d.MaCbNavigation).WithMany(p => p.ChitietLichdats)
                .HasForeignKey(d => d.MaCb)
                .HasConstraintName("FK__CHITIET_LI__maCB__7B5B524B");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.ChitietLichdats)
                .HasForeignKey(d => d.MaDv)
                .HasConstraintName("FK__CHITIET_LI__maDV__7A672E12");

            entity.HasOne(d => d.MaLdNavigation).WithMany(p => p.ChitietLichdats)
                .HasForeignKey(d => d.MaLd)
                .HasConstraintName("FK__CHITIET_LI__maLD__787EE5A0");

            entity.HasOne(d => d.MaNvNavigation).WithMany(p => p.ChitietLichdats)
                .HasForeignKey(d => d.MaNv)
                .HasConstraintName("FK__CHITIET_LI__maNV__797309D9");
        });

        modelBuilder.Entity<ChitietTieude>(entity =>
        {
            entity.HasKey(e => e.MaCttd).HasName("PK__CHITIET___FD26D0E6362B88FA");

            entity.ToTable("CHITIET_TIEUDE");

            entity.Property(e => e.MaCttd).HasColumnName("maCTTD");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");
            entity.Property(e => e.MaTd)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maTD");
            entity.Property(e => e.MoTaTieuDe)
                .HasMaxLength(255)
                .HasColumnName("moTaTieuDe");
            entity.Property(e => e.SoThuTu).HasColumnName("soThuTu");
            entity.Property(e => e.TenTieuDe)
                .HasMaxLength(100)
                .HasColumnName("tenTieuDe");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.ChitietTieudes)
                .HasForeignKey(d => d.MaDv)
                .HasConstraintName("FK__CHITIET_TI__maDV__6754599E");

            entity.HasOne(d => d.MaTdNavigation).WithMany(p => p.ChitietTieudes)
                .HasForeignKey(d => d.MaTd)
                .HasConstraintName("FK__CHITIET_TI__maTD__66603565");
        });

        modelBuilder.Entity<Combo>(entity =>
        {
            entity.HasKey(e => e.MaCb).HasName("PK__COMBO__7A3E0CE4ED642CC5");

            entity.ToTable("COMBO");

            entity.Property(e => e.MaCb)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maCB");
            entity.Property(e => e.Gia)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("gia");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.MoTa).HasColumnName("moTa");
            entity.Property(e => e.TenCombo)
                .HasMaxLength(100)
                .HasColumnName("tenCombo");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<DanhMuc>(entity =>
        {
            entity.HasKey(e => e.MaDm).HasName("PK__DANH_MUC__7A3EF4081AD9A7FB");

            entity.ToTable("DANH_MUC");

            entity.Property(e => e.MaDm)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDM");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.NgayTao).HasColumnName("ngayTao");
            entity.Property(e => e.TenDanhMuc)
                .HasMaxLength(100)
                .HasColumnName("tenDanhMuc");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<DanhgiaDichvu>(entity =>
        {
            entity.HasKey(e => e.MaDgdv).HasName("PK__DANHGIA___351DF5097437A20A");

            entity.ToTable("DANHGIA_DICHVU");

            entity.Property(e => e.MaDgdv).HasColumnName("maDGDV");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.MaCb)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maCB");
            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");
            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.NgayDanhGia)
                .HasColumnType("datetime")
                .HasColumnName("ngayDanhGia");
            entity.Property(e => e.NgayPhanHoi)
                .HasColumnType("datetime")
                .HasColumnName("ngayPhanHoi");
            entity.Property(e => e.NoiDung).HasColumnName("noiDung");
            entity.Property(e => e.PhanHoi).HasColumnName("phanHoi");
            entity.Property(e => e.SoSao).HasColumnName("soSao");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaCbNavigation).WithMany(p => p.DanhgiaDichvus)
                .HasForeignKey(d => d.MaCb)
                .HasConstraintName("FK__DANHGIA_DI__maCB__08B54D69");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.DanhgiaDichvus)
                .HasForeignKey(d => d.MaDv)
                .HasConstraintName("FK__DANHGIA_DI__maDV__07C12930");

            entity.HasOne(d => d.MaHdNavigation).WithMany(p => p.DanhgiaDichvus)
                .HasForeignKey(d => d.MaHd)
                .HasConstraintName("FK__DANHGIA_DI__maHD__06CD04F7");
        });

        modelBuilder.Entity<DanhgiaNhanvien>(entity =>
        {
            entity.HasKey(e => e.MaDgnv).HasName("PK__DANHGIA___351E2BDA5F4D0AB0");

            entity.ToTable("DANHGIA_NHANVIEN");

            entity.Property(e => e.MaDgnv).HasColumnName("maDGNV");
            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");
            entity.Property(e => e.NgayDanhGia)
                .HasColumnType("datetime")
                .HasColumnName("ngayDanhGia");
            entity.Property(e => e.NoiDung).HasColumnName("noiDung");
            entity.Property(e => e.SoSao).HasColumnName("soSao");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaHdNavigation).WithMany(p => p.DanhgiaNhanviens)
                .HasForeignKey(d => d.MaHd)
                .HasConstraintName("FK__DANHGIA_NH__maHD__0B91BA14");

            entity.HasOne(d => d.MaNvNavigation).WithMany(p => p.DanhgiaNhanviens)
                .HasForeignKey(d => d.MaNv)
                .HasConstraintName("FK__DANHGIA_NH__maNV__0C85DE4D");
        });

        modelBuilder.Entity<Dichvu>(entity =>
        {
            entity.HasKey(e => e.MaDv).HasName("PK__DICHVU__7A3EF4111F544280");

            entity.ToTable("DICHVU");

            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");
            entity.Property(e => e.Gia)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("gia");
            entity.Property(e => e.MaDm)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDM");
            entity.Property(e => e.MoTa).HasColumnName("moTa");
            entity.Property(e => e.TenDv)
                .HasMaxLength(100)
                .HasColumnName("tenDV");
            entity.Property(e => e.ThoiGianLam).HasColumnName("thoiGianLam");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaDmNavigation).WithMany(p => p.Dichvus)
                .HasForeignKey(d => d.MaDm)
                .HasConstraintName("FK__DICHVU__maDM__4F7CD00D");
        });

        modelBuilder.Entity<Flashsale>(entity =>
        {
            entity.HasKey(e => e.MaFl).HasName("PK__FLASHSAL__7A3E04C9E9420861");

            entity.ToTable("FLASHSALE");

            entity.Property(e => e.MaFl)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maFL");
            entity.Property(e => e.NgayBatDau)
                .HasColumnType("datetime")
                .HasColumnName("ngayBatDau");
            entity.Property(e => e.NgayKetThuc)
                .HasColumnType("datetime")
                .HasColumnName("ngayKetThuc");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<Hangthanhvien>(entity =>
        {
            entity.HasKey(e => e.MaRank).HasName("PK__HANGTHAN__AC1EB3F04B5DDA24");

            entity.ToTable("HANGTHANHVIEN");

            entity.Property(e => e.MaRank)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maRank");
            entity.Property(e => e.DiemToiThieu).HasColumnName("diemToiThieu");
            entity.Property(e => e.PhanTramGiamGia).HasColumnName("phanTramGiamGia");
            entity.Property(e => e.TenRank)
                .HasMaxLength(50)
                .HasColumnName("tenRank");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<Hinhanh>(entity =>
        {
            entity.HasKey(e => e.MaHa).HasName("PK__HINHANH__7A3E1483BFEC708E");

            entity.ToTable("HINHANH");

            entity.Property(e => e.MaHa).HasColumnName("maHA");
            entity.Property(e => e.DuongDan)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("duongDan");
            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.Hinhanhs)
                .HasForeignKey(d => d.MaDv)
                .HasConstraintName("FK__HINHANH__maDV__60A75C0F");
        });

        modelBuilder.Entity<HoaDon>(entity =>
        {
            entity.HasKey(e => e.MaHd).HasName("PK__HOA_DON__7A3E1486F3B8EE3A");

            entity.ToTable("HOA_DON");

            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.MaCode)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("maCode");
            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");
            entity.Property(e => e.MaLd)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLD");
            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");
            entity.Property(e => e.NgayTao)
                .HasColumnType("datetime")
                .HasColumnName("ngayTao");
            entity.Property(e => e.PhanTramGiamVip).HasColumnName("phanTramGiamVIP");
            entity.Property(e => e.ThanhTien)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("thanhTien");
            entity.Property(e => e.TienDaTra)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("tienDaTra");
            entity.Property(e => e.TongTien)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("tongTien");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaCodeNavigation).WithMany(p => p.HoaDons)
                .HasForeignKey(d => d.MaCode)
                .HasConstraintName("FK__HOA_DON__maCode__00200768");

            entity.HasOne(d => d.MaKhNavigation).WithMany(p => p.HoaDons)
                .HasForeignKey(d => d.MaKh)
                .HasConstraintName("FK__HOA_DON__maKH__01142BA1");

            entity.HasOne(d => d.MaLdNavigation).WithMany(p => p.HoaDons)
                .HasForeignKey(d => d.MaLd)
                .HasConstraintName("FK__HOA_DON__maLD__7E37BEF6");

            entity.HasOne(d => d.MaNvNavigation).WithMany(p => p.HoaDons)
                .HasForeignKey(d => d.MaNv)
                .HasConstraintName("FK__HOA_DON__maNV__7F2BE32F");
        });

        modelBuilder.Entity<Khachhang>(entity =>
        {
            entity.HasKey(e => e.MaKh).HasName("PK__KHACHHAN__7A3ECFE43DC91534");

            entity.ToTable("KHACHHANG");

            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");
            entity.Property(e => e.DiemTichLuy).HasColumnName("diemTichLuy");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("email");
            entity.Property(e => e.GioiTinh)
                .HasMaxLength(10)
                .HasColumnName("gioiTinh");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.HoTen)
                .HasMaxLength(100)
                .HasColumnName("hoTen");
            entity.Property(e => e.MaRank)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maRank");
            entity.Property(e => e.MatKhau)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("matKhau");
            entity.Property(e => e.NgayTao)
                .HasColumnType("datetime")
                .HasColumnName("ngayTao");
            entity.Property(e => e.Sdt)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("SDT");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaRankNavigation).WithMany(p => p.Khachhangs)
                .HasForeignKey(d => d.MaRank)
                .HasConstraintName("FK__KHACHHANG__maRan__4CA06362");

            entity.HasMany(d => d.MaCodes).WithMany(p => p.MaKhs)
                .UsingEntity<Dictionary<string, object>>(
                    "KhachhangVoucher",
                    r => r.HasOne<Voucher>().WithMany()
                        .HasForeignKey("MaCode")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK__KHACHHANG__maCod__6B24EA82"),
                    l => l.HasOne<Khachhang>().WithMany()
                        .HasForeignKey("MaKh")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK__KHACHHANG___maKH__6A30C649"),
                    j =>
                    {
                        j.HasKey("MaKh", "MaCode").HasName("PK__KHACHHAN__C958E6AAAE16A29A");
                        j.ToTable("KHACHHANG_VOUCHER");
                        j.IndexerProperty<string>("MaKh")
                            .HasMaxLength(10)
                            .IsUnicode(false)
                            .HasColumnName("maKH");
                        j.IndexerProperty<string>("MaCode")
                            .HasMaxLength(20)
                            .IsUnicode(false)
                            .HasColumnName("maCode");
                    });
        });

        modelBuilder.Entity<Kifutoc>(entity =>
        {
            entity.HasKey(e => e.MaKt).HasName("PK__KIFUTOC__7A3ECFF831EEC40E");

            entity.ToTable("KIFUTOC");

            entity.Property(e => e.MaKt)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKT");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");
            entity.Property(e => e.MoTa).HasColumnName("moTa");
            entity.Property(e => e.TenKieuToc)
                .HasMaxLength(100)
                .HasColumnName("tenKieuToc");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.Kifutocs)
                .HasForeignKey(d => d.MaDv)
                .HasConstraintName("FK__KIFUTOC__maDV__5AEE82B9");
        });

        modelBuilder.Entity<Lichdat>(entity =>
        {
            entity.HasKey(e => e.MaLd).HasName("PK__LICHDAT__7A3EB709BDC0CDFF");

            entity.ToTable("LICHDAT");

            entity.Property(e => e.MaLd)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLD");
            entity.Property(e => e.GioBatDau).HasColumnName("gioBatDau");
            entity.Property(e => e.GioKetThuc).HasColumnName("gioKetThuc");
            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");
            entity.Property(e => e.NgayHen).HasColumnName("ngayHen");
            entity.Property(e => e.TongTienDuKien)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("tongTienDuKien");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaKhNavigation).WithMany(p => p.Lichdats)
                .HasForeignKey(d => d.MaKh)
                .HasConstraintName("FK__LICHDAT__maKH__75A278F5");
        });

        modelBuilder.Entity<Lichlamviec>(entity =>
        {
            entity.HasKey(e => e.MaLich).HasName("PK__LICHLAMV__F0B8217BB1B5E87D");

            entity.ToTable("LICHLAMVIEC");

            entity.Property(e => e.MaLich).HasColumnName("maLich");
            entity.Property(e => e.CaLam)
                .HasMaxLength(50)
                .HasColumnName("caLam");
            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");
            entity.Property(e => e.NgayLamViec).HasColumnName("ngayLamViec");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaNvNavigation).WithMany(p => p.Lichlamviecs)
                .HasForeignKey(d => d.MaNv)
                .HasConstraintName("FK__LICHLAMVIE__maNV__5DCAEF64");
        });

        modelBuilder.Entity<Lichsuthutoc>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LICHSUTH__3214EC277D377291");

            entity.ToTable("LICHSUTHUTOC");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.AnhGhep)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("anhGhep");
            entity.Property(e => e.AnhGoc)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("anhGoc");
            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");
            entity.Property(e => e.MaKt)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKT");
            entity.Property(e => e.MaMau)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maMau");
            entity.Property(e => e.NgayThu)
                .HasColumnType("datetime")
                .HasColumnName("ngayThu");

            entity.HasOne(d => d.MaKhNavigation).WithMany(p => p.Lichsuthutocs)
                .HasForeignKey(d => d.MaKh)
                .HasConstraintName("FK__LICHSUTHUT__maKH__6E01572D");

            entity.HasOne(d => d.MaKtNavigation).WithMany(p => p.Lichsuthutocs)
                .HasForeignKey(d => d.MaKt)
                .HasConstraintName("FK__LICHSUTHUT__maKT__6EF57B66");

            entity.HasOne(d => d.MaMauNavigation).WithMany(p => p.Lichsuthutocs)
                .HasForeignKey(d => d.MaMau)
                .HasConstraintName("FK__LICHSUTHU__maMau__6FE99F9F");
        });

        modelBuilder.Entity<Loainhanvien>(entity =>
        {
            entity.HasKey(e => e.MaLoaiNv).HasName("PK__LOAINHAN__7A9A6EBA1DC5396A");

            entity.ToTable("LOAINHANVIEN");

            entity.Property(e => e.MaLoaiNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLoaiNV");
            entity.Property(e => e.DuocDatLich).HasColumnName("duocDatLich");
            entity.Property(e => e.Luong)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("luong");
            entity.Property(e => e.MoTa)
                .HasMaxLength(255)
                .HasColumnName("moTa");
            entity.Property(e => e.TenLoai)
                .HasMaxLength(50)
                .HasColumnName("tenLoai");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<Nhanvien>(entity =>
        {
            entity.HasKey(e => e.MaNv).HasName("PK__NHANVIEN__7A3EC7D510272919");

            entity.ToTable("NHANVIEN");

            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");
            entity.Property(e => e.Cccd)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("CCCD");
            entity.Property(e => e.DiaChi)
                .HasMaxLength(255)
                .HasColumnName("diaChi");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("email");
            entity.Property(e => e.GioiTinh)
                .HasMaxLength(10)
                .HasColumnName("gioiTinh");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.HoTenNv)
                .HasMaxLength(100)
                .HasColumnName("hoTenNV");
            entity.Property(e => e.MaLoaiNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLoaiNV");
            entity.Property(e => e.MaRole)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maRole");
            entity.Property(e => e.MatKhau)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("matKhau");
            entity.Property(e => e.NamSinh).HasColumnName("namSinh");
            entity.Property(e => e.NgayVaoLam).HasColumnName("ngayVaoLam");
            entity.Property(e => e.Sdt)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("SDT");
            entity.Property(e => e.TenDangNhap)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("tenDangNhap");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaLoaiNvNavigation).WithMany(p => p.Nhanviens)
                .HasForeignKey(d => d.MaLoaiNv)
                .HasConstraintName("FK__NHANVIEN__maLoai__49C3F6B7");

            entity.HasOne(d => d.MaRoleNavigation).WithMany(p => p.Nhanviens)
                .HasForeignKey(d => d.MaRole)
                .HasConstraintName("FK__NHANVIEN__maRole__48CFD27E");
        });

        modelBuilder.Entity<Refreshtoken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__REFRESHT__3214EC07E53BB784");

            entity.ToTable("REFRESHTOKEN");

            entity.Property(e => e.ExpiredAt).HasColumnType("datetime");
            entity.Property(e => e.IssuedAt).HasColumnType("datetime");
            entity.Property(e => e.Token).IsUnicode(false);
            entity.Property(e => e.UserId)
                .HasMaxLength(10)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.MaRole).HasName("PK__ROLE__509E051278464AD2");

            entity.ToTable("ROLE");

            entity.Property(e => e.MaRole)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maRole");
            entity.Property(e => e.TenChucVu)
                .HasMaxLength(50)
                .HasColumnName("tenChucVu");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<Thanhtoan>(entity =>
        {
            entity.HasKey(e => e.MaTt).HasName("PK__THANHTOA__7A2262477BF7F293");

            entity.ToTable("THANHTOAN");

            entity.Property(e => e.MaTt).HasColumnName("maTT");
            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.PhuongThuc)
                .HasMaxLength(50)
                .HasColumnName("phuongThuc");
            entity.Property(e => e.SoTien)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("soTien");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaHdNavigation).WithMany(p => p.Thanhtoans)
                .HasForeignKey(d => d.MaHd)
                .HasConstraintName("FK__THANHTOAN__maHD__03F0984C");
        });

        modelBuilder.Entity<Thongbao>(entity =>
        {
            entity.HasKey(e => e.MaTb).HasName("PK__THONGBAO__7A2262699AD3AF89");

            entity.ToTable("THONGBAO");

            entity.Property(e => e.MaTb).HasColumnName("maTB");
            entity.Property(e => e.KenhGui)
                .HasMaxLength(50)
                .HasColumnName("kenhGui");
            entity.Property(e => e.LoaiTin)
                .HasMaxLength(50)
                .HasColumnName("loaiTin");
            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");
            entity.Property(e => e.NgayCanGui)
                .HasColumnType("datetime")
                .HasColumnName("ngayCanGui");
            entity.Property(e => e.NgayTao)
                .HasColumnType("datetime")
                .HasColumnName("ngayTao");
            entity.Property(e => e.NoiDung).HasColumnName("noiDung");
            entity.Property(e => e.TieuDe)
                .HasMaxLength(100)
                .HasColumnName("tieuDe");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaKhNavigation).WithMany(p => p.Thongbaos)
                .HasForeignKey(d => d.MaKh)
                .HasConstraintName("FK__THONGBAO__maKH__72C60C4A");
        });

        modelBuilder.Entity<TieudeDichvu>(entity =>
        {
            entity.HasKey(e => e.MaTd).HasName("PK__TIEUDE_D__7A2262573E7A4201");

            entity.ToTable("TIEUDE_DICHVU");

            entity.Property(e => e.MaTd)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maTD");
            entity.Property(e => e.MaDv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");
            entity.Property(e => e.MoTaTieuDe)
                .HasMaxLength(255)
                .HasColumnName("moTaTieuDe");
            entity.Property(e => e.SoThuTu).HasColumnName("soThuTu");
            entity.Property(e => e.TenTieuDe)
                .HasMaxLength(100)
                .HasColumnName("tenTieuDe");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaDvNavigation).WithMany(p => p.TieudeDichvus)
                .HasForeignKey(d => d.MaDv)
                .HasConstraintName("FK__TIEUDE_DIC__maDV__6383C8BA");
        });

        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.HasKey(e => e.MaCode).HasName("PK__VOUCHER__366294EAE5A78846");

            entity.ToTable("VOUCHER");

            entity.Property(e => e.MaCode)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("maCode");
            entity.Property(e => e.GiaTriToiThieu)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("giaTriToiThieu");
            entity.Property(e => e.MoTa)
                .HasMaxLength(255)
                .HasColumnName("moTa");
            entity.Property(e => e.NgayBatDau)
                .HasColumnType("datetime")
                .HasColumnName("ngayBatDau");
            entity.Property(e => e.NgayKetThuc)
                .HasColumnType("datetime")
                .HasColumnName("ngayKetThuc");
            entity.Property(e => e.PhanTramGiam).HasColumnName("phanTramGiam");
            entity.Property(e => e.SoLuong).HasColumnName("soLuong");
            entity.Property(e => e.SoLuongDaDung).HasColumnName("soLuongDaDung");
            entity.Property(e => e.SoTienGiam)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("soTienGiam");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });
        modelBuilder.Entity<Combo_DichVu>(entity =>
        {
            entity.ToTable("CHITIET_COMBO");

            entity.HasKey(e => new { e.MaCB, e.MaDV }); // Khai báo khóa chính composite

            entity.Property(e => e.MaCB)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maCB");

            entity.Property(e => e.MaDV)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maDV");

            entity.HasOne(d => d.Combo)
                .WithMany(p => p.Combo_DichVus)
                .HasForeignKey(d => d.MaCB)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_CO__maCB__52593CB8");

            entity.HasOne(d => d.DichVu)
                .WithMany(p => p.Combo_DichVus)
                .HasForeignKey(d => d.MaDV)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_CO__maDV__534D60F1");
        });
        modelBuilder.Entity<ChitietGiohang>(entity =>
        {
            entity.HasKey(e => new { e.MaGh, e.MaSp }).HasName("PK__CHITIET___CD9C0ACC0072A942");

            entity.ToTable("CHITIET_GIOHANG");

            entity.Property(e => e.MaGh).HasColumnName("maGH");
            entity.Property(e => e.MaSp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maSP");
            entity.Property(e => e.SoLuong).HasColumnName("soLuong");

            entity.HasOne(d => d.MaGhNavigation).WithMany(p => p.ChitietGiohangs)
                .HasForeignKey(d => d.MaGh)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_GI__maGH__4B7734FF");

            entity.HasOne(d => d.MaSpNavigation).WithMany(p => p.ChitietGiohangs)
                .HasForeignKey(d => d.MaSp)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_GI__maSP__4C6B5938");
        });

        modelBuilder.Entity<ChitietHoadonSp>(entity =>
        {
            entity.HasKey(e => new { e.MaHd, e.MaSp }).HasName("PK__CHITIET___CD9C3321972BC3EA");

            entity.ToTable("CHITIET_HOADON_SP");

            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.MaSp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maSP");
            entity.Property(e => e.DonGiaLucBan)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("donGiaLucBan");
            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");
            entity.Property(e => e.SoLuong).HasColumnName("soLuong");
            entity.Property(e => e.ThanhTien)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("thanhTien");
            entity.Property(e => e.TienHoaHongSp)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("tienHoaHongSP");

            entity.HasOne(d => d.MaHdNavigation).WithMany(p => p.ChitietHoadonSps)
                .HasForeignKey(d => d.MaHd)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_HO__maHD__43D61337");

            entity.HasOne(d => d.MaNvNavigation).WithMany(p => p.ChitietHoadonSps)
                .HasForeignKey(d => d.MaNv)
                .HasConstraintName("FK__CHITIET_HO__maNV__45BE5BA9");

            entity.HasOne(d => d.MaSpNavigation).WithMany(p => p.ChitietHoadonSps)
                .HasForeignKey(d => d.MaSp)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_HO__maSP__44CA3770");
        });

        modelBuilder.Entity<ChitietPhieunhap>(entity =>
        {
            entity.HasKey(e => new { e.MaPn, e.MaSp }).HasName("PK__CHITIET___CD9CF03D4A71F270");

            entity.ToTable("CHITIET_PHIEUNHAP");

            entity.Property(e => e.MaPn)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maPN");
            entity.Property(e => e.MaSp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maSP");
            entity.Property(e => e.GiaNhap)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("giaNhap");
            entity.Property(e => e.SoLuong).HasColumnName("soLuong");

            entity.HasOne(d => d.MaPnNavigation).WithMany(p => p.ChitietPhieunhaps)
                .HasForeignKey(d => d.MaPn)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_PH__maPN__4F47C5E3");

            entity.HasOne(d => d.MaSpNavigation).WithMany(p => p.ChitietPhieunhaps)
                .HasForeignKey(d => d.MaSp)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CHITIET_PH__maSP__503BEA1C");
        });

        modelBuilder.Entity<DanhgiaSanpham>(entity =>
        {
            entity.HasKey(e => e.MaDgsp).HasName("PK__DANHGIA___351E030695815D88");

            entity.ToTable("DANHGIA_SANPHAM");

            entity.Property(e => e.MaDgsp).HasColumnName("maDGSP");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");
            entity.Property(e => e.MaSp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maSP");
            entity.Property(e => e.NgayDanhGia)
                .HasColumnType("datetime")
                .HasColumnName("ngayDanhGia");
            entity.Property(e => e.NoiDung).HasColumnName("noiDung");
            entity.Property(e => e.PhanHoi).HasColumnName("phanHoi");
            entity.Property(e => e.SoSao).HasColumnName("soSao");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaHdNavigation).WithMany(p => p.DanhgiaSanphams)
                .HasForeignKey(d => d.MaHd)
                .HasConstraintName("FK__DANHGIA_SA__maHD__489AC854");

            entity.HasOne(d => d.MaKhNavigation).WithMany(p => p.DanhgiaSanphams)
                .HasForeignKey(d => d.MaKh)
                .HasConstraintName("FK__DANHGIA_SA__maKH__498EEC8D");

            entity.HasOne(d => d.MaSpNavigation).WithMany(p => p.DanhgiaSanphams)
                .HasForeignKey(d => d.MaSp)
                .HasConstraintName("FK__DANHGIA_SA__maSP__47A6A41B");
        });

        modelBuilder.Entity<Giohang>(entity =>
        {
            entity.HasKey(e => e.MaGh).HasName("PK__GIOHANG__7A3E2D6B214EEED3");

            entity.ToTable("GIOHANG");

            entity.Property(e => e.MaGh).HasColumnName("maGH");
            entity.Property(e => e.MaKh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maKH");

            entity.HasOne(d => d.MaKhNavigation).WithMany(p => p.Giohangs)
                .HasForeignKey(d => d.MaKh)
                .HasConstraintName("FK__GIOHANG__maKH__4A8310C6");
        });

        modelBuilder.Entity<Hinhanhsp>(entity =>
        {
            entity.HasKey(e => e.MaHa).HasName("PK__HINHANHS__7A3E1483AD681ED2");

            entity.ToTable("HINHANHSP");

            entity.Property(e => e.MaHa).HasColumnName("maHA");
            entity.Property(e => e.DuongDan)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("duongDan");
            entity.Property(e => e.MaSp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maSP");

            entity.HasOne(d => d.MaSpNavigation).WithMany(p => p.Hinhanhsps)
                .HasForeignKey(d => d.MaSp)
                .HasConstraintName("FK__HINHANHSP__maSP__42E1EEFE");
        });

        modelBuilder.Entity<Loaisanphamcha>(entity =>
        {
            entity.HasKey(e => e.MaLoaiSpcha).HasName("PK__LOAISANP__D9D83107C001562A");

            entity.ToTable("LOAISANPHAMCHA");

            entity.Property(e => e.MaLoaiSpcha)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLoaiSPCha");
            entity.Property(e => e.TenLoaiSp)
                .HasMaxLength(100)
                .HasColumnName("tenLoaiSP");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<Loaisanphamcon>(entity =>
        {
            entity.HasKey(e => e.MaLoaiSpcon).HasName("PK__LOAISANP__D9D8EE1F438F042A");

            entity.ToTable("LOAISANPHAMCON");

            entity.Property(e => e.MaLoaiSpcon)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLoaiSPCon");
            entity.Property(e => e.MaLoaiSpcha)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLoaiSPCha");
            entity.Property(e => e.TenLoaiSp)
                .HasMaxLength(100)
                .HasColumnName("tenLoaiSP");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaLoaiSpchaNavigation).WithMany(p => p.Loaisanphamcons)
                .HasForeignKey(d => d.MaLoaiSpcha)
                .HasConstraintName("FK__LOAISANPH__maLoa__40058253");
        });

        modelBuilder.Entity<Nhacungcap>(entity =>
        {
            entity.HasKey(e => e.MaNcc).HasName("PK__NHACUNGC__2699C45F8FEC190C");

            entity.ToTable("NHACUNGCAP");

            entity.Property(e => e.MaNcc)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNCC");
            entity.Property(e => e.DiaChi)
                .HasMaxLength(255)
                .HasColumnName("diaChi");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.SoDienThoai)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("soDienThoai");
            entity.Property(e => e.TenNcc)
                .HasMaxLength(150)
                .HasColumnName("tenNCC");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

        modelBuilder.Entity<Phieunhap>(entity =>
        {
            entity.HasKey(e => e.MaPn).HasName("PK__PHIEUNHA__7A3ED79AB44CA582");

            entity.ToTable("PHIEUNHAP");

            entity.Property(e => e.MaPn)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maPN");
            entity.Property(e => e.GhiChu).HasColumnName("ghiChu");
            entity.Property(e => e.MaNcc)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNCC");
            entity.Property(e => e.MaNv)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maNV");
            entity.Property(e => e.NgayNhap)
                .HasColumnType("datetime")
                .HasColumnName("ngayNhap");
            entity.Property(e => e.TongTien)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("tongTien");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaNccNavigation).WithMany(p => p.Phieunhaps)
                .HasForeignKey(d => d.MaNcc)
                .HasConstraintName("FK__PHIEUNHAP__maNCC__4E53A1AA");

            entity.HasOne(d => d.MaNvNavigation).WithMany(p => p.Phieunhaps)
                .HasForeignKey(d => d.MaNv)
                .HasConstraintName("FK__PHIEUNHAP__maNV__4D5F7D71");
        });

        modelBuilder.Entity<Sanpham>(entity =>
        {
            entity.HasKey(e => e.MaSp).HasName("PK__SANPHAM__7A227A7AE4506676");

            entity.ToTable("SANPHAM");

            entity.Property(e => e.MaSp)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maSP");
            entity.Property(e => e.GiaBan)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("giaBan");
            entity.Property(e => e.HinhAnhDaiDien)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnhDaiDien");
            entity.Property(e => e.HuongDanSuDung).HasColumnName("huongDanSuDung");
            entity.Property(e => e.MaLoaiSpcon)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maLoaiSPCon");
            entity.Property(e => e.MaTh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maTH");
            entity.Property(e => e.MoTa).HasColumnName("moTa");
            entity.Property(e => e.PhanTramHoaHong)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("phanTramHoaHong");
            entity.Property(e => e.SoLuongTon).HasColumnName("soLuongTon");
            entity.Property(e => e.TenSp)
                .HasMaxLength(150)
                .HasColumnName("tenSP");
            entity.Property(e => e.ThanhPhan).HasColumnName("thanhPhan");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");

            entity.HasOne(d => d.MaLoaiSpconNavigation).WithMany(p => p.Sanphams)
                .HasForeignKey(d => d.MaLoaiSpcon)
                .HasConstraintName("FK__SANPHAM__maLoaiS__40F9A68C");

            entity.HasOne(d => d.MaThNavigation).WithMany(p => p.Sanphams)
                .HasForeignKey(d => d.MaTh)
                .HasConstraintName("FK__SANPHAM__maTH__41EDCAC5");
        });

        modelBuilder.Entity<ThongtinGiaohang>(entity =>
        {
            entity.HasKey(e => e.MaGiaoHang).HasName("PK__THONGTIN__AF360BCB30B56C0A");

            entity.ToTable("THONGTIN_GIAOHANG");

            entity.Property(e => e.MaGiaoHang).HasColumnName("maGiaoHang");
            entity.Property(e => e.DiaChiNhan)
                .HasMaxLength(255)
                .HasColumnName("diaChiNhan");
            entity.Property(e => e.DonViVanChuyen)
                .HasMaxLength(100)
                .HasColumnName("donViVanChuyen");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.MaHd)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maHD");
            entity.Property(e => e.MaVanDon)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("maVanDon");
            entity.Property(e => e.PhiShip)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("phiShip");
            entity.Property(e => e.SdtNhan)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("sdtNhan");
            entity.Property(e => e.TenNguoiNhan)
                .HasMaxLength(100)
                .HasColumnName("tenNguoiNhan");
            entity.Property(e => e.TrangThaiGiaoHang).HasColumnName("trangThaiGiaoHang");

            entity.HasOne(d => d.MaHdNavigation).WithMany(p => p.ThongtinGiaohangs)
                .HasForeignKey(d => d.MaHd)
                .HasConstraintName("FK__THONGTIN_G__maHD__46B27FE2");
        });

        modelBuilder.Entity<Thuonghieu>(entity =>
        {
            entity.HasKey(e => e.MaTh).HasName("PK__THUONGHI__7A22625364318BFC");

            entity.ToTable("THUONGHIEU");

            entity.Property(e => e.MaTh)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("maTH");
            entity.Property(e => e.HinhAnh)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("hinhAnh");
            entity.Property(e => e.MoTa).HasColumnName("moTa");
            entity.Property(e => e.TenThuongHieu)
                .HasMaxLength(100)
                .HasColumnName("tenThuongHieu");
            entity.Property(e => e.TrangThai).HasColumnName("trangThai");
        });

       

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}


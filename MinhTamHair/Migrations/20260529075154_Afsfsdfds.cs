using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinhTamHair.Migrations
{
    /// <inheritdoc />
    public partial class Afsfsdfds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BANGMAU",
                columns: table => new
                {
                    maMau = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenMau = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__BANGMAU__27572EAE77605C70", x => x.maMau);
                });

            migrationBuilder.CreateTable(
                name: "COMBO",
                columns: table => new
                {
                    maCB = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenCombo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    gia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    moTa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__COMBO__7A3E0CE4ED642CC5", x => x.maCB);
                });

            migrationBuilder.CreateTable(
                name: "DANH_MUC",
                columns: table => new
                {
                    maDM = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenDanhMuc = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    ngayTao = table.Column<DateOnly>(type: "date", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DANH_MUC__7A3EF4081AD9A7FB", x => x.maDM);
                });

            migrationBuilder.CreateTable(
                name: "DANHMUCSP",
                columns: table => new
                {
                    maDMSP = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenDanhMuc = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DANHMUCSP", x => x.maDMSP);
                });

            migrationBuilder.CreateTable(
                name: "FLASHSALE",
                columns: table => new
                {
                    maFL = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ngayBatDau = table.Column<DateTime>(type: "datetime", nullable: true),
                    ngayKetThuc = table.Column<DateTime>(type: "datetime", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__FLASHSAL__7A3E04C9E9420861", x => x.maFL);
                });

            migrationBuilder.CreateTable(
                name: "HANGTHANHVIEN",
                columns: table => new
                {
                    maRank = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenRank = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    diemToiThieu = table.Column<int>(type: "int", nullable: true),
                    phanTramGiamGia = table.Column<int>(type: "int", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__HANGTHAN__AC1EB3F04B5DDA24", x => x.maRank);
                });

            migrationBuilder.CreateTable(
                name: "LOAINHANVIEN",
                columns: table => new
                {
                    maLoaiNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenLoai = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    moTa = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    luong = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    duocDatLich = table.Column<bool>(type: "bit", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LOAINHAN__7A9A6EBA1DC5396A", x => x.maLoaiNV);
                });

            migrationBuilder.CreateTable(
                name: "NHACUNGCAP",
                columns: table => new
                {
                    maNCC = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenNCC = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    soDienThoai = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    diaChi = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__NHACUNGC__2699C45F8FEC190C", x => x.maNCC);
                });

            migrationBuilder.CreateTable(
                name: "REFRESHTOKEN",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Token = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ExpiredAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__REFRESHT__3214EC07E53BB784", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ROLE",
                columns: table => new
                {
                    maRole = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenChucVu = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    trangThai = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ROLE__509E051278464AD2", x => x.maRole);
                });

            migrationBuilder.CreateTable(
                name: "THUONGHIEU",
                columns: table => new
                {
                    maTH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    tenThuongHieu = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    moTa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__THUONGHI__7A22625364318BFC", x => x.maTH);
                });

            migrationBuilder.CreateTable(
                name: "VOUCHER",
                columns: table => new
                {
                    maCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    moTa = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    phanTramGiam = table.Column<int>(type: "int", nullable: true),
                    soTienGiam = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    giaTriToiThieu = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ngayBatDau = table.Column<DateTime>(type: "datetime", nullable: true),
                    ngayKetThuc = table.Column<DateTime>(type: "datetime", nullable: true),
                    soLuong = table.Column<int>(type: "int", nullable: true),
                    soLuongDaDung = table.Column<int>(type: "int", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__VOUCHER__366294EAE5A78846", x => x.maCode);
                });

            migrationBuilder.CreateTable(
                name: "DICHVU",
                columns: table => new
                {
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maDM = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tenDV = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    moTa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    gia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PhanTramHoaHong = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    thoiGianLam = table.Column<int>(type: "int", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DICHVU__7A3EF4111F544280", x => x.maDV);
                    table.ForeignKey(
                        name: "FK__DICHVU__maDM__4F7CD00D",
                        column: x => x.maDM,
                        principalTable: "DANH_MUC",
                        principalColumn: "maDM");
                });

            migrationBuilder.CreateTable(
                name: "KHACHHANG",
                columns: table => new
                {
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maRank = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    hoTen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    SDT = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    matKhau = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    gioiTinh = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ngayTao = table.Column<DateTime>(type: "datetime", nullable: true),
                    diemTichLuy = table.Column<int>(type: "int", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__KHACHHAN__7A3ECFE43DC91534", x => x.maKH);
                    table.ForeignKey(
                        name: "FK__KHACHHANG__maRan__4CA06362",
                        column: x => x.maRank,
                        principalTable: "HANGTHANHVIEN",
                        principalColumn: "maRank");
                });

            migrationBuilder.CreateTable(
                name: "NHANVIEN",
                columns: table => new
                {
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maRole = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maLoaiNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    hoTenNV = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    gioiTinh = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CCCD = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    namSinh = table.Column<int>(type: "int", nullable: true),
                    email = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    SDT = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    diaChi = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    tenDangNhap = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    matKhau = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    ngayVaoLam = table.Column<DateOnly>(type: "date", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__NHANVIEN__7A3EC7D510272919", x => x.maNV);
                    table.ForeignKey(
                        name: "FK__NHANVIEN__maLoai__49C3F6B7",
                        column: x => x.maLoaiNV,
                        principalTable: "LOAINHANVIEN",
                        principalColumn: "maLoaiNV");
                    table.ForeignKey(
                        name: "FK__NHANVIEN__maRole__48CFD27E",
                        column: x => x.maRole,
                        principalTable: "ROLE",
                        principalColumn: "maRole");
                });

            migrationBuilder.CreateTable(
                name: "SANPHAM",
                columns: table => new
                {
                    maSP = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maDanhMuc = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maTH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tenSP = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    MoTa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ThanhPhan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HuongDanSuDung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hinhAnhDaiDien = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    TrangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SANPHAM", x => x.maSP);
                    table.ForeignKey(
                        name: "FK_SANPHAM_DANHMUCSP_maDanhMuc",
                        column: x => x.maDanhMuc,
                        principalTable: "DANHMUCSP",
                        principalColumn: "maDMSP");
                    table.ForeignKey(
                        name: "FK_SANPHAM_THUONGHIEU_maTH",
                        column: x => x.maTH,
                        principalTable: "THUONGHIEU",
                        principalColumn: "maTH");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_COMBO",
                columns: table => new
                {
                    maCB = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CHITIET_COMBO", x => new { x.maCB, x.maDV });
                    table.ForeignKey(
                        name: "FK__CHITIET_CO__maCB__52593CB8",
                        column: x => x.maCB,
                        principalTable: "COMBO",
                        principalColumn: "maCB");
                    table.ForeignKey(
                        name: "FK__CHITIET_CO__maDV__534D60F1",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_FLASHSALE",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maFL = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maCB = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    phanTramGiam = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CHITIET_FLASHSALE", x => x.Id);
                    table.ForeignKey(
                        name: "FK__CHITIET_FL__maCB__571DF1D5",
                        column: x => x.maCB,
                        principalTable: "COMBO",
                        principalColumn: "maCB");
                    table.ForeignKey(
                        name: "FK__CHITIET_FL__maDV__5812160E",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                    table.ForeignKey(
                        name: "FK__CHITIET_FL__maFL__5629CD9C",
                        column: x => x.maFL,
                        principalTable: "FLASHSALE",
                        principalColumn: "maFL",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComboDichvu",
                columns: table => new
                {
                    MaCbsMaCb = table.Column<string>(type: "varchar(10)", nullable: false),
                    MaDvsMaDv = table.Column<string>(type: "varchar(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComboDichvu", x => new { x.MaCbsMaCb, x.MaDvsMaDv });
                    table.ForeignKey(
                        name: "FK_ComboDichvu_COMBO_MaCbsMaCb",
                        column: x => x.MaCbsMaCb,
                        principalTable: "COMBO",
                        principalColumn: "maCB",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComboDichvu_DICHVU_MaDvsMaDv",
                        column: x => x.MaDvsMaDv,
                        principalTable: "DICHVU",
                        principalColumn: "maDV",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HINHANH",
                columns: table => new
                {
                    maHA = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    duongDan = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__HINHANH__7A3E1483BFEC708E", x => x.maHA);
                    table.ForeignKey(
                        name: "FK__HINHANH__maDV__60A75C0F",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                });

            migrationBuilder.CreateTable(
                name: "KIFUTOC",
                columns: table => new
                {
                    maKT = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tenKieuToc = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    moTa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__KIFUTOC__7A3ECFF831EEC40E", x => x.maKT);
                    table.ForeignKey(
                        name: "FK__KIFUTOC__maDV__5AEE82B9",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                });

            migrationBuilder.CreateTable(
                name: "TIEUDE_DICHVU",
                columns: table => new
                {
                    maTD = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tenTieuDe = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    moTaTieuDe = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    soThuTu = table.Column<int>(type: "int", nullable: true),
                    LoaiHienThi = table.Column<int>(type: "int", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__TIEUDE_D__7A2262573E7A4201", x => x.maTD);
                    table.ForeignKey(
                        name: "FK__TIEUDE_DIC__maDV__6383C8BA",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                });

            migrationBuilder.CreateTable(
                name: "GIOHANG",
                columns: table => new
                {
                    maGH = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__GIOHANG__7A3E2D6B214EEED3", x => x.maGH);
                    table.ForeignKey(
                        name: "FK__GIOHANG__maKH__4A8310C6",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                });

            migrationBuilder.CreateTable(
                name: "KHACHHANG_VOUCHER",
                columns: table => new
                {
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__KHACHHAN__C958E6AAAE16A29A", x => new { x.maKH, x.maCode });
                    table.ForeignKey(
                        name: "FK__KHACHHANG___maKH__6A30C649",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                    table.ForeignKey(
                        name: "FK__KHACHHANG__maCod__6B24EA82",
                        column: x => x.maCode,
                        principalTable: "VOUCHER",
                        principalColumn: "maCode");
                });

            migrationBuilder.CreateTable(
                name: "LICHDAT",
                columns: table => new
                {
                    maLD = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    ngayHen = table.Column<DateOnly>(type: "date", nullable: true),
                    gioBatDau = table.Column<TimeOnly>(type: "time", nullable: true),
                    gioKetThuc = table.Column<TimeOnly>(type: "time", nullable: true),
                    tongTienDuKien = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LICHDAT__7A3EB709BDC0CDFF", x => x.maLD);
                    table.ForeignKey(
                        name: "FK__LICHDAT__maKH__75A278F5",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                });

            migrationBuilder.CreateTable(
                name: "THONGBAO",
                columns: table => new
                {
                    maTB = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tieuDe = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    noiDung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    kenhGui = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    loaiTin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ngayTao = table.Column<DateTime>(type: "datetime", nullable: true),
                    ngayCanGui = table.Column<DateTime>(type: "datetime", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__THONGBAO__7A2262699AD3AF89", x => x.maTB);
                    table.ForeignKey(
                        name: "FK__THONGBAO__maKH__72C60C4A",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                });

            migrationBuilder.CreateTable(
                name: "YeuThichDichVus",
                columns: table => new
                {
                    MaKH = table.Column<string>(type: "varchar(10)", nullable: false),
                    MaDV = table.Column<string>(type: "varchar(10)", nullable: false),
                    NgayThem = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YeuThichDichVus", x => new { x.MaKH, x.MaDV });
                    table.ForeignKey(
                        name: "FK_YeuThichDichVus_DICHVU_MaDV",
                        column: x => x.MaDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_YeuThichDichVus_KHACHHANG_MaKH",
                        column: x => x.MaKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LICHLAMVIEC",
                columns: table => new
                {
                    maLich = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    ngayLamViec = table.Column<DateOnly>(type: "date", nullable: true),
                    GhiChu = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    caLam = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LICHLAMV__F0B8217BB1B5E87D", x => x.maLich);
                    table.ForeignKey(
                        name: "FK__LICHLAMVIE__maNV__5DCAEF64",
                        column: x => x.maNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV");
                });

            migrationBuilder.CreateTable(
                name: "PHIEUNHAP",
                columns: table => new
                {
                    maPN = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maNCC = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    ngayNhap = table.Column<DateTime>(type: "datetime", nullable: true),
                    tongTien = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ghiChu = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PHIEUNHA__7A3ED79AB44CA582", x => x.maPN);
                    table.ForeignKey(
                        name: "FK__PHIEUNHAP__maNCC__4E53A1AA",
                        column: x => x.maNCC,
                        principalTable: "NHACUNGCAP",
                        principalColumn: "maNCC");
                    table.ForeignKey(
                        name: "FK__PHIEUNHAP__maNV__4D5F7D71",
                        column: x => x.maNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV");
                });

            migrationBuilder.CreateTable(
                name: "YeuThichNhanViens",
                columns: table => new
                {
                    MaKH = table.Column<string>(type: "varchar(10)", nullable: false),
                    MaNV = table.Column<string>(type: "varchar(10)", nullable: false),
                    NgayThem = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YeuThichNhanViens", x => new { x.MaKH, x.MaNV });
                    table.ForeignKey(
                        name: "FK_YeuThichNhanViens_KHACHHANG_MaKH",
                        column: x => x.MaKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_YeuThichNhanViens_NHANVIEN_MaNV",
                        column: x => x.MaNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BIENTHE_SANPHAM",
                columns: table => new
                {
                    maBienThe = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maSanPham = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tenBienThe = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GiaBan = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SoLuongTon = table.Column<int>(type: "int", nullable: true),
                    HinhAnhDaiDien = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    PhanTramHoaHong = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    TrangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BIENTHE_SANPHAM", x => x.maBienThe);
                    table.ForeignKey(
                        name: "FK_BIENTHE_SANPHAM_SANPHAM_maSanPham",
                        column: x => x.maSanPham,
                        principalTable: "SANPHAM",
                        principalColumn: "maSP");
                });

            migrationBuilder.CreateTable(
                name: "HINHANHSP",
                columns: table => new
                {
                    maHA = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maSP = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    duongDan = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__HINHANHS__7A3E1483AD681ED2", x => x.maHA);
                    table.ForeignKey(
                        name: "FK__HINHANHSP__maSP__42E1EEFE",
                        column: x => x.maSP,
                        principalTable: "SANPHAM",
                        principalColumn: "maSP");
                });

            migrationBuilder.CreateTable(
                name: "YeuThichSanPhams",
                columns: table => new
                {
                    MaKH = table.Column<string>(type: "varchar(10)", nullable: false),
                    MaSP = table.Column<string>(type: "varchar(10)", nullable: false),
                    NgayThem = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YeuThichSanPhams", x => new { x.MaKH, x.MaSP });
                    table.ForeignKey(
                        name: "FK_YeuThichSanPhams_KHACHHANG_MaKH",
                        column: x => x.MaKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_YeuThichSanPhams_SANPHAM_MaSP",
                        column: x => x.MaSP,
                        principalTable: "SANPHAM",
                        principalColumn: "maSP",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LICHSUTHUTOC",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maKT = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maMau = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    anhGoc = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    anhGhep = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    ngayThu = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LICHSUTH__3214EC277D377291", x => x.ID);
                    table.ForeignKey(
                        name: "FK__LICHSUTHUT__maKH__6E01572D",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                    table.ForeignKey(
                        name: "FK__LICHSUTHUT__maKT__6EF57B66",
                        column: x => x.maKT,
                        principalTable: "KIFUTOC",
                        principalColumn: "maKT");
                    table.ForeignKey(
                        name: "FK__LICHSUTHU__maMau__6FE99F9F",
                        column: x => x.maMau,
                        principalTable: "BANGMAU",
                        principalColumn: "maMau");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_TIEUDE",
                columns: table => new
                {
                    maCTTD = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maTD = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    tenTieuDe = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    moTaTieuDe = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    soThuTu = table.Column<int>(type: "int", nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CHITIET___FD26D0E6362B88FA", x => x.maCTTD);
                    table.ForeignKey(
                        name: "FK__CHITIET_TI__maDV__6754599E",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                    table.ForeignKey(
                        name: "FK__CHITIET_TI__maTD__66603565",
                        column: x => x.maTD,
                        principalTable: "TIEUDE_DICHVU",
                        principalColumn: "maTD");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_LICHDAT",
                columns: table => new
                {
                    maCTLD = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maLD = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maCB = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    giaLucDat = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TienHoaHongDv = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ghiChu = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CHITIET___FD279DF0AAB0864A", x => x.maCTLD);
                    table.ForeignKey(
                        name: "FK__CHITIET_LI__maCB__7B5B524B",
                        column: x => x.maCB,
                        principalTable: "COMBO",
                        principalColumn: "maCB");
                    table.ForeignKey(
                        name: "FK__CHITIET_LI__maDV__7A672E12",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                    table.ForeignKey(
                        name: "FK__CHITIET_LI__maLD__787EE5A0",
                        column: x => x.maLD,
                        principalTable: "LICHDAT",
                        principalColumn: "maLD");
                    table.ForeignKey(
                        name: "FK__CHITIET_LI__maNV__797309D9",
                        column: x => x.maNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV");
                });

            migrationBuilder.CreateTable(
                name: "HOA_DON",
                columns: table => new
                {
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    maLD = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    ngayTao = table.Column<DateTime>(type: "datetime", nullable: true),
                    tongTien = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    phanTramGiamVIP = table.Column<int>(type: "int", nullable: true),
                    thanhTien = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    tienDaTra = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__HOA_DON__7A3E1486F3B8EE3A", x => x.maHD);
                    table.ForeignKey(
                        name: "FK__HOA_DON__maCode__00200768",
                        column: x => x.maCode,
                        principalTable: "VOUCHER",
                        principalColumn: "maCode");
                    table.ForeignKey(
                        name: "FK__HOA_DON__maKH__01142BA1",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                    table.ForeignKey(
                        name: "FK__HOA_DON__maLD__7E37BEF6",
                        column: x => x.maLD,
                        principalTable: "LICHDAT",
                        principalColumn: "maLD");
                    table.ForeignKey(
                        name: "FK__HOA_DON__maNV__7F2BE32F",
                        column: x => x.maNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_GIOHANG",
                columns: table => new
                {
                    maGH = table.Column<int>(type: "int", nullable: false),
                    maBienThe = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    soLuong = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CHITIET___CD9C0ACC0072A942", x => new { x.maGH, x.maBienThe });
                    table.ForeignKey(
                        name: "FK__CHITIET_GI__maBT__4C6B5938",
                        column: x => x.maBienThe,
                        principalTable: "BIENTHE_SANPHAM",
                        principalColumn: "maBienThe");
                    table.ForeignKey(
                        name: "FK__CHITIET_GI__maGH__4B7734FF",
                        column: x => x.maGH,
                        principalTable: "GIOHANG",
                        principalColumn: "maGH");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_PHIEUNHAP",
                columns: table => new
                {
                    maPN = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maBienThe = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    soLuong = table.Column<int>(type: "int", nullable: true),
                    giaNhap = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CHITIET___CD9CF03D4A71F270", x => new { x.maPN, x.maBienThe });
                    table.ForeignKey(
                        name: "FK__CHITIET_PH__maBT__503BEA1C",
                        column: x => x.maBienThe,
                        principalTable: "BIENTHE_SANPHAM",
                        principalColumn: "maBienThe");
                    table.ForeignKey(
                        name: "FK__CHITIET_PH__maPN__4F47C5E3",
                        column: x => x.maPN,
                        principalTable: "PHIEUNHAP",
                        principalColumn: "maPN");
                });

            migrationBuilder.CreateTable(
                name: "CHITIET_HOADON_SP",
                columns: table => new
                {
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    maBienThe = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    soLuong = table.Column<int>(type: "int", nullable: true),
                    donGiaLucBan = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    tienHoaHongSP = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    thanhTien = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CHITIET___CD9C3321972BC3EA", x => new { x.maHD, x.maBienThe });
                    table.ForeignKey(
                        name: "FK__CHITIET_HO__maBT__44CA3770",
                        column: x => x.maBienThe,
                        principalTable: "BIENTHE_SANPHAM",
                        principalColumn: "maBienThe");
                    table.ForeignKey(
                        name: "FK__CHITIET_HO__maHD__43D61337",
                        column: x => x.maHD,
                        principalTable: "HOA_DON",
                        principalColumn: "maHD");
                    table.ForeignKey(
                        name: "FK__CHITIET_HO__maNV__45BE5BA9",
                        column: x => x.maNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV");
                });

            migrationBuilder.CreateTable(
                name: "DANHGIA_DICHVU",
                columns: table => new
                {
                    maDGDV = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    maDV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maCB = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    soSao = table.Column<int>(type: "int", nullable: true),
                    ngayDanhGia = table.Column<DateTime>(type: "datetime", nullable: true),
                    noiDung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phanHoi = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ngayPhanHoi = table.Column<DateTime>(type: "datetime", nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DANHGIA___351DF5097437A20A", x => x.maDGDV);
                    table.ForeignKey(
                        name: "FK__DANHGIA_DI__maCB__08B54D69",
                        column: x => x.maCB,
                        principalTable: "COMBO",
                        principalColumn: "maCB");
                    table.ForeignKey(
                        name: "FK__DANHGIA_DI__maDV__07C12930",
                        column: x => x.maDV,
                        principalTable: "DICHVU",
                        principalColumn: "maDV");
                    table.ForeignKey(
                        name: "FK__DANHGIA_DI__maHD__06CD04F7",
                        column: x => x.maHD,
                        principalTable: "HOA_DON",
                        principalColumn: "maHD");
                });

            migrationBuilder.CreateTable(
                name: "DANHGIA_NHANVIEN",
                columns: table => new
                {
                    maDGNV = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    soSao = table.Column<int>(type: "int", nullable: true),
                    ngayDanhGia = table.Column<DateTime>(type: "datetime", nullable: true),
                    noiDung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DANHGIA___351E2BDA5F4D0AB0", x => x.maDGNV);
                    table.ForeignKey(
                        name: "FK__DANHGIA_NH__maHD__0B91BA14",
                        column: x => x.maHD,
                        principalTable: "HOA_DON",
                        principalColumn: "maHD");
                    table.ForeignKey(
                        name: "FK__DANHGIA_NH__maNV__0C85DE4D",
                        column: x => x.maNV,
                        principalTable: "NHANVIEN",
                        principalColumn: "maNV");
                });

            migrationBuilder.CreateTable(
                name: "DANHGIA_SANPHAM",
                columns: table => new
                {
                    maDGSP = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maSP = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    maKH = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    soSao = table.Column<int>(type: "int", nullable: true),
                    noiDung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ngayDanhGia = table.Column<DateTime>(type: "datetime", nullable: true),
                    hinhAnh = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    phanHoi = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DANHGIA___351E030695815D88", x => x.maDGSP);
                    table.ForeignKey(
                        name: "FK__DANHGIA_SA__maHD__489AC854",
                        column: x => x.maHD,
                        principalTable: "HOA_DON",
                        principalColumn: "maHD");
                    table.ForeignKey(
                        name: "FK__DANHGIA_SA__maKH__498EEC8D",
                        column: x => x.maKH,
                        principalTable: "KHACHHANG",
                        principalColumn: "maKH");
                    table.ForeignKey(
                        name: "FK__DANHGIA_SA__maSP__47A6A41B",
                        column: x => x.maSP,
                        principalTable: "SANPHAM",
                        principalColumn: "maSP");
                });

            migrationBuilder.CreateTable(
                name: "THANHTOAN",
                columns: table => new
                {
                    maTT = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    phuongThuc = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    soTien = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    trangThai = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__THANHTOA__7A2262477BF7F293", x => x.maTT);
                    table.ForeignKey(
                        name: "FK__THANHTOAN__maHD__03F0984C",
                        column: x => x.maHD,
                        principalTable: "HOA_DON",
                        principalColumn: "maHD");
                });

            migrationBuilder.CreateTable(
                name: "THONGTIN_GIAOHANG",
                columns: table => new
                {
                    maGiaoHang = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maHD = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    tenNguoiNhan = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    sdtNhan = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    diaChiNhan = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    donViVanChuyen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    maVanDon = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    phiShip = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Email = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    trangThaiGiaoHang = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__THONGTIN__AF360BCB30B56C0A", x => x.maGiaoHang);
                    table.ForeignKey(
                        name: "FK__THONGTIN_G__maHD__46B27FE2",
                        column: x => x.maHD,
                        principalTable: "HOA_DON",
                        principalColumn: "maHD");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BIENTHE_SANPHAM_maSanPham",
                table: "BIENTHE_SANPHAM",
                column: "maSanPham");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_COMBO_maDV",
                table: "CHITIET_COMBO",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_FLASHSALE_maCB",
                table: "CHITIET_FLASHSALE",
                column: "maCB");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_FLASHSALE_maDV",
                table: "CHITIET_FLASHSALE",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_FLASHSALE_maFL",
                table: "CHITIET_FLASHSALE",
                column: "maFL");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_GIOHANG_maBienThe",
                table: "CHITIET_GIOHANG",
                column: "maBienThe");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_HOADON_SP_maBienThe",
                table: "CHITIET_HOADON_SP",
                column: "maBienThe");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_HOADON_SP_maNV",
                table: "CHITIET_HOADON_SP",
                column: "maNV");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_LICHDAT_maCB",
                table: "CHITIET_LICHDAT",
                column: "maCB");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_LICHDAT_maDV",
                table: "CHITIET_LICHDAT",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_LICHDAT_maLD",
                table: "CHITIET_LICHDAT",
                column: "maLD");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_LICHDAT_maNV",
                table: "CHITIET_LICHDAT",
                column: "maNV");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_PHIEUNHAP_maBienThe",
                table: "CHITIET_PHIEUNHAP",
                column: "maBienThe");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_TIEUDE_maDV",
                table: "CHITIET_TIEUDE",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_CHITIET_TIEUDE_maTD",
                table: "CHITIET_TIEUDE",
                column: "maTD");

            migrationBuilder.CreateIndex(
                name: "IX_ComboDichvu_MaDvsMaDv",
                table: "ComboDichvu",
                column: "MaDvsMaDv");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_DICHVU_maCB",
                table: "DANHGIA_DICHVU",
                column: "maCB");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_DICHVU_maDV",
                table: "DANHGIA_DICHVU",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_DICHVU_maHD",
                table: "DANHGIA_DICHVU",
                column: "maHD");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_NHANVIEN_maHD",
                table: "DANHGIA_NHANVIEN",
                column: "maHD");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_NHANVIEN_maNV",
                table: "DANHGIA_NHANVIEN",
                column: "maNV");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_SANPHAM_maHD",
                table: "DANHGIA_SANPHAM",
                column: "maHD");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_SANPHAM_maKH",
                table: "DANHGIA_SANPHAM",
                column: "maKH");

            migrationBuilder.CreateIndex(
                name: "IX_DANHGIA_SANPHAM_maSP",
                table: "DANHGIA_SANPHAM",
                column: "maSP");

            migrationBuilder.CreateIndex(
                name: "IX_DICHVU_maDM",
                table: "DICHVU",
                column: "maDM");

            migrationBuilder.CreateIndex(
                name: "IX_GIOHANG_maKH",
                table: "GIOHANG",
                column: "maKH");

            migrationBuilder.CreateIndex(
                name: "IX_HINHANH_maDV",
                table: "HINHANH",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_HINHANHSP_maSP",
                table: "HINHANHSP",
                column: "maSP");

            migrationBuilder.CreateIndex(
                name: "IX_HOA_DON_maCode",
                table: "HOA_DON",
                column: "maCode");

            migrationBuilder.CreateIndex(
                name: "IX_HOA_DON_maKH",
                table: "HOA_DON",
                column: "maKH");

            migrationBuilder.CreateIndex(
                name: "IX_HOA_DON_maLD",
                table: "HOA_DON",
                column: "maLD");

            migrationBuilder.CreateIndex(
                name: "IX_HOA_DON_maNV",
                table: "HOA_DON",
                column: "maNV");

            migrationBuilder.CreateIndex(
                name: "IX_KHACHHANG_maRank",
                table: "KHACHHANG",
                column: "maRank");

            migrationBuilder.CreateIndex(
                name: "IX_KHACHHANG_VOUCHER_maCode",
                table: "KHACHHANG_VOUCHER",
                column: "maCode");

            migrationBuilder.CreateIndex(
                name: "IX_KIFUTOC_maDV",
                table: "KIFUTOC",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_LICHDAT_maKH",
                table: "LICHDAT",
                column: "maKH");

            migrationBuilder.CreateIndex(
                name: "IX_LICHLAMVIEC_maNV",
                table: "LICHLAMVIEC",
                column: "maNV");

            migrationBuilder.CreateIndex(
                name: "IX_LICHSUTHUTOC_maKH",
                table: "LICHSUTHUTOC",
                column: "maKH");

            migrationBuilder.CreateIndex(
                name: "IX_LICHSUTHUTOC_maKT",
                table: "LICHSUTHUTOC",
                column: "maKT");

            migrationBuilder.CreateIndex(
                name: "IX_LICHSUTHUTOC_maMau",
                table: "LICHSUTHUTOC",
                column: "maMau");

            migrationBuilder.CreateIndex(
                name: "IX_NHANVIEN_maLoaiNV",
                table: "NHANVIEN",
                column: "maLoaiNV");

            migrationBuilder.CreateIndex(
                name: "IX_NHANVIEN_maRole",
                table: "NHANVIEN",
                column: "maRole");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEUNHAP_maNCC",
                table: "PHIEUNHAP",
                column: "maNCC");

            migrationBuilder.CreateIndex(
                name: "IX_PHIEUNHAP_maNV",
                table: "PHIEUNHAP",
                column: "maNV");

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_maDanhMuc",
                table: "SANPHAM",
                column: "maDanhMuc");

            migrationBuilder.CreateIndex(
                name: "IX_SANPHAM_maTH",
                table: "SANPHAM",
                column: "maTH");

            migrationBuilder.CreateIndex(
                name: "IX_THANHTOAN_maHD",
                table: "THANHTOAN",
                column: "maHD");

            migrationBuilder.CreateIndex(
                name: "IX_THONGBAO_maKH",
                table: "THONGBAO",
                column: "maKH");

            migrationBuilder.CreateIndex(
                name: "IX_THONGTIN_GIAOHANG_maHD",
                table: "THONGTIN_GIAOHANG",
                column: "maHD");

            migrationBuilder.CreateIndex(
                name: "IX_TIEUDE_DICHVU_maDV",
                table: "TIEUDE_DICHVU",
                column: "maDV");

            migrationBuilder.CreateIndex(
                name: "IX_YeuThichDichVus_MaDV",
                table: "YeuThichDichVus",
                column: "MaDV");

            migrationBuilder.CreateIndex(
                name: "IX_YeuThichNhanViens_MaNV",
                table: "YeuThichNhanViens",
                column: "MaNV");

            migrationBuilder.CreateIndex(
                name: "IX_YeuThichSanPhams_MaSP",
                table: "YeuThichSanPhams",
                column: "MaSP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CHITIET_COMBO");

            migrationBuilder.DropTable(
                name: "CHITIET_FLASHSALE");

            migrationBuilder.DropTable(
                name: "CHITIET_GIOHANG");

            migrationBuilder.DropTable(
                name: "CHITIET_HOADON_SP");

            migrationBuilder.DropTable(
                name: "CHITIET_LICHDAT");

            migrationBuilder.DropTable(
                name: "CHITIET_PHIEUNHAP");

            migrationBuilder.DropTable(
                name: "CHITIET_TIEUDE");

            migrationBuilder.DropTable(
                name: "ComboDichvu");

            migrationBuilder.DropTable(
                name: "DANHGIA_DICHVU");

            migrationBuilder.DropTable(
                name: "DANHGIA_NHANVIEN");

            migrationBuilder.DropTable(
                name: "DANHGIA_SANPHAM");

            migrationBuilder.DropTable(
                name: "HINHANH");

            migrationBuilder.DropTable(
                name: "HINHANHSP");

            migrationBuilder.DropTable(
                name: "KHACHHANG_VOUCHER");

            migrationBuilder.DropTable(
                name: "LICHLAMVIEC");

            migrationBuilder.DropTable(
                name: "LICHSUTHUTOC");

            migrationBuilder.DropTable(
                name: "REFRESHTOKEN");

            migrationBuilder.DropTable(
                name: "THANHTOAN");

            migrationBuilder.DropTable(
                name: "THONGBAO");

            migrationBuilder.DropTable(
                name: "THONGTIN_GIAOHANG");

            migrationBuilder.DropTable(
                name: "YeuThichDichVus");

            migrationBuilder.DropTable(
                name: "YeuThichNhanViens");

            migrationBuilder.DropTable(
                name: "YeuThichSanPhams");

            migrationBuilder.DropTable(
                name: "FLASHSALE");

            migrationBuilder.DropTable(
                name: "GIOHANG");

            migrationBuilder.DropTable(
                name: "BIENTHE_SANPHAM");

            migrationBuilder.DropTable(
                name: "PHIEUNHAP");

            migrationBuilder.DropTable(
                name: "TIEUDE_DICHVU");

            migrationBuilder.DropTable(
                name: "COMBO");

            migrationBuilder.DropTable(
                name: "KIFUTOC");

            migrationBuilder.DropTable(
                name: "BANGMAU");

            migrationBuilder.DropTable(
                name: "HOA_DON");

            migrationBuilder.DropTable(
                name: "SANPHAM");

            migrationBuilder.DropTable(
                name: "NHACUNGCAP");

            migrationBuilder.DropTable(
                name: "DICHVU");

            migrationBuilder.DropTable(
                name: "VOUCHER");

            migrationBuilder.DropTable(
                name: "LICHDAT");

            migrationBuilder.DropTable(
                name: "NHANVIEN");

            migrationBuilder.DropTable(
                name: "DANHMUCSP");

            migrationBuilder.DropTable(
                name: "THUONGHIEU");

            migrationBuilder.DropTable(
                name: "DANH_MUC");

            migrationBuilder.DropTable(
                name: "KHACHHANG");

            migrationBuilder.DropTable(
                name: "LOAINHANVIEN");

            migrationBuilder.DropTable(
                name: "ROLE");

            migrationBuilder.DropTable(
                name: "HANGTHANHVIEN");
        }
    }
}

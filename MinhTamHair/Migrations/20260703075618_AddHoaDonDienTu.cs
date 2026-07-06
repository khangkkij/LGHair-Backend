using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinhTamHair.Migrations
{
    /// <inheritdoc />
    public partial class AddHoaDonDienTu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HINHANH_BT",
                columns: table => new
                {
                    maHA = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maBienThe = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    duongDan = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HINHANH_BT", x => x.maHA);
                    table.ForeignKey(
                        name: "FK_HINHANH_BT_BIENTHE",
                        column: x => x.maBienThe,
                        principalTable: "BIENTHE_SANPHAM",
                        principalColumn: "maBienThe");
                });

            migrationBuilder.CreateTable(
                name: "HOA_DON_DIEN_TU",
                columns: table => new
                {
                    MaHddt = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    MaHd = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    LoaiKhach = table.Column<byte>(type: "tinyint", nullable: false),
                    TenNguoiMua = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Mst = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    DiaChiXuatHd = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EmailNhanHd = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TrangThai = table.Column<byte>(type: "tinyint", nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SoHoaDon = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    KyHieu = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LinkPdf = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkXml = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NgayXuat = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GhiChuLoi = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HOA_DON_DIEN_TU", x => x.MaHddt);
                });

            migrationBuilder.CreateTable(
                name: "LICHSU_TONKHO",
                columns: table => new
                {
                    maLich = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    maBienThe = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    loaiBienDong = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    soLuongThayDoi = table.Column<int>(type: "int", nullable: false),
                    tonTruoc = table.Column<int>(type: "int", nullable: false),
                    tonSau = table.Column<int>(type: "int", nullable: false),
                    maChungTu = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    maNV = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    ghiChu = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    thoiGian = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LICHSU_TONKHO", x => x.maLich);
                    table.ForeignKey(
                        name: "FK_LICHSU_TONKHO_BIENTHE",
                        column: x => x.maBienThe,
                        principalTable: "BIENTHE_SANPHAM",
                        principalColumn: "maBienThe",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HINHANH_BT_maBienThe",
                table: "HINHANH_BT",
                column: "maBienThe");

            migrationBuilder.CreateIndex(
                name: "IX_LICHSU_TONKHO_maBienThe",
                table: "LICHSU_TONKHO",
                column: "maBienThe");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HINHANH_BT");

            migrationBuilder.DropTable(
                name: "HOA_DON_DIEN_TU");

            migrationBuilder.DropTable(
                name: "LICHSU_TONKHO");
        }
    }
}

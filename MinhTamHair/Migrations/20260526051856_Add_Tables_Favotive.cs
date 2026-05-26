using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinhTamHair.Migrations
{
    /// <inheritdoc />
    public partial class Add_Tables_Favotive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "YeuThichDichVus");

            migrationBuilder.DropTable(
                name: "YeuThichNhanViens");

            migrationBuilder.DropTable(
                name: "YeuThichSanPhams");
        }
    }
}

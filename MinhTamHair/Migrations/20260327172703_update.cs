using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinhTamHair.Migrations
{
    /// <inheritdoc />
    public partial class update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
     name: "CHITIET_FLASHSALE",
   columns: table => new
   {
       // Thêm cột Id làm PK để gánh cho maFL
       Id = table.Column<int>(type: "int", nullable: false)
 .Annotation("SqlServer:Identity", "1, 1"),

       maFL = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),

       // CHỈNH Ở ĐÂY: nullable: true thì m mới gán null được
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
             principalColumn: "maFL");
     });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropTable(
                name: "CHITIET_FLASHSALE");
;
        }
    }
}

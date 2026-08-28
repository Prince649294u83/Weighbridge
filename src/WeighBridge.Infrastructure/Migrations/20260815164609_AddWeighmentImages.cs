using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWeighmentImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeighmentImages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WeighmentId = table.Column<long>(type: "INTEGER", nullable: false),
                    CameraName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RelativeFilePath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256Checksum = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeighmentImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeighmentImages_Weighments_WeighmentId",
                        column: x => x.WeighmentId,
                        principalTable: "Weighments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeighmentImages_CapturedAtUtc",
                table: "WeighmentImages",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WeighmentImages_WeighmentId",
                table: "WeighmentImages",
                column: "WeighmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeighmentImages");
        }
    }
}

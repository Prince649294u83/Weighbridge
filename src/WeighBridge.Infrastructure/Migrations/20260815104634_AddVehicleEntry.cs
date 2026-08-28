using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Weighments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SlipNumber = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    VehicleNumber = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PartyName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MaterialName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DriverName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TransporterName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    FirstWeightGrams = table.Column<long>(type: "INTEGER", nullable: true),
                    FirstWeightAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstWeightSource = table.Column<int>(type: "INTEGER", nullable: true),
                    SecondWeightGrams = table.Column<long>(type: "INTEGER", nullable: true),
                    SecondWeightAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SecondWeightSource = table.Column<int>(type: "INTEGER", nullable: true),
                    NetWeightGrams = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Weighments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_CreatedAtUtc",
                table: "Weighments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_SlipNumber",
                table: "Weighments",
                column: "SlipNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_Status",
                table: "Weighments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_VehicleNumber",
                table: "Weighments",
                column: "VehicleNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Weighments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenConstraintsAndAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Weighments_SlipNumber",
                table: "Weighments");

            migrationBuilder.DropIndex(
                name: "IX_Weighments_VehicleNumber",
                table: "Weighments");

            migrationBuilder.AlterColumn<int>(
                name: "Source",
                table: "WeighmentImages",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "TypeName",
                table: "VehicleTypes",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "Vehicles",
                type: "TEXT",
                maxLength: 24,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Parties",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Materials",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OperatorName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Module = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_SlipNumber",
                table: "Weighments",
                column: "SlipNumber",
                unique: true,
                filter: "[SlipNumber] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_VehicleNumber_Open",
                table: "Weighments",
                column: "VehicleNumber",
                unique: true,
                filter: "[Status] IN (0, 1) AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OperatorName",
                table: "AuditEntries",
                column: "OperatorName");

            migrationBuilder.AddForeignKey(
                name: "FK_Weighments_Materials_MaterialId",
                table: "Weighments",
                column: "MaterialId",
                principalTable: "Materials",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Weighments_Parties_PartyId",
                table: "Weighments",
                column: "PartyId",
                principalTable: "Parties",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Weighments_VehicleTypes_VehicleTypeId",
                table: "Weighments",
                column: "VehicleTypeId",
                principalTable: "VehicleTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Weighments_Vehicles_VehicleId",
                table: "Weighments",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Weighments_Materials_MaterialId",
                table: "Weighments");

            migrationBuilder.DropForeignKey(
                name: "FK_Weighments_Parties_PartyId",
                table: "Weighments");

            migrationBuilder.DropForeignKey(
                name: "FK_Weighments_VehicleTypes_VehicleTypeId",
                table: "Weighments");

            migrationBuilder.DropForeignKey(
                name: "FK_Weighments_Vehicles_VehicleId",
                table: "Weighments");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_Weighments_SlipNumber",
                table: "Weighments");

            migrationBuilder.DropIndex(
                name: "IX_Weighments_VehicleNumber_Open",
                table: "Weighments");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "WeighmentImages",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "TypeName",
                table: "VehicleTypes",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "Vehicles",
                type: "TEXT",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 24,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Parties",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Materials",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldCollation: "NOCASE");

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_SlipNumber",
                table: "Weighments",
                column: "SlipNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_VehicleNumber",
                table: "Weighments",
                column: "VehicleNumber");
        }
    }
}

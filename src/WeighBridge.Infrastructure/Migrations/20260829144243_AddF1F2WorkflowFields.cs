using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddF1F2WorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BagWeightGrams",
                table: "Weighments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ChargesPaise",
                table: "Weighments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CustomField1",
                table: "Weighments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomField2",
                table: "Weighments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomField3",
                table: "Weighments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomField4",
                table: "Weighments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatePassNumber",
                table: "Weighments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NumberOfBags",
                table: "Weighments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SecondChargesPaise",
                table: "Weighments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Weighments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Ensure every existing historical row receives a valid non-empty GUID version
            migrationBuilder.Sql(
                "UPDATE Weighments SET Version = (" +
                "lower(hex(randomblob(4))) || '-' || " +
                "lower(hex(randomblob(2))) || '-4' || " +
                "substr(lower(hex(randomblob(2))), 2) || '-a' || " +
                "substr(lower(hex(randomblob(2))), 2) || '-' || " +
                "lower(hex(randomblob(6)))) " +
                "WHERE Version = '00000000-0000-0000-0000-000000000000' OR Version IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BagWeightGrams",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "ChargesPaise",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "CustomField1",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "CustomField2",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "CustomField3",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "CustomField4",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "GatePassNumber",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "NumberOfBags",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "SecondChargesPaise",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Weighments");
        }
    }
}

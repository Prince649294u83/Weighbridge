using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReservationId",
                table: "Weighments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TicketReservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SlipNumber = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TentativeVehicleNumber = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    TerminalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConsumedByWeighmentId = table.Column<long>(type: "INTEGER", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsumedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketReservations_Weighments_ConsumedByWeighmentId",
                        column: x => x.ConsumedByWeighmentId,
                        principalTable: "Weighments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Weighments_ReservationId",
                table: "Weighments",
                column: "ReservationId",
                unique: true,
                filter: "[ReservationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TicketReservations_ConsumedByWeighmentId",
                table: "TicketReservations",
                column: "ConsumedByWeighmentId",
                unique: true,
                filter: "[ConsumedByWeighmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TicketReservations_CreatedAtUtc",
                table: "TicketReservations",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TicketReservations_SlipNumber",
                table: "TicketReservations",
                column: "SlipNumber",
                unique: true,
                filter: "[SlipNumber] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TicketReservations_Status",
                table: "TicketReservations",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Weighments_TicketReservations_ReservationId",
                table: "Weighments",
                column: "ReservationId",
                principalTable: "TicketReservations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Weighments_TicketReservations_ReservationId",
                table: "Weighments");

            migrationBuilder.DropTable(
                name: "TicketReservations");

            migrationBuilder.DropIndex(
                name: "IX_Weighments_ReservationId",
                table: "Weighments");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "Weighments");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePrefixFromSlipNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Strip 'WB-' prefix from Weighments table (SQLite SUBSTR is 1-based; index 4 skips 'WB-')
            migrationBuilder.Sql(
                "UPDATE Weighments " +
                "SET SlipNumber = SUBSTR(SlipNumber, 4) " +
                "WHERE SlipNumber LIKE 'WB-%';");

            // 2. Strip 'WB-' prefix from TicketReservations table
            migrationBuilder.Sql(
                "UPDATE TicketReservations " +
                "SET SlipNumber = SUBSTR(SlipNumber, 4) " +
                "WHERE SlipNumber LIKE 'WB-%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: Re-attach 'WB-' prefix to records that lack it
            migrationBuilder.Sql(
                "UPDATE Weighments " +
                "SET SlipNumber = 'WB-' || SlipNumber " +
                "WHERE SlipNumber NOT LIKE 'WB-%' AND LENGTH(SlipNumber) > 0;");

            migrationBuilder.Sql(
                "UPDATE TicketReservations " +
                "SET SlipNumber = 'WB-' || SlipNumber " +
                "WHERE SlipNumber NOT LIKE 'WB-%' AND LENGTH(SlipNumber) > 0;");
        }
    }
}

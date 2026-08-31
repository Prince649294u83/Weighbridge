using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeighBridge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3Pass2OperationalFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OperatorName",
                table: "AuditEntries");

            migrationBuilder.AddColumn<int>(
                name: "FailedAccessCount",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutUntilUtc",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "SmsOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    WeighmentId = table.Column<long>(type: "INTEGER", nullable: true),
                    RecipientPhoneNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MessageContent = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    SendingSinceUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ProviderUsed = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Entity_EntityId",
                table: "AuditEntries",
                columns: new[] { "Entity", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc_OperatorName_Action",
                table: "AuditEntries",
                columns: new[] { "OccurredAtUtc", "OperatorName", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Outcome",
                table: "AuditEntries",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_SmsOutboxMessages_CorrelationId",
                table: "SmsOutboxMessages",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsOutboxMessages_MessageKey",
                table: "SmsOutboxMessages",
                column: "MessageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsOutboxMessages_SendingSinceUtc",
                table: "SmsOutboxMessages",
                column: "SendingSinceUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SmsOutboxMessages_Status_NextAttemptUtc",
                table: "SmsOutboxMessages",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsOutboxMessages_WeighmentId",
                table: "SmsOutboxMessages",
                column: "WeighmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Entity_EntityId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OccurredAtUtc_OperatorName_Action",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Outcome",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "FailedAccessCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockoutUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OperatorName",
                table: "AuditEntries",
                column: "OperatorName");
        }
    }
}

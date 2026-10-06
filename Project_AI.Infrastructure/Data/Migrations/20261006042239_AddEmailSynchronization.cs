using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_AI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    From = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    To = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Snippet = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BodyText = table.Column<string>(type: "text", nullable: false),
                    BodyTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    HasAttachments = table.Column<bool>(type: "boolean", nullable: false),
                    Labels = table.Column<string[]>(type: "text[]", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailMessages_MailboxConnections_MailboxConnectionId",
                        column: x => x.MailboxConnectionId,
                        principalTable: "MailboxConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailboxSyncStates",
                columns: table => new
                {
                    MailboxConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    HistoryId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailboxSyncStates", x => x.MailboxConnectionId);
                    table.ForeignKey(
                        name: "FK_MailboxSyncStates_MailboxConnections_MailboxConnectionId",
                        column: x => x.MailboxConnectionId,
                        principalTable: "MailboxConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_MailboxConnectionId_ProviderMessageId",
                table: "EmailMessages",
                columns: new[] { "MailboxConnectionId", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_MailboxConnectionId_ReceivedAt_Id",
                table: "EmailMessages",
                columns: new[] { "MailboxConnectionId", "ReceivedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailMessages");

            migrationBuilder.DropTable(
                name: "MailboxSyncStates");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_AI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReplyDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplyDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    From = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    To = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BodyText = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    InReplyTo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    References = table.Column<string[]>(type: "text[]", nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ThreadFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    SendAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutgoingMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SendStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyDrafts_EmailMessages_EmailMessageId",
                        column: x => x.EmailMessageId,
                        principalTable: "EmailMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_EmailMessageId",
                table: "ReplyDrafts",
                column: "EmailMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplyDrafts");
        }
    }
}

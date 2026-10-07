using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_AI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetainReplySendRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReplyDrafts_EmailMessages_EmailMessageId",
                table: "ReplyDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ReplyDrafts_EmailMessageId",
                table: "ReplyDrafts");

            migrationBuilder.AlterColumn<Guid>(
                name: "EmailMessageId",
                table: "ReplyDrafts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "MailboxConnectionId",
                table: "ReplyDrafts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEmailId",
                table: "ReplyDrafts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE "ReplyDrafts" AS draft
                SET "MailboxConnectionId" = email."MailboxConnectionId", "SourceEmailId" = email."Id"
                FROM "EmailMessages" AS email WHERE draft."EmailMessageId" = email."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_EmailMessageId",
                table: "ReplyDrafts",
                column: "EmailMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_MailboxConnectionId_CreatedAt",
                table: "ReplyDrafts",
                columns: new[] { "MailboxConnectionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_SourceEmailId",
                table: "ReplyDrafts",
                column: "SourceEmailId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplyDrafts_EmailMessages_EmailMessageId",
                table: "ReplyDrafts",
                column: "EmailMessageId",
                principalTable: "EmailMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplyDrafts_MailboxConnections_MailboxConnectionId",
                table: "ReplyDrafts",
                column: "MailboxConnectionId",
                principalTable: "MailboxConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema cannot represent a retained receipt whose source has been removed.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ReplyDrafts" WHERE "EmailMessageId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back retained reply records with missing source emails.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ReplyDrafts_EmailMessages_EmailMessageId",
                table: "ReplyDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_ReplyDrafts_MailboxConnections_MailboxConnectionId",
                table: "ReplyDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ReplyDrafts_EmailMessageId",
                table: "ReplyDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ReplyDrafts_MailboxConnectionId_CreatedAt",
                table: "ReplyDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ReplyDrafts_SourceEmailId",
                table: "ReplyDrafts");

            migrationBuilder.DropColumn(
                name: "MailboxConnectionId",
                table: "ReplyDrafts");

            migrationBuilder.DropColumn(
                name: "SourceEmailId",
                table: "ReplyDrafts");

            migrationBuilder.AlterColumn<Guid>(
                name: "EmailMessageId",
                table: "ReplyDrafts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_EmailMessageId",
                table: "ReplyDrafts",
                column: "EmailMessageId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplyDrafts_EmailMessages_EmailMessageId",
                table: "ReplyDrafts",
                column: "EmailMessageId",
                principalTable: "EmailMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

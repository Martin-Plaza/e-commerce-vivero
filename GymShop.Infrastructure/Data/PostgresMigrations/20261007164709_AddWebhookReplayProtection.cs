using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddWebhookReplayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookReceipts_ProcessedAtUtc",
                table: "WebhookReceipts",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookReceipts_Provider_RequestIdHash",
                table: "WebhookReceipts",
                columns: new[] { "Provider", "RequestIdHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookReceipts");
        }
    }
}

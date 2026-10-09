using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddGuestCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Orders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GuestAccessToken",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestEmail",
                table: "Orders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestFirstName",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestLastName",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestPhone",
                table: "Orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StockReserved",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "CheckoutSessions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "CartId",
                table: "CheckoutSessions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "GuestAccessToken",
                table: "CheckoutSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestEmail",
                table: "CheckoutSessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestFirstName",
                table: "CheckoutSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestLastName",
                table: "CheckoutSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestPhone",
                table: "CheckoutSessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_GuestAccessToken",
                table: "Orders",
                column: "GuestAccessToken",
                unique: true,
                filter: "\"GuestAccessToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_Guest_CheckoutIdempotencyKey",
                table: "Orders",
                column: "CheckoutIdempotencyKey",
                unique: true,
                filter: "\"UserId\" IS NULL AND \"CheckoutIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_GuestAccessToken",
                table: "CheckoutSessions",
                column: "GuestAccessToken",
                unique: true,
                filter: "\"GuestAccessToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CheckoutSessions_Guest_IdempotencyKey",
                table: "CheckoutSessions",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"UserId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_GuestAccessToken",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "UX_Orders_Guest_CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_GuestAccessToken",
                table: "CheckoutSessions");

            migrationBuilder.DropIndex(
                name: "UX_CheckoutSessions_Guest_IdempotencyKey",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestAccessToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestEmail",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestFirstName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestLastName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestPhone",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StockReserved",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GuestAccessToken",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "GuestEmail",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "GuestFirstName",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "GuestLastName",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "GuestPhone",
                table: "CheckoutSessions");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "CheckoutSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CartId",
                table: "CheckoutSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}

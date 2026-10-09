using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddOrderCheckoutIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutIdempotencyKey",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutRequestFingerprint",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Orders_UserId_CheckoutIdempotencyKey",
                table: "Orders",
                columns: new[] { "UserId", "CheckoutIdempotencyKey" },
                unique: true,
                filter: "\"CheckoutIdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Orders_UserId_CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutRequestFingerprint",
                table: "Orders");
        }
    }
}

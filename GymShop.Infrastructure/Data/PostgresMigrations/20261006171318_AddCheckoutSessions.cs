using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddCheckoutSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Payments_OrderId_Active",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "UX_Orders_UserId_Pending",
                table: "Orders");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "Payments",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "CheckoutSessionId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CartId = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CouponId = table.Column<int>(type: "integer", nullable: true),
                    CouponCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DeliveryMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShippingCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ShippingPostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ShippingProvince = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippingCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippingStreet = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ShippingStreetNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShippingFloor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShippingApartment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShippingNotes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ShippingQuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippingProviderCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShippingServiceCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippingServiceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    PickupAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PickupHours = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PickupInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutSessions", x => x.Id);
                    table.CheckConstraint("CK_CheckoutSessions_Amounts_NonNegative", "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"ShippingCost\" >= 0 AND \"Total\" >= 0");
                    table.CheckConstraint("CK_CheckoutSessions_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_CheckoutSessions_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckoutSessions_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CheckoutSessions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckoutSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CheckoutSessionId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductVariantId = table.Column<int>(type: "integer", nullable: true),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VariantSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VariantAttributesJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutItems", x => x.Id);
                    table.CheckConstraint("CK_CheckoutItems_Amounts_NonNegative", "\"UnitPrice\" >= 0 AND \"Subtotal\" >= 0");
                    table.CheckConstraint("CK_CheckoutItems_Quantity_Positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_CheckoutItems_CheckoutSessions_CheckoutSessionId",
                        column: x => x.CheckoutSessionId,
                        principalTable: "CheckoutSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CheckoutItems_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CheckoutItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_CheckoutSessionId_Active",
                table: "Payments",
                column: "CheckoutSessionId",
                unique: true,
                filter: "\"CheckoutSessionId\" IS NOT NULL AND \"Status\" IN ('Creating', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_OrderId_Active",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL AND \"Status\" IN ('Creating', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItems_CheckoutSessionId_ProductId_ProductVariantId",
                table: "CheckoutItems",
                columns: new[] { "CheckoutSessionId", "ProductId", "ProductVariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItems_ProductId",
                table: "CheckoutItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutItems_ProductVariantId",
                table: "CheckoutItems",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_CartId",
                table: "CheckoutSessions",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_CouponId",
                table: "CheckoutSessions",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_OrderId",
                table: "CheckoutSessions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_UserId_Status",
                table: "CheckoutSessions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_CheckoutSessions_UserId_IdempotencyKey",
                table: "CheckoutSessions",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_CheckoutSessions_CheckoutSessionId",
                table: "Payments",
                column: "CheckoutSessionId",
                principalTable: "CheckoutSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_CheckoutSessions_CheckoutSessionId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "CheckoutItems");

            migrationBuilder.DropTable(
                name: "CheckoutSessions");

            migrationBuilder.DropIndex(
                name: "UX_Payments_CheckoutSessionId_Active",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "UX_Payments_OrderId_Active",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutSessionId",
                table: "Payments");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Payments_OrderId_Active",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" IN ('Creating', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_UserId_Pending",
                table: "Orders",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 'Pending'");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddStockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PreviousStock = table.Column<int>(type: "integer", nullable: false),
                    ResultingStock = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    OrderId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Quantity_NotZero", "\"Quantity\" <> 0");
                    table.CheckConstraint("CK_StockMovements_StockBalance", "\"ResultingStock\" = \"PreviousStock\" + \"Quantity\"");
                    table.CheckConstraint("CK_StockMovements_Stocks_NonNegative", "\"PreviousStock\" >= 0 AND \"ResultingStock\" >= 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ActorUserId",
                table: "StockMovements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CreatedAtUtc_Id",
                table: "StockMovements",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrderId_ProductId_Type",
                table: "StockMovements",
                columns: new[] { "OrderId", "ProductId", "Type" },
                unique: true,
                filter: "\"OrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_CreatedAtUtc_Id",
                table: "StockMovements",
                columns: new[] { "ProductId", "CreatedAtUtc", "Id" });

            migrationBuilder.Sql("""
                INSERT INTO "StockMovements"
                    ("ProductId", "Type", "Quantity", "PreviousStock", "ResultingStock", "Reason", "ActorUserId", "OrderId", "CreatedAtUtc")
                SELECT
                    "Id", 'InitialStock', "Stock", 0, "Stock",
                    'Saldo inicial incorporado por migración', NULL, NULL, CURRENT_TIMESTAMP
                FROM "Products"
                WHERE "Stock" > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements");
        }
    }
}

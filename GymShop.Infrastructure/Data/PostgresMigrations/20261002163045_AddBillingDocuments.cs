using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddBillingDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    RelatedDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IssuerBusinessName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuerCuit = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    IssuerTaxCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IssuerFiscalAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IssuerGrossIncomeNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IssuerActivityStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientDocumentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecipientDocumentNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RecipientTaxCondition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RecipientAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetTaxedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetUntaxedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExemptAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherTaxesAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PointOfSale = table.Column<int>(type: "integer", nullable: true),
                    DocumentNumber = table.Column<long>(type: "bigint", nullable: true),
                    AuthorizationProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProviderRequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cae = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CaeExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingDocuments", x => x.Id);
                    table.CheckConstraint("CK_BillingDocuments_Amounts_NonNegative", "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"ShippingAmount\" >= 0 AND \"NetTaxedAmount\" >= 0 AND \"NetUntaxedAmount\" >= 0 AND \"ExemptAmount\" >= 0 AND \"VatAmount\" >= 0 AND \"OtherTaxesAmount\" >= 0 AND \"Total\" >= 0");
                    table.CheckConstraint("CK_BillingDocuments_Category_Type", "(\"Category\" = 'Receipt' AND \"Type\" = 'PurchaseReceipt') OR (\"Category\" = 'Invoice' AND \"Type\" IN ('InvoiceA', 'InvoiceB', 'InvoiceC')) OR (\"Category\" = 'CreditNote' AND \"Type\" IN ('CreditNoteA', 'CreditNoteB', 'CreditNoteC'))");
                    table.CheckConstraint("CK_BillingDocuments_DocumentNumber_Positive", "\"DocumentNumber\" IS NULL OR \"DocumentNumber\" > 0");
                    table.CheckConstraint("CK_BillingDocuments_FiscalAuthorization", "\"Type\" = 'PurchaseReceipt' OR \"Status\" <> 'Authorized' OR (\"PointOfSale\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL AND \"Cae\" IS NOT NULL AND \"CaeExpiresOn\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_BillingDocuments_PointOfSale_Range", "\"PointOfSale\" IS NULL OR (\"PointOfSale\" >= 1 AND \"PointOfSale\" <= 99999)");
                    table.ForeignKey(
                        name: "FK_BillingDocuments_BillingDocuments_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "BillingDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingDocumentItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillingDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingDocumentItems", x => x.Id);
                    table.CheckConstraint("CK_BillingDocumentItems_Amounts_NonNegative", "\"UnitPrice\" >= 0 AND \"DiscountAmount\" >= 0 AND \"NetAmount\" >= 0 AND \"VatAmount\" >= 0 AND \"TotalAmount\" >= 0");
                    table.CheckConstraint("CK_BillingDocumentItems_Quantity_Positive", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_BillingDocumentItems_VatRate_Range", "\"VatRate\" >= 0 AND \"VatRate\" <= 100");
                    table.ForeignKey(
                        name: "FK_BillingDocumentItems_BillingDocuments_BillingDocumentId",
                        column: x => x.BillingDocumentId,
                        principalTable: "BillingDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingDocumentItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocumentItems_BillingDocumentId",
                table: "BillingDocumentItems",
                column: "BillingDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocumentItems_OrderItemId",
                table: "BillingDocumentItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_OrderId_CreatedAtUtc",
                table: "BillingDocuments",
                columns: new[] { "OrderId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_PaymentId",
                table: "BillingDocuments",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_RelatedDocumentId",
                table: "BillingDocuments",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "UX_BillingDocuments_FiscalNumber",
                table: "BillingDocuments",
                columns: new[] { "PointOfSale", "Type", "DocumentNumber" },
                unique: true,
                filter: "\"PointOfSale\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_BillingDocuments_IdempotencyKey",
                table: "BillingDocuments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BillingDocuments_OrderId_SaleCategory",
                table: "BillingDocuments",
                columns: new[] { "OrderId", "Category" },
                unique: true,
                filter: "\"Category\" IN ('Receipt', 'Invoice')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingDocumentItems");

            migrationBuilder.DropTable(
                name: "BillingDocuments");
        }
    }
}

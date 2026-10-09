using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddReusableProductAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductAttributeOptionId",
                table: "ProductVariantAttributes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductAttributeOptionId",
                table: "ProductColorImages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Presentation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductAttributeId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VisualValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductAttributeOptions_ProductAttributes_ProductAttributeId",
                        column: x => x.ProductAttributeId,
                        principalTable: "ProductAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Promote every existing free-form pair into the reusable catalog without losing variants.
            migrationBuilder.Sql("""
                INSERT INTO "ProductAttributes" ("Name", "Presentation", "DisplayOrder", "IsActive")
                SELECT MIN("Name"), CASE WHEN LOWER("Name") = 'color' THEN 'ColorSwatch' ELSE 'Button' END,
                       (ROW_NUMBER() OVER (ORDER BY LOWER("Name")) - 1)::integer, TRUE
                FROM "ProductVariantAttributes"
                GROUP BY LOWER("Name");

                INSERT INTO "ProductAttributeOptions" ("ProductAttributeId", "Value", "VisualValue", "DisplayOrder", "IsActive")
                SELECT a."Id", MIN(v."Value"),
                       CASE WHEN a."Presentation" = 'ColorSwatch' THEN
                           CASE LOWER(TRIM(MIN(v."Value")))
                               WHEN 'negro' THEN '#111111' WHEN 'black' THEN '#111111'
                               WHEN 'blanco' THEN '#FFFFFF' WHEN 'white' THEN '#FFFFFF'
                               WHEN 'rojo' THEN '#D32F2F' WHEN 'red' THEN '#D32F2F'
                               WHEN 'azul' THEN '#2463EB' WHEN 'blue' THEN '#2463EB'
                               WHEN 'verde' THEN '#2E7D32' WHEN 'green' THEN '#2E7D32'
                               WHEN 'gris' THEN '#777777' WHEN 'gray' THEN '#777777' WHEN 'grey' THEN '#777777'
                               WHEN 'amarillo' THEN '#F4C430' WHEN 'yellow' THEN '#F4C430'
                               WHEN 'rosa' THEN '#E78CA8' WHEN 'pink' THEN '#E78CA8'
                               WHEN 'violeta' THEN '#7E57C2' WHEN 'purple' THEN '#7E57C2'
                               WHEN 'marrón' THEN '#795548' WHEN 'marron' THEN '#795548' WHEN 'brown' THEN '#795548'
                               WHEN 'naranja' THEN '#EF7D22' WHEN 'orange' THEN '#EF7D22'
                               ELSE '#6B7280'
                           END
                       ELSE NULL END,
                       (ROW_NUMBER() OVER (PARTITION BY a."Id" ORDER BY LOWER(v."Value")) - 1)::integer, TRUE
                FROM "ProductVariantAttributes" v
                JOIN "ProductAttributes" a ON LOWER(a."Name") = LOWER(v."Name")
                GROUP BY a."Id", LOWER(v."Value");

                UPDATE "ProductVariantAttributes" v SET "ProductAttributeOptionId" = o."Id"
                FROM "ProductAttributeOptions" o JOIN "ProductAttributes" a ON a."Id" = o."ProductAttributeId"
                WHERE LOWER(a."Name") = LOWER(v."Name") AND LOWER(o."Value") = LOWER(v."Value");

                UPDATE "ProductColorImages" i SET "ProductAttributeOptionId" = o."Id"
                FROM "ProductAttributeOptions" o JOIN "ProductAttributes" a ON a."Id" = o."ProductAttributeId"
                WHERE LOWER(a."Name") = 'color' AND LOWER(o."Value") = LOWER(i."Color");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributes_ProductAttributeOptionId",
                table: "ProductVariantAttributes",
                column: "ProductAttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductColorImages_ProductAttributeOptionId",
                table: "ProductColorImages",
                column: "ProductAttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductColorImages_ProductId_ProductAttributeOptionId",
                table: "ProductColorImages",
                columns: new[] { "ProductId", "ProductAttributeOptionId" },
                unique: true,
                filter: "\"ProductAttributeOptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeOptions_ProductAttributeId_Value",
                table: "ProductAttributeOptions",
                columns: new[] { "ProductAttributeId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributes_Name",
                table: "ProductAttributes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductColorImages_ProductAttributeOptions_ProductAttribute~",
                table: "ProductColorImages",
                column: "ProductAttributeOptionId",
                principalTable: "ProductAttributeOptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariantAttributes_ProductAttributeOptions_ProductAtt~",
                table: "ProductVariantAttributes",
                column: "ProductAttributeOptionId",
                principalTable: "ProductAttributeOptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductColorImages_ProductAttributeOptions_ProductAttribute~",
                table: "ProductColorImages");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariantAttributes_ProductAttributeOptions_ProductAtt~",
                table: "ProductVariantAttributes");

            migrationBuilder.DropTable(
                name: "ProductAttributeOptions");

            migrationBuilder.DropTable(
                name: "ProductAttributes");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariantAttributes_ProductAttributeOptionId",
                table: "ProductVariantAttributes");

            migrationBuilder.DropIndex(
                name: "IX_ProductColorImages_ProductAttributeOptionId",
                table: "ProductColorImages");

            migrationBuilder.DropIndex(
                name: "IX_ProductColorImages_ProductId_ProductAttributeOptionId",
                table: "ProductColorImages");

            migrationBuilder.DropColumn(
                name: "ProductAttributeOptionId",
                table: "ProductVariantAttributes");

            migrationBuilder.DropColumn(
                name: "ProductAttributeOptionId",
                table: "ProductColorImages");
        }
    }
}

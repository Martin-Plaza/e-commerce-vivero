using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class BackfillReusableAttributeColorVisuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ProductAttributeOptions" o
                SET "VisualValue" = CASE LOWER(TRIM(o."Value"))
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
                    ELSE '#6B7280' END
                FROM "ProductAttributes" a
                WHERE a."Id" = o."ProductAttributeId"
                  AND a."Presentation" = 'ColorSwatch'
                  AND (o."VisualValue" IS NULL OR BTRIM(o."VisualValue") = '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill: existing visual values are intentionally preserved on downgrade.
        }
    }
}

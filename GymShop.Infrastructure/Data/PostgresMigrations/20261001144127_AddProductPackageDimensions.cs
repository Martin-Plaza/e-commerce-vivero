using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddProductPackageDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PackageHeightCm",
                table: "ProductVariants",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageLengthCm",
                table: "ProductVariants",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageWeightGrams",
                table: "ProductVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageWidthCm",
                table: "ProductVariants",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageHeightCm",
                table: "Products",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageLengthCm",
                table: "Products",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageWeightGrams",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageWidthCm",
                table: "Products",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_PackageDimensions_Positive",
                table: "ProductVariants",
                sql: "(\"PackageLengthCm\" IS NULL OR \"PackageLengthCm\" > 0) AND (\"PackageWidthCm\" IS NULL OR \"PackageWidthCm\" > 0) AND (\"PackageHeightCm\" IS NULL OR \"PackageHeightCm\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_PackageWeight_Positive",
                table: "ProductVariants",
                sql: "\"PackageWeightGrams\" IS NULL OR \"PackageWeightGrams\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_PackageDimensions_Positive",
                table: "Products",
                sql: "(\"PackageLengthCm\" IS NULL OR \"PackageLengthCm\" > 0) AND (\"PackageWidthCm\" IS NULL OR \"PackageWidthCm\" > 0) AND (\"PackageHeightCm\" IS NULL OR \"PackageHeightCm\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_PackageWeight_Positive",
                table: "Products",
                sql: "\"PackageWeightGrams\" IS NULL OR \"PackageWeightGrams\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_PackageDimensions_Positive",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_PackageWeight_Positive",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_PackageDimensions_Positive",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_PackageWeight_Positive",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackageHeightCm",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PackageLengthCm",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PackageWeightGrams",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PackageWidthCm",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PackageHeightCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackageLengthCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackageWeightGrams",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackageWidthCm",
                table: "Products");
        }
    }
}

using System;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    [DbContext(typeof(GymShopDbContext))]
    [Migration("20261008120000_ClearCartAfterCheckout")]
    public partial class ClearCartAfterCheckout : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CartClearedAtUtc",
                table: "CheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CartClearedAtUtc",
                table: "CheckoutSessions");
        }
    }
}

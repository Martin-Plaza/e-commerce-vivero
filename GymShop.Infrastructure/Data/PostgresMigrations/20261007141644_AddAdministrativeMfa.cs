using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymShop.Infrastructure.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MfaEnabledAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MfaLastUsedTimeStep",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecretEncrypted",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_UserId_UsedAtUtc",
                table: "MfaRecoveryCodes",
                columns: new[] { "UserId", "UsedAtUtc" });

            // Existing privileged sessions predate the MFA claim and must not remain valid.
            migrationBuilder.Sql("""
                UPDATE "Users" AS u
                SET "TokenVersion" = "TokenVersion" + 1
                FROM "Roles" AS r
                WHERE u."RoleId" = r."Id" AND r."Name" IN ('Admin', 'SuperAdmin');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropColumn(
                name: "MfaEnabledAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaLastUsedTimeStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaSecretEncrypted",
                table: "Users");
        }
    }
}

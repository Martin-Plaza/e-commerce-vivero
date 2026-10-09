using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Text.RegularExpressions;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Postgres")]
public sealed class ReusableAttributeMigrationTests
{
    [Fact]
    public async Task Migrates_legacy_black_variant_and_product_image_with_visible_swatch()
    {
        var configured = Environment.GetEnvironmentVariable("GYMSHOP_TEST_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(configured), "GYMSHOP_TEST_POSTGRES debe apuntar a PostgreSQL local aislado.");
        var databaseName = $"gymshop_attribute_migration_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false };
        var target = new NpgsqlConnectionStringBuilder(configured) { Database = databaseName, Pooling = false };
        AssertSafeGeneratedDatabaseName(databaseName);
        AssertLocalAdminConnection(admin);

        await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
        var databaseCreated = false;
        try
        {
            await adminConnection.OpenAsync();
            AssertLocalAdminConnection(new NpgsqlConnectionStringBuilder(adminConnection.ConnectionString));
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection))
            {
                await create.ExecuteNonQueryAsync();
            }
            databaseCreated = true;

            var options = new DbContextOptionsBuilder<GymShopDbContext>().UseNpgsql(target.ConnectionString).Options;
            await using var db = new GymShopDbContext(options); var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260925190113_AddProductColorImages");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Products" ("Id", "Name", "Price", "Stock", "ImageUrl", "IsActive") VALUES (9001, 'Remera anterior', 100, 0, '/general.webp', TRUE);
                INSERT INTO "ProductVariants" ("Id", "ProductId", "Sku", "Stock", "IsActive") VALUES (9002, 9001, 'LEG-NEG-M', 3, TRUE);
                INSERT INTO "ProductVariantAttributes" ("ProductVariantId", "Name", "Value") VALUES (9002, 'Color', 'Negro'), (9002, 'Talle', 'M');
                INSERT INTO "ProductColorImages" ("ProductId", "Color", "ImageUrl") VALUES (9001, 'Negro', '/negro.webp');
                """);

            await migrator.MigrateAsync("20260925200341_AddReusableProductAttributes");
            await using var verify = new NpgsqlConnection(target.ConnectionString); await verify.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT a."Name", o."Value", o."VisualValue", i."ImageUrl",
                       v."ProductAttributeOptionId" = o."Id", i."ProductAttributeOptionId" = o."Id"
                FROM "ProductAttributes" a
                JOIN "ProductAttributeOptions" o ON o."ProductAttributeId" = a."Id"
                JOIN "ProductVariantAttributes" v ON v."ProductAttributeOptionId" = o."Id"
                JOIN "ProductColorImages" i ON i."ProductAttributeOptionId" = o."Id"
                WHERE LOWER(a."Name") = 'color' AND LOWER(o."Value") = 'negro';
                """, verify);
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("Color", reader.GetString(0)); Assert.Equal("Negro", reader.GetString(1)); Assert.Equal("#111111", reader.GetString(2)); Assert.Equal("/negro.webp", reader.GetString(3)); Assert.True(reader.GetBoolean(4)); Assert.True(reader.GetBoolean(5));
            }

            // Simula una base donde la migración anterior ya quedó aplicada con colores sin muestra.
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "ProductAttributeOptions"
                SET "VisualValue" = NULL
                WHERE LOWER("Value") = 'negro';
                """);
            await migrator.MigrateAsync("20260925212519_BackfillReusableAttributeColorVisuals");

            await using var correctiveCommand = new NpgsqlCommand("""
                SELECT o."VisualValue", i."ImageUrl", v."ProductVariantId"
                FROM "ProductAttributes" a
                JOIN "ProductAttributeOptions" o ON o."ProductAttributeId" = a."Id"
                JOIN "ProductVariantAttributes" v ON v."ProductAttributeOptionId" = o."Id"
                JOIN "ProductColorImages" i ON i."ProductAttributeOptionId" = o."Id"
                WHERE LOWER(a."Name") = 'color' AND LOWER(o."Value") = 'negro';
                """, verify);
            await using var correctiveReader = await correctiveCommand.ExecuteReaderAsync();
            Assert.True(await correctiveReader.ReadAsync());
            Assert.Equal("#111111", correctiveReader.GetString(0));
            Assert.Equal("/negro.webp", correctiveReader.GetString(1));
            Assert.Equal(9002, correctiveReader.GetInt32(2));
        }
        finally
        {
            if (databaseCreated)
            {
                if (adminConnection.State != System.Data.ConnectionState.Open)
                {
                    await adminConnection.OpenAsync();
                }

                AssertSafeGeneratedDatabaseName(databaseName);
                AssertLocalAdminConnection(new NpgsqlConnectionStringBuilder(adminConnection.ConnectionString));
                await using (var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid()", adminConnection))
                {
                    terminate.Parameters.AddWithValue("databaseName", databaseName);
                    await terminate.ExecuteNonQueryAsync();
                }
                await using (var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\"", adminConnection))
                {
                    await drop.ExecuteNonQueryAsync();
                }
            }
        }
    }

    private static void AssertLocalAdminConnection(NpgsqlConnectionStringBuilder connection)
    {
        Assert.Equal("postgres", connection.Database);
        Assert.Contains(connection.Host, new[] { "localhost", "127.0.0.1", "::1" }, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertSafeGeneratedDatabaseName(string databaseName) =>
        Assert.Matches(new Regex("^gymshop_attribute_migration_[0-9a-f]{32}$", RegexOptions.CultureInvariant), databaseName);

}

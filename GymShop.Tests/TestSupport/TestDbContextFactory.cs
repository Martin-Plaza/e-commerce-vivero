using GymShop.Domain.Entities;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.TestSupport;

internal static class TestDbContextFactory
{
    public static async Task<GymShopDbContext> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<GymShopDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new GymShopDbContext(options);

        db.Roles.AddRange(
            new Role { Id = 1, Name = "User", Description = "Cliente del e-commerce" },
            new Role { Id = 2, Name = "Admin", Description = "Administrador de productos y pedidos" },
            new Role { Id = 3, Name = "SuperAdmin", Description = "Administrador total del sistema" }
        );

        await db.SaveChangesAsync();
        return db;
    }
}

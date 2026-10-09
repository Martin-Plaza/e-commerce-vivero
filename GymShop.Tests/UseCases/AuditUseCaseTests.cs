using GymShop.Application.DTOs.Audit;
using GymShop.Application.UseCases.Audit;
using GymShop.Application.UseCases.Products;
using GymShop.Application.DTOs.Products;
using GymShop.Application.DTOs.Stock;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Data;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymShop.Tests.UseCases;

public class AuditUseCaseTests
{
    [Fact]
    public async Task Query_is_filtered_paginated_and_ordered()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        db.Users.Add(new User
        {
            Id = 2, Email = "ana@test.com", Name = "Ana", LastName = "Pérez", PasswordHash = "not-used", RoleId = 1, IsActive = true
        });
        db.AuditEntries.AddRange(
            Entry("ProductStockChanged", "Product", "1", DateTime.UtcNow.AddMinutes(-2)),
            Entry("ProductStatusChanged", "Product", "1", DateTime.UtcNow.AddMinutes(-1)),
            Entry("UserRoleChanged", "User", "2", DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await new GetAuditEntriesUseCase(db).ExecuteAsync(
            new AuditQueryRequest(Page: 1, PageSize: 1, EntityType: "Product"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalItems);
        Assert.Equal(2, result.Value.TotalPages);
        Assert.Equal("ProductStatusChanged", Assert.Single(result.Value.Items).Action);

        var userResult = await new GetAuditEntriesUseCase(db).ExecuteAsync(
            new AuditQueryRequest(EntityType: "User", EntityId: "2"));
        Assert.Equal("Ana Pérez", Assert.Single(userResult.Value!.Items).EntityDisplayName);
    }

    [Fact]
    public async Task Audit_failure_prevents_sensitive_change_from_being_persisted()
    {
        var database = $"audit-failure-{Guid.NewGuid():N}";
        var normalOptions = new DbContextOptionsBuilder<GymShopDbContext>().UseInMemoryDatabase(database).Options;
        await using (var seed = new GymShopDbContext(normalOptions))
        {
            seed.Products.Add(new Product { Id = 10, Name = "Producto", Price = 10, Stock = 5, IsActive = true });
            await seed.SaveChangesAsync();
        }

        var failingOptions = new DbContextOptionsBuilder<GymShopDbContext>()
            .UseInMemoryDatabase(database).AddInterceptors(new RejectAuditSaveInterceptor()).Options;
        await using (var failing = new GymShopDbContext(failingOptions))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AdjustStockUseCase(failing, new FakeAuditContext(null, "corr-fail"))
                    .ExecuteAsync(10, new ManualStockAdjustmentRequest(4, "Reposición")));
        }

        await using var verification = new GymShopDbContext(normalOptions);
        Assert.Equal(5, (await verification.Products.SingleAsync()).Stock);
        Assert.Empty(verification.AuditEntries);
    }

    private static AuditEntry Entry(string action, string type, string id, DateTime created) => new()
    {
        Action = action, EntityType = type, EntityId = id, CreatedAtUtc = created, CorrelationId = "corr-test"
    };

    private sealed class RejectAuditSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuditEntry>().Any(x => x.State == EntityState.Added) == true)
                throw new InvalidOperationException("Audit persistence failed.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

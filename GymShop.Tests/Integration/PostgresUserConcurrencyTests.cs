using GymShop.Application.Common;
using GymShop.Application.DTOs.Users;
using GymShop.Application.UseCases.Users;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Postgres")]
[Trait("Category", "Concurrency")]
public sealed class PostgresUserConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_changes_can_never_remove_both_active_superadmins(bool deactivate)
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        int firstId; int secondId;
        await using (var seed = database.CreateContext())
        {
            var role = await seed.Roles.SingleAsync(x => x.Name == "SuperAdmin");
            var first = new User { Name = "First", Email = "first-super@test.com", PasswordHash = "unused", RoleId = role.Id, IsActive = true };
            var second = new User { Name = "Second", Email = "second-super@test.com", PasswordHash = "unused", RoleId = role.Id, IsActive = true };
            seed.Users.AddRange(first, second); await seed.SaveChangesAsync(); firstId = first.Id; secondId = second.Id;
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        Task<AppResult> firstTask; Task<AppResult> secondTask;
        if (deactivate)
        {
            firstTask = new UpdateUserStatusUseCase(firstContext, transactionManager: new EfTransactionManager(firstContext)).ExecuteAsync(firstId, new UpdateUserStatusRequest(false), 999);
            secondTask = new UpdateUserStatusUseCase(secondContext, transactionManager: new EfTransactionManager(secondContext)).ExecuteAsync(secondId, new UpdateUserStatusRequest(false), 999);
        }
        else
        {
            firstTask = new UpdateUserRoleUseCase(firstContext, transactionManager: new EfTransactionManager(firstContext)).ExecuteAsync(firstId, new UpdateUserRoleRequest("Admin"), 999);
            secondTask = new UpdateUserRoleUseCase(secondContext, transactionManager: new EfTransactionManager(secondContext)).ExecuteAsync(secondId, new UpdateUserRoleRequest("Admin"), 999);
        }

        var results = await Task.WhenAll(firstTask, secondTask);
        Assert.Single(results, result => result.IsSuccess);
        var conflict = Assert.Single(results, result => !result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, conflict.Error!.Type);
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.Users.CountAsync(x => x.IsActive && x.Role.Name == "SuperAdmin"));
    }
}

using GymShop.Application.Common;
using GymShop.Application.DTOs.Users;
using GymShop.Application.UseCases.Users;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;

namespace GymShop.Tests.UseCases;

public class UserAdminUseCaseTests
{
    [Fact]
    public async Task GetUsers_searches_filters_and_paginates_stably()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var first = await SeedUserAsync(db, "ana.one@test.com", "User"); first.Name = "Ana"; first.CreatedAt = new DateTime(2026, 1, 1);
        var second = await SeedUserAsync(db, "ana.two@test.com", "User"); second.Name = "Ana"; second.CreatedAt = new DateTime(2026, 2, 1); second.IsActive = false;
        await SeedUserAsync(db, "staff@test.com", "Admin"); await db.SaveChangesAsync();
        var useCase = new GetUsersUseCase(db);

        var active = await useCase.ExecuteAsync(new UserFilterRequest(1, 1, "ANA", "User", true));
        var inactive = await useCase.ExecuteAsync(new UserFilterRequest(1, 20, "ana", "user", false));

        Assert.True(active.IsSuccess); Assert.Equal(1, active.Value!.TotalItems); Assert.Equal(first.Id, active.Value.Items[0].Id);
        Assert.True(inactive.IsSuccess); Assert.Equal(second.Id, Assert.Single(inactive.Value!.Items).Id);
    }

    [Fact]
    public async Task Create_normalizes_email_rejects_case_insensitive_duplicate_and_audits()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var actor = await SeedUserAsync(db, "actor@test.com", "SuperAdmin");
        var useCase = new CreateUserUseCase(db, new PasswordHasher(), new FakeAuditContext(actor.Id, "create"));
        var created = await useCase.ExecuteAsync(new CreateUserRequest(" Nuevo ", " New@Test.COM ", "123456", "Admin"));
        var duplicate = await useCase.ExecuteAsync(new CreateUserRequest("Otro", "NEW@test.com", "123456", "User"));
        Assert.True(created.IsSuccess); Assert.Equal("new@test.com", created.Value!.Email); Assert.False(duplicate.IsSuccess); Assert.Equal(AppErrorType.Conflict, duplicate.Error!.Type);
        Assert.Equal("UserCreated", Assert.Single(db.AuditEntries).Action);
    }

    [Fact]
    public async Task Detail_calculates_only_commercially_valid_orders()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var user = await SeedUserAsync(db, "buyer@test.com", "User");
        db.Orders.AddRange(new Order { UserId = user.Id, Total = 100, Status = OrderStatus.Paid, ShippingAddress = "A", CreatedAt = new DateTime(2026, 1, 1) }, new Order { UserId = user.Id, Total = 250, Status = OrderStatus.Delivered, ShippingAddress = "A", CreatedAt = new DateTime(2026, 2, 1) }, new Order { UserId = user.Id, Total = 999, Status = OrderStatus.Canceled, ShippingAddress = "A", CreatedAt = new DateTime(2026, 3, 1) }); await db.SaveChangesAsync();
        var result = await new GetUserByIdUseCase(db).ExecuteAsync(user.Id);
        Assert.True(result.IsSuccess); Assert.Equal(2, result.Value!.OrderCount); Assert.Equal(350, result.Value.TotalPurchased); Assert.Equal(new DateTime(2026, 2, 1), result.Value.LastOrderAt); Assert.Equal(3, result.Value.OrdersTotal); Assert.Equal(3, result.Value.RecentOrders.Count);
    }

    [Fact]
    public async Task Detail_exposes_total_and_only_one_bounded_recent_order_page()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var user = await SeedUserAsync(db, "many@test.com", "User");
        for (var index = 1; index <= 13; index++) db.Orders.Add(new Order { UserId = user.Id, Total = index, Status = OrderStatus.Paid, ShippingAddress = "A", CreatedAt = new DateTime(2026, 1, index) });
        await db.SaveChangesAsync(); var result = await new GetUserByIdUseCase(db).ExecuteAsync(user.Id);
        Assert.True(result.IsSuccess); Assert.Equal(13, result.Value!.OrdersTotal); Assert.Equal(10, result.Value.OrdersPageSize); Assert.Equal(10, result.Value.RecentOrders.Count); Assert.Equal(13, result.Value.RecentOrders[0].Total);
    }

    [Fact]
    public async Task Last_active_superadmin_cannot_be_demoted_or_deactivated()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var user = await SeedUserAsync(db, "last@test.com", "SuperAdmin");
        var demote = await new UpdateUserRoleUseCase(db).ExecuteAsync(user.Id, new UpdateUserRoleRequest("Admin"), user.Id);
        var deactivate = await new UpdateUserStatusUseCase(db).ExecuteAsync(user.Id, new UpdateUserStatusRequest(false), 999);
        Assert.False(demote.IsSuccess); Assert.False(deactivate.IsSuccess); Assert.Equal("SuperAdmin", user.Role.Name); Assert.True(user.IsActive);
    }
    [Fact]
    public async Task UpdateUserStatus_rejects_self_deactivation()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "admin@test.com", "SuperAdmin");

        var useCase = new UpdateUserStatusUseCase(db);
        var result = await useCase.ExecuteAsync(user.Id, new UpdateUserStatusRequest(false), currentUserId: user.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.True(user.IsActive);
        Assert.Empty(db.AuditEntries);
    }

    [Fact]
    public async Task UpdateUserStatus_deactivation_increments_token_version()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "user@test.com", "User");

        var actor = await SeedUserAsync(db, "superadmin-status@test.com", "SuperAdmin");
        var result = await new UpdateUserStatusUseCase(db, new FakeAuditContext(actor.Id, "corr-status"))
            .ExecuteAsync(user.Id, new UpdateUserStatusRequest(false), currentUserId: 999);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsActive);
        Assert.Equal(1, user.TokenVersion);
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal("UserStatusChanged", audit.Action);
        Assert.Equal("corr-status", audit.CorrelationId);
        Assert.Contains("true", audit.OldValue);
        Assert.Contains("false", audit.NewValue);
    }

    [Fact]
    public async Task UpdateUserRole_change_increments_token_version_but_same_role_does_not()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "admin@test.com", "Admin");
        var actor = await SeedUserAsync(db, "superadmin-role@test.com", "SuperAdmin");
        var useCase = new UpdateUserRoleUseCase(db, new FakeAuditContext(actor.Id, "corr-role"));

        var unchanged = await useCase.ExecuteAsync(user.Id, new UpdateUserRoleRequest("Admin"));
        var changed = await useCase.ExecuteAsync(user.Id, new UpdateUserRoleRequest("User"));

        Assert.True(unchanged.IsSuccess);
        Assert.True(changed.IsSuccess);
        Assert.Equal(1, user.TokenVersion);
        Assert.Equal(db.Roles.Single(x => x.Name == "User").Id, user.RoleId);
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal("UserRoleChanged", audit.Action);
        Assert.Equal(user.Id.ToString(), audit.EntityId);
        Assert.Contains("Admin", audit.OldValue);
        Assert.Contains("User", audit.NewValue);
        Assert.DoesNotContain("123456", audit.OldValue + audit.NewValue + audit.Reason);
    }

    private static async Task<User> SeedUserAsync(GymShop.Infrastructure.Data.GymShopDbContext db, string email, string roleName)
    {
        var role = db.Roles.Single(x => x.Name == roleName);
        var user = new User
        {
            Email = email,
            Name = "Usuario Test",
            PasswordHash = new PasswordHasher().Hash("123456"),
            RoleId = role.Id,
            Role = role,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}

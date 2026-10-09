using GymShop.Domain.Enums;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymShop.Infrastructure.Services;

public sealed class GuestOrderExpirationWorker(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<GuestOrderExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExpireAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await ExpireAsync(stoppingToken);
    }

    private async Task ExpireAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
            var now = time.GetUtcNow().UtcDateTime;
            var orders = await db.Orders.Include(x => x.Payments)
                .Where(x => x.UserId == null && x.Status == OrderStatus.Pending && x.ExpiresAtUtc <= now)
                .ToListAsync(cancellationToken);
            foreach (var order in orders)
            {
                order.Status = OrderStatus.Canceled;
                order.CancellationReason = "Pedido de transferencia vencido.";
                order.UpdatedAt = now;
                foreach (var payment in order.Payments.Where(x => x.Status is PaymentStatus.Creating or PaymentStatus.Pending))
                {
                    payment.Status = PaymentStatus.Expired;
                    payment.FailureReason = order.CancellationReason;
                    payment.UpdatedAt = now;
                }
            }
            if (orders.Count > 0) await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "No se pudieron vencer las órdenes de transferencia de invitados."); }
    }
}

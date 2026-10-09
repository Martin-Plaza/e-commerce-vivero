using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Data;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GymShop.Tests.Infrastructure;

public sealed class BillingDocumentPersistenceTests
{
    [Fact]
    public async Task Fiscal_and_customer_snapshots_do_not_follow_later_order_changes()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = new User
        {
            RoleId = 1,
            Name = "Cliente original",
            Email = "cliente@example.com",
            PasswordHash = "hash",
            IsActive = true
        };
        var product = new Product { Name = "Disco 20 kg", Price = 30000, Stock = 1 };
        var order = new Order
        {
            User = user,
            Status = OrderStatus.Paid,
            DeliveryMethod = DeliveryMethod.HomeDelivery,
            ShippingAddress = "Destino original",
            Subtotal = 30000,
            ShippingCost = 5000,
            Total = 35000
        };
        var orderItem = new OrderItem
        {
            Order = order,
            Product = product,
            ProductName = product.Name,
            Quantity = 1,
            UnitPrice = 30000,
            Subtotal = 30000
        };
        order.Items.Add(orderItem);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var document = new BillingDocument
        {
            OrderId = order.Id,
            IdempotencyKey = "billing-order-1-receipt",
            Category = BillingDocumentCategory.Receipt,
            Type = BillingDocumentType.PurchaseReceipt,
            Status = BillingDocumentStatus.Authorized,
            IssuerBusinessName = "Comercio original",
            IssuerCuit = "30-53625919-4",
            IssuerFiscalAddress = "Domicilio fiscal original",
            IssuerGrossIncomeNumber = "Exento",
            RecipientName = user.Name,
            RecipientEmail = user.Email,
            RecipientAddress = order.ShippingAddress,
            Subtotal = 30000,
            ShippingAmount = 5000,
            Total = 35000,
            Items =
            {
                new BillingDocumentItem
                {
                    OrderItemId = orderItem.Id,
                    Description = orderItem.ProductName,
                    Quantity = 1,
                    UnitPrice = 30000,
                    NetAmount = 30000,
                    TotalAmount = 30000
                }
            }
        };
        db.BillingDocuments.Add(document);
        await db.SaveChangesAsync();

        user.Name = "Cliente modificado";
        order.ShippingAddress = "Destino modificado";
        orderItem.ProductName = "Producto modificado";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var persisted = await db.BillingDocuments.Include(x => x.Items).SingleAsync();
        Assert.Equal("Cliente original", persisted.RecipientName);
        Assert.Equal("Destino original", persisted.RecipientAddress);
        Assert.Equal("Disco 20 kg", Assert.Single(persisted.Items).Description);
        Assert.Equal("Comercio original", persisted.IssuerBusinessName);
    }

    [Fact]
    public async Task Model_has_idempotency_sale_and_fiscal_number_guards()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(BillingDocument));

        Assert.NotNull(entity);
        var indexes = entity!.GetIndexes().ToList();
        Assert.Contains(indexes, index => index.IsUnique && index.GetDatabaseName() == "UX_BillingDocuments_IdempotencyKey");
        Assert.Contains(indexes, index => index.IsUnique && index.GetDatabaseName() == "UX_BillingDocuments_OrderId_SaleCategory");
        Assert.Contains(indexes, index => index.IsUnique && index.GetDatabaseName() == "UX_BillingDocuments_FiscalNumber");
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == "CK_BillingDocuments_Category_Type");

        Assert.Equal(typeof(string), entity.FindProperty(nameof(BillingDocument.Type))!.GetProviderClrType());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(BillingDocument.Status))!.GetProviderClrType());
    }
}

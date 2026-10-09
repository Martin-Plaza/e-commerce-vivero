using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Infrastructure.Data;

public class GymShopDbContext : DbContext, IApplicationDbContext
{
    public GymShopDbContext(DbContextOptions<GymShopDbContext> options) : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
    public DbSet<ProductColorImage> ProductColorImages => Set<ProductColorImage>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeOption> ProductAttributeOptions => Set<ProductAttributeOption>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();
    public DbSet<CheckoutItem> CheckoutItems => Set<CheckoutItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<WebhookReceipt> WebhookReceipts => Set<WebhookReceipt>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<ShippingQuoteReservation> ShippingQuoteReservations => Set<ShippingQuoteReservation>();
    public DbSet<BillingDocument> BillingDocuments => Set<BillingDocument>();
    public DbSet<BillingDocumentItem> BillingDocumentItems => Set<BillingDocumentItem>();
    public DbSet<NotificationOutboxMessage> NotificationOutboxMessages => Set<NotificationOutboxMessage>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        QueueTransactionalNotifications();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("AuditEntries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OldValue).HasMaxLength(2000);
            entity.Property(x => x.NewValue).HasMaxLength(2000);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc });
            entity.HasIndex(x => x.CorrelationId);
            entity.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificationOutboxMessage>(entity =>
        {
            entity.ToTable("NotificationOutboxMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.DeduplicationKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LastFailureType).HasMaxLength(50);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.DeduplicationKey).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BillingDocument).WithMany().HasForeignKey(x => x.BillingDocumentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("StockMovements", table =>
            {
                table.HasCheckConstraint("CK_StockMovements_Quantity_NotZero", "\"Quantity\" <> 0");
                table.HasCheckConstraint("CK_StockMovements_Stocks_NonNegative", "\"PreviousStock\" >= 0 AND \"ResultingStock\" >= 0");
                table.HasCheckConstraint("CK_StockMovements_StockBalance", "\"ResultingStock\" = \"PreviousStock\" + \"Quantity\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.CreatedAtUtc, x.Id });
            entity.HasIndex(x => new { x.ProductId, x.CreatedAtUtc, x.Id });
            entity.HasIndex(x => new { x.OrderId, x.ProductId, x.Type })
                .IsUnique()
                .HasDatabaseName("UX_StockMovements_Order_Product_Simple_Type")
                .HasFilter("\"OrderId\" IS NOT NULL AND \"ProductVariantId\" IS NULL");
            entity.HasIndex(x => new { x.OrderId, x.ProductId, x.ProductVariantId, x.Type })
                .IsUnique()
                .HasDatabaseName("UX_StockMovements_Order_Product_Variant_Type")
                .HasFilter("\"OrderId\" IS NOT NULL AND \"ProductVariantId\" IS NOT NULL");
            entity.HasOne(x => x.Product).WithMany(x => x.StockMovements).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProductVariant).WithMany(x => x.StockMovements).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(200);
            entity.HasIndex(x => x.Name).IsUnique();

            entity.HasData(
                new Role { Id = 1, Name = "User", Description = "Cliente del e-commerce" },
                new Role { Id = 2, Name = "Admin", Description = "Administrador de productos y pedidos" },
                new Role { Id = 3, Name = "SuperAdmin", Description = "Administrador total del sistema" }
            );
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Phone).HasMaxLength(50);
            entity.Property(x => x.Address).HasMaxLength(300);
            entity.Property(x => x.TokenVersion).HasDefaultValue(0);
            entity.Property(x => x.MfaSecretEncrypted).HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.Email).IsUnique();

            entity
                .HasOne(x => x.Role)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookReceipt>(entity =>
        {
            entity.ToTable("WebhookReceipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            entity.Property(x => x.RequestIdHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.Provider, x.RequestIdHash }).IsUnique();
            entity.HasIndex(x => x.ProcessedAtUtc);
        });

        modelBuilder.Entity<MfaRecoveryCode>(entity =>
        {
            entity.ToTable("MfaRecoveryCodes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.UserId, x.UsedAtUtc });
            entity.HasOne(x => x.User).WithMany(x => x.MfaRecoveryCodes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailVerificationCode>(entity =>
        {
            entity.ToTable("EmailVerificationCodes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            entity.HasOne(x => x.User).WithMany(x => x.EmailVerificationCodes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetCode>(entity =>
        {
            entity.ToTable("PasswordResetCodes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            entity.HasOne(x => x.User).WithMany(x => x.PasswordResetCodes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserExternalLogin>(entity =>
        {
            entity.ToTable("UserExternalLogins");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ProviderSubject).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.Provider, x.ProviderSubject }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.ExternalLogins).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Color).HasMaxLength(7);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.DisplayOrder });
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", table =>
            {
                table.HasCheckConstraint("CK_Products_Price_Positive", "\"Price\" > 0");
                table.HasCheckConstraint("CK_Products_Stock_NonNegative", "\"Stock\" >= 0");
                table.HasCheckConstraint("CK_Products_PackageWeight_Positive", "\"PackageWeightGrams\" IS NULL OR \"PackageWeightGrams\" > 0");
                table.HasCheckConstraint("CK_Products_PackageDimensions_Positive", "(\"PackageLengthCm\" IS NULL OR \"PackageLengthCm\" > 0) AND (\"PackageWidthCm\" IS NULL OR \"PackageWidthCm\" > 0) AND (\"PackageHeightCm\" IS NULL OR \"PackageHeightCm\" > 0)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.PackageLengthCm).HasPrecision(8, 2);
            entity.Property(x => x.PackageWidthCm).HasPrecision(8, 2);
            entity.Property(x => x.PackageHeightCm).HasPrecision(8, 2);
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.CategoryId);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.Products)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariants", table =>
            {
                table.HasCheckConstraint("CK_ProductVariants_Stock_NonNegative", "\"Stock\" >= 0");
                table.HasCheckConstraint("CK_ProductVariants_PackageWeight_Positive", "\"PackageWeightGrams\" IS NULL OR \"PackageWeightGrams\" > 0");
                table.HasCheckConstraint("CK_ProductVariants_PackageDimensions_Positive", "(\"PackageLengthCm\" IS NULL OR \"PackageLengthCm\" > 0) AND (\"PackageWidthCm\" IS NULL OR \"PackageWidthCm\" > 0) AND (\"PackageHeightCm\" IS NULL OR \"PackageHeightCm\" > 0)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.PackageLengthCm).HasPrecision(8, 2);
            entity.Property(x => x.PackageWidthCm).HasPrecision(8, 2);
            entity.Property(x => x.PackageHeightCm).HasPrecision(8, 2);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.IsActive });
            entity.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariantAttribute>(entity =>
        {
            entity.ToTable("ProductVariantAttributes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.ProductVariantId, x.Name }).IsUnique();
            entity.HasOne(x => x.ProductVariant).WithMany(x => x.Attributes).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ProductAttributeOption).WithMany(x => x.VariantAttributes).HasForeignKey(x => x.ProductAttributeOptionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductAttribute>(entity =>
        {
            entity.ToTable("ProductAttributes"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Presentation).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<ProductAttributeOption>(entity =>
        {
            entity.ToTable("ProductAttributeOptions"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Value).HasMaxLength(100).IsRequired();
            entity.Property(x => x.VisualValue).HasMaxLength(100);
            entity.HasIndex(x => new { x.ProductAttributeId, x.Value }).IsUnique();
            entity.HasOne(x => x.ProductAttribute).WithMany(x => x.Options).HasForeignKey(x => x.ProductAttributeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductColorImage>(entity =>
        {
            entity.ToTable("ProductColorImages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Color).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.ProductId, x.Color }).IsUnique();
            entity.HasOne(x => x.Product).WithMany(x => x.ColorImages).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ProductAttributeOption).WithMany(x => x.ColorImages).HasForeignKey(x => x.ProductAttributeOptionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProductId, x.ProductAttributeOptionId }).IsUnique().HasFilter("\"ProductAttributeOptionId\" IS NOT NULL");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.ShippingCost).HasPrecision(18, 2);
            entity.Property(x => x.DeliveryMethod).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.CouponCode).HasMaxLength(50);
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(x => x.ShippingAddress).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ShippingPostalCode).HasMaxLength(ValidationLimits.ShippingPostalCode);
            entity.Property(x => x.ShippingProvince).HasMaxLength(ValidationLimits.ShippingProvince);
            entity.Property(x => x.ShippingCity).HasMaxLength(ValidationLimits.ShippingCity);
            entity.Property(x => x.ShippingStreet).HasMaxLength(ValidationLimits.ShippingStreet);
            entity.Property(x => x.ShippingStreetNumber).HasMaxLength(ValidationLimits.ShippingStreetNumber);
            entity.Property(x => x.ShippingFloor).HasMaxLength(ValidationLimits.ShippingFloor);
            entity.Property(x => x.ShippingApartment).HasMaxLength(ValidationLimits.ShippingApartment);
            entity.Property(x => x.ShippingNotes).HasMaxLength(ValidationLimits.ShippingNotes);
            entity.Property(x => x.ShippingProviderCode).HasMaxLength(ValidationLimits.ShippingProviderCode);
            entity.Property(x => x.ShippingServiceCode).HasMaxLength(ValidationLimits.ShippingServiceCode);
            entity.Property(x => x.ShippingServiceName).HasMaxLength(ValidationLimits.ShippingServiceName);
            entity.Property(x => x.PickupAddress).HasMaxLength(ValidationLimits.ShippingAddress).IsRequired();
            entity.Property(x => x.PickupHours).HasMaxLength(ValidationLimits.PickupHours).IsRequired();
            entity.Property(x => x.PickupInstructions).HasMaxLength(ValidationLimits.PickupInstructions).IsRequired();
            entity.Property(x => x.Carrier).HasMaxLength(100);
            entity.Property(x => x.TrackingNumber).HasMaxLength(100);
            entity.Property(x => x.TrackingUrl).HasMaxLength(500);
            entity.Property(x => x.CancellationReason).HasMaxLength(500);
            entity.Property(x => x.CheckoutIdempotencyKey).HasMaxLength(ValidationLimits.IdempotencyKey);
            entity.Property(x => x.CheckoutRequestFingerprint).HasMaxLength(64);
            entity.Property(x => x.GuestFirstName).HasMaxLength(ValidationLimits.UserName);
            entity.Property(x => x.GuestLastName).HasMaxLength(ValidationLimits.UserName);
            entity.Property(x => x.GuestEmail).HasMaxLength(ValidationLimits.Email);
            entity.Property(x => x.GuestPhone).HasMaxLength(ValidationLimits.Phone);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAt).IsConcurrencyToken();

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.GuestAccessToken).IsUnique().HasFilter("\"GuestAccessToken\" IS NOT NULL");
            entity.HasIndex(x => x.ShippingQuoteId).IsUnique().HasFilter("\"ShippingQuoteId\" IS NOT NULL");
            entity.HasIndex(x => new { x.UserId, x.CheckoutIdempotencyKey })
                .IsUnique()
                .HasDatabaseName("UX_Orders_UserId_CheckoutIdempotencyKey")
                .HasFilter("\"CheckoutIdempotencyKey\" IS NOT NULL");
            entity.HasIndex(x => x.CheckoutIdempotencyKey)
                .IsUnique()
                .HasDatabaseName("UX_Orders_Guest_CheckoutIdempotencyKey")
                .HasFilter("\"UserId\" IS NULL AND \"CheckoutIdempotencyKey\" IS NOT NULL");
            entity
                .HasOne(x => x.User)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ShippingQuoteReservation>()
                .WithOne()
                .HasForeignKey<Order>(x => x.ShippingQuoteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingDocument>(entity =>
        {
            entity.ToTable("BillingDocuments", table =>
            {
                table.HasCheckConstraint("CK_BillingDocuments_Amounts_NonNegative", "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"ShippingAmount\" >= 0 AND \"NetTaxedAmount\" >= 0 AND \"NetUntaxedAmount\" >= 0 AND \"ExemptAmount\" >= 0 AND \"VatAmount\" >= 0 AND \"OtherTaxesAmount\" >= 0 AND \"Total\" >= 0");
                table.HasCheckConstraint("CK_BillingDocuments_PointOfSale_Range", "\"PointOfSale\" IS NULL OR (\"PointOfSale\" >= 1 AND \"PointOfSale\" <= 99999)");
                table.HasCheckConstraint("CK_BillingDocuments_DocumentNumber_Positive", "\"DocumentNumber\" IS NULL OR \"DocumentNumber\" > 0");
                table.HasCheckConstraint("CK_BillingDocuments_FiscalAuthorization", "\"Type\" = 'PurchaseReceipt' OR \"Status\" <> 'Authorized' OR (\"PointOfSale\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL AND \"Cae\" IS NOT NULL AND \"CaeExpiresOn\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL)");
                table.HasCheckConstraint("CK_BillingDocuments_Category_Type", "(\"Category\" = 'Receipt' AND \"Type\" = 'PurchaseReceipt') OR (\"Category\" = 'Invoice' AND \"Type\" IN ('InvoiceA', 'InvoiceB', 'InvoiceC')) OR (\"Category\" = 'CreditNote' AND \"Type\" IN ('CreditNoteA', 'CreditNoteB', 'CreditNoteC'))");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(ValidationLimits.IdempotencyKey).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.IssuerBusinessName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IssuerCuit).HasMaxLength(13).IsRequired();
            entity.Property(x => x.IssuerTaxCondition).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.IssuerFiscalAddress).HasMaxLength(300).IsRequired();
            entity.Property(x => x.IssuerGrossIncomeNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RecipientDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.RecipientDocumentNumber).HasMaxLength(30);
            entity.Property(x => x.RecipientTaxCondition).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.RecipientEmail).HasMaxLength(ValidationLimits.Email);
            entity.Property(x => x.RecipientAddress).HasMaxLength(300);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.ShippingAmount).HasPrecision(18, 2);
            entity.Property(x => x.NetTaxedAmount).HasPrecision(18, 2);
            entity.Property(x => x.NetUntaxedAmount).HasPrecision(18, 2);
            entity.Property(x => x.ExemptAmount).HasPrecision(18, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.OtherTaxesAmount).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.AuthorizationProvider).HasMaxLength(50);
            entity.Property(x => x.ProviderRequestId).HasMaxLength(100);
            entity.Property(x => x.Cae).HasMaxLength(20);
            entity.Property(x => x.RejectionCode).HasMaxLength(100);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.UpdatedAtUtc).IsConcurrencyToken();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_BillingDocuments_IdempotencyKey");
            entity.HasIndex(x => new { x.OrderId, x.Category }).IsUnique().HasDatabaseName("UX_BillingDocuments_OrderId_SaleCategory").HasFilter("\"Category\" IN ('Receipt', 'Invoice')");
            entity.HasIndex(x => new { x.PointOfSale, x.Type, x.DocumentNumber }).IsUnique().HasDatabaseName("UX_BillingDocuments_FiscalNumber").HasFilter("\"PointOfSale\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL");
            entity.HasIndex(x => new { x.OrderId, x.CreatedAtUtc });
            entity.HasOne(x => x.Order).WithMany(x => x.BillingDocuments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Payment).WithMany(x => x.BillingDocuments).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RelatedDocument).WithMany(x => x.RelatedDocuments).HasForeignKey(x => x.RelatedDocumentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingDocumentItem>(entity =>
        {
            entity.ToTable("BillingDocumentItems", table =>
            {
                table.HasCheckConstraint("CK_BillingDocumentItems_Quantity_Positive", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_BillingDocumentItems_Amounts_NonNegative", "\"UnitPrice\" >= 0 AND \"DiscountAmount\" >= 0 AND \"NetAmount\" >= 0 AND \"VatAmount\" >= 0 AND \"TotalAmount\" >= 0");
                table.HasCheckConstraint("CK_BillingDocumentItems_VatRate_Range", "\"VatRate\" >= 0 AND \"VatRate\" <= 100");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(300).IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.NetAmount).HasPrecision(18, 2);
            entity.Property(x => x.VatRate).HasPrecision(5, 2);
            entity.Property(x => x.VatAmount).HasPrecision(18, 2);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.HasIndex(x => x.BillingDocumentId);
            entity.HasOne(x => x.BillingDocument).WithMany(x => x.Items).HasForeignKey(x => x.BillingDocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OrderItem).WithMany(x => x.BillingDocumentItems).HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShippingQuoteReservation>(entity =>
        {
            entity.ToTable("ShippingQuoteReservations", table =>
            {
                table.HasCheckConstraint("CK_ShippingQuoteReservations_Price_NonNegative", "\"Price\" >= 0");
                table.HasCheckConstraint("CK_ShippingQuoteReservations_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderCode).HasMaxLength(ValidationLimits.ShippingProviderCode).IsRequired();
            entity.Property(x => x.ServiceCode).HasMaxLength(ValidationLimits.ShippingServiceCode).IsRequired();
            entity.Property(x => x.ServiceName).HasMaxLength(ValidationLimits.ShippingServiceName).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.CartFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PostalCode).HasMaxLength(ValidationLimits.ShippingPostalCode).IsRequired();
            entity.Property(x => x.Province).HasMaxLength(ValidationLimits.ShippingProvince).IsRequired();
            entity.Property(x => x.City).HasMaxLength(ValidationLimits.ShippingCity).IsRequired();
            entity.Property(x => x.Street).HasMaxLength(ValidationLimits.ShippingStreet).IsRequired();
            entity.Property(x => x.StreetNumber).HasMaxLength(ValidationLimits.ShippingStreetNumber).IsRequired();
            entity.Property(x => x.Floor).HasMaxLength(ValidationLimits.ShippingFloor);
            entity.Property(x => x.Apartment).HasMaxLength(ValidationLimits.ShippingApartment);
            entity.Property(x => x.Notes).HasMaxLength(ValidationLimits.ShippingNotes);
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Cart>().WithMany().HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne(x => x.Coupon).WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.SetNull);

            entity
                .HasOne(x => x.User)
                .WithOne(x => x.Cart)
                .HasForeignKey<Cart>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.ToTable("Coupons", table =>
            {
                table.HasCheckConstraint("CK_Coupons_Value_Positive", "\"Value\" > 0");
                table.HasCheckConstraint("CK_Coupons_Percentage_Range", "\"Type\" <> 'Percentage' OR \"Value\" <= 100");
                table.HasCheckConstraint("CK_Coupons_Date_Range", "\"StartsAtUtc\" IS NULL OR \"EndsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Value).HasPrecision(18, 2);
            entity.Property(x => x.MinimumPurchase).HasPrecision(18, 2);
            entity.Property(x => x.MaximumDiscount).HasPrecision(18, 2);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.StartsAtUtc, x.EndsAtUtc });
        });

        modelBuilder.Entity<CouponRedemption>(entity =>
        {
            entity.ToTable("CouponRedemptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.HasIndex(x => new { x.CouponId, x.Status });
            entity.HasIndex(x => new { x.CouponId, x.UserId, x.Status });
            entity.HasOne(x => x.Coupon).WithMany(x => x.Redemptions).HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Order).WithOne(x => x.CouponRedemption).HasForeignKey<CouponRedemption>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems", table =>
            {
                table.HasCheckConstraint("CK_CartItems_Quantity_Positive", "\"Quantity\" > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique().HasFilter("\"ProductVariantId\" IS NULL");
            entity.HasIndex(x => new { x.CartId, x.ProductId, x.ProductVariantId }).IsUnique().HasFilter("\"ProductVariantId\" IS NOT NULL");

            entity
                .HasOne(x => x.Cart)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(x => x.Product)
                .WithMany(x => x.CartItems)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProductVariant).WithMany(x => x.CartItems).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        });


        modelBuilder.Entity<CheckoutSession>(entity =>
        {
            entity.ToTable("CheckoutSessions", table =>
            {
                table.HasCheckConstraint("CK_CheckoutSessions_Amounts_NonNegative", "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"ShippingCost\" >= 0 AND \"Total\" >= 0");
                table.HasCheckConstraint("CK_CheckoutSessions_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(ValidationLimits.IdempotencyKey).IsRequired();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.ShippingCost).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.CouponCode).HasMaxLength(50);
            entity.Property(x => x.DeliveryMethod).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ShippingAddress).HasMaxLength(ValidationLimits.ShippingAddress).IsRequired();
            entity.Property(x => x.ShippingPostalCode).HasMaxLength(ValidationLimits.ShippingPostalCode);
            entity.Property(x => x.ShippingProvince).HasMaxLength(ValidationLimits.ShippingProvince);
            entity.Property(x => x.ShippingCity).HasMaxLength(ValidationLimits.ShippingCity);
            entity.Property(x => x.ShippingStreet).HasMaxLength(ValidationLimits.ShippingStreet);
            entity.Property(x => x.ShippingStreetNumber).HasMaxLength(ValidationLimits.ShippingStreetNumber);
            entity.Property(x => x.ShippingFloor).HasMaxLength(ValidationLimits.ShippingFloor);
            entity.Property(x => x.ShippingApartment).HasMaxLength(ValidationLimits.ShippingApartment);
            entity.Property(x => x.ShippingNotes).HasMaxLength(ValidationLimits.ShippingNotes);
            entity.Property(x => x.ShippingProviderCode).HasMaxLength(ValidationLimits.ShippingProviderCode);
            entity.Property(x => x.ShippingServiceCode).HasMaxLength(ValidationLimits.ShippingServiceCode);
            entity.Property(x => x.ShippingServiceName).HasMaxLength(ValidationLimits.ShippingServiceName);
            entity.Property(x => x.PickupAddress).HasMaxLength(ValidationLimits.ShippingAddress).IsRequired();
            entity.Property(x => x.PickupHours).HasMaxLength(ValidationLimits.PickupHours).IsRequired();
            entity.Property(x => x.PickupInstructions).HasMaxLength(ValidationLimits.PickupInstructions).IsRequired();
            entity.Property(x => x.GuestFirstName).HasMaxLength(ValidationLimits.UserName);
            entity.Property(x => x.GuestLastName).HasMaxLength(ValidationLimits.UserName);
            entity.Property(x => x.GuestEmail).HasMaxLength(ValidationLimits.Email);
            entity.Property(x => x.GuestPhone).HasMaxLength(ValidationLimits.Phone);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique().HasDatabaseName("UX_CheckoutSessions_UserId_IdempotencyKey");
            entity.HasIndex(x => x.GuestAccessToken).IsUnique().HasFilter("\"GuestAccessToken\" IS NOT NULL");
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_CheckoutSessions_Guest_IdempotencyKey").HasFilter("\"UserId\" IS NULL");
            entity.HasIndex(x => x.OrderId).IsUnique().HasFilter("\"OrderId\" IS NOT NULL");
            entity.HasIndex(x => new { x.UserId, x.Status });
            entity.HasOne(x => x.User).WithMany(x => x.CheckoutSessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Cart).WithMany().HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Coupon).WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Order).WithOne().HasForeignKey<CheckoutSession>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TokenVersion).IsRequired();
            entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            entity.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CheckoutItem>(entity =>
        {
            entity.ToTable("CheckoutItems", table =>
            {
                table.HasCheckConstraint("CK_CheckoutItems_Quantity_Positive", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_CheckoutItems_Amounts_NonNegative", "\"UnitPrice\" >= 0 AND \"Subtotal\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.VariantSku).HasMaxLength(100);
            entity.Property(x => x.VariantAttributesJson).HasMaxLength(2000);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.CheckoutSessionId, x.ProductId, x.ProductVariantId }).IsUnique();
            entity.HasOne(x => x.CheckoutSession).WithMany(x => x.Items).HasForeignKey(x => x.CheckoutSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ExternalReference).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProviderPreferenceId).HasMaxLength(100);
            entity.Property(x => x.ProviderPaymentId).HasMaxLength(100);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(x => x.CheckoutUrl).HasMaxLength(500);
            entity.Property(x => x.FailureReason).HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.ExternalReference);
            entity.HasIndex(x => new { x.Provider, x.ProviderPreferenceId }).HasFilter("\"ProviderPreferenceId\" IS NOT NULL");
            entity.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).HasFilter("\"ProviderPaymentId\" IS NOT NULL");
            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("UX_Payments_IdempotencyKey")
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
            entity.HasIndex(x => x.OrderId)
                .IsUnique()
                .HasDatabaseName("UX_Payments_OrderId_Active")
                .HasFilter("\"OrderId\" IS NOT NULL AND \"Status\" IN ('Creating', 'Pending')");
            entity.HasIndex(x => x.CheckoutSessionId)
                .IsUnique()
                .HasDatabaseName("UX_Payments_CheckoutSessionId_Active")
                .HasFilter("\"CheckoutSessionId\" IS NOT NULL AND \"Status\" IN ('Creating', 'Pending')");

            entity
                .HasOne(x => x.Order)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CheckoutSession)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.CheckoutSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems", table =>
            {
                table.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "\"Quantity\" > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.VariantSku).HasMaxLength(100);
            entity.Property(x => x.VariantAttributesJson).HasColumnType("jsonb");
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);

            entity
                .HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(x => x.Product)
                .WithMany(x => x.OrderItems)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ProductVariant).WithMany(x => x.OrderItems).HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void QueueTransactionalNotifications()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Order>().ToList())
        {
            if (entry.State == EntityState.Added)
                Queue(TransactionalNotificationType.OrderCreated, entry.Entity,
                    $"order-created:{entry.Entity.UserId}:{entry.Entity.CheckoutIdempotencyKey ?? entry.Entity.GetHashCode().ToString()}", now);
            if (entry.State != EntityState.Modified || !entry.Property(x => x.Status).IsModified) continue;
            var type = entry.Entity.Status switch
            {
                OrderStatus.Preparing => TransactionalNotificationType.OrderPreparing,
                OrderStatus.Shipped when entry.Entity.DeliveryMethod == DeliveryMethod.StorePickup => TransactionalNotificationType.OrderReadyForPickup,
                OrderStatus.Shipped => TransactionalNotificationType.OrderShipped,
                OrderStatus.Canceled when entry.Entity.CancellationReason?.Contains("vencid", StringComparison.OrdinalIgnoreCase) == true => TransactionalNotificationType.OrderExpired,
                OrderStatus.Canceled when entry.Entity.CancellationReason?.Contains("stock", StringComparison.OrdinalIgnoreCase) == true => TransactionalNotificationType.StockUnavailableAfterPayment,
                _ => (TransactionalNotificationType?)null
            };
            if (type is not null) Queue(type.Value, entry.Entity, $"order:{entry.Entity.Id}:{type}", now);
        }

        foreach (var entry in ChangeTracker.Entries<Payment>().ToList())
        {
            if (entry.State != EntityState.Modified || !entry.Property(x => x.Status).IsModified || entry.Entity.Order is null) continue;
            var type = entry.Entity.Status switch
            {
                PaymentStatus.Approved when entry.Entity.Order.Status != OrderStatus.Canceled => TransactionalNotificationType.PaymentApproved,
                PaymentStatus.Rejected => TransactionalNotificationType.PaymentRejected,
                PaymentStatus.Refunded => TransactionalNotificationType.PaymentRefunded,
                _ => (TransactionalNotificationType?)null
            };
            if (type is not null) Queue(type.Value, entry.Entity.Order, $"payment:{entry.Entity.Id}:{type}", now,
                entry.Entity, orderId: entry.Entity.OrderId);
        }

        foreach (var entry in ChangeTracker.Entries<BillingDocument>().ToList())
        {
            var authorized = entry.Entity.Status == BillingDocumentStatus.Authorized &&
                (entry.State == EntityState.Added || entry.State == EntityState.Modified && entry.Property(x => x.Status).IsModified);
            if (authorized) Queue(TransactionalNotificationType.BillingDocumentAvailable, entry.Entity.Order,
                $"billing:{entry.Entity.Id}:authorized", now, billingDocument: entry.Entity, orderId: entry.Entity.OrderId);
        }
    }

    private void Queue(TransactionalNotificationType type, Order? order, string key, DateTime now,
        Payment? payment = null, BillingDocument? billingDocument = null, int? orderId = null)
    {
        if (ChangeTracker.Entries<NotificationOutboxMessage>().Any(x => x.Entity.DeduplicationKey == key)) return;
        NotificationOutboxMessages.Add(new NotificationOutboxMessage
        {
            Type = type, DeduplicationKey = key, Order = order, OrderId = order?.Id > 0 ? order.Id : orderId,
            Payment = payment, PaymentId = payment?.Id > 0 ? payment.Id : null,
            BillingDocument = billingDocument, BillingDocumentId = billingDocument?.Id,
            CreatedAtUtc = now, NextAttemptAtUtc = now
        });
    }
}

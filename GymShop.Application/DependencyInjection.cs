using GymShop.Application.Abstractions;
using GymShop.Application.UseCases.Auth;
using GymShop.Application.UseCases.Audit;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Categories;
using GymShop.Application.UseCases.Dashboard;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Payments;
using GymShop.Application.UseCases.Products;
using GymShop.Application.UseCases.Stock;
using GymShop.Application.UseCases.Users;
using GymShop.Application.UseCases.Coupons;
using GymShop.Application.UseCases.Attributes;
using GymShop.Application.UseCases.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace GymShop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStoreTimeZone>(new StoreTimeZone(null));
        services.AddScoped<IRegisterUserUseCase, RegisterUserUseCase>();
        services.AddScoped<IVerifyEmailUseCase, VerifyEmailUseCase>();
        services.AddScoped<IResendVerificationUseCase, ResendVerificationUseCase>();
        services.AddScoped<IGoogleLoginUseCase, GoogleLoginUseCase>();
        services.AddScoped<ILoginUserUseCase, LoginUserUseCase>();
        services.AddScoped<IRequestPasswordResetUseCase, RequestPasswordResetUseCase>();
        services.AddScoped<IConfirmPasswordResetUseCase, ConfirmPasswordResetUseCase>();
        services.AddScoped<IGetCurrentUserUseCase, GetCurrentUserUseCase>();
        services.AddScoped<IGetAuditEntriesUseCase, GetAuditEntriesUseCase>();
        services.AddScoped<IGetDashboardStatisticsUseCase, GetDashboardStatisticsUseCase>();
        services.AddScoped<ICreateOrderReceiptUseCase, CreateOrderReceiptUseCase>();
        services.AddScoped<IGetOrderBillingDocumentsUseCase, GetOrderBillingDocumentsUseCase>();
        services.AddScoped<IGetBillingDocumentPdfUseCase, GetBillingDocumentPdfUseCase>();
        services.AddScoped<IGetCustomerOrderBillingDocumentsUseCase, GetCustomerOrderBillingDocumentsUseCase>();
        services.AddScoped<IGetCustomerBillingDocumentPdfUseCase, GetCustomerBillingDocumentPdfUseCase>();
        services.AddScoped<IGetArcaConnectionStatusUseCase, GetArcaConnectionStatusUseCase>();
        services.AddScoped<ICreateArcaHomologationInvoiceUseCase, CreateArcaHomologationInvoiceUseCase>();
        services.AddScoped<ICreateArcaHomologationCreditNoteUseCase, CreateArcaHomologationCreditNoteUseCase>();

        services.AddScoped<IGetProductsUseCase, GetProductsUseCase>();
        services.AddScoped<IAttributeAdminService, AttributeAdminService>();
        services.AddScoped<IGetCategoriesUseCase, GetCategoriesUseCase>();
        services.AddScoped<IGetAdminCategoriesUseCase, GetAdminCategoriesUseCase>();
        services.AddScoped<IGetAdminCategoryByIdUseCase, GetAdminCategoryByIdUseCase>();
        services.AddScoped<ICreateCategoryUseCase, CreateCategoryUseCase>();
        services.AddScoped<IUpdateCategoryUseCase, UpdateCategoryUseCase>();
        services.AddScoped<IUpdateCategoryStatusUseCase, UpdateCategoryStatusUseCase>();
        services.AddScoped<IGetProductByIdUseCase, GetProductByIdUseCase>();
        services.AddScoped<ICreateProductUseCase, CreateProductUseCase>();
        services.AddScoped<IUpdateProductUseCase, UpdateProductUseCase>();
        services.AddScoped<IUpdateProductStatusUseCase, UpdateProductStatusUseCase>();
        services.AddScoped<IGetStockMovementsUseCase, GetStockMovementsUseCase>();
        services.AddScoped<IAdjustStockUseCase, AdjustStockUseCase>();

        services.AddScoped<IGetMyOrdersUseCase, GetMyOrdersUseCase>();
        services.AddScoped<IGetOrderByIdUseCase, GetOrderByIdUseCase>();
        services.AddScoped<IGetOrdersUseCase, GetOrdersUseCase>();
        services.AddScoped<IGetOrderHistoryUseCase, GetOrderHistoryUseCase>();
        services.AddScoped<IUpdateOrderStatusUseCase, UpdateOrderStatusUseCase>();
        services.AddScoped<ICancelOrderUseCase, CancelOrderUseCase>();
        services.AddScoped<IExpirePendingOrdersUseCase, ExpirePendingOrdersUseCase>();

        services.AddScoped<ICreatePaymentUseCase, CreatePaymentUseCase>();
        services.AddScoped<IGetPaymentByIdUseCase, GetPaymentByIdUseCase>();
        services.AddScoped<IGetOrderPaymentsUseCase, GetOrderPaymentsUseCase>();
        services.AddScoped<IUpdatePaymentStatusUseCase, UpdatePaymentStatusUseCase>();
        services.AddScoped<IHandlePaymentWebhookUseCase, HandlePaymentWebhookUseCase>();
        services.AddScoped<ICreateCheckoutPaymentUseCase, CreateCheckoutPaymentUseCase>();

        services.AddScoped<IGetCartUseCase, GetCartUseCase>();
        services.AddScoped<IAddCartItemUseCase, AddCartItemUseCase>();
        services.AddScoped<IUpdateCartItemUseCase, UpdateCartItemUseCase>();
        services.AddScoped<IRemoveCartItemUseCase, RemoveCartItemUseCase>();
        services.AddScoped<IClearCartUseCase, ClearCartUseCase>();
        services.AddScoped<ICheckoutCartUseCase, CheckoutCartUseCase>();
        services.AddScoped<IGetCheckoutSessionUseCase, GetCheckoutSessionUseCase>();
        services.AddScoped<IGuestCheckoutUseCase, GuestCheckoutUseCase>();
        services.AddScoped<IQuoteCartShippingUseCase, QuoteCartShippingUseCase>();
        services.AddScoped<IApplyCartCouponUseCase, ApplyCartCouponUseCase>();
        services.AddScoped<IRemoveCartCouponUseCase, RemoveCartCouponUseCase>();
        services.AddScoped<IGetCouponsUseCase, GetCouponsUseCase>();
        services.AddScoped<IGetCouponUseCase, GetCouponUseCase>();
        services.AddScoped<ICreateCouponUseCase, CreateCouponUseCase>();
        services.AddScoped<IUpdateCouponUseCase, UpdateCouponUseCase>();
        services.AddScoped<IUpdateCouponStatusUseCase, UpdateCouponStatusUseCase>();

        services.AddScoped<IGetUsersUseCase, GetUsersUseCase>();
        services.AddScoped<IGetUserByIdUseCase, GetUserByIdUseCase>();
        services.AddScoped<ICreateUserUseCase, CreateUserUseCase>();
        services.AddScoped<IUpdateUserRoleUseCase, UpdateUserRoleUseCase>();
        services.AddScoped<IUpdateUserStatusUseCase, UpdateUserStatusUseCase>();

        return services;
    }
}








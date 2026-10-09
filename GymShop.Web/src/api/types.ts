export type Role = 'User' | 'Admin' | 'SuperAdmin'
export type OrderStatus = 'Pending' | 'Paid' | 'Preparing' | 'Shipped' | 'Delivered' | 'Canceled' | 'Refunded'
export type PaymentStatus = 'Creating' | 'Pending' | 'CreationFailed' | 'Approved' | 'Rejected' | 'Canceled' | 'Expired' | 'Refunded'
export type CheckoutStatus = 'AwaitingPayment' | 'Completed' | 'PaymentFailed' | 'StockUnavailable' | 'Refunded'

export interface User { id: number; email: string; name: string; lastName?: string | null; role: Role }
export interface RegistrationPending { email: string; expiresInSeconds: number; developmentCode?: string | null }
export interface PasswordResetPending { message: string; expiresInSeconds: number; developmentCode?: string | null }
export interface PasswordResetCompleted { message: string }
export interface AdminUser extends User { isActive: boolean; createdAt: string }
export interface AdminUserPage { items: AdminUser[]; page: number; pageSize: number; totalItems: number; totalPages: number }
export interface AdminUserFilters { page?: number; pageSize?: number; search?: string; role?: Role; isActive?: boolean }
export interface UserOrderSummary { id: number; createdAt: string; total: number; status: OrderStatus; deliveryMethod?: DeliveryMethod }
export interface AdminUserDetail extends AdminUser { orderCount: number; totalPurchased: number; lastOrderAt: string | null; ordersTotal: number; ordersPageSize: number; recentOrders: UserOrderSummary[] }
export interface AuthResponse { user: User; mfaRequired?: boolean; setupRequired?: boolean }
export interface MfaSetup { sharedKey: string; otpAuthUri: string; qrCodeRows: string[] }
export interface MfaCompleted { user: User; recoveryCodes: string[] }
export interface CategorySummary { id: number; name: string; slug: string }
export interface Category extends CategorySummary { description: string | null; displayOrder: number; color: string | null }
export interface AdminCategory extends Category { isActive: boolean; productCount: number }
export interface CategoryInput { name: string; slug: string; description: string | null; displayOrder: number; color: string | null }
export type AttributePresentation = 'Button' | 'ColorSwatch'
export interface AttributeOption { id: number; value: string; visualValue: string | null; displayOrder: number; isActive?: boolean; usageCount?: number }
export interface ProductAttributeDefinition { id: number; name: string; presentation: AttributePresentation; displayOrder: number; isActive?: boolean; options: AttributeOption[] }
export interface PackageDimensions { packageWeightGrams?: number | null; packageLengthCm?: number | null; packageWidthCm?: number | null; packageHeightCm?: number | null }
export interface ProductVariant extends PackageDimensions { id?: number; sku: string; price: number | null; stock: number; isActive: boolean; attributes: Record<string, string>; optionIds?: number[]; hasCompletePackageDimensions?: boolean }
export interface Product extends PackageDimensions { id: number; name: string; description: string | null; price: number; stock: number; imageUrl: string | null; isActive: boolean; category: CategorySummary | null; variants?: ProductVariant[]; colorImages?: Record<string, string>; productAttributes?: ProductAttributeDefinition[]; hasCompletePackageDimensions?: boolean }
export interface CreateProductInput extends PackageDimensions { name: string; description: string | null; price: number; stock: number; imageUrl: string | null; categoryId: number | null; variants: ProductVariant[]; colorImages: Record<string, string> }
export interface UpdateProductInput extends PackageDimensions { name: string; description: string | null; price: number; imageUrl: string | null; isActive: boolean; categoryId: number | null; variants: ProductVariant[]; colorImages: Record<string, string> }
export type StockMovementType = 'InitialStock' | 'Sale' | 'CancellationReturn' | 'ManualEntry' | 'ManualCorrection' | 'LossDamage'
export interface StockMovement { id: number; productId: number; productName: string; type: StockMovementType; quantity: number; previousStock: number; resultingStock: number; reason: string; actorUserId: number | null; actorName: string | null; orderId: number | null; createdAtUtc: string; productVariantId?: number | null; variantSku?: string | null }
export interface StockMovementPage { items: StockMovement[]; page: number; pageSize: number; totalItems: number; totalPages: number }
export interface StockAdjustment { productId: number; previousStock: number; resultingStock: number; movement: StockMovement }
export interface ProductImageUpload { url: string; key: string }
export interface CartItem { productId: number; productName: string; unitPrice: number; quantity: number; subtotal: number; stock: number; imageUrl: string | null; productVariantId?: number | null; variantSku?: string | null; variantAttributes?: Record<string, string> | null }
export interface Cart { id: number; userId: number; subtotal: number; discount: number; total: number; couponCode: string | null; items: CartItem[] }
export interface OrderItem { productId: number; productName: string; unitPrice: number; quantity: number; subtotal: number; productVariantId?: number | null; variantSku?: string | null; variantAttributes?: Record<string, string> | null }
export interface OrderPayment { id: number; provider: string; externalReference: string; amount: number; currency: string; status: PaymentStatus; createdAt: string; paidAt: string | null; failureReason?: string | null; requiresReview?: boolean }
export type DeliveryMethod = 'StorePickup' | 'HomeDelivery'
export interface ShippingOptions { homeDeliveryCost: number; pickupAddress: string; pickupInstructions: string; pickupHours: string }
export interface ShippingAddressInput { postalCode: string; province: string; city: string; street: string; streetNumber: string; floor?: string | null; apartment?: string | null; notes?: string | null }
export interface ShippingQuote { id: string; providerCode: string; serviceCode: string; serviceName: string; price: number; estimatedDeliveryFrom: string; estimatedDeliveryTo: string; expiresAtUtc: string }
export type PaymentProvider = 'BankTransfer' | 'MercadoPago'
export interface BankTransferDetails { bankName: string; accountHolder: string; cbu: string; alias: string; cuit: string }
export interface PaymentMethods { bankTransferAvailable: boolean; bankTransferPendingOrderLifetimeHours?: number; mercadoPagoAvailable: boolean; mercadoPagoUnavailableReason: string | null }
export interface Order { id: number; userId: number | null; userEmail: string | null; userName: string; customerPhone?: string | null; expiresAt?: string | null; createdAt: string; subtotal?: number; couponCode?: string | null; discountAmount?: number; deliveryMethod?: DeliveryMethod; shippingCost?: number; total: number; status: OrderStatus; shippingAddress: string; shippingDestination?: ShippingAddressInput | null; shippingProviderCode?: string | null; shippingServiceCode?: string | null; shippingServiceName?: string | null; pickupAddress?: string; pickupHours?: string; pickupInstructions?: string; carrier?: string | null; trackingNumber?: string | null; trackingUrl?: string | null; cancellationReason: string | null; updatedAt: string | null; items: OrderItem[]; payments: OrderPayment[] }
export interface CheckoutSession { id: number; orderId: number | null; userId: number | null; createdAt: string; expiresAt: string; subtotal: number; couponCode: string | null; discountAmount: number; deliveryMethod: DeliveryMethod; shippingCost: number; total: number; status: CheckoutStatus; shippingAddress: string; shippingDestination?: ShippingAddressInput | null; shippingProviderCode?: string | null; shippingServiceCode?: string | null; shippingServiceName?: string | null; pickupAddress: string; pickupHours: string; pickupInstructions: string; items: OrderItem[]; payment: Payment | null }
export interface GuestCustomerInput { firstName: string; lastName: string; email: string; phone: string }
export interface GuestCartItemInput { productId: number; quantity: number; productVariantId?: number | null }
export interface GuestCheckoutInput { customer: GuestCustomerInput; items: GuestCartItemInput[]; deliveryMethod: DeliveryMethod; shippingDestination: ShippingAddressInput | null; expectedShippingCost: number; expectedSubtotal: number; paymentProvider: PaymentProvider; idempotencyKey: string }
export interface GuestCheckoutResponse { kind: 'Order' | 'Checkout'; accessToken: string; expiresAt: string; order: Order | null; checkout: CheckoutSession | null }
export type CouponType = 'Percentage' | 'FixedAmount'
export interface Coupon { id: number; code: string; name: string; type: CouponType; value: number; minimumPurchase: number | null; maximumDiscount: number | null; startsAtUtc: string | null; endsAtUtc: string | null; totalUsageLimit: number | null; usageLimitPerUser: number | null; isActive: boolean; reservedUses: number; consumedUses: number; createdAtUtc: string; updatedAtUtc: string | null }
export interface CouponPage { items: Coupon[]; page: number; pageSize: number; totalItems: number; totalPages: number }
export interface CouponInput { code: string; name: string; type: CouponType; value: number; minimumPurchase: number | null; maximumDiscount: number | null; startsAtUtc: string | null; endsAtUtc: string | null; totalUsageLimit: number | null; usageLimitPerUser: number | null; isActive: boolean }
export interface OrderSummary { id: number; userId: number | null; userEmail: string | null; userName: string; createdAt: string; total: number; deliveryMethod?: DeliveryMethod; status: OrderStatus; updatedAt: string | null; lastPaymentStatus: PaymentStatus | null; lastPaymentId: number | null }
export interface OrderPage { items: OrderSummary[]; page: number; pageSize: number; totalItems: number; totalPages: number }
export interface OrderFilters { page?: number; pageSize?: number; search?: string; status?: string; fromUtc?: string; toUtc?: string }
export type OrderHistorySource = 'Manual' | 'Automatic' | 'Provider'
export interface OrderHistoryEvent { id: number; action: string; previousStatus: string | null; newStatus: string | null; reason: string | null; createdAtUtc: string; actorUserId: number | null; actorName: string | null; actorEmail: string | null; source: OrderHistorySource }
export interface Payment { id: number; orderId: number | null; checkoutSessionId?: number | null; provider: string; externalReference: string; providerPreferenceId: string | null; providerPaymentId: string | null; idempotencyKey: string | null; amount: number; currency: string; status: PaymentStatus; checkoutUrl: string | null; failureReason: string | null; createdAt: string; updatedAt: string | null; paidAt: string | null }
export interface BillingDocumentItem { id: number; orderItemId: number | null; description: string; quantity: number; unitPrice: number; discountAmount: number; totalAmount: number }
export interface BillingDocument { id: string; orderId: number; paymentId: number | null; relatedDocumentId: string | null; category: 'Receipt' | 'Invoice' | 'CreditNote'; type: string; status: 'Draft' | 'PendingAuthorization' | 'Authorized' | 'Rejected'; currency: string; issuerBusinessName: string; recipientName: string; recipientEmail: string | null; recipientAddress: string | null; subtotal: number; discountAmount: number; shippingAmount: number; total: number; pointOfSale: number | null; documentNumber: number | null; authorizationProvider: string | null; cae: string | null; caeExpiresOn: string | null; rejectionCode: string | null; rejectionReason: string | null; createdAtUtc: string; authorizedAtUtc: string | null; items: BillingDocumentItem[] }
export interface BillingProfile { mode: string; taxCondition: string; businessName: string; cuit: string; fiscalAddress: string; grossIncomeNumber: string; activityStartDate: string | null; pointOfSale: number | null; arcaEnabled: boolean; electronicInvoicingReady: boolean }
export interface ArcaConnectionStatus { environment: string; configurationReady: boolean; wsaaAuthenticated: boolean; wsfeReachable: boolean; pointsOfSale: number[]; errorCode: string | null; message: string | null; checkedAtUtc: string }
export interface AuditPage { items: AuditEntry[]; page: number; pageSize: number; totalItems: number; totalPages: number }
export interface AuditEntry { id: number; actorUserId: number | null; actorName?: string | null; action: string; entityType: string; entityId: string; entityDisplayName?: string | null; reason: string | null; createdAtUtc: string; correlationId: string }
export interface DashboardStatusCount { status: OrderStatus; count: number }
export interface DashboardDailySales { date: string; amount: number; orders: number }
export interface DashboardTopProduct { productId: number; productName: string; quantity: number; amount: number }
export interface DashboardStockProduct { productId: number; productName: string; stock: number; isActive: boolean }
export interface DashboardStatistics {
  fromUtc: string; toUtc: string; timeZoneId: string; totalSales: number; paidOrders: number; averageTicket: number
  ordersByStatus: DashboardStatusCount[]; salesByDay: DashboardDailySales[]; topProducts: DashboardTopProduct[]
  outOfStockProducts: DashboardStockProduct[]; lowStockProducts: DashboardStockProduct[]; lowStockThreshold: number
}
export interface DashboardFilters { period?: '7d' | '30d' | 'month'; from?: string; to?: string }

export interface ApiErrorShape { status: number; message: string; code?: string; traceId?: string; retryAfter?: number; validationErrors?: Record<string, string[]> }

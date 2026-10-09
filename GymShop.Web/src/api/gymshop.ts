import { json, request, requestBlob } from './client'
import type { AdminCategory, AdminUser, AdminUserDetail, AdminUserFilters, AdminUserPage, ArcaConnectionStatus, AuditPage, AuthResponse, BillingDocument, BillingProfile, Cart, Category, CategoryInput, CheckoutSession, Coupon, CouponInput, CouponPage, CreateProductInput, DashboardFilters, DashboardStatistics, DeliveryMethod, GuestCheckoutInput, GuestCheckoutResponse, MfaCompleted, MfaSetup, Order, OrderFilters, OrderHistoryEvent, OrderPage, OrderSummary, PasswordResetCompleted, PasswordResetPending, Payment, Product, ProductAttributeDefinition, ProductImageUpload, RegistrationPending, Role, ShippingAddressInput, ShippingOptions, ShippingQuote, StockAdjustment, StockMovementPage, UpdateProductInput, User } from './types'
import type { BankTransferDetails, PaymentMethods, PaymentProvider } from './types'

export const api = {
  register: (data: { name: string; lastName: string; email: string; password: string }) => request<RegistrationPending>('/api/auth/register', json('POST', data)),
  verifyEmail: (data: { email: string; code: string }) => request<AuthResponse>('/api/auth/verify-email', json('POST', data)),
  resendVerification: (email: string) => request<RegistrationPending>('/api/auth/resend-verification', json('POST', { email })),
  googleLogin: (credential: string) => request<AuthResponse>('/api/auth/google', json('POST', { credential })),
  login: (data: { email: string; password: string }) => request<AuthResponse>('/api/auth/login', json('POST', data)),
  mfaSetup: () => request<MfaSetup>('/api/auth/mfa/setup', json('POST')),
  mfaEnable: (code: string) => request<MfaCompleted>('/api/auth/mfa/enable', json('POST', { code })),
  mfaComplete: (code: string) => request<MfaCompleted>('/api/auth/mfa/complete', json('POST', { code })),
  forgotPassword: (email: string) => request<PasswordResetPending>('/api/auth/forgot-password', json('POST', { email })),
  resetPassword: (data: { email: string; code: string; newPassword: string }) => request<PasswordResetCompleted>('/api/auth/reset-password', json('POST', data)),
  me: () => request<User>('/api/auth/me'),
  logout: () => request<void>('/api/auth/logout', json('POST')),
  products: (includeInactive = false) => request<Product[]>(`/api/products${includeInactive ? '?includeInactive=true' : ''}`),
  categories: () => request<Category[]>('/api/categories'),
  attributes: () => request<ProductAttributeDefinition[]>('/api/attributes'),
  adminAttributes: () => request<ProductAttributeDefinition[]>('/api/attributes/admin'),
  createAttribute: (data: { name: string; presentation: string; displayOrder: number }) => request<ProductAttributeDefinition>('/api/attributes', json('POST', data)),
  updateAttribute: (id: number, data: { name: string; presentation: string; displayOrder: number }) => request<ProductAttributeDefinition>(`/api/attributes/${id}`, json('PUT', data)),
  setAttributeStatus: (id: number, isActive: boolean) => request<void>(`/api/attributes/${id}/status`, json('PATCH', { isActive })),
  addAttributeOption: (id: number, data: { value: string; visualValue: string | null; displayOrder: number }) => request(`/api/attributes/${id}/options`, json('POST', data)),
  updateAttributeOption: (id: number, optionId: number, data: { value: string; visualValue: string | null; displayOrder: number }) => request(`/api/attributes/${id}/options/${optionId}`, json('PUT', data)),
  setAttributeOptionStatus: (id: number, optionId: number, isActive: boolean) => request<void>(`/api/attributes/${id}/options/${optionId}/status`, json('PATCH', { isActive })),
  adminCategories: () => request<AdminCategory[]>('/api/categories/admin'),
  adminCategory: (id: number) => request<AdminCategory>(`/api/categories/${id}`),
  createCategory: (data: CategoryInput) => request<AdminCategory>('/api/categories', json('POST', data)),
  updateCategory: (id: number, data: CategoryInput) => request<AdminCategory>(`/api/categories/${id}`, json('PUT', data)),
  setCategoryStatus: (id: number, isActive: boolean) => request<void>(`/api/categories/${id}/status`, json('PATCH', { isActive })),
  product: (id: number) => request<Product>(`/api/products/${id}`),
  createProduct: (data: CreateProductInput) => request<Product>('/api/products', json('POST', data)),
  updateProduct: (id: number, data: UpdateProductInput) => request<Product>(`/api/products/${id}`, json('PUT', data)),
  uploadProductImage: (file: File, productId?: number) => {
    const body = new FormData(); body.append('file', file); if (productId) body.append('productId', String(productId))
    return request<ProductImageUpload>('/api/products/images', { method: 'POST', body })
  },
  deleteProductImage: (data: { key?: string; url?: string }) => request<void>('/api/products/images', json('DELETE', data)),
  setProductStatus: (id: number, isActive: boolean) => request<void>(`/api/products/${id}/status`, json('PATCH', { isActive })),
  stockMovements: (filters: { productId?: number; page?: number; pageSize?: number; type?: string; fromUtc?: string; toUtc?: string } = {}) => {
    const query = new URLSearchParams(); Object.entries(filters).forEach(([key, value]) => { if (value !== undefined && value !== '') query.set(key, String(value)) })
    return request<StockMovementPage>(`/api/stock/movements?${query}`)
  },
  productStockMovements: (productId: number, page = 1) => request<StockMovementPage>(`/api/stock/products/${productId}/movements?page=${page}&pageSize=20`),
  adjustStock: (productId: number, data: { quantity: number; reason: string; productVariantId?: number | null }) =>
    request<StockAdjustment>(`/api/stock/products/${productId}/adjustments`, json('POST', data)),
  cart: () => request<Cart>('/api/cart'),
  addCartItem: (productId: number, quantity: number, productVariantId?: number | null) => request<Cart>('/api/cart/items', json('POST', { productId, quantity, productVariantId })),
  updateCartItem: (productId: number, quantity: number, productVariantId?: number | null) => request<Cart>(`/api/cart/items/${productId}${productVariantId ? `?productVariantId=${productVariantId}` : ''}`, json('PUT', { quantity })),
  removeCartItem: (productId: number, productVariantId?: number | null) => request<Cart>(`/api/cart/items/${productId}${productVariantId ? `?productVariantId=${productVariantId}` : ''}`, json('DELETE')),
  clearCart: () => request<void>('/api/cart', json('DELETE')),
  applyCoupon: (code: string) => request<Cart>('/api/cart/coupon', json('POST', { code })),
  removeCoupon: () => request<Cart>('/api/cart/coupon', json('DELETE')),
  shippingOptions: () => request<ShippingOptions>('/api/cart/shipping-options'),
  shippingQuotes: (destination: ShippingAddressInput) => request<ShippingQuote[]>('/api/cart/shipping-quotes', json('POST', { destination })),
  checkout: (data: { deliveryMethod: DeliveryMethod; shippingAddress: string | null; expectedShippingCost: number; expectedSubtotal: number; expectedDiscount: number; idempotencyKey: string; shippingQuoteId?: string | null; shippingDestination?: ShippingAddressInput | null; paymentProvider?: PaymentProvider; paymentIdempotencyKey?: string }) => request<CheckoutSession>('/api/cart/checkout', json('POST', data)),
  checkoutSession: (id: number) => request<CheckoutSession>(`/api/cart/checkout/${id}`),
  guestCheckout: (data: GuestCheckoutInput) => request<GuestCheckoutResponse>('/api/guest-checkout', json('POST', data)),
  guestOrder: (id: number, accessToken: string) => request<Order>(`/api/guest-checkout/orders/${id}?accessToken=${encodeURIComponent(accessToken)}`),
  guestCheckoutSession: (id: number, accessToken: string) => request<CheckoutSession>(`/api/guest-checkout/sessions/${id}?accessToken=${encodeURIComponent(accessToken)}`),
  myOrders: () => request<OrderSummary[]>('/api/orders/my'),
  myOrderBillingDocuments: (id: number) => request<BillingDocument[]>(`/api/orders/${id}/billing-documents`),
  myBillingDocumentPdf: (orderId: number, documentId: string) => requestBlob(`/api/orders/${orderId}/billing-documents/${documentId}/pdf`),
  orders: (filters: OrderFilters = {}) => {
    const query = new URLSearchParams()
    Object.entries(filters).forEach(([key, value]) => { if (value !== undefined && value !== '') query.set(key, String(value)) })
    return request<OrderPage>(`/api/orders?${query}`)
  },
  order: (id: number) => request<Order>(`/api/orders/${id}`),
  orderHistory: (id: number) => request<OrderHistoryEvent[]>(`/api/orders/${id}/history`),
  orderBillingDocuments: (id: number) => request<BillingDocument[]>(`/api/admin/billing/orders/${id}/documents`),
  createOrderReceipt: (id: number) => request<BillingDocument>(`/api/admin/billing/orders/${id}/receipts`, { method: 'POST', headers: { 'Idempotency-Key': `receipt-order-${id}` } }),
  billingDocumentPdf: (orderId: number, documentId: string) => requestBlob(`/api/admin/billing/orders/${orderId}/documents/${documentId}/pdf`),
  billingProfile: () => request<BillingProfile>('/api/admin/billing/profile'),
  arcaStatus: () => request<ArcaConnectionStatus>('/api/admin/billing/arca/status'),
  createArcaHomologationInvoice: (orderId: number) => request<BillingDocument>(`/api/admin/billing/arca/homologation/orders/${orderId}/invoice`, { method: 'POST', headers: { 'Idempotency-Key': `arca-homologation-order-${orderId}` } }),
  createArcaHomologationCreditNote: (orderId: number) => request<BillingDocument>(`/api/admin/billing/arca/homologation/orders/${orderId}/credit-note`, { method: 'POST', headers: { 'Idempotency-Key': `arca-homologation-credit-note-order-${orderId}` } }),
  cancelOrder: (id: number, reason?: string) => request<Order>(`/api/orders/${id}/cancel`, json('POST', { reason: reason || null })),
  setOrderStatus: (id: number, status: string, expectedUpdatedAt: string | null, tracking?: { carrier: string; trackingNumber: string; trackingUrl: string }) => request<void>(`/api/orders/${id}/status`, json('PATCH', { status, expectedUpdatedAt, ...tracking })),
  createPayment: (orderId: number, provider: PaymentProvider, idempotencyKey: string) => request<Payment>(`/api/orders/${orderId}/payments`, json('POST', { provider, idempotencyKey })),
  createCheckoutPayment: (checkoutId: number, provider: PaymentProvider, idempotencyKey: string) => request<Payment>(`/api/checkout-sessions/${checkoutId}/payments`, json('POST', { provider, idempotencyKey })),
  bankTransferDetails: () => request<BankTransferDetails>('/api/payments/bank-transfer-details'),
  paymentMethods: () => request<PaymentMethods>('/api/payments/methods'),
  payment: (id: number) => request<Payment>(`/api/payments/${id}`),
  orderPayments: (orderId: number) => request<Payment[]>(`/api/payments/orders/${orderId}`),
  setPaymentStatus: (id: number, status: string, reason?: string) => request<Payment>(`/api/payments/${id}/status`, json('POST', { status, failureReason: reason || null, providerPaymentId: null })),
  users: (filters: AdminUserFilters = {}) => {
    const query = new URLSearchParams()
    Object.entries(filters).forEach(([key, value]) => { if (value !== undefined && value !== '') query.set(key, String(value)) })
    return request<AdminUserPage>(`/api/users?${query}`)
  },
  user: (id: number) => request<AdminUserDetail>(`/api/users/${id}`),
  createUser: (data: { name: string; email: string; password: string; role: Role }) => request<AdminUser>('/api/users', json('POST', data)),
  setUserRole: (id: number, role: Role) => request<void>(`/api/users/${id}/role`, json('PATCH', { role })),
  setUserStatus: (id: number, isActive: boolean) => request<void>(`/api/users/${id}/status`, json('PATCH', { isActive })),
  audit: () => request<AuditPage>('/api/audit?page=1&pageSize=50'),
  coupons: (filters: Record<string, string | number> = {}) => { const query = new URLSearchParams(); Object.entries(filters).forEach(([key, value]) => { if (value !== '') query.set(key, String(value)) }); return request<CouponPage>(`/api/coupons?${query}`) },
  coupon: (id: number) => request<Coupon>(`/api/coupons/${id}`),
  createCoupon: (data: CouponInput) => request<Coupon>('/api/coupons', json('POST', data)),
  updateCoupon: (id: number, data: CouponInput) => request<Coupon>(`/api/coupons/${id}`, json('PUT', data)),
  setCouponStatus: (id: number, isActive: boolean) => request<void>(`/api/coupons/${id}/status`, json('PATCH', { isActive })),
  dashboard: (filters: DashboardFilters = { period: '30d' }) => {
    const query = new URLSearchParams()
    Object.entries(filters).forEach(([key, value]) => { if (value) query.set(key, value) })
    return request<DashboardStatistics>(`/api/admin/dashboard?${query}`)
  },
}

export function checkoutKey(userId: number) {
  const key = `gymshop.checkout-key.${userId}`
  let value = localStorage.getItem(key)
  if (!value) {
    value = crypto.randomUUID()
    localStorage.setItem(key, value)
  }
  return value
}

export function guestCheckoutKey() {
  const key = 'gymshop.guest-checkout-key'
  let value = localStorage.getItem(key)
  if (!value) { value = crypto.randomUUID(); localStorage.setItem(key, value) }
  return value
}

export function clearGuestCheckoutKey() { localStorage.removeItem('gymshop.guest-checkout-key') }

export function saveGuestAccess(kind: 'order' | 'checkout', id: number, token: string) {
  sessionStorage.setItem(`gymshop.guest-access.${kind}.${id}`, token)
}

export function guestAccess(kind: 'order' | 'checkout', id: number) {
  return new URLSearchParams(window.location.search).get('access') || sessionStorage.getItem(`gymshop.guest-access.${kind}.${id}`)
}

export function clearCheckoutKey(userId: number) {
  localStorage.removeItem(`gymshop.checkout-key.${userId}`)
}

export function paymentKey(orderId: number) {
  const key = `gymshop.payment-key.${orderId}`
  let value = localStorage.getItem(key)
  if (!value) {
    value = crypto.randomUUID()
    localStorage.setItem(key, value)
  }
  return value
}

export function rotatePaymentKey(orderId: number) {
  const key = `gymshop.payment-key.${orderId}`
  const value = crypto.randomUUID()
  localStorage.setItem(key, value)
  return value
}

export function savePaymentProvider(orderId: number, provider: PaymentProvider) {
  localStorage.setItem(`gymshop.payment-provider.${orderId}`, provider)
}

export function savedPaymentProvider(orderId: number): PaymentProvider | null {
  const value = localStorage.getItem(`gymshop.payment-provider.${orderId}`)
  return value === 'BankTransfer' || value === 'MercadoPago' ? value : null
}

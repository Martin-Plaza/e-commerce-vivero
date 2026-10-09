import { ApiError } from '../../api/client'
import type { PaymentStatus } from '../../api/types'

export const paymentLabels: Record<PaymentStatus, string> = {
  Creating: 'Creando pago',
  Pending: 'Pendiente',
  CreationFailed: 'No pudo crearse',
  Approved: 'Aprobado',
  Rejected: 'Rechazado',
  Canceled: 'Cancelado',
  Expired: 'Vencido',
  Refunded: 'Reembolsado',
}

export const terminalRetryablePayments: PaymentStatus[] = ['CreationFailed', 'Rejected', 'Canceled', 'Expired']

export function isCheckoutPricingConflict(value: unknown) {
  return value instanceof ApiError
    && value.status === 409
    && value.code === 'checkout_pricing_changed'
}

export function isPendingOrderConflict(value: unknown) {
  return value instanceof ApiError
    && value.status === 409
    && value.code === 'pending_order_exists'
}

export function checkoutErrorMessage(value: unknown) {
  if (!(value instanceof ApiError)) return 'No pudimos comunicarnos con el servidor. Antes de reintentar, revisá tus órdenes para evitar duplicados.'
  const details = [value.message]
  if (value.status === 409) details.push('Revisá si ya tenés una orden pendiente o si cambió el estado del carrito.')
  if (value.retryAfter) details.push(`Podés reintentar en ${value.retryAfter} segundos.`)
  if (value.traceId) details.push(`Referencia: ${value.traceId}.`)
  return details.join(' ')
}

export function paymentExplanation(status: PaymentStatus, hasCheckoutUrl: boolean) {
  if (status === 'Creating') return hasCheckoutUrl ? 'El proveedor está preparando el pago.' : 'El pago se está creando y todavía no tiene un enlace disponible.'
  if (status === 'Pending') return 'Estamos esperando que el proveedor confirme el resultado del pago.'
  if (status === 'CreationFailed') return 'No se pudo crear el pago. La orden sigue pendiente y podés iniciar un nuevo intento.'
  if (status === 'Approved') return 'El pago fue aprobado correctamente.'
  if (status === 'Rejected') return 'El proveedor rechazó el pago. Podés iniciar un nuevo intento mientras la orden siga pendiente.'
  if (status === 'Canceled') return 'Este intento de pago fue cancelado.'
  if (status === 'Expired') return 'Este intento venció antes de completarse.'
  return 'El importe fue reembolsado.'
}

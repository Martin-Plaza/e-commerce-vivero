import type { DeliveryMethod } from '../../api/types'

const baseLabels: Record<string, string> = {
  Pending: 'Pendiente', Paid: 'Pagado', Preparing: 'Preparando', Canceled: 'Cancelado', Refunded: 'Reembolsado',
  Creating: 'Creando pago', CreationFailed: 'Falló la creación', Approved: 'Aprobado', Rejected: 'Rechazado', Expired: 'Vencido',
}

export function orderStatusLabel(status: string, deliveryMethod?: DeliveryMethod, feminine = false) {
  if (deliveryMethod === 'StorePickup') {
    if (status === 'Shipped') return 'Listo para retirar'
    if (status === 'Delivered') return 'Retirado'
  }
  if (status === 'Shipped') return feminine ? 'Enviada' : 'Enviado'
  if (status === 'Delivered') return feminine ? 'Entregada' : 'Entregado'
  const label = baseLabels[status] || status
  if (!feminine) return label
  return ({ Pagado: 'Pagada', Preparando: 'Preparando', Cancelado: 'Cancelada', Reembolsado: 'Reembolsada' } as Record<string, string>)[label] || label
}

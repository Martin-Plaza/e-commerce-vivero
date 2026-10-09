import { useEffect, useState } from 'react'
import { api } from '../../api/gymshop'
import type { AuditEntry } from '../../api/types'
import { storefront } from '../../config/storefront'
import { describeAdminError } from './adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from './adminUi'

const date = (value: string) => new Intl.DateTimeFormat(storefront.market.locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const actionLabels: Record<string, string> = {
  UserCreated: 'Usuario creado', UserRoleChanged: 'Rol de usuario modificado', UserStatusChanged: 'Estado de usuario modificado',
  ProductUpdated: 'Producto actualizado', ProductStatusChanged: 'Estado de producto modificado', ProductStockAdjusted: 'Stock de producto ajustado', ProductImageUploaded: 'Imagen de producto cargada',
  CategoryCreated: 'Categoría creada', CategoryUpdated: 'Categoría actualizada', CategoryStatusChanged: 'Estado de categoría modificado',
  CouponCreated: 'Cupón creado', CouponUpdated: 'Cupón actualizado', CouponStatusChanged: 'Estado de cupón modificado',
  OrderCanceled: 'Pedido cancelado', OrderExpiredAdministratively: 'Pedido vencido administrativamente', OrderStatusChanged: 'Estado de pedido modificado', OrderTrackingUpdated: 'Seguimiento del pedido actualizado', FreeOrderConfirmed: 'Pedido sin cargo confirmado',
  PurchaseReceiptCreated: 'Comprobante de compra creado', PaymentPartialRefundFlagged: 'Pago marcado con reintegro parcial', PaymentPreferenceInvalidationFailed: 'Falló la invalidación de la preferencia de pago', PaymentWebhookUnmatched: 'Notificación de pago sin coincidencia', PaymentApprovedAfterOrderCancellation: 'Pago aprobado después de cancelar el pedido', PaymentRefundedByProvider: 'Pago reintegrado por el proveedor',
  ArcaHomologationInvoiceRequested: 'Factura solicitada a ARCA', ArcaHomologationInvoiceAuthorized: 'Factura autorizada por ARCA', ArcaHomologationInvoiceRejected: 'Factura rechazada por ARCA', ArcaHomologationInvoiceRecovered: 'Factura recuperada de ARCA',
  ArcaHomologationCreditNoteRequested: 'Nota de crédito solicitada a ARCA', ArcaHomologationCreditNoteAuthorized: 'Nota de crédito autorizada por ARCA', ArcaHomologationCreditNoteRejected: 'Nota de crédito rechazada por ARCA', ArcaHomologationCreditNoteRecovered: 'Nota de crédito recuperada de ARCA',
}
const entityLabels: Record<string, string> = { User: 'Usuario', Product: 'Producto', ProductImage: 'Imagen de producto', Category: 'Categoría', Coupon: 'Cupón', Order: 'Pedido', Payment: 'Pago' }

export function AuditAdmin() {
  const [entries, setEntries] = useState<AuditEntry[]>([]); const [loading, setLoading] = useState(true); const [error, setError] = useState('')
  useEffect(() => { let active = true; api.audit().then(page => { if (active) setEntries(page.items) }).catch(value => { if (active) setError(describeAdminError(value)) }).finally(() => { if (active) setLoading(false) }); return () => { active = false } }, [])
  return <section className="admin-page"><div className="admin-page-heading"><div><p className="eyebrow">SUPERADMIN</p><h1>Auditoría</h1><p>Registro de operaciones sensibles del sistema.</p></div></div><AdminFeedback error={error} />
    {loading ? <AdminLoading label="Cargando auditoría…" /> : entries.length === 0 ? <AdminEmpty>No hay eventos de auditoría para mostrar.</AdminEmpty> : <div className="list">{entries.map(entry => <div className="list-row audit-row" key={entry.id}><div><h3>{actionLabels[entry.action] ?? entry.action}</h3><p>{entityLabels[entry.entityType] ?? entry.entityType} #{entry.entityId}{entry.entityDisplayName ? ` · ${entry.entityDisplayName}` : ''} · {date(entry.createdAtUtc)}</p>{entry.actorName && <span className="audit-actor">Realizado por {entry.actorName}{entry.actorUserId ? ` (#${entry.actorUserId})` : ''}</span>}</div><small title="Identificador de seguimiento">Seguimiento: {entry.correlationId}</small></div>)}</div>}
  </section>
}

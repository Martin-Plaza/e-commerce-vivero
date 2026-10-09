import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/gymshop'
import type { BillingDocument, Order, OrderSummary, Payment } from '../../api/types'
import { money, storefront } from '../../config/storefront'
import { describeAdminError } from '../admin/adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from '../admin/adminUi'
import { AdminOrders } from '../admin/AdminOrders'
import { orderStatusLabel } from './orderPresentation'

const date = (value: string) => new Intl.DateTimeFormat(storefront.market.locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const labels: Record<string, string> = { Pending: 'Pendiente', Paid: 'Pagada', Preparing: 'Preparando', Shipped: 'Enviada', Delivered: 'Entregada', Canceled: 'Cancelada', Refunded: 'Reembolsada', Creating: 'Creando pago', CreationFailed: 'Falló la creación', Approved: 'Aprobado', Rejected: 'Rechazado', Expired: 'Vencido' }
const fiscalDocumentLabels: Record<string, string> = { InvoiceA: 'Factura A', InvoiceB: 'Factura B', InvoiceC: 'Factura C', CreditNoteA: 'Nota de crédito A', CreditNoteB: 'Nota de crédito B', CreditNoteC: 'Nota de crédito C' }
const fiscalNumber = (value: number | null, length: number) => value === null ? '—' : String(value).padStart(length, '0')
function Status({ value, deliveryMethod }: { value: string | null; deliveryMethod?: Order['deliveryMethod'] }) { return value ? <span className={`status status-${value.toLowerCase()}`}>{deliveryMethod ? orderStatusLabel(value, deliveryMethod, true) : labels[value] || value}</span> : <span>—</span> }

export function OrdersView({ admin = false }: { admin?: boolean }) {
  return admin ? <AdminOrders /> : <CustomerOrders />
}

function CustomerOrders() {
  const [orders, setOrders] = useState<OrderSummary[]>([]); const [detail, setDetail] = useState<Order | null>(null); const [payments, setPayments] = useState<Payment[]>([])
  const [billingDocuments, setBillingDocuments] = useState<BillingDocument[]>([]); const [pdfAction, setPdfAction] = useState<string | null>(null)
  const [loading, setLoading] = useState(true); const [error, setError] = useState(''); const [success, setSuccess] = useState(''); const [pending, setPending] = useState(false)
  const loadInFlight = useRef(false)
  const load = useCallback(async () => {
    if (loadInFlight.current) return
    loadInFlight.current = true; setLoading(true); setError('')
    try { setOrders(await api.myOrders()) } catch (value) { setError(describeAdminError(value)) } finally { loadInFlight.current = false; setLoading(false) }
  }, [])
  useEffect(() => { void load() }, [load])
  const run = async (action: () => Promise<void>) => { if (pending) return; setPending(true); setError(''); setSuccess(''); try { await action() } catch (value) { setError(describeAdminError(value)) } finally { setPending(false) } }
  const open = (id: number) => void run(async () => { setBillingDocuments([]); const [order, list, documents] = await Promise.all([api.order(id), api.orderPayments(id), api.myOrderBillingDocuments(id)]); setDetail(order); setPayments(list); setBillingDocuments(documents) })
  const getDocumentPdf = async (document: BillingDocument, download: boolean) => {
    if (!detail || pdfAction) return
    const action = `${document.id}:${download ? 'download' : 'view'}`
    const popup = download ? null : window.open('about:blank', '_blank')
    if (!download && !popup) { setError('El navegador bloqueó la nueva pestaña. Habilitá ventanas emergentes o descargá el PDF.'); return }
    if (popup) popup.opener = null
    setPdfAction(action); setError('')
    try {
      const blob = await api.myBillingDocumentPdf(detail.id, document.id)
      const url = URL.createObjectURL(blob)
      if (download) {
        const link = window.document.createElement('a')
        link.href = url
        link.download = document.category === 'Receipt'
          ? `pedido-${detail.id}-comprobante.pdf`
          : `pedido-${detail.id}-${document.category === 'CreditNote' ? 'nota-credito' : 'factura'}-${fiscalNumber(document.pointOfSale, 5)}-${fiscalNumber(document.documentNumber, 8)}.pdf`
        window.document.body.appendChild(link); link.click(); link.remove()
      } else if (popup) popup.location.href = url
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (value) { popup?.close(); setError(describeAdminError(value)) } finally { setPdfAction(null) }
  }
  return <section><div className="section-title"><div><p className="eyebrow">SEGUIMIENTO</p><h1>Mis órdenes</h1></div></div>
    <AdminFeedback error={error} success={success} />
    {loading && orders.length === 0 ? <AdminLoading label="Cargando pedidos…" /> : orders.length === 0 ? <AdminEmpty>No hay pedidos para mostrar.</AdminEmpty> : <div className="list">{orders.map(order => <button disabled={pending} className="order-row" key={order.id} onClick={() => open(order.id)}><b>#{order.id}</b><span>{date(order.createdAt)}</span>{order.userEmail && <span>{order.userEmail}</span>}<strong>{money(order.total)}</strong><span className="order-state"><small>Orden</small><Status value={order.status} deliveryMethod={order.deliveryMethod} /></span><span className="order-state"><small>Pago</small><Status value={order.lastPaymentStatus} /></span></button>)}</div>}
    {detail && <div className="drawer"><button className="close" onClick={() => { setDetail(null); setBillingDocuments([]) }} aria-label="Cerrar detalle">×</button><p className="eyebrow">ORDEN #{detail.id}</p><h2>{money(detail.total)}</h2><Status value={detail.status} deliveryMethod={detail.deliveryMethod} /><section><h3>Entrega</h3><p><strong>{detail.deliveryMethod === 'StorePickup' ? 'Retiro en tienda' : 'Envío a domicilio'}</strong>{detail.shippingAddress && <><br />{detail.shippingAddress}</>}<br />Costo: {detail.shippingCost ? money(detail.shippingCost) : 'Sin costo'}</p>{detail.deliveryMethod === 'StorePickup' && <div className="pickup-details"><strong>{detail.pickupAddress}</strong>{detail.pickupHours && <span>{detail.pickupHours}</span>}{detail.pickupInstructions && <p>{detail.pickupInstructions}</p>}</div>}{detail.deliveryMethod !== 'StorePickup' && detail.carrier && <p>{detail.carrier}{detail.trackingNumber && <> · {detail.trackingNumber}</>}</p>}{detail.deliveryMethod !== 'StorePickup' && detail.trackingUrl && <a className="link-button primary" href={detail.trackingUrl} target="_blank" rel="noopener noreferrer">Seguir mi paquete</a>}</section><div className="list">{detail.items.map(item => <div className="list-row" key={item.productId}><span>{item.quantity} × {item.productName}</span><strong>{money(item.subtotal)}</strong></div>)}</div>
      {detail.status === 'Pending' && <div className="actions"><Link className="primary link-button" to={`/checkout/orden/${detail.id}`}>{storefront.copy.orderPaymentAction}</Link></div>}
      <h3>Pagos</h3>{payments.length === 0 ? <p>Sin pagos.</p> : payments.map(payment => <div className="payment" key={payment.id}><span>#{payment.id} · {payment.provider}</span><Status value={payment.status} />{payment.status === 'Creating' && !payment.checkoutUrl && <small>El pago se está creando. Consultá nuevamente en unos instantes.</small>}{payment.failureReason && <small>{payment.failureReason}</small>}</div>)}
      <section><h3>Comprobantes</h3>{billingDocuments.length === 0 ? <p>Todavía no hay comprobantes disponibles.</p> : billingDocuments.map(document => <article className="payment" key={document.id}><span><strong>{document.category === 'Receipt' ? 'Comprobante interno' : fiscalDocumentLabels[document.type] || document.type}</strong><small>{date(document.authorizedAtUtc || document.createdAtUtc)}</small></span><p>{money(document.total)} {document.currency}</p>{document.category !== 'Receipt' && <><p>Punto de venta {fiscalNumber(document.pointOfSale, 5)} · Número {fiscalNumber(document.documentNumber, 8)}</p>{document.cae && <p><strong>CAE {document.cae}</strong></p>}</>}<div className="actions receipt-actions"><button type="button" disabled={Boolean(pdfAction)} onClick={() => void getDocumentPdf(document, false)}>{pdfAction === `${document.id}:view` ? 'Abriendo…' : 'Ver PDF'}</button><button type="button" disabled={Boolean(pdfAction)} onClick={() => void getDocumentPdf(document, true)}>{pdfAction === `${document.id}:download` ? 'Descargando…' : 'Descargar PDF'}</button></div></article>)}</section>
    </div>}
  </section>
}

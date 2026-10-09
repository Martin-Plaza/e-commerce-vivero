import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/gymshop'
import type { BillingDocument, Order, OrderFilters, OrderHistoryEvent, OrderPage, OrderStatus } from '../../api/types'
import { money, storefront } from '../../config/storefront'
import { describeAdminError } from './adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from './adminUi'
import { orderStatusLabel } from '../orders/orderPresentation'

const initialPage: OrderPage = { items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 }
const statusLabels: Record<string, string> = { Pending: 'Pendiente', PendingAuthorization: 'Pendiente de autorización', Paid: 'Pagado', Preparing: 'Preparando', Shipped: 'Enviado', Delivered: 'Entregado', Canceled: 'Cancelado', Refunded: 'Reembolsado', Creating: 'Creando pago', Approved: 'Aprobado', Authorized: 'Autorizado', Rejected: 'Rechazado', Expired: 'Vencido' }
const nextStatuses: Partial<Record<OrderStatus, OrderStatus[]>> = { Paid: ['Preparing'], Preparing: ['Shipped'], Shipped: ['Delivered'] }
const transitionGuidance: Record<OrderStatus, string> = {
  Pending: 'Podés cancelarlo indicando un motivo. Si existe un pago tardío, quedará señalado para revisión.',
  Paid: 'El pago ya fue confirmado. Podés iniciar la preparación del pedido.',
  Preparing: 'Cuando completes la preparación, marcá el pedido como enviado o listo para retirar.',
  Shipped: 'Confirmá la entrega cuando el cliente haya recibido o retirado el pedido.',
  Delivered: 'El pedido fue entregado y no admite más cambios administrativos.',
  Canceled: 'El pedido fue cancelado y no admite más cambios administrativos.',
  Refunded: 'El reembolso se resolvió desde el flujo de pagos; no admite cambios administrativos.'
}
const actionLabels: Record<string, string> = { OrderStatusChanged: 'Estado del pedido actualizado', OrderTrackingUpdated: 'Seguimiento corregido', OrderCanceled: 'Pedido cancelado', OrderExpiredAdministratively: 'Pedido vencido automáticamente', FreeOrderConfirmed: 'Pedido gratuito confirmado', PurchaseReceiptCreated: 'Comprobante interno generado', ArcaHomologationInvoiceRequested: 'Factura de homologación solicitada', ArcaHomologationInvoiceAuthorized: 'Factura de homologación autorizada', ArcaHomologationInvoiceRecovered: 'Factura recuperada desde ARCA', ArcaHomologationInvoiceRejected: 'Factura de homologación rechazada', ArcaHomologationCreditNoteRequested: 'Nota de crédito de homologación solicitada', ArcaHomologationCreditNoteAuthorized: 'Nota de crédito de homologación autorizada', ArcaHomologationCreditNoteRecovered: 'Nota de crédito recuperada desde ARCA', ArcaHomologationCreditNoteRejected: 'Nota de crédito de homologación rechazada', PaymentResolvedByProvider: 'Pago resuelto por el proveedor', PaymentResolvedManually: 'Pago resuelto manualmente', PaymentRefundedByProvider: 'Reembolso confirmado por el proveedor', PaymentPartialRefundFlagged: 'Reembolso parcial informado por el proveedor', PaymentApprovedAfterOrderCancellation: 'Incidencia: pago aprobado después de cancelar', PaymentWebhookUnmatched: 'Incidencia: webhook sin intento inequívoco' }
const sourceLabels = { Manual: 'Manual', Automatic: 'Automático', Provider: 'Proveedor' }
const formatDate = (value: string) => new Intl.DateTimeFormat(storefront.market.locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const formatDateOnly = (value: string) => {
  const [year, month, day] = value.split('-').map(Number)
  return new Intl.DateTimeFormat(storefront.market.locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, day)))
}
const fiscalDocumentLabels: Record<string, string> = { InvoiceA: 'Factura A', InvoiceB: 'Factura B', InvoiceC: 'Factura C', CreditNoteA: 'Nota de crédito A', CreditNoteB: 'Nota de crédito B', CreditNoteC: 'Nota de crédito C' }
const fiscalNumber = (value: number | null, length: number) => value === null ? '—' : String(value).padStart(length, '0')
const utcStart = (value: string) => value ? new Date(`${value}T00:00:00`).toISOString() : undefined
const utcEnd = (value: string) => value ? new Date(`${value}T23:59:59.999`).toISOString() : undefined
const allowedFilterStatuses = new Set(['Pending', 'Paid', 'Preparing', 'Shipped', 'Delivered', 'Canceled'])
type OrderFilterDraft = { search: string; status: string; from: string; to: string }

function singleValue(params: URLSearchParams, name: string) {
  const values = params.getAll(name)
  return values.length === 1 ? values[0].trim() : ''
}

function validDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  const candidate = new Date(Date.UTC(year, month - 1, day))
  return candidate.getUTCFullYear() === year && candidate.getUTCMonth() === month - 1 && candidate.getUTCDate() === day
}

function normalizeOrderFilterParams(input: URLSearchParams) {
  const search = singleValue(input, 'search')
  const statusValue = singleValue(input, 'status')
  const status = allowedFilterStatuses.has(statusValue) ? statusValue : ''
  const pageValue = singleValue(input, 'page')
  const page = /^\d+$/.test(pageValue) && Number(pageValue) > 0 && Number.isSafeInteger(Number(pageValue)) ? Number(pageValue) : 1
  let from = singleValue(input, 'from'); let to = singleValue(input, 'to')
  if (!validDate(from)) from = ''
  if (!validDate(to)) to = ''
  if (from && to && from > to) { from = ''; to = '' }
  const params = new URLSearchParams()
  if (search) params.set('search', search)
  if (status) params.set('status', status)
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  if (page > 1) params.set('page', String(page))
  const draft: OrderFilterDraft = { search, status, from, to }
  const filters: OrderFilters = { page, pageSize: 20, search: search || undefined, status: status || undefined, fromUtc: from ? utcStart(from) : undefined, toUtc: to ? utcEnd(to) : undefined }
  return { params, draft, filters, key: params.toString() }
}
function Status({ value, deliveryMethod }: { value: string | null; deliveryMethod?: Order['deliveryMethod'] }) { return value ? <span className={`status status-${value.toLowerCase()}`}>{deliveryMethod ? orderStatusLabel(value, deliveryMethod) : statusLabels[value] || value}</span> : <span>—</span> }

export function AdminOrders() {
  const [searchParams, setSearchParams] = useSearchParams()
  const rawKey = searchParams.toString()
  const normalized = useMemo(() => normalizeOrderFilterParams(new URLSearchParams(rawKey)), [rawKey])
  const [page, setPage] = useState(initialPage)
  const [detail, setDetail] = useState<Order | null>(null)
  const [draft, setDraft] = useState<OrderFilterDraft>(() => normalized.draft)
  const [loading, setLoading] = useState(true)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [history, setHistory] = useState<OrderHistoryEvent[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)
  const [historyError, setHistoryError] = useState('')
  const [billingDocuments, setBillingDocuments] = useState<BillingDocument[]>([])
  const [pdfAction, setPdfAction] = useState<string | null>(null)
  const [tracking, setTracking] = useState({ carrier: '', trackingNumber: '', trackingUrl: '' })
  const [transferReference, setTransferReference] = useState('')
  const [cancellationReason, setCancellationReason] = useState('')
  const historyRequest = useRef(0)
  const openOrderId = useRef<number | null>(null)
  const lastLoadedFilterKey = useRef<string | null>(null)

  const load = useCallback(async (next: OrderFilters) => {
    setLoading(true); setError('')
    try { setPage(await api.orders(next)) } catch (value) { setError(describeAdminError(value)) } finally { setLoading(false) }
  }, [])
  useEffect(() => {
    if (rawKey !== normalized.key) setSearchParams(normalized.params, { replace: true })
    setDraft(normalized.draft)
    if (lastLoadedFilterKey.current === normalized.key) return
    lastLoadedFilterKey.current = normalized.key
    void load(normalized.filters)
  }, [load, normalized, rawKey, setSearchParams])

  const apply = () => { const next = new URLSearchParams(); if (draft.search.trim()) next.set('search', draft.search.trim()); if (draft.status) next.set('status', draft.status); if (draft.from) next.set('from', draft.from); if (draft.to) next.set('to', draft.to); setSearchParams(normalizeOrderFilterParams(next).params) }
  const clear = () => setSearchParams(new URLSearchParams())
  const goToPage = (value: number) => { const next = new URLSearchParams(normalized.params); if (value > 1) next.set('page', String(value)); else next.delete('page'); setSearchParams(next) }
  const loadHistory = async (id: number) => {
    const requestId = ++historyRequest.current
    setHistoryLoading(true); setHistoryError('')
    try {
      const events = await api.orderHistory(id)
      if (requestId === historyRequest.current && openOrderId.current === id) setHistory(events)
    } catch (value) {
      if (requestId === historyRequest.current && openOrderId.current === id) setHistoryError(describeAdminError(value))
    } finally {
      if (requestId === historyRequest.current && openOrderId.current === id) setHistoryLoading(false)
    }
  }
  const open = async (id: number) => {
    openOrderId.current = id; historyRequest.current += 1
    setPending(true); setError(''); setSuccess('')
    setHistory([]); setHistoryError(''); setHistoryLoading(false); setBillingDocuments([])
    try { const [order, documents] = await Promise.all([api.order(id), api.orderBillingDocuments(id)]); setDetail(order); setBillingDocuments(documents); setTracking({ carrier: order.carrier || '', trackingNumber: order.trackingNumber || '', trackingUrl: order.trackingUrl || '' }); void loadHistory(id) } catch (value) { setError(describeAdminError(value)) } finally { setPending(false) }
  }
  const changeStatus = async (status: OrderStatus) => {
    if (!detail || pending || !window.confirm(`¿Cambiar el pedido #${detail.id} a “${orderStatusLabel(status, detail.deliveryMethod)}”?`)) return
    setPending(true); setError(''); setSuccess('')
    try {
      await api.setOrderStatus(detail.id, status, detail.updatedAt, status === 'Shipped' ? tracking : undefined)
      const updated = await api.order(detail.id)
      setDetail(updated); setSuccess(`Pedido #${detail.id} actualizado a ${orderStatusLabel(status, detail.deliveryMethod)}.`); await Promise.all([load(normalized.filters), loadHistory(detail.id)])
    } catch (value) { setError(describeAdminError(value)) } finally { setPending(false) }
  }
  const confirmTransfer = async (paymentId: number) => {
    if (!detail || pending) return
    const reference = transferReference.trim()
    if (!reference) { setError('Ingresá la referencia o motivo de la acreditación.'); return }
    if (!window.confirm(`¿Confirmás que verificaste la acreditación bancaria del pedido #${detail.id}? Esta acción marcará el pago y el pedido como pagados.`)) return
    setPending(true); setError(''); setSuccess('')
    try {
      await api.setPaymentStatus(paymentId, 'Approved', reference)
      const updated = await api.order(detail.id)
      setDetail(updated); setTransferReference(''); setSuccess(`Transferencia del pedido #${detail.id} confirmada.`)
      await Promise.all([load(normalized.filters), loadHistory(detail.id)])
    } catch (value) { setError(describeAdminError(value)) } finally { setPending(false) }
  }
  const cancelOrder = async () => {
    if (!detail || pending) return
    const reason = cancellationReason.trim()
    if (!reason) { setError('Ingresá el motivo de cancelación.'); return }
    if (!window.confirm(`¿Cancelar el pedido #${detail.id}? Se repondrá el stock reservado.`)) return
    setPending(true); setError(''); setSuccess('')
    try {
      const updated = await api.cancelOrder(detail.id, reason)
      setDetail(updated); setCancellationReason(''); setSuccess(`Pedido #${detail.id} cancelado.`)
      await Promise.all([load(normalized.filters), loadHistory(detail.id)])
    } catch (value) { setError(describeAdminError(value)) } finally { setPending(false) }
  }
  const createReceipt = async () => {
    if (!detail || pending || !window.confirm(`¿Generar el comprobante interno del pedido #${detail.id}?`)) return
    setPending(true); setError(''); setSuccess('')
    try {
      const document = await api.createOrderReceipt(detail.id)
      setBillingDocuments(current => [document, ...current.filter(item => item.id !== document.id)])
      setSuccess(`Comprobante interno del pedido #${detail.id} generado correctamente.`)
      await loadHistory(detail.id)
    } catch (value) { setError(describeAdminError(value)) } finally { setPending(false) }
  }
  const getDocumentPdf = async (document: BillingDocument, download: boolean) => {
    if (!detail || pdfAction) return
    const action = `${document.id}:${download ? 'download' : 'view'}`
    const popup = download ? null : window.open('about:blank', '_blank')
    if (!download && !popup) { setError('El navegador bloqueó la nueva pestaña. Habilitá ventanas emergentes o descargá el PDF.'); return }
    if (popup) popup.opener = null
    setPdfAction(action); setError('')
    try {
      const blob = await api.billingDocumentPdf(detail.id, document.id)
      const url = URL.createObjectURL(blob)
      if (download) {
        const link = window.document.createElement('a')
        link.href = url
        link.download = document.category !== 'Receipt'
          ? `pedido-${detail.id}-${document.category === 'CreditNote' ? 'nota-credito' : 'factura'}-${fiscalNumber(document.pointOfSale, 5)}-${fiscalNumber(document.documentNumber, 8)}.pdf`
          : `pedido-${detail.id}-comprobante.pdf`
        window.document.body.appendChild(link); link.click(); link.remove()
      } else if (popup) {
        popup.location.href = url
      }
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (value) {
      popup?.close()
      setError(describeAdminError(value))
    } finally { setPdfAction(null) }
  }
  const transitions = detail ? nextStatuses[detail.status] || [] : []
  const canCancelFreeOrder = Boolean(detail && detail.total === 0 &&
    (detail.status === 'Paid' || detail.status === 'Preparing') &&
    detail.payments.length === 0)
  const canCancelOrder = detail?.status === 'Pending' || canCancelFreeOrder
  const canGenerateReceipt = Boolean(detail && ['Paid', 'Preparing', 'Shipped', 'Delivered'].includes(detail.status) && !billingDocuments.some(document => document.category === 'Receipt'))
  const closeDetail = () => { openOrderId.current = null; historyRequest.current += 1; setDetail(null); setBillingDocuments([]); setHistory([]); setHistoryError(''); setHistoryLoading(false) }

  return <section className="admin-page">
    <div className="admin-page-heading"><div><p className="eyebrow">OPERACIONES</p><h1>Pedidos</h1><p>Gestioná el ciclo de los pedidos y consultá sus pagos.</p></div><strong>{page.totalItems} pedidos</strong></div>
    <AdminFeedback error={error} success={success} />
    <form className="admin-filters order-filters" aria-label="Filtros de pedidos" onSubmit={event => { event.preventDefault(); apply() }}>
      <label className="admin-search">Buscar<input type="search" placeholder="Número, email o cliente" value={draft.search} onChange={event => setDraft(current => ({ ...current, search: event.target.value }))} /></label>
      <label>Estado<select value={draft.status} onChange={event => setDraft(current => ({ ...current, status: event.target.value }))}><option value="">Todos</option>{['Pending','Paid','Preparing','Shipped','Delivered','Canceled'].map(value => <option key={value} value={value}>{statusLabels[value]}</option>)}</select></label>
      <label>Desde<input type="date" value={draft.from} onChange={event => setDraft(current => ({ ...current, from: event.target.value }))} /></label>
      <label>Hasta<input type="date" value={draft.to} onChange={event => setDraft(current => ({ ...current, to: event.target.value }))} /></label>
      <div className="order-filter-actions"><button disabled={loading}>Aplicar</button><button type="button" onClick={clear}>Limpiar</button></div>
    </form>
    {loading && page.items.length === 0 ? <AdminLoading label="Cargando pedidos…" /> : error && page.items.length === 0 ? <button onClick={() => void load(normalized.filters)}>Reintentar</button> : page.items.length === 0 ? <AdminEmpty>No hay pedidos que coincidan con los filtros.</AdminEmpty> : <div className="admin-order-list">
      <div className="admin-order-row admin-order-header" aria-hidden="true"><span>Pedido</span><span>Cliente</span><span>Fecha</span><span>Total</span><span>Pedido</span><span>Pago</span></div>
      {page.items.map(order => <button type="button" className="admin-order-row" key={order.id} disabled={pending} onClick={() => void open(order.id)}><b>#{order.id}</b><span data-label="Cliente"><strong>{order.userName}</strong><small>{order.userEmail}</small></span><span data-label="Fecha">{formatDate(order.createdAt)}</span><strong data-label="Total">{money(order.total)}</strong><span data-label="Pedido"><Status value={order.status} deliveryMethod={order.deliveryMethod} /></span><span data-label="Pago"><Status value={order.lastPaymentStatus} /></span></button>)}
    </div>}
    {page.totalPages > 1 && <nav className="admin-pagination" aria-label="Paginación de pedidos"><button disabled={loading || page.page <= 1} onClick={() => goToPage(page.page - 1)}>Anterior</button><span>Página {page.page} de {page.totalPages}</span><button disabled={loading || page.page >= page.totalPages} onClick={() => goToPage(page.page + 1)}>Siguiente</button></nav>}
    {detail && <aside className="drawer admin-order-detail" aria-label={`Detalle del pedido ${detail.id}`} aria-busy={pending}><button className="close" onClick={closeDetail} aria-label="Cerrar detalle">×</button><p className="eyebrow">PEDIDO #{detail.id}</p><h2>{money(detail.total)}</h2><Status value={detail.status} deliveryMethod={detail.deliveryMethod} />
      <section><h3>Cliente</h3><p><strong>{detail.userName}</strong><br />{detail.userEmail}</p></section>
      <section><h3>Entrega</h3><p><strong>{detail.deliveryMethod === 'StorePickup' ? 'Retiro en tienda' : 'Envío a domicilio'}</strong><br />Costo: {detail.shippingCost ? money(detail.shippingCost) : 'Sin costo'}</p>{detail.shippingAddress && <p>{detail.shippingAddress}</p>}{(detail.status === 'Preparing' || detail.status === 'Shipped') && detail.deliveryMethod !== 'StorePickup' && <div className="tracking-form"><label>Empresa transportista<input value={tracking.carrier} maxLength={100} onChange={event => setTracking(current => ({ ...current, carrier: event.target.value }))} /></label><label>Número de seguimiento<input value={tracking.trackingNumber} maxLength={100} onChange={event => setTracking(current => ({ ...current, trackingNumber: event.target.value }))} /></label><label>URL de seguimiento (HTTPS)<input type="url" value={tracking.trackingUrl} maxLength={500} placeholder="https://…" onChange={event => setTracking(current => ({ ...current, trackingUrl: event.target.value }))} /></label>{detail.status === 'Shipped' && <button type="button" disabled={pending} onClick={() => void changeStatus('Shipped')}>Corregir seguimiento</button>}</div>}{detail.trackingUrl && <a href={detail.trackingUrl} target="_blank" rel="noopener noreferrer">Abrir seguimiento</a>}</section>
      <section><h3>Productos</h3>{detail.items.map(item => <div className="list-row" key={item.productId}><span>{item.quantity} × {item.productName}<small>{money(item.unitPrice)} c/u</small></span><strong>{money(item.subtotal)}</strong></div>)}{Boolean(detail.discountAmount) && <div className="list-row"><span>Descuento</span><strong>−{money(detail.discountAmount || 0)}</strong></div>}<div className="list-row"><span>Envío</span><strong>{detail.shippingCost ? money(detail.shippingCost) : 'Sin costo'}</strong></div><div className="list-row"><strong>Total</strong><strong>{money(detail.total)}</strong></div></section>
      <section><h3>Pago</h3>{detail.payments.some(payment => payment.requiresReview) && <div className="error payment-incident" role="alert"><strong>Pago aprobado después de cancelar</strong><span>Mercado Pago informó una acreditación para este pedido cancelado. Requiere revisión y devolución.</span></div>}{detail.payments.length === 0 ? <p>Sin pagos registrados.</p> : detail.payments.map(payment => <div className="payment" key={payment.id}><span>#{payment.id} · {payment.provider}<small>{money(payment.amount)} {payment.currency}</small></span><Status value={payment.status} /><small>Creado: {formatDate(payment.createdAt)}{payment.paidAt ? ` · Pagado: ${formatDate(payment.paidAt)}` : ''}</small>{payment.provider === 'BankTransfer' && payment.status === 'Pending' && <div className="transfer-confirmation"><label>Referencia o motivo de acreditación<input value={transferReference} maxLength={500} onChange={event => setTransferReference(event.target.value)} placeholder="Movimiento bancario, fecha o motivo" /></label><button className="primary" type="button" disabled={pending || !transferReference.trim()} onClick={() => void confirmTransfer(payment.id)}>{pending ? 'Confirmando…' : 'Confirmar acreditación verificada'}</button><small>Confirmá únicamente después de comprobar que el dinero fue acreditado.</small></div>}</div>)}</section>
      <section><h3>Comprobantes</h3>{billingDocuments.length === 0 ? <p>Todavía no se generó ningún comprobante.</p> : billingDocuments.map(document => {
        const isReceipt = document.category === 'Receipt'
        const isHomologation = document.authorizationProvider === 'ARCA-Homologation'
        const pdfAvailable = isReceipt || (document.category !== 'Receipt' && document.status === 'Authorized' && Boolean(document.cae))
        return <article className="payment" key={document.id}><span><strong>{isReceipt ? 'Comprobante interno' : `${fiscalDocumentLabels[document.type] || document.type}${isHomologation ? ' · Homologación' : ''}`}</strong><small>{formatDate(document.authorizedAtUtc || document.createdAtUtc)}</small></span><Status value={document.status} /><p>A nombre de {document.recipientName} · {money(document.total)} {document.currency}</p>{!isReceipt && <><p>Punto de venta {fiscalNumber(document.pointOfSale, 5)} · Número {fiscalNumber(document.documentNumber, 8)}</p>{document.cae && <p><strong>CAE {document.cae}</strong>{document.caeExpiresOn ? ` · Vence ${formatDateOnly(document.caeExpiresOn)}` : ''}</p>}{document.rejectionReason && <p className="error">{document.rejectionCode ? `${document.rejectionCode}: ` : ''}{document.rejectionReason}</p>}</>}<small>Identificador: {document.id}</small>{pdfAvailable && <div className="actions receipt-actions"><button type="button" disabled={Boolean(pdfAction)} onClick={() => void getDocumentPdf(document, false)}>{pdfAction === `${document.id}:view` ? 'Abriendo…' : 'Ver PDF'}</button><button type="button" disabled={Boolean(pdfAction)} onClick={() => void getDocumentPdf(document, true)}>{pdfAction === `${document.id}:download` ? 'Descargando…' : 'Descargar PDF'}</button></div>}</article>
      })}{canGenerateReceipt && <button className="primary" type="button" disabled={pending} onClick={() => void createReceipt()}>{pending ? 'Generando…' : 'Generar comprobante'}</button>}{(canGenerateReceipt || billingDocuments.some(document => document.category === 'Receipt')) && <p className="admin-footnote">El comprobante interno es una constancia de compra y no reemplaza una factura fiscal de ARCA.</p>}{billingDocuments.some(document => document.category === 'Invoice') && <p className="admin-footnote">La representación fiscal incluye CAE, vencimiento y código QR de ARCA.</p>}</section>
      <section><h3>Fechas</h3><p>Creado: {formatDate(detail.createdAt)}<br />Última actualización: {detail.updatedAt ? formatDate(detail.updatedAt) : 'Sin cambios posteriores'}</p>{detail.cancellationReason && <p>Motivo: {detail.cancellationReason}</p>}</section>
      <section className="order-history" aria-labelledby="order-history-title"><h3 id="order-history-title">Historial</h3>
        {historyLoading ? <AdminLoading label="Cargando historial…" /> : historyError ? <div className="history-error"><p role="alert">{historyError}</p><button type="button" onClick={() => void loadHistory(detail.id)}>Reintentar historial</button></div> : history.length === 0 ? <p className="admin-footnote">Todavía no hay eventos relevantes.</p> : <ol className="order-timeline">{history.map(event => <li key={event.id} className={`history-${event.source.toLowerCase()}`}><div className="timeline-marker" aria-hidden="true" /><article><div className="timeline-heading"><strong>{actionLabels[event.action] || event.action}</strong><span>{sourceLabels[event.source]}</span></div><time dateTime={event.createdAtUtc}>{formatDate(event.createdAtUtc)}</time>{(event.previousStatus || event.newStatus) && <p>{event.previousStatus ? orderStatusLabel(event.previousStatus, detail.deliveryMethod) : '—'} <span aria-hidden="true">→</span><span className="sr-only"> a </span> {event.newStatus ? orderStatusLabel(event.newStatus, detail.deliveryMethod) : '—'}</p>}{event.reason && <p className="timeline-reason">{event.reason}</p>}<small>{event.actorName ? `${event.actorName}${event.actorEmail ? ` · ${event.actorEmail}` : ''}` : event.source === 'Provider' ? 'Proveedor de pagos' : 'Sistema'}</small></article></li>)}</ol>}
      </section>
      <section className="order-status-management" aria-labelledby="order-status-management-title"><h3 id="order-status-management-title">Gestionar estado</h3><p>{canCancelFreeOrder ? 'Este pedido gratuito puede cancelarse mientras no haya sido enviado. Se repondrá el stock y se liberará el uso del cupón.' : transitionGuidance[detail.status]}</p>
        {canCancelOrder && <div className="tracking-form"><label>Motivo de cancelación<textarea value={cancellationReason} maxLength={500} onChange={event => setCancellationReason(event.target.value)} /></label><button type="button" disabled={pending || !cancellationReason.trim()} onClick={() => void cancelOrder()}>{pending ? 'Cancelando…' : 'Cancelar pedido'}</button></div>}
        {transitions.length > 0 && <div className="actions">{transitions.map(status => <button className={status === 'Canceled' ? '' : 'primary'} disabled={pending} key={status} onClick={() => void changeStatus(status)}>{pending ? 'Guardando…' : `Marcar como ${orderStatusLabel(status, detail.deliveryMethod).toLowerCase()}`}</button>)}</div>}
      </section>
    </aside>}
  </section>
}

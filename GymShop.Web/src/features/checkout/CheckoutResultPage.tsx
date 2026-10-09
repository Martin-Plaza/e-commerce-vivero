import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { api, guestAccess, paymentKey, rotatePaymentKey, savePaymentProvider, savedPaymentProvider } from '../../api/gymshop'
import { money } from '../../config/storefront'
import { orderStatusLabel } from '../orders/orderPresentation'
import type { BankTransferDetails, Order, Payment, PaymentMethods, PaymentProvider } from '../../api/types'
import { checkoutErrorMessage, paymentExplanation, paymentLabels, terminalRetryablePayments } from './checkoutPresentation'


export function CheckoutResultPage({ canRefreshPayment }: { canRefreshPayment: boolean }) {
  const { orderId } = useParams()
  const location = useLocation()
  const initialState = location.state as { paymentError?: string } | null
  const id = Number(orderId)
  const accessToken = Number.isInteger(id) ? guestAccess('order', id) : null
  const isGuestOrder = Boolean(accessToken)
  const [order, setOrder] = useState<Order | null>(null)
  const [payments, setPayments] = useState<Payment[]>([])
  const [bankDetails, setBankDetails] = useState<BankTransferDetails | null>(null)
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethods | null>(null)
  const [selectedProvider, setSelectedProvider] = useState<PaymentProvider | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(initialState?.paymentError ?? '')
  const actionInProgress = useRef(false)

  const load = useCallback(async () => {
    if (!Number.isInteger(id) || id < 1) { setError('La orden solicitada no es válida.'); setLoading(false); return }
    setLoading(true)
    try {
      const currentOrder = accessToken ? await api.guestOrder(id, accessToken) : await api.order(id)
      const currentPayments: Payment[] = accessToken
        ? currentOrder.payments.map(payment => ({ ...payment, failureReason: payment.failureReason ?? null, orderId: currentOrder.id, providerPreferenceId: null, providerPaymentId: null, idempotencyKey: null, checkoutUrl: null, updatedAt: null }))
        : await api.orderPayments(id)
      const [details, methods] = await Promise.all([api.bankTransferDetails().catch(() => null), api.paymentMethods().catch(() => ({ bankTransferAvailable: true, mercadoPagoAvailable: false, mercadoPagoUnavailableReason: 'No pudimos verificar la disponibilidad de Mercado Pago.' }))])
      const sortedPayments = [...currentPayments].sort((a, b) => b.id - a.id)
      setOrder(currentOrder); setPayments(sortedPayments)
      setBankDetails(details)
      setPaymentMethods(methods)
      const persisted = savedPaymentProvider(id)
      setSelectedProvider(sortedPayments[0]?.provider === 'MercadoPago' ? 'MercadoPago' : sortedPayments[0]?.provider === 'BankTransfer' ? 'BankTransfer' : persisted)
    } catch (value) { setError(checkoutErrorMessage(value)) }
    finally { setLoading(false) }
  }, [accessToken, id])
  useEffect(() => { void load() }, [load])

  const handlePaymentAction = async () => {
    if (!order || actionInProgress.current) return
    actionInProgress.current = true; setBusy(true); setError('')
    try {
      const latest = payments[0]
      if (latest?.status === 'Pending') {
        await load()
        return
      }
      const provider = latest?.provider === 'MercadoPago' ? 'MercadoPago' : latest?.provider === 'BankTransfer' ? 'BankTransfer' : selectedProvider
      if (!provider) { setError('Seleccioná un medio de pago antes de continuar.'); return }
      if (provider === 'MercadoPago' && !paymentMethods?.mercadoPagoAvailable) { setError(paymentMethods?.mercadoPagoUnavailableReason || 'Mercado Pago no está disponible.'); return }
      savePaymentProvider(order.id, provider)
      const key = latest && terminalRetryablePayments.includes(latest.status) ? rotatePaymentKey(order.id) : paymentKey(order.id)
      await api.createPayment(order.id, provider, key)
      await load()
    } catch (value) { setError(checkoutErrorMessage(value)) }
    finally { actionInProgress.current = false; setBusy(false) }
  }

  if (loading) return <div className="empty">Consultando orden y pago…</div>
  if (!order) return <div className="empty"><p>{error || 'No encontramos la orden.'}</p><Link to="/catalogo">Volver al catálogo</Link></div>
  const latest = payments[0]
  const canceled = order.status === 'Canceled'
  const canceledWithApprovedPayment = canceled && payments.some(payment => payment.status === 'Approved')
  const paid = order.status === 'Paid'
  const freeOrder = paid && order.total === 0
  const pendingPayment = latest?.status === 'Creating' || latest?.status === 'Pending'
  const canPay = order.status === 'Pending' && (!latest || pendingPayment || terminalRetryablePayments.includes(latest.status))
  const resultTitle = canceled ? 'Pedido cancelado' : freeOrder ? 'Pedido confirmado' : paid ? '¡Pago aprobado!' : pendingPayment ? 'Estamos confirmando tu pago' : 'Orden creada'
  const resultMessage = canceledWithApprovedPayment
    ? 'Detectamos un pago aprobado para este pedido cancelado. La situación está en revisión.'
    : canceled
      ? 'El pedido fue cancelado.'
      : freeOrder
        ? 'El cupón cubrió el total de la compra. No fue necesario realizar un pago.'
      : paid
        ? 'La compra quedó confirmada.'
        : pendingPayment
          ? latest?.provider === 'BankTransfer'
            ? `Tenés tiempo hasta ${order.expiresAt ? new Date(order.expiresAt).toLocaleString('es-AR') : 'el vencimiento informado por email'} para transferir. El stock se validará cuando se acredite el pago.`
            : 'El proveedor todavía no informó el resultado. Podés salir y volver a consultar esta orden.'
          : 'La orden permanece pendiente hasta que se apruebe un pago.'
  const paymentMessage = canceled
    ? 'Este intento de pago pertenece a un pedido cancelado. No realices pagos para esta orden.'
    : latest?.provider === 'BankTransfer' && latest.status === 'Pending'
      ? 'Transferí el monto exacto usando el pedido como referencia. Enviar un comprobante o iniciar la transferencia no confirma el pago: la orden se aprobará después de verificar la acreditación.'
      : latest ? paymentExplanation(latest.status, Boolean(latest.checkoutUrl)) : ''

  return <section className="checkout-result">
    <div className="checkout-steps" aria-label="Progreso del checkout"><span className="done">1 Carrito</span><span className="done">2 Confirmación</span><span className="active">3 Resultado</span></div>
    <div className={`result-hero ${paid ? 'success' : pendingPayment && !canceled ? 'pending' : ''}`}><p className="eyebrow">ORDEN #{order.id}</p><h1>{resultTitle}</h1><p>{resultMessage}</p></div>
    {error && <div className="error" role="alert">{error}</div>}
    <div className="checkout-result-layout"><div className="order-summary-card"><h2>Resumen de la orden</h2>{order.items.map(item => <div className="result-line" key={item.productId}><span>{item.quantity} × {item.productName}</span><strong>{money(item.subtotal)}</strong></div>)}{Boolean(order.shippingCost) && <div className="result-line"><span>Envío</span><strong>{money(order.shippingCost || 0)}</strong></div>}<div className="checkout-total"><span>Total</span><strong>{money(order.total)}</strong></div><p><strong>Entrega:</strong><br />{order.deliveryMethod === 'StorePickup' ? 'Retiro en tienda' : 'Envío a domicilio'}{order.shippingAddress && <><br />{order.shippingAddress}</>}</p>{order.deliveryMethod === 'StorePickup' && <div className="pickup-details"><strong>{order.pickupAddress}</strong>{order.pickupHours && <span>{order.pickupHours}</span>}{order.pickupInstructions && <p>{order.pickupInstructions}</p>}</div>}{order.deliveryMethod !== 'StorePickup' && order.carrier && <p><strong>Transportista:</strong> {order.carrier}<br /><strong>Seguimiento:</strong> {order.trackingNumber}</p>}{order.deliveryMethod !== 'StorePickup' && order.trackingUrl && <a className="link-button primary" href={order.trackingUrl} target="_blank" rel="noopener noreferrer">Seguir mi paquete</a>}<p><strong>Estado:</strong> {orderStatusLabel(order.status, order.deliveryMethod)}</p></div>
      <div className="payment-card"><div className="payment-card-title"><h2>Estado del pago</h2></div>{!latest ? freeOrder ? <p>Esta orden no requiere pago.</p> : canceled ? <p>Todavía no hay intentos de pago.</p> : <><p>Todavía no hay intentos de pago. Elegí cómo continuar:</p><fieldset className="payment-methods"><legend>Medio de pago</legend><label><input type="radio" name="retryProvider" checked={selectedProvider === 'BankTransfer'} onChange={() => { setSelectedProvider('BankTransfer'); savePaymentProvider(order.id, 'BankTransfer') }} /> Transferencia bancaria</label><label><input type="radio" name="retryProvider" checked={selectedProvider === 'MercadoPago'} disabled={!paymentMethods?.mercadoPagoAvailable} onChange={() => { setSelectedProvider('MercadoPago'); savePaymentProvider(order.id, 'MercadoPago') }} /> Mercado Pago<small>{paymentMethods?.mercadoPagoAvailable ? 'Disponible' : paymentMethods?.mercadoPagoUnavailableReason || 'No disponible.'}</small></label></fieldset></> : <><span className={`status status-${latest.status.toLowerCase()}`}>{paymentLabels[latest.status]}</span><p>{paymentMessage}</p><p><strong>{money(latest.amount, latest.currency)}</strong></p>{!canceled && latest.provider === 'BankTransfer' && bankDetails && <dl className="bank-details"><div><dt>Referencia</dt><dd>{latest.externalReference}</dd></div><div><dt>Banco</dt><dd>{bankDetails.bankName || 'A configurar'}</dd></div><div><dt>Titular</dt><dd>{bankDetails.accountHolder || 'A configurar'}</dd></div><div><dt>CBU</dt><dd>{bankDetails.cbu || 'A configurar'}</dd></div><div><dt>Alias</dt><dd>{bankDetails.alias || 'A configurar'}</dd></div>{bankDetails.cuit && <div><dt>CUIT</dt><dd>{bankDetails.cuit}</dd></div>}</dl>}{latest.failureReason && <p className="payment-failure">{latest.failureReason}</p>}{!canceled && latest.checkoutUrl && /^https?:\/\//i.test(latest.checkoutUrl) && <a className="primary link-button" href={latest.checkoutUrl}>Continuar con el proveedor</a>}</>}
        <div className="result-actions">{!isGuestOrder && canPay && (!latest || terminalRetryablePayments.includes(latest.status) || canRefreshPayment) && <button className="primary" disabled={busy} onClick={() => void handlePaymentAction()}>{busy ? 'Procesando…' : latest && terminalRetryablePayments.includes(latest.status) ? 'Intentar pagar nuevamente' : latest ? 'Actualizar estado' : 'Iniciar pago'}</button>}</div>
      </div></div>
    <div className="result-navigation">{!isGuestOrder && <Link to="/ordenes">Ver todas mis órdenes</Link>}<Link to="/catalogo">Seguir comprando</Link></div>
  </section>
}

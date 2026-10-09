import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, guestAccess, paymentKey, rotatePaymentKey, savedPaymentProvider } from '../../api/gymshop'
import type { BankTransferDetails, CheckoutSession, PaymentProvider } from '../../api/types'
import { money } from '../../config/storefront'
import { checkoutErrorMessage, paymentExplanation, paymentLabels, terminalRetryablePayments } from './checkoutPresentation'

export function CheckoutSessionPage() {
  const { checkoutId } = useParams()
  const id = Number(checkoutId)
  const accessToken = Number.isInteger(id) ? guestAccess('checkout', id) : null
  const isGuestCheckout = Boolean(accessToken)
  const [checkout, setCheckout] = useState<CheckoutSession | null>(null)
  const [bankDetails, setBankDetails] = useState<BankTransferDetails | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const actionInProgress = useRef(false)

  const load = useCallback(async () => {
    if (!Number.isInteger(id) || id < 1) { setError('El checkout solicitado no es válido.'); setLoading(false); return }
    try {
      const [current, details] = await Promise.all([accessToken ? api.guestCheckoutSession(id, accessToken) : api.checkoutSession(id), api.bankTransferDetails().catch(() => null)])
      setCheckout(current); setBankDetails(details)
    } catch (value) { setError(checkoutErrorMessage(value)) }
    finally { setLoading(false) }
  }, [accessToken, id])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (checkout?.status !== 'AwaitingPayment' || checkout.payment?.status !== 'Pending') return
    const timer = window.setInterval(() => void load(), 5000)
    return () => window.clearInterval(timer)
  }, [checkout?.status, checkout?.payment?.status, load])

  const retry = async () => {
    if (!checkout || actionInProgress.current) return
    const provider = (checkout.payment?.provider === 'MercadoPago' || checkout.payment?.provider === 'BankTransfer'
      ? checkout.payment.provider
      : savedPaymentProvider(checkout.id)) as PaymentProvider | null
    if (!provider) { setError('No encontramos el medio de pago seleccionado.'); return }
    actionInProgress.current = true; setBusy(true); setError('')
    try {
      const key = checkout.payment && terminalRetryablePayments.includes(checkout.payment.status)
        ? rotatePaymentKey(checkout.id)
        : paymentKey(checkout.id)
      await api.createCheckoutPayment(checkout.id, provider, key)
      await load()
    } catch (value) { setError(checkoutErrorMessage(value)) }
    finally { actionInProgress.current = false; setBusy(false) }
  }

  if (loading) return <div className="empty">Consultando el pago…</div>
  if (!checkout) return <div className="empty"><p>{error || 'No encontramos el checkout.'}</p><Link to="/carrito">Volver al carrito</Link></div>

  const payment = checkout.payment
  const completed = checkout.status === 'Completed' && Boolean(checkout.orderId)
  const refunded = checkout.status === 'Refunded'
  const stockFailure = checkout.status === 'StockUnavailable'
  const waiting = checkout.status === 'AwaitingPayment'
  const waitingForTransfer = waiting && payment?.provider === 'BankTransfer'
  const title = completed ? '¡Compra confirmada!' : refunded ? 'Pago devuelto' : stockFailure ? 'No pudimos confirmar la compra' : 'Estamos esperando el pago'
  const message = completed
      ? 'La orden se creó después de confirmar la acreditación.'
    : refunded
      ? 'La compra ya no podía confirmarse. El importe fue devuelto.'
      : stockFailure
        ? payment?.failureReason || 'La compra ya no podía confirmarse. Estamos gestionando la devolución.'
        : waitingForTransfer
          ? 'Podés seguir comprando mientras verificamos tu transferencia. Si algún producto deja de estar disponible, te contactaremos para ofrecerte un cambio o devolverte el dinero.'
          : 'Podés seguir comprando mientras confirmamos el pago.'
  const canRetry = !isGuestCheckout && waiting && payment && terminalRetryablePayments.includes(payment.status)

  return <section className="checkout-result">
    <div className="checkout-steps" aria-label="Progreso del checkout"><span className="done">1 Carrito</span><span className="done">2 Confirmación</span><span className="active">3 Pago</span></div>
    <div className={`result-hero ${completed ? 'success' : waiting ? 'pending' : ''}`}><p className="eyebrow">CHECKOUT #{checkout.id}</p><h1>{title}</h1><p>{message}</p></div>
    {error && <div className="error" role="alert">{error}</div>}
    <div className="checkout-result-layout"><div className="order-summary-card"><h2>Resumen de la compra</h2>{checkout.items.map(item => <div className="result-line" key={`${item.productId}-${item.productVariantId ?? 'base'}`}><span>{item.quantity} × {item.productName}</span><strong>{money(item.subtotal)}</strong></div>)}{checkout.shippingCost > 0 && <div className="result-line"><span>Envío</span><strong>{money(checkout.shippingCost)}</strong></div>}<div className="checkout-total"><span>Total</span><strong>{money(checkout.total)}</strong></div><p><strong>Entrega:</strong><br />{checkout.deliveryMethod === 'StorePickup' ? 'Retiro en tienda' : checkout.shippingAddress}</p></div>
      <div className="payment-card"><div className="payment-card-title"><h2>Estado del pago</h2></div>{payment ? <><span className={`status status-${payment.status.toLowerCase()}`}>{paymentLabels[payment.status]}</span><p>{paymentExplanation(payment.status, Boolean(payment.checkoutUrl))}</p><p><strong>{money(payment.amount, payment.currency)}</strong></p>{payment.provider === 'BankTransfer' && payment.status === 'Pending' && bankDetails && <dl className="bank-details"><div><dt>Referencia</dt><dd>{payment.externalReference}</dd></div><div><dt>Banco</dt><dd>{bankDetails.bankName || 'A configurar'}</dd></div><div><dt>Titular</dt><dd>{bankDetails.accountHolder || 'A configurar'}</dd></div><div><dt>CBU</dt><dd>{bankDetails.cbu || 'A configurar'}</dd></div><div><dt>Alias</dt><dd>{bankDetails.alias || 'A configurar'}</dd></div>{bankDetails.cuit && <div><dt>CUIT</dt><dd>{bankDetails.cuit}</dd></div>}</dl>}{payment.failureReason && <p className="payment-failure">{payment.failureReason}</p>}{waiting && payment.checkoutUrl && /^https?:\/\//i.test(payment.checkoutUrl) && <a className="primary link-button" href={payment.checkoutUrl}>Continuar con Mercado Pago</a>}</> : <p>No se pudo iniciar el pago.</p>}
        <div className="result-actions">{canRetry && <button className="primary" disabled={busy} onClick={() => void retry()}>{busy ? 'Procesando…' : 'Intentar nuevamente'}</button>}{completed && checkout.orderId && <Link className="primary link-button" to={`/checkout/orden/${checkout.orderId}${accessToken ? `?access=${encodeURIComponent(accessToken)}` : ''}`}>Ver orden #{checkout.orderId}</Link>}</div>
      </div></div>
    <div className="result-navigation"><Link to="/carrito">Volver al carrito</Link><Link to="/catalogo">Seguir comprando</Link></div>
  </section>
}

import { FormEvent, useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { api, checkoutKey, clearCheckoutKey, clearGuestCheckoutKey, guestCheckoutKey, paymentKey, saveGuestAccess, savePaymentProvider } from '../../api/gymshop'
import type { DeliveryMethod, GuestCustomerInput, OrderSummary, PaymentMethods, PaymentProvider, ShippingAddressInput, ShippingOptions, ShippingQuote } from '../../api/types'
import { session } from '../../auth/session'
import { ProductImage } from '../catalog/ProductImage'
import { money } from '../../config/storefront'
import { useCart } from '../cart/useCart'
import { checkoutErrorMessage, isCheckoutPricingConflict, isPendingOrderConflict } from './checkoutPresentation'


export function CheckoutPage() {
  const cart = useCart()
  const navigate = useNavigate()
  const currentUser = session.user()
  const isGuest = !currentUser
  const [customer, setCustomer] = useState<GuestCustomerInput>({ firstName: '', lastName: '', email: '', phone: '' })
  const [address, setAddress] = useState<ShippingAddressInput>({ postalCode: '', province: '', city: '', street: '', streetNumber: '', floor: '', apartment: '', notes: '' })
  const [deliveryMethod, setDeliveryMethod] = useState<DeliveryMethod>('HomeDelivery')
  const [paymentProvider, setPaymentProvider] = useState<PaymentProvider>('BankTransfer')
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethods | null>(null)
  const [paymentMethodsLoading, setPaymentMethodsLoading] = useState(true)
  const [homeDeliveryCost, setHomeDeliveryCost] = useState<number | null>(null)
  const [shippingQuote, setShippingQuote] = useState<ShippingQuote | null>(null)
  const [quoteLoading, setQuoteLoading] = useState(false)
  const [pickupDetails, setPickupDetails] = useState<Pick<ShippingOptions, 'pickupAddress' | 'pickupInstructions' | 'pickupHours'> | null>(null)
  const [shippingError, setShippingError] = useState('')
  const [quoteError, setQuoteError] = useState('')
  const [shippingLoading, setShippingLoading] = useState(true)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [recoveryOrder, setRecoveryOrder] = useState<OrderSummary | null>(null)
  const submitting = useRef(false)
  const loadShippingOptions = useCallback(async () => {
    setShippingLoading(true)
    try {
      const value = await api.shippingOptions()
      if (!Number.isFinite(value.homeDeliveryCost) || value.homeDeliveryCost < 0) throw new Error('invalid shipping cost')
      if (isGuest) setHomeDeliveryCost(value.homeDeliveryCost)
      setPickupDetails({ pickupAddress: value.pickupAddress, pickupInstructions: value.pickupInstructions, pickupHours: value.pickupHours })
      setShippingError(value.pickupAddress?.trim() ? '' : 'El retiro en tienda no está disponible porque falta configurar su dirección.')
    } catch {
      setHomeDeliveryCost(null)
      setPickupDetails(null)
      setShippingError('No pudimos obtener las opciones de entrega.')
    } finally { setShippingLoading(false) }
  }, [isGuest])

  const updateAddress = (field: keyof ShippingAddressInput, value: string) => {
    setAddress(current => ({ ...current, [field]: value }))
    setShippingQuote(null)
    if (!isGuest) setHomeDeliveryCost(null)
    setQuoteError('')
  }

  const quoteShipping = async () => {
    const required = [address.postalCode, address.province, address.city, address.street, address.streetNumber]
    if (required.some(value => !value.trim())) { setError('Completá código postal, provincia, ciudad, calle y número para cotizar.'); return }
    if (isGuest) { setError(''); return }
    setQuoteLoading(true); setError(''); setQuoteError('')
    try {
      const quotes = await api.shippingQuotes({
        postalCode: address.postalCode.trim(), province: address.province.trim(), city: address.city.trim(),
        street: address.street.trim(), streetNumber: address.streetNumber.trim(), floor: address.floor?.trim() || null,
        apartment: address.apartment?.trim() || null, notes: address.notes?.trim() || null,
      })
      const quote = quotes[0]
      if (!quote) throw new Error('empty quote')
      setShippingQuote(quote); setHomeDeliveryCost(quote.price)
    } catch (value) {
      setShippingQuote(null); setHomeDeliveryCost(null)
      setQuoteError(checkoutErrorMessage(value))
    } finally { setQuoteLoading(false) }
  }
  useEffect(() => { void loadShippingOptions() }, [loadShippingOptions])
  useEffect(() => {
    let active = true
    api.paymentMethods()
      .then(value => { if (active) setPaymentMethods(value) })
      .catch(() => { if (active) setPaymentMethods({ bankTransferAvailable: true, mercadoPagoAvailable: false, mercadoPagoUnavailableReason: 'No pudimos verificar la disponibilidad de Mercado Pago.' }) })
      .finally(() => { if (active) setPaymentMethodsLoading(false) })
    return () => { active = false }
  }, [])

  const recoverPendingOrder = async () => {
    try {
      const pending = (await api.myOrders()).filter(order => order.status === 'Pending').sort((a, b) => b.id - a.id)[0]
      setRecoveryOrder(pending ?? null)
      const currentUserId = session.user()?.id
      if (pending && currentUserId) clearCheckoutKey(currentUserId)
    } catch { /* el error original sigue siendo el dato útil */ }
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting.current) return
    if (deliveryMethod === 'HomeDelivery' && !isGuest && !shippingQuote) { setError('Cotizá el envío antes de confirmar la compra.'); return }
    if (deliveryMethod === 'StorePickup' && !pickupDetails?.pickupAddress?.trim()) { setError('El retiro en tienda no está disponible. Reintentá cargar las opciones de entrega.'); return }
    const shippingCost = deliveryMethod === 'HomeDelivery' ? homeDeliveryCost : 0
    if (shippingCost === null) { setError('Esperá mientras cargamos el costo de envío.'); return }
    const isFreeCheckout = cart.total + shippingCost === 0
    if (!isFreeCheckout && paymentMethodsLoading) { setError('Esperá mientras verificamos los medios de pago disponibles.'); return }
    if (!isFreeCheckout && paymentProvider === 'MercadoPago' && !paymentMethods?.mercadoPagoAvailable) { setError(paymentMethods?.mercadoPagoUnavailableReason || 'Mercado Pago no está disponible.'); return }
    if (isGuest && Object.values(customer).some(value => !value.trim())) { setError('Completá nombre, apellido, email y teléfono.'); return }
    submitting.current = true; setBusy(true); setError(''); setRecoveryOrder(null)
    try {
      if (isGuest) {
        const result = await api.guestCheckout({
          customer: { firstName: customer.firstName.trim(), lastName: customer.lastName.trim(), email: customer.email.trim(), phone: customer.phone.trim() },
          items: cart.items.map(item => ({ productId: item.productId, quantity: item.quantity, productVariantId: item.productVariantId ?? null })),
          deliveryMethod, shippingDestination: deliveryMethod === 'HomeDelivery' ? address : null,
          expectedShippingCost: shippingCost, expectedSubtotal: cart.subtotal, paymentProvider,
          idempotencyKey: guestCheckoutKey(),
        })
        if (result.kind === 'Order' && result.order) {
          saveGuestAccess('order', result.order.id, result.accessToken)
          await cart.clear(); clearGuestCheckoutKey()
          navigate(`/checkout/orden/${result.order.id}?access=${encodeURIComponent(result.accessToken)}`, { replace: true })
          return
        }
        if (result.kind === 'Checkout' && result.checkout) {
          saveGuestAccess('checkout', result.checkout.id, result.accessToken)
          await cart.clear(); clearGuestCheckoutKey()
          if (result.checkout.payment?.checkoutUrl && /^https?:\/\//i.test(result.checkout.payment.checkoutUrl)) {
            window.location.assign(result.checkout.payment.checkoutUrl)
          } else {
            navigate(`/checkout/pago/${result.checkout.id}?access=${encodeURIComponent(result.accessToken)}`, { replace: true })
          }
          return
        }
        throw new Error('Respuesta de checkout inválida.')
      }
      const currentUserId = currentUser!.id
      const attemptKey = checkoutKey(currentUserId)
      const checkout = await api.checkout({ deliveryMethod, shippingAddress: null, expectedShippingCost: shippingCost, expectedSubtotal: cart.subtotal, expectedDiscount: cart.discount, idempotencyKey: attemptKey, shippingQuoteId: deliveryMethod === 'HomeDelivery' ? shippingQuote?.id : null, shippingDestination: deliveryMethod === 'HomeDelivery' ? address : null, paymentProvider, paymentIdempotencyKey: attemptKey })
      savePaymentProvider(checkout.id, paymentProvider)
      clearCheckoutKey(currentUserId)
      const legacyOrder = !Object.prototype.hasOwnProperty.call(checkout, 'orderId')
      if (legacyOrder) {
        let paymentError = ''
        if (checkout.total > 0) {
          try { await api.createPayment(checkout.id, paymentProvider, paymentKey(checkout.id)) }
          catch (value) { paymentError = checkoutErrorMessage(value) }
        }
        sessionStorage.setItem('gymshop.last-order', String(checkout.id))
        await cart.refresh()
        navigate(`/checkout/orden/${checkout.id}`, { replace: true, state: { paymentError } })
      } else if (checkout.orderId) {
        sessionStorage.setItem('gymshop.last-order', String(checkout.orderId))
        await cart.refresh()
        navigate(`/checkout/orden/${checkout.orderId}`, { replace: true })
      } else {
        await cart.refresh()
        navigate(`/checkout/pago/${checkout.id}`, { replace: true })
      }
    } catch (value) {
      if (isCheckoutPricingConflict(value)) {
        setRecoveryOrder(null)
        await Promise.all([cart.refresh(), loadShippingOptions()])
        setShippingQuote(null); if (!isGuest) setHomeDeliveryCost(null)
        setError('Actualizamos los precios, el descuento y el costo de envío. Revisá el nuevo total y confirmá nuevamente.')
      } else {
        setError(checkoutErrorMessage(value))
        if (!isGuest && (!(value instanceof ApiError) || isPendingOrderConflict(value))) await recoverPendingOrder()
        await cart.refresh()
      }
    } finally {
      submitting.current = false; setBusy(false)
    }
  }

  if (busy && cart.items.length === 0) return <section className="checkout-transition" role="status"><div className="checkout-transition-spinner" /><p className="eyebrow">PROCESANDO COMPRA</p><h1>Estamos preparando tu orden</h1><p>No cierres esta ventana. Enseguida vas a ver la confirmación.</p></section>
  if (cart.loading) return <section className="checkout-transition" role="status"><div className="checkout-transition-spinner" /><p className="eyebrow">CHECKOUT</p><h1>Validando tu carrito</h1><p>Estamos comprobando precios, stock y descuentos.</p></section>
  if (cart.items.length === 0) return <section className="checkout-empty"><p className="eyebrow">CHECKOUT</p><h1>Tu carrito está vacío</h1><p>Agregá productos antes de iniciar una compra.</p><Link className="primary link-button" to="/catalogo">Ir al catálogo</Link></section>
  const displayedTotal = cart.total + (deliveryMethod === 'HomeDelivery' ? homeDeliveryCost ?? 0 : 0)
  const isFreeCheckout = displayedTotal === 0

  return <section className="checkout-page">
    <div className="checkout-steps" aria-label="Progreso del checkout"><span className="done">1 Carrito</span><span className="active">2 Confirmación</span><span>3 Resultado</span></div>
    <div className="section-title"><div><p className="eyebrow">REVISIÓN FINAL</p><h1>Confirmá tu compra</h1></div><Link to="/carrito">Editar carrito</Link></div>
    {error && <div className="error" role="alert">{error}</div>}
    {shippingError && <div className="error shipping-cost-error" role="alert"><span>{shippingError}</span><button type="button" onClick={() => void loadShippingOptions()} disabled={shippingLoading}>{shippingLoading ? 'Reintentando…' : 'Reintentar opciones de entrega'}</button></div>}
    {quoteError && deliveryMethod === 'HomeDelivery' && <div className="error shipping-cost-error" role="alert"><span>{quoteError}</span><button type="button" onClick={() => void quoteShipping()} disabled={quoteLoading}>{quoteLoading ? 'Reintentando…' : 'Reintentar cotización'}</button></div>}
    {recoveryOrder && <div className="notice" role="status">Encontramos la orden pendiente #{recoveryOrder.id}. <Link to={`/checkout/orden/${recoveryOrder.id}`}>Ver orden</Link></div>}
    <div className="checkout-layout"><div>
      <div className="checkout-review-list">{cart.items.map(item => <article key={item.productId}><div className="checkout-thumb"><ProductImage src={item.imageUrl} alt={item.productName} /></div><div><h3>{item.productName}</h3><p>{item.quantity} × {money(item.unitPrice)}</p></div><strong>{money(item.subtotal)}</strong></article>)}</div>
    </div><form className="checkout-confirmation" onSubmit={submit}>
      {isGuest && <><p className="eyebrow">TUS DATOS</p><h2>Datos de contacto</h2><p className="checkout-disclaimer">Los usaremos para enviarte la confirmación, los datos de pago y novedades sobre la compra.</p><div className="shipping-address-grid guest-customer-grid"><label>Nombre<input autoComplete="given-name" value={customer.firstName} onChange={event => setCustomer(value => ({ ...value, firstName: event.target.value }))} required maxLength={100} /></label><label>Apellido<input autoComplete="family-name" value={customer.lastName} onChange={event => setCustomer(value => ({ ...value, lastName: event.target.value }))} required maxLength={100} /></label><label>Email<input type="email" autoComplete="email" value={customer.email} onChange={event => setCustomer(value => ({ ...value, email: event.target.value }))} required maxLength={320} /></label><label>Teléfono<input type="tel" autoComplete="tel" value={customer.phone} onChange={event => setCustomer(value => ({ ...value, phone: event.target.value }))} required maxLength={30} /></label></div></>}
      <p className="eyebrow">ENTREGA</p><h2>Modalidad de entrega</h2><div className="delivery-options" role="radiogroup" aria-label="Modalidad de entrega"><label><input type="radio" name="delivery" checked={deliveryMethod === 'StorePickup'} onChange={() => setDeliveryMethod('StorePickup')} /> Retiro en tienda <small>Sin costo</small></label><label><input type="radio" name="delivery" checked={deliveryMethod === 'HomeDelivery'} onChange={() => setDeliveryMethod('HomeDelivery')} /> Envío a domicilio <small>{homeDeliveryCost === null ? 'A cotizar' : money(homeDeliveryCost)}</small></label></div>{deliveryMethod === 'HomeDelivery' ? <div className="shipping-address-grid"><label>Código postal<input value={address.postalCode} onChange={event => updateAddress('postalCode', event.target.value)} required maxLength={16} /></label><label>Provincia<input value={address.province} onChange={event => updateAddress('province', event.target.value)} required maxLength={100} /></label><label>Ciudad o localidad<input value={address.city} onChange={event => updateAddress('city', event.target.value)} required maxLength={100} /></label><label>Calle<input value={address.street} onChange={event => updateAddress('street', event.target.value)} required maxLength={150} /></label><label>Número<input value={address.streetNumber} onChange={event => updateAddress('streetNumber', event.target.value)} required maxLength={20} /></label><label>Piso<input value={address.floor ?? ''} onChange={event => updateAddress('floor', event.target.value)} maxLength={20} /></label><label>Departamento<input value={address.apartment ?? ''} onChange={event => updateAddress('apartment', event.target.value)} maxLength={20} /></label><label className="shipping-address-notes">Indicaciones<textarea value={address.notes ?? ''} onChange={event => updateAddress('notes', event.target.value)} maxLength={300} placeholder="Timbre, entrecalles u otra referencia" /></label>{isGuest ? <div className="notice" role="status">Costo de envío: <strong>{homeDeliveryCost === null ? 'cargando…' : money(homeDeliveryCost)}</strong></div> : <><button type="button" onClick={() => void quoteShipping()} disabled={quoteLoading}>{quoteLoading ? 'Cotizando…' : shippingQuote ? 'Volver a cotizar' : 'Calcular envío'}</button>{shippingQuote && <div className="notice" role="status"><strong>{shippingQuote.serviceName}: {money(shippingQuote.price)}</strong><span> Cotización válida hasta {new Date(shippingQuote.expiresAtUtc).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' })}.</span></div>}</>}</div> : pickupDetails && <div className="pickup-details"><strong>{pickupDetails.pickupAddress}</strong>{pickupDetails.pickupHours && <span>{pickupDetails.pickupHours}</span>}{pickupDetails.pickupInstructions && <p>{pickupDetails.pickupInstructions}</p>}</div>}
      {isFreeCheckout ? <div className="notice" role="status">El cupón cubre el total. Este pedido no requiere pago.</div> : <fieldset className="payment-methods" aria-busy={paymentMethodsLoading}><legend>Medio de pago</legend><label><input type="radio" name="paymentProvider" checked={paymentProvider === 'BankTransfer'} onChange={() => setPaymentProvider('BankTransfer')} /> Transferencia bancaria<small>Te enviaremos por email los datos y tendrás {paymentMethods?.bankTransferPendingOrderLifetimeHours ?? 24} horas para transferir. La disponibilidad se confirma al acreditar el pago; si un producto se agota, te contactaremos para ofrecerte un cambio o devolverte el dinero.</small></label><label><input type="radio" name="paymentProvider" checked={paymentProvider === 'MercadoPago'} disabled={paymentMethodsLoading || !paymentMethods?.mercadoPagoAvailable} onChange={() => setPaymentProvider('MercadoPago')} /> Mercado Pago<small>{paymentMethodsLoading ? 'Verificando disponibilidad…' : paymentMethods?.mercadoPagoAvailable ? 'La orden se crea cuando Mercado Pago confirma el pago.' : paymentMethods?.mercadoPagoUnavailableReason || 'No disponible.'}</small></label></fieldset>}
      <div className="checkout-total"><span>Subtotal</span><strong>{money(cart.subtotal)}</strong></div>{cart.discount > 0 && <div className="checkout-total"><span>Descuento {cart.couponCode && `(${cart.couponCode})`}</span><strong>−{money(cart.discount)}</strong></div>}<div className="checkout-total"><span>Envío</span><strong>{deliveryMethod === 'StorePickup' ? 'Sin costo' : homeDeliveryCost === null ? '—' : money(homeDeliveryCost)}</strong></div><div className="checkout-total"><span>Total</span><strong>{money(displayedTotal)}</strong></div><p className="checkout-disclaimer">El cupón se aplica a los productos y se vuelve a validar al confirmar. El envío se agrega después del descuento.</p>
      <button className="primary" disabled={busy || quoteLoading || (!isFreeCheckout && paymentMethodsLoading) || (deliveryMethod === 'HomeDelivery' && (!isGuest && !shippingQuote || homeDeliveryCost === null)) || (deliveryMethod === 'StorePickup' && !pickupDetails?.pickupAddress?.trim())}>{busy ? 'Confirmando compra…' : isFreeCheckout ? 'Confirmar pedido' : !isGuest ? 'Confirmar y pagar' : paymentProvider === 'BankTransfer' ? 'Generar orden de transferencia' : 'Ir a Mercado Pago'}</button><Link className="secondary-link" to="/carrito">Volver al carrito</Link>
    </form></div>
  </section>
}

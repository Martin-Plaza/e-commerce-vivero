import { FormEvent, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { session } from '../../auth/session'
import { ProductImage } from '../catalog/ProductImage'
import { money, storefront } from '../../config/storefront'
import { useCart } from './useCart'

export function CartPage() {
  const cart = useCart()
  const navigate = useNavigate()
  const [code, setCode] = useState('')
  const [couponBusy, setCouponBusy] = useState(false)
  const applying = useRef(false)
  const apply = async (event: FormEvent) => {
    event.preventDefault()
    if (applying.current || !code.trim()) return
    applying.current = true; setCouponBusy(true)
    try { await cart.applyCoupon(code) }
    catch { /* context exposes the API message */ }
    finally { applying.current = false; setCouponBusy(false) }
  }
  const continueToCheckout = async () => {
    if (!session.user()) { navigate('/checkout'); return }
    const normalizedCode = code.trim()
    if (!normalizedCode || cart.couponCode) { navigate('/checkout'); return }
    if (applying.current) return
    applying.current = true; setCouponBusy(true)
    try { if (await cart.applyCoupon(normalizedCode)) navigate('/checkout') }
    catch { /* context exposes the API message */ }
    finally { applying.current = false; setCouponBusy(false) }
  }

  return <section className="cart-page">
    <div className="section-title"><div><p className="eyebrow">TU SELECCIÓN</p><h1>Carrito</h1></div><strong>{money(cart.total)}</strong></div>
    {cart.notice && <div className="notice" role="status">{cart.notice}</div>}
    {cart.error && <div className="error" role="alert">{cart.error}</div>}
    {cart.loading ? <div className="empty">Cargando carrito…</div> : cart.items.length === 0 ? <div className="empty">Tu carrito está vacío.</div> : <div className="split">
      <div className="cart-list">{cart.items.map(item => <article className="cart-line" key={`${item.productId}-${item.productVariantId ?? 'simple'}`}>
        <div className="cart-line-image"><ProductImage src={item.imageUrl} alt={item.productName} /></div>
        <div className="cart-line-info"><h3>{item.productName}</h3>{item.variantAttributes && <small>{Object.entries(item.variantAttributes).map(([key, value]) => `${key}: ${value}`).join(' · ')}</small>}<p>{money(item.unitPrice)} · Stock {item.stock}</p><button className="text-button" onClick={() => void cart.remove(item.productId, item.productVariantId)}>Quitar</button></div>
        <div className="quantity-control">
          <button aria-label={`Quitar una unidad de ${item.productName}`} disabled={item.quantity <= 1} onClick={() => void cart.update(item.productId, item.quantity - 1, item.productVariantId)}>−</button>
          <input aria-label={`Cantidad de ${item.productName}`} type="number" min="1" max={item.stock} value={item.quantity} onChange={event => void cart.update(item.productId, Number(event.target.value), item.productVariantId)} />
          <button aria-label={`Sumar una unidad de ${item.productName}`} disabled={item.quantity >= item.stock} onClick={() => void cart.update(item.productId, item.quantity + 1, item.productVariantId)}>+</button>
        </div>
        <strong>{money(item.subtotal)}</strong>
      </article>)}</div>
      <aside className="summary cart-summary">
        <h2>Resumen</h2><p>{session.user() ? storefront.copy.cartAuthenticatedExplanation : storefront.copy.cartGuestExplanation}</p>
        {session.user() && <form className="coupon-apply" onSubmit={apply}>
          <label>Código de descuento<input placeholder="Ej.: FUERZA10" value={code} onChange={event => setCode(event.target.value)} disabled={couponBusy || Boolean(cart.couponCode)} /></label>
          {cart.couponCode ? <button type="button" disabled={couponBusy} onClick={() => void cart.removeCoupon()}>{couponBusy ? 'Quitando…' : `Quitar ${cart.couponCode}`}</button> : <button type="submit" disabled={couponBusy || !code.trim()}>{couponBusy ? 'Aplicando…' : 'Aplicar'}</button>}
        </form>}
        <div><span>Subtotal</span><strong>{money(cart.subtotal)}</strong></div>
        {cart.discount > 0 && <div><span>Descuento</span><strong>−{money(cart.discount)}</strong></div>}
        <div className="cart-total"><span>Total</span><strong>{money(cart.total)}</strong></div>
        <div className="cart-summary-actions">
          <button type="button" className="primary checkout-action" aria-label="Continuar al checkout" disabled={couponBusy} onClick={() => void continueToCheckout()}>{couponBusy ? 'Validando cupón…' : session.user() ? 'Continuar al checkout →' : 'Comprar sin cuenta →'}</button>
          <button type="button" className="clear-cart-action" onClick={() => void cart.clear()}>Vaciar carrito</button>
        </div>
      </aside>
    </div>}
  </section>
}

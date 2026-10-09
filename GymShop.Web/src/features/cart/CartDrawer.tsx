import { Link } from 'react-router-dom'
import { ProductImage } from '../catalog/ProductImage'
import { money } from '../../config/storefront'
import { useCart } from './useCart'


export function CartDrawer() {
  const cart = useCart()
  if (!cart.drawerOpen) return null

  return <div className="cart-drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) cart.closeDrawer() }}>
    <aside className="cart-drawer" role="dialog" aria-modal="true" aria-label="Carrito">
      <div className="cart-drawer-header"><div><p className="eyebrow">TU SELECCIÓN</p><h2>Carrito ({cart.count})</h2></div><button className="close" aria-label="Cerrar carrito" onClick={cart.closeDrawer}>×</button></div>
      {cart.notice && <div className="notice" role="status">{cart.notice}</div>}
      {cart.error && <div className="error" role="alert">{cart.error}</div>}
      {cart.items.length === 0 ? <div className="empty">Tu carrito está vacío.</div> : <div className="mini-cart-list">{cart.items.map(item => <article className="mini-cart-item" key={`${item.productId}-${item.productVariantId ?? 'simple'}`}>
        <div className="mini-cart-image"><ProductImage src={item.imageUrl} alt={item.productName} /></div>
        <div><h3>{item.productName}</h3>{item.variantAttributes && <small>{Object.values(item.variantAttributes).join(' · ')}</small>}<p>{money(item.unitPrice)}</p><div className="quantity-control"><button aria-label={`Quitar una unidad de ${item.productName}`} disabled={item.quantity <= 1} onClick={() => void cart.update(item.productId, item.quantity - 1, item.productVariantId)}>−</button><span aria-label={`Cantidad de ${item.productName}`}>{item.quantity}</span><button aria-label={`Sumar una unidad de ${item.productName}`} disabled={item.quantity >= item.stock} onClick={() => void cart.update(item.productId, item.quantity + 1, item.productVariantId)}>+</button></div></div>
        <button className="text-button" onClick={() => void cart.remove(item.productId, item.productVariantId)}>Quitar</button>
      </article>)}</div>}
      <div className="cart-drawer-footer"><div><span>Total</span><strong>{money(cart.total)}</strong></div><Link className="primary link-button" to="/carrito" onClick={cart.closeDrawer}>Ir al carrito</Link></div>
    </aside>
  </div>
}

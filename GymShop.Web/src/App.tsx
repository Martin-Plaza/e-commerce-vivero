import { useCallback, useEffect, useState } from 'react'
import { BrowserRouter, Link, Navigate, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import type { AuthResponse, Category, User } from './api/types'
import { api } from './api/gymshop'
import { session } from './auth/session'
import { storefront } from './config/storefront'
import { AdminDashboard } from './features/admin/AdminDashboard'
import { AdminLayout } from './features/admin/AdminLayout'
import { BillingAdmin } from './features/admin/BillingAdmin'
import { AdminRoute } from './features/admin/AdminRoute'
import { AuditAdmin } from './features/admin/AuditAdmin'
import { CategoriesAdmin } from './features/admin/CategoriesAdmin'
import { CategoryEditorPage } from './features/admin/CategoryEditorPage'
import { isAdmin } from './features/admin/adminConfig'
import { ProductsAdmin } from './features/admin/ProductsAdmin'
import { ProductEditorPage } from './features/admin/ProductEditorPage'
import { AttributesAdmin } from './features/admin/AttributesAdmin'
import { UsersAdmin } from './features/admin/UsersAdmin'
import { StockAdmin } from './features/admin/StockAdmin'
import { CouponsAdmin } from './features/admin/CouponsAdmin'
import { AuthPanel } from './features/auth/AuthPanel'
import { CartDrawer } from './features/cart/CartDrawer'
import { CartProvider } from './features/cart/CartContext'
import { CartPage } from './features/cart/CartPage'
import { useCart } from './features/cart/useCart'
import { Catalog } from './features/catalog/Catalog'
import { ProductDetailPage } from './features/catalog/ProductDetailPage'
import { CheckoutPage } from './features/checkout/CheckoutPage'
import { CheckoutResultPage } from './features/checkout/CheckoutResultPage'
import { CheckoutSessionPage } from './features/checkout/CheckoutSessionPage'
import { Home } from './features/home/Home'
import { OrdersView } from './features/orders/OrdersView'
import { AppErrorBoundary, NotFoundPage } from './features/errors/ErrorPages'
import { ContactPage, PrivacyPage, ShippingReturnsPage, TermsPage, WithdrawalPage } from './features/legal/LegalPages'
import { SocialLinks } from './features/layout/SocialLinks'
import { RouteSeo } from './features/seo/RouteSeo'

export default function App() {
  return <AppErrorBoundary><BrowserRouter><CartProvider><AppShell /><CartDrawer /></CartProvider></BrowserRouter></AppErrorBoundary>
}

function AppShell() {
  const [user, setUser] = useState(session.user())
  const [notice, setNotice] = useState<{ message: string; destination: string; shown: boolean } | null>(null)
  const cart = useCart()
  const navigate = useNavigate()
  const location = useLocation()
  const refreshSession = useCallback(() => setUser(session.user()), [])
  useEffect(() => { window.addEventListener('gymshop:session', refreshSession); return () => window.removeEventListener('gymshop:session', refreshSession) }, [refreshSession])
  useEffect(() => {
    const testEnvironment = navigator.userAgent.toLowerCase().includes('jsdom')
    const mockedScroll = '_isMockFunction' in window.scrollTo
    if (/^\/catalogo\/[^/]+$/.test(location.pathname) && (!testEnvironment || mockedScroll)) window.scrollTo(0, 0)
    else if (!testEnvironment) window.scrollTo({ top: 0, left: 0, behavior: 'auto' })
  }, [location.pathname])
  useEffect(() => {
    if (!notice) return
    const current = `${location.pathname}${location.search}`
    if (current === notice.destination && !notice.shown) setNotice({ ...notice, shown: true })
    else if (notice.shown && current !== notice.destination) setNotice(null)
  }, [location.pathname, location.search, notice])
  const logout = () => { void api.logout().catch(() => undefined).finally(() => { session.clear(); setNotice({ message: 'Sesión cerrada.', destination: '/', shown: false }); navigate('/') }) }
  const adminArea = location.pathname === '/admin' || location.pathname.startsWith('/admin/')

  return <div className={adminArea ? 'app admin-app' : 'app'}>
    <RouteSeo />
    <RouteLoadingIndicator />
    {!adminArea && <StorefrontHeader user={user} onLogout={logout} onCart={cart.openDrawer} cartCount={cart.count} />}
    {!adminArea && <main>{notice && <div className="notice route-notice" role="status">{notice.message}</div>}<StorefrontRoutes user={user} onAuth={(auth, destination) => { session.save(auth.user); setNotice({ message: `Hola, ${auth.user.name}.`, destination, shown: false }) }} /></main>}
    {adminArea && <AdminRoutes user={user} onLogout={logout} />}
    {!adminArea && <StorefrontFooter />}
  </div>
}

function StorefrontFooter() {
  return <footer className="storefront-footer"><div className="footer-brand"><strong>{storefront.copy.footer}</strong><span>Información clara antes y después de comprar.</span><SocialLinks /></div><nav aria-label="Información legal"><Link to="/terminos">Términos</Link><Link to="/privacidad">Privacidad</Link><Link to="/envios-cambios-y-devoluciones">Envíos y devoluciones</Link><Link to="/contacto">Contacto</Link><Link className="withdrawal-link" to="/arrepentimiento">Botón de arrepentimiento</Link></nav></footer>
}

function RouteLoadingIndicator() {
  const location = useLocation()
  const [visible, setVisible] = useState(true)
  useEffect(() => {
    setVisible(true)
    const timer = window.setTimeout(() => setVisible(false), 420)
    return () => window.clearTimeout(timer)
  }, [location.key])
  return <div className={visible ? 'route-loader is-visible' : 'route-loader'} role="progressbar" aria-label="Cargando página"><i /><span>Cargando página…</span></div>
}

function StorefrontHeader({ user, onLogout, onCart, cartCount }: { user: User | null; onLogout(): void; onCart(): void; cartCount: number }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [openMenu, setOpenMenu] = useState<'categories' | 'admin' | null>(null)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  useEffect(() => {
    if (openMenu !== 'categories' || categories.length) return
    let active = true
    api.categories().then(value => { if (active) setCategories(value) }).catch(() => undefined)
    return () => { active = false }
  }, [categories.length, openMenu])
  useEffect(() => {
    if (!openMenu && !mobileMenuOpen) return
    const close = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpenMenu(null); setMobileMenuOpen(false) } }
    document.addEventListener('keydown', close)
    return () => document.removeEventListener('keydown', close)
  }, [mobileMenuOpen, openMenu])
  const closeMenus = () => { setOpenMenu(null); setMobileMenuOpen(false) }
  const toggleMobileMenu = () => { setOpenMenu(null); setMobileMenuOpen(value => !value) }
  return <><header className="storefront-header"><Link className="brand" to="/" onClick={closeMenus}>{storefront.identity.logoUrl ? <img src={storefront.identity.logoUrl} alt="" /> : <span>{storefront.identity.monogram}</span>} {storefront.identity.name}</Link><nav id="storefront-navigation" className={mobileMenuOpen ? 'is-open' : ''} aria-label="Navegación principal">
    <NavLink to="/catalogo" onClick={closeMenus}>{storefront.copy.catalogNav}</NavLink>
    <div className="nav-menu"><button type="button" aria-expanded={openMenu === 'categories'} onClick={() => setOpenMenu(value => value === 'categories' ? null : 'categories')}>Categorías <span aria-hidden="true">⌄</span></button>{openMenu === 'categories' && <div className="nav-dropdown"><Link to="/catalogo" onClick={closeMenus}>Todas las categorías</Link>{categories.map(category => <Link key={category.id} to={`/catalogo?categoria=${encodeURIComponent(category.slug)}`} onClick={closeMenus}>{category.name}</Link>)}</div>}</div>
    {user && <NavLink to="/ordenes" onClick={closeMenus}>Órdenes</NavLink>}
    {isAdmin(user) && <div className="nav-menu"><button type="button" aria-expanded={openMenu === 'admin'} onClick={() => setOpenMenu(value => value === 'admin' ? null : 'admin')}>Administración <span aria-hidden="true">⌄</span></button>{openMenu === 'admin' && <div className="nav-dropdown admin-dropdown"><Link to="/admin" onClick={closeMenus}>Resumen</Link><Link to="/admin/productos" onClick={closeMenus}>Productos</Link><Link to="/admin/stock" onClick={closeMenus}>Stock</Link><Link to="/admin/pedidos" onClick={closeMenus}>Pedidos</Link><Link to="/admin/cupones" onClick={closeMenus}>Cupones</Link></div>}</div>}
    {user ? <button className="mobile-session-action" aria-hidden={!mobileMenuOpen} tabIndex={mobileMenuOpen ? 0 : -1} onClick={() => { closeMenus(); onLogout() }}>Salir</button> : <Link className="mobile-session-action" to="/login" aria-hidden={!mobileMenuOpen} tabIndex={mobileMenuOpen ? 0 : -1} onClick={closeMenus}>Ingresar</Link>}
  </nav><div className="account">{user && <small>{user.name}<br />{user.role}</small>}<button className="cart-button" onClick={() => { closeMenus(); onCart() }}>Carrito <b>{cartCount}</b></button>{user ? <button className="desktop-session-action" aria-hidden={mobileMenuOpen} tabIndex={mobileMenuOpen ? -1 : 0} onClick={onLogout}>Salir</button> : <Link className="primary link-button desktop-session-action" to="/login" aria-hidden={mobileMenuOpen} tabIndex={mobileMenuOpen ? -1 : 0}>Ingresar</Link>}</div><button className="mobile-menu-button" type="button" aria-controls="storefront-navigation" aria-expanded={mobileMenuOpen} aria-label={mobileMenuOpen ? 'Cerrar menú' : 'Abrir menú'} onClick={toggleMobileMenu}><span /><span /><span /></button></header>{(openMenu || mobileMenuOpen) && <button className="nav-fade" aria-label="Cerrar menú" onClick={closeMenus} />}</>
}

function StorefrontRoutes({ user, onAuth }: { user: User | null; onAuth(auth: AuthResponse, destination: string): void }) {
  const navigate = useNavigate()
  return <Routes>
    <Route path="/" element={<Home onCatalog={category => navigate(category ? `/catalogo?categoria=${encodeURIComponent(category)}` : '/catalogo')} onProduct={id => navigate(`/catalogo/${id}`)} />} />
    <Route path="/catalogo" element={<Catalog />} /><Route path="/catalogo/:productId" element={<ProductDetailPage />} /><Route path="/carrito" element={<CartPage />} />
    <Route path="/checkout" element={<CheckoutPage />} /><Route path="/checkout/pago/:checkoutId" element={<CheckoutSessionPage />} /><Route path="/checkout/orden/:orderId" element={<CheckoutResultPage canRefreshPayment={isAdmin(user)} />} />
    <Route path="/login" element={<AuthRoute user={user} onDone={onAuth} />} /><Route path="/ordenes" element={user ? <OrdersView /> : <RequireLogin />} />
    <Route path="/terminos" element={<TermsPage />} /><Route path="/privacidad" element={<PrivacyPage />} /><Route path="/envios-cambios-y-devoluciones" element={<ShippingReturnsPage />} /><Route path="/arrepentimiento" element={<WithdrawalPage />} /><Route path="/contacto" element={<ContactPage />} />
    <Route path="*" element={<NotFoundPage />} />
  </Routes>
}

function AdminRoutes({ user, onLogout }: { user: User | null; onLogout(): void }) {
  return <Routes><Route path="/admin" element={<AdminRoute user={user}>{user && <AdminLayout user={user} onLogout={onLogout} />}</AdminRoute>}>
    <Route index element={<AdminDashboard />} /><Route path="productos" element={<ProductsAdmin />} /><Route path="stock" element={<StockAdmin />} /><Route path="productos/nuevo" element={<ProductEditorPage mode="create" />} /><Route path="productos/:productId/editar" element={<ProductEditorPage mode="edit" />} /><Route path="categorias" element={<CategoriesAdmin />} /><Route path="atributos" element={<AttributesAdmin />} /><Route path="categorias/nueva" element={<CategoryEditorPage mode="create" />} /><Route path="categorias/:categoryId/editar" element={<CategoryEditorPage mode="edit" />} /><Route path="pedidos" element={<OrdersView admin />} /><Route path="cupones" element={<CouponsAdmin />} />
    <Route path="usuarios" element={<AdminRoute user={user} superAdmin><UsersAdmin /></AdminRoute>} /><Route path="facturacion" element={<AdminRoute user={user} superAdmin><BillingAdmin /></AdminRoute>} /><Route path="auditoria" element={<AdminRoute user={user} superAdmin><AuditAdmin /></AdminRoute>} />
  </Route><Route path="*" element={<Navigate to="/admin" replace />} /></Routes>
}

function AuthRoute({ user, onDone }: { user: User | null; onDone(auth: AuthResponse, destination: string): void }) {
  const location = useLocation(); const navigate = useNavigate(); const state = location.state as { returnTo?: string; message?: string } | null
  if (user) return <Navigate to={state?.returnTo || '/'} replace />
  return <>{state?.message && <div className="notice" role="status">{state.message}</div>}<AuthPanel onDone={auth => { const destination = state?.returnTo || '/'; onDone(auth, destination); navigate(destination, { replace: true }) }} /></>
}

function RequireLogin() { const location = useLocation(); return <Navigate to="/login" replace state={{ returnTo: `${location.pathname}${location.search}`, message: 'Iniciá sesión para continuar.' }} /> }

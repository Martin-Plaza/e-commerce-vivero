import { useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router-dom'
import type { User } from '../../api/types'
import { storefront } from '../../config/storefront'
import { isSuperAdmin } from './adminConfig'

const links = [
  { to: '/admin', label: 'Resumen', end: true },
  { to: '/admin/productos', label: 'Productos' },
  { to: '/admin/stock', label: 'Stock' },
  { to: '/admin/categorias', label: 'Categorías' },
  { to: '/admin/atributos', label: 'Atributos' },
  { to: '/admin/pedidos', label: 'Pedidos' },
  { to: '/admin/cupones', label: 'Cupones' },
]

export function AdminLayout({ user, onLogout }: { user: User; onLogout(): void }) {
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)
  return <div className="admin-shell">
    <button className="admin-menu-button" type="button" aria-expanded={open} aria-controls="admin-navigation" onClick={() => setOpen(value => !value)}>☰ <span>Menú</span></button>
    {open && <button className="admin-nav-backdrop" aria-label="Cerrar menú" onClick={close} />}
    <aside className={open ? 'admin-sidebar is-open' : 'admin-sidebar'} id="admin-navigation">
      <Link className="admin-brand" to="/admin" onClick={close}>
        <span>{storefront.identity.monogram}</span><div><strong>{storefront.identity.name}</strong><small>Panel administrativo</small></div>
      </Link>
      <nav aria-label="Navegación administrativa">
        {links.map(link => <NavLink key={link.to} end={link.end} to={link.to} onClick={close}>{link.label}</NavLink>)}
        {isSuperAdmin(user) && <NavLink to="/admin/usuarios" onClick={close}>Usuarios</NavLink>}
        {isSuperAdmin(user) && <NavLink to="/admin/facturacion" onClick={close}>Facturación</NavLink>}
        {isSuperAdmin(user) && <NavLink to="/admin/auditoria" onClick={close}>Auditoría</NavLink>}
      </nav>
      <div className="admin-account">
        <div><strong>{user.name}</strong><span>{user.email}</span><small>{user.role}</small></div>
        <Link to="/">← Volver a la tienda</Link>
        <button type="button" onClick={onLogout}>Cerrar sesión</button>
      </div>
    </aside>
    <main className="admin-main"><Outlet /></main>
  </div>
}

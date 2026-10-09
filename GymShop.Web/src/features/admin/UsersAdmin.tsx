import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/gymshop'
import type { AdminUser, AdminUserDetail, AdminUserPage, Role } from '../../api/types'
import { describeAdminError } from './adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from './adminUi'
import { orderStatusLabel } from '../orders/orderPresentation'

const emptyPage: AdminUserPage = { items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 }
const money = (value: number) => new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS' }).format(value)
const date = (value: string | null) => value ? new Date(value).toLocaleString('es-AR') : 'Sin pedidos válidos'

export function UsersAdmin() {
  const [result, setResult] = useState(emptyPage)
  const [searchInput, setSearchInput] = useState(''); const [search, setSearch] = useState(''); const [role, setRole] = useState(''); const [status, setStatus] = useState('')
  const [loading, setLoading] = useState(true); const [pending, setPending] = useState<number | 'create' | null>(null); const [error, setError] = useState(''); const [success, setSuccess] = useState('')
  const [creating, setCreating] = useState(false); const [detail, setDetail] = useState<AdminUserDetail | null>(null); const [detailLoading, setDetailLoading] = useState(false)
  const [form, setForm] = useState({ name: '', email: '', password: '', role: 'User' as Role })
  const [refreshRevision, setRefreshRevision] = useState(0)

  const load = useCallback(async (page = 1) => {
    setLoading(true); setError('')
    try { setResult(await api.users({ page, pageSize: 20, search: search || undefined, role: role ? role as Role : undefined, isActive: status === '' ? undefined : status === 'active' })) }
    catch (value) { setError(describeAdminError(value)) } finally { setLoading(false) }
  }, [search, role, status])
  useEffect(() => { void load(1) }, [load, refreshRevision])

  const showDetail = async (id: number) => { setDetailLoading(true); setError(''); try { setDetail(await api.user(id)) } catch (value) { setError(describeAdminError(value)) } finally { setDetailLoading(false) } }
  const mutate = async (user: AdminUser, action: () => Promise<void>, message: string) => {
    if (pending !== null) return; setPending(user.id); setError(''); setSuccess('')
    try { await action(); await load(result.page); if (detail?.id === user.id) setDetail(await api.user(user.id)); setSuccess(message) }
    catch (value) { setError(describeAdminError(value)) } finally { setPending(null) }
  }
  const changeRole = (user: AdminUser, next: Role) => { if (next === user.role || !window.confirm(`¿Cambiar el rol de ${user.name} a ${next}?`)) return; void mutate(user, () => api.setUserRole(user.id, next), `Rol de ${user.name} actualizado.`) }
  const changeStatus = (user: AdminUser) => { const verb = user.isActive ? 'desactivar' : 'activar'; if (!window.confirm(`¿Querés ${verb} la cuenta de ${user.name}?`)) return; void mutate(user, () => api.setUserStatus(user.id, !user.isActive), `${user.name} fue ${user.isActive ? 'desactivado' : 'activado'}.`) }
  const create = async (event: FormEvent) => {
    event.preventDefault(); if (pending !== null) return; setPending('create'); setError(''); setSuccess('')
    try { const created = await api.createUser(form); setCreating(false); setForm({ name: '', email: '', password: '', role: 'User' }); setSearch(''); setSearchInput(''); setRole(''); setStatus(''); setRefreshRevision(value => value + 1); setSuccess(`${created.name} fue creado correctamente.`) }
    catch (value) { setError(describeAdminError(value)) } finally { setPending(null) }
  }

  return <section className="admin-page users-admin">
    <div className="admin-page-heading"><div><p className="eyebrow">SUPERADMIN</p><h1>Usuarios y clientes</h1><p>Clientes, personal, permisos y actividad comercial.</p></div><button className="primary" onClick={() => setCreating(true)}>Crear usuario</button></div>
    <AdminFeedback error={error} success={success} />
    <form className="admin-filters users-filters" onSubmit={event => { event.preventDefault(); setSearch(searchInput.trim()) }}>
      <label className="admin-search">Nombre o email<input value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Buscar usuarios" /></label>
      <label>Tipo / rol<select value={role} onChange={event => setRole(event.target.value)}><option value="">Todos</option><option value="User">Clientes</option><option value="Admin">Admin</option><option value="SuperAdmin">SuperAdmin</option></select></label>
      <label>Estado<select value={status} onChange={event => setStatus(event.target.value)}><option value="">Todos</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select></label>
      <button type="submit">Buscar</button>
    </form>
    <p className="users-result-summary">{result.totalItems} usuario{result.totalItems === 1 ? '' : 's'} · {role === 'User' ? 'Clientes' : role ? 'Personal' : 'Clientes y personal'}</p>
    {loading && result.items.length === 0 ? <AdminLoading label="Cargando usuarios…" /> : result.items.length === 0 ? <AdminEmpty>No hay usuarios que coincidan con los filtros.</AdminEmpty> : <div className={loading ? 'admin-user-list is-loading' : 'admin-user-list'}>
      <div className="admin-user-row admin-user-header"><span>N.º · Usuario</span><span>Tipo</span><span>Estado</span><span>Registro</span><span>Acciones</span></div>
      {result.items.map(user => <div className="admin-user-row" key={user.id} aria-busy={pending === user.id}>
        <button className="user-identity" onClick={() => void showDetail(user.id)}><strong><span className="user-number">#{user.id}</span> {user.name}</strong><small>{user.email}</small></button>
        <select data-label="Rol" disabled={pending !== null} aria-label={`Rol de ${user.name}`} value={user.role} onChange={event => changeRole(user, event.target.value as Role)}><option>User</option><option>Admin</option><option>SuperAdmin</option></select>
        <span data-label="Estado" className={`admin-pill ${user.isActive ? 'active' : 'inactive'}`}>{user.isActive ? 'Activo' : 'Inactivo'}</span>
        <span data-label="Registro">{date(user.createdAt)}</span>
        <div className="admin-user-actions"><button disabled={pending !== null} onClick={() => void showDetail(user.id)}>Detalle</button><button disabled={pending !== null} onClick={() => changeStatus(user)}>{pending === user.id ? 'Guardando…' : user.isActive ? 'Desactivar' : 'Activar'}</button></div>
      </div>)}</div>}
    {result.totalPages > 1 && <div className="admin-pagination"><button disabled={loading || result.page <= 1} onClick={() => void load(result.page - 1)}>Anterior</button><span>Página {result.page} de {result.totalPages}</span><button disabled={loading || result.page >= result.totalPages} onClick={() => void load(result.page + 1)}>Siguiente</button></div>}
    {creating && <div className="modal-backdrop" role="presentation"><section className="user-dialog" role="dialog" aria-modal="true" aria-label="Crear usuario"><button className="modal-close" aria-label="Cerrar" disabled={pending === 'create'} onClick={() => setCreating(false)}>×</button><h2>Crear usuario</h2><p>Los roles Admin y SuperAdmin otorgan acceso al panel.</p><form onSubmit={event => void create(event)}><label>Nombre<input required maxLength={150} value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} /></label><label>Email<input required type="email" value={form.email} onChange={event => setForm({ ...form, email: event.target.value })} /></label><label>Contraseña<input required minLength={6} type="password" value={form.password} onChange={event => setForm({ ...form, password: event.target.value })} /></label><label>Rol<select value={form.role} onChange={event => setForm({ ...form, role: event.target.value as Role })}><option>User</option><option>Admin</option><option>SuperAdmin</option></select></label><button className="primary" disabled={pending !== null}>{pending === 'create' ? 'Creando…' : 'Crear usuario'}</button></form></section></div>}
    {(detail || detailLoading) && <aside className="drawer user-detail" role="dialog" aria-modal="true" aria-label="Detalle de usuario"><button className="close" aria-label="Cerrar detalle" onClick={() => setDetail(null)}>×</button>{detailLoading && !detail ? <AdminLoading label="Cargando detalle…" /> : detail && <><p className="eyebrow">{detail.role === 'User' ? 'CLIENTE' : 'PERSONAL'}</p><h2><span className="user-number">#{detail.id}</span> {detail.name}</h2><p>{detail.email}</p><div className="user-metrics"><article><span>Pedidos válidos</span><strong>{detail.orderCount}</strong></article><article><span>Total comprado</span><strong>{money(detail.totalPurchased)}</strong></article><article><span>Último pedido</span><strong>{date(detail.lastOrderAt)}</strong></article></div><h3>Pedidos recientes</h3>{detail.recentOrders.length === 0 ? <AdminEmpty>Este usuario todavía no tiene pedidos.</AdminEmpty> : <><p className="admin-footnote">Mostrando {detail.recentOrders.length} de {detail.ordersTotal} pedidos.</p><div className="list">{detail.recentOrders.map(order => <Link className="list-row" key={order.id} to={`/admin/pedidos?search=%23${order.id}`}><span><strong>Pedido #{order.id}</strong><small>{date(order.createdAt)} · {orderStatusLabel(order.status, order.deliveryMethod)}</small></span><strong>{money(order.total)}</strong></Link>)}</div>{detail.ordersTotal > detail.recentOrders.length && <Link className="user-orders-link" to={`/admin/pedidos?search=${encodeURIComponent(detail.email)}`}>Ver historial completo ({detail.ordersTotal})</Link>}</>}</>}</aside>}
  </section>
}

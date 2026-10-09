import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../../api/gymshop'
import type { Product } from '../../api/types'
import { money } from '../../config/storefront'
import { LOW_STOCK_THRESHOLD } from './adminConfig'
import { describeAdminError } from './adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from './adminUi'

type StockFilter = 'all' | 'available' | 'low' | 'none'
type StatusFilter = 'all' | 'active' | 'inactive'

export function ProductsAdmin() {
  const location = useLocation()
  const routeNotice = (location.state as { productNotice?: string } | null)?.productNotice || ''
  const [products, setProducts] = useState<Product[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [mutationError, setMutationError] = useState('')
  const [success, setSuccess] = useState(routeNotice)
  const [pending, setPending] = useState<number | null>(null)
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('all')
  const [status, setStatus] = useState<StatusFilter>('all')
  const [stock, setStock] = useState<StockFilter>('all')

  const loadInitial = useCallback(async () => {
    setLoading(true); setLoadError('')
    try { setProducts(await api.products(true)) } catch (error) { setLoadError(describeAdminError(error)) } finally { setLoading(false) }
  }, [])
  useEffect(() => { void loadInitial() }, [loadInitial])

  const categories = useMemo(() => [...new Set(products.map(product => product.category?.name).filter((value): value is string => Boolean(value)))].sort(), [products])
  const filtered = useMemo(() => products.filter(product => {
    const matchesSearch = product.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())
    const matchesCategory = category === 'all' || product.category?.name === category
    const matchesStatus = status === 'all' || (status === 'active' ? product.isActive : !product.isActive)
    const matchesStock = stock === 'all' || (stock === 'available' && product.stock > 0) || (stock === 'low' && product.stock > 0 && product.stock <= LOW_STOCK_THRESHOLD) || (stock === 'none' && product.stock === 0)
    return matchesSearch && matchesCategory && matchesStatus && matchesStock
  }), [products, search, category, status, stock])
  const hasFilters = Boolean(search || category !== 'all' || status !== 'all' || stock !== 'all')

  const mutate = async (product: Product, action: () => Promise<void>, message: string) => {
    if (pending !== null) return
    setPending(product.id); setLoadError(''); setMutationError(''); setSuccess('')
    try {
      await action()
    } catch (error) {
      setMutationError(describeAdminError(error)); setPending(null); return
    }
    try {
      setProducts(await api.products(true)); setSuccess(message)
    } catch (error) {
      setMutationError(`El cambio pudo realizarse, pero no se pudo actualizar el listado. ${describeAdminError(error)}`)
    } finally {
      setPending(null)
    }
  }
  const changeStatus = (product: Product) => {
    const action = product.isActive ? 'desactivar' : 'activar'
    if (!window.confirm(`¿Querés ${action} “${product.name}”?`)) return
    void mutate(product, () => api.setProductStatus(product.id, !product.isActive), `${product.name} fue ${product.isActive ? 'desactivado' : 'activado'} correctamente.`)
  }
  const clear = () => { setSearch(''); setCategory('all'); setStatus('all'); setStock('all') }

  return <section className="admin-page">
    <div className="admin-page-heading"><div><p className="eyebrow">CATÁLOGO</p><h1>Productos</h1><p>Consultá el catálogo completo y administrá disponibilidad y stock.</p></div><div className="admin-heading-actions"><span>{filtered.length} de {products.length}</span><Link className="primary link-button" to="/admin/productos/nuevo">Nuevo producto</Link></div></div>
    <AdminFeedback error={loadError || mutationError} success={success} />
    <div className="admin-filters" aria-label="Filtros de productos">
      <label className="admin-search">Buscar por nombre<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Ej. mancuerna" /></label>
      <label>Categoría<select value={category} onChange={event => setCategory(event.target.value)}><option value="all">Todas</option>{categories.map(value => <option key={value}>{value}</option>)}</select></label>
      <label>Estado<select value={status} onChange={event => setStatus(event.target.value as StatusFilter)}><option value="all">Todos</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select></label>
      <label>Stock<select value={stock} onChange={event => setStock(event.target.value as StockFilter)}><option value="all">Todos</option><option value="available">Con stock</option><option value="low">Stock bajo</option><option value="none">Sin stock</option></select></label>
      {hasFilters && <button type="button" onClick={clear}>Limpiar</button>}
    </div>
    {loading && products.length === 0 ? <AdminLoading label="Cargando productos…" /> : loadError && products.length === 0 ? <button onClick={() => void loadInitial()}>Reintentar</button> : products.length === 0 ? <AdminEmpty>No hay productos cargados.</AdminEmpty> : filtered.length === 0 ? <AdminEmpty>No hay productos que coincidan con los filtros. <button type="button" className="text-button" onClick={clear}>Limpiar filtros</button></AdminEmpty> :
      <div className="admin-product-list"><div className="admin-product-header" aria-hidden="true"><span>Producto</span><span>Precio</span><span>Stock</span><span>Estado</span><span>Acción</span></div>{filtered.map(product => {
        const isPending = pending === product.id
        const low = product.stock > 0 && product.stock <= LOW_STOCK_THRESHOLD
        return <article className="admin-product-row" key={product.id} aria-busy={isPending}>
          <div><strong>{product.name}</strong><span>{product.category?.name || 'Sin categoría'}</span></div>
          <div data-label="Precio"><strong>{money(product.price)}</strong></div>
          <div data-label="Stock"><strong>{product.stock}</strong>{low && <small className="low-stock"> Stock bajo</small>}{product.stock === 0 && <small className="no-stock"> Sin stock</small>}</div>
          <div data-label="Estado"><span className={product.isActive ? 'admin-pill active' : 'admin-pill inactive'}>{product.isActive ? 'Activo' : 'Inactivo'}</span></div>
          <div className="admin-product-actions"><Link to={`/admin/productos/${product.id}/editar`}>Editar</Link><button type="button" disabled={pending !== null} onClick={() => changeStatus(product)}>{isPending ? 'Guardando…' : product.isActive ? 'Desactivar' : 'Activar'}</button></div>
        </article>
      })}</div>}
    <p className="admin-footnote">La API entrega actualmente la lista completa; los filtros se aplican en esta pantalla y no se simula paginación.</p>
  </section>
}

import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../../api/gymshop'
import type { Product, StockMovement, StockMovementType } from '../../api/types'

type StockFilter = 'all' | 'available' | 'low' | 'none'
interface MovementFilters { productId: string; type: string; from: string; to: string }
const LOW_STOCK = 5
const labels: Record<StockMovementType, string> = { InitialStock: 'Stock inicial', Sale: 'Venta', CancellationReturn: 'Cancelación o devolución', ManualEntry: 'Ingreso manual', ManualCorrection: 'Corrección manual', LossDamage: 'Pérdida o rotura' }
const emptyMovementFilters = (): MovementFilters => ({ productId: '', type: '', from: '', to: '' })

export function StockAdmin() {
  const [searchParams] = useSearchParams()
  const initialFilter = searchParams.get('estado')
  const [products, setProducts] = useState<Product[]>([]); const [loading, setLoading] = useState(true); const [error, setError] = useState('')
  const [search, setSearch] = useState(''); const [filter, setFilter] = useState<StockFilter>(initialFilter === 'low' || initialFilter === 'none' || initialFilter === 'available' ? initialFilter : 'all'); const [selected, setSelected] = useState<Product | null>(null)
  const [movementsRefresh, setMovementsRefresh] = useState(0)
  const load = () => { setLoading(true); setError(''); api.products(true).then(setProducts).catch(e => setError(e instanceof Error ? e.message : 'No se pudo cargar el stock.')).finally(() => setLoading(false)) }
  useEffect(load, [])
  const filtered = useMemo(() => products.filter(p => {
    const matchesSearch = p.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())
    return matchesSearch && (filter === 'all' || (filter === 'available' && p.stock > 0) || (filter === 'low' && p.stock > 0 && p.stock <= LOW_STOCK) || (filter === 'none' && p.stock === 0))
  }), [products, search, filter])
  const updateProduct = (id: number, stock: number, variantId?: number | null) => setProducts(current => current.map(p => {
    if (p.id !== id) return p
    if (!variantId) return { ...p, stock }
    const variants = (p.variants ?? []).map(v => v.id === variantId ? { ...v, stock } : v)
    return { ...p, variants, stock: variants.filter(v => v.isActive).reduce((sum, v) => sum + v.stock, 0) }
  }))

  return <section className="admin-section stock-admin">
    <div className="admin-page-heading"><div><p className="eyebrow">INVENTARIO</p><h1>Stock</h1><p>Consultá existencias, registrá ajustes y revisá cada movimiento.</p></div></div>
    <div className="admin-filters"><label>Buscar<input value={search} placeholder="Nombre del producto" onChange={e => setSearch(e.target.value)} /></label><label>Estado<select value={filter} onChange={e => setFilter(e.target.value as StockFilter)}><option value="all">Todos</option><option value="available">Con stock</option><option value="low">Stock bajo</option><option value="none">Sin stock</option></select></label></div>
    {loading && <p role="status">Cargando stock…</p>}{error && <div className="admin-error" role="alert">{error} <button onClick={load}>Reintentar</button></div>}
    {!loading && !error && filtered.length === 0 && <div className="admin-empty">No hay productos para los filtros seleccionados.</div>}
    {!loading && !error && filtered.length > 0 && <div className="stock-table-wrap"><table className="dashboard-table stock-table"><thead><tr><th>Producto</th><th>Estado</th><th>Stock actual</th><th>Acciones</th></tr></thead><tbody>{filtered.map(p => <tr key={p.id}><td><strong>{p.name}</strong>{!p.isActive && <small> Inactivo</small>}</td><td>{p.stock === 0 ? 'Sin stock' : p.stock <= LOW_STOCK ? 'Stock bajo' : 'Con stock'}</td><td><strong>{p.stock}</strong></td><td><button className="manage-stock-button" onClick={() => setSelected(p)}>Gestionar</button></td></tr>)}</tbody></table></div>}
    {!loading && !error && <GlobalMovements products={products} refreshSignal={movementsRefresh} />}
    {selected && <StockDialog product={products.find(p => p.id === selected.id) || selected} onClose={() => setSelected(null)} onAdjusted={(stock, variantId) => { updateProduct(selected.id, stock, variantId); setMovementsRefresh(value => value + 1) }} />}
  </section>
}

function GlobalMovements({ products, refreshSignal }: { products: Product[]; refreshSignal: number }) {
  const [draft, setDraft] = useState<MovementFilters>(emptyMovementFilters); const [applied, setApplied] = useState<MovementFilters>(emptyMovementFilters)
  const [movements, setMovements] = useState<StockMovement[]>([]); const [page, setPage] = useState(1); const [totalPages, setTotalPages] = useState(0)
  const [loading, setLoading] = useState(true); const [error, setError] = useState(''); const [reload, setReload] = useState(0)
  useEffect(() => {
    let active = true; setLoading(true); setError('')
    api.stockMovements({ page, pageSize: 20, productId: applied.productId ? Number(applied.productId) : undefined, type: applied.type || undefined, fromUtc: applied.from ? `${applied.from}T00:00:00.000Z` : undefined, toUtc: applied.to ? `${applied.to}T23:59:59.999Z` : undefined })
      .then(result => { if (active) { setMovements(result.items); setTotalPages(result.totalPages) } })
      .catch(reason => { if (active) setError(reason instanceof Error ? reason.message : 'No se pudieron cargar los movimientos.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [applied, page, reload, refreshSignal])
  const apply = (event: React.FormEvent) => { event.preventDefault(); setPage(1); setApplied({ ...draft }); setReload(value => value + 1) }
  const clear = () => { const empty = emptyMovementFilters(); setDraft(empty); setApplied(empty); setPage(1); setReload(value => value + 1) }

  return <section className="stock-movements" aria-labelledby="movements-title"><div className="admin-page-heading"><div><h2 id="movements-title">Movimientos</h2><p>Historial global auditable de entradas y salidas.</p></div></div>
    <form className="admin-filters movement-filters" onSubmit={apply}><label>Producto<select value={draft.productId} onChange={event => setDraft(current => ({ ...current, productId: event.target.value }))}><option value="">Todos</option>{products.map(product => <option key={product.id} value={product.id}>{product.name}</option>)}</select></label><label>Tipo<select value={draft.type} onChange={event => setDraft(current => ({ ...current, type: event.target.value }))}><option value="">Todos</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><label>Desde<input aria-label="Movimientos desde" type="date" value={draft.from} onChange={event => setDraft(current => ({ ...current, from: event.target.value }))} /></label><label>Hasta<input aria-label="Movimientos hasta" type="date" value={draft.to} onChange={event => setDraft(current => ({ ...current, to: event.target.value }))} /></label><button className="primary" type="submit">Aplicar</button><button type="button" onClick={clear}>Limpiar</button></form>
    {loading && <p role="status">Cargando movimientos…</p>}{error && <div className="admin-error" role="alert">{error} <button onClick={() => setReload(value => value + 1)}>Reintentar</button></div>}
    {!loading && !error && movements.length === 0 && <div className="admin-empty">No hay movimientos para los filtros seleccionados.</div>}
    {!loading && !error && movements.length > 0 && <><MovementTable movements={movements} /><div className="pagination"><button disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {totalPages}</span><button disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Siguiente</button></div></>}
  </section>
}

function MovementTable({ movements }: { movements: StockMovement[] }) {
  return <div className="stock-history"><table className="dashboard-table"><thead><tr><th>Fecha</th><th>Producto</th><th>Tipo</th><th>Variación</th><th>Stock</th><th>Motivo</th><th>Pedido</th><th>Responsable</th></tr></thead><tbody>{movements.map(m => <tr key={m.id}><td>{new Date(m.createdAtUtc).toLocaleString()}</td><td>{m.productName}{m.variantSku ? <small> · {m.variantSku}</small> : null}</td><td>{labels[m.type]}</td><td className={m.quantity > 0 ? 'in-stock' : 'no-stock'}>{m.quantity > 0 ? '+' : ''}{m.quantity}</td><td>{m.previousStock} → {m.resultingStock}</td><td>{m.reason}</td><td>{m.orderId ? <Link to={`/admin/pedidos?orderId=${m.orderId}`}>#{m.orderId}</Link> : '—'}</td><td>{m.actorName || (m.actorUserId ? `Usuario #${m.actorUserId}` : 'Sistema')}</td></tr>)}</tbody></table></div>
}

function StockDialog({ product, onClose, onAdjusted }: { product: Product; onClose(): void; onAdjusted(stock: number, variantId?: number | null): void }) {
  const [movements, setMovements] = useState<StockMovement[]>([]); const [historyLoading, setHistoryLoading] = useState(true); const [historyError, setHistoryError] = useState('')
  const [page, setPage] = useState(1); const [totalPages, setTotalPages] = useState(0)
  const [quantity, setQuantity] = useState(''); const [reason, setReason] = useState(''); const [pending, setPending] = useState(false); const [notice, setNotice] = useState('')
  const activeVariants = (product.variants ?? []).filter(v => v.isActive); const [variantId, setVariantId] = useState('')
  const variant = activeVariants.find(v => v.id === Number(variantId)); const currentStock = activeVariants.length ? variant?.stock ?? 0 : product.stock
  const numeric = Number(quantity); const delta = numeric; const resulting = Number.isInteger(numeric) && numeric !== 0 ? currentStock + delta : currentStock
  const loadHistory = () => { setHistoryLoading(true); setHistoryError(''); api.productStockMovements(product.id, page).then(r => { setMovements(r.items); setTotalPages(r.totalPages) }).catch(e => setHistoryError(e instanceof Error ? e.message : 'No se pudo cargar el historial.')).finally(() => setHistoryLoading(false)) }
  useEffect(loadHistory, [product.id, page])
  const submit = async (event: React.FormEvent) => {
    event.preventDefault(); if (pending || (activeVariants.length > 0 && !variant) || !Number.isInteger(numeric) || numeric === 0 || resulting < 0 || !reason.trim()) return
    setPending(true); setNotice('')
    try { const result = await api.adjustStock(product.id, { quantity: numeric, reason: reason.trim(), productVariantId: variant?.id ?? null }); onAdjusted(result.resultingStock, variant?.id); if (page === 1) setMovements(current => [result.movement, ...current].slice(0, 20)); else setPage(1); setQuantity(''); setReason(''); setNotice('Stock actualizado correctamente.') }
    catch (e) { setNotice(e instanceof Error ? e.message : 'No se pudo ajustar el stock.') } finally { setPending(false) }
  }
  return <div className="modal-backdrop" role="presentation"><section className="stock-dialog" role="dialog" aria-modal="true" aria-labelledby="stock-title"><button className="modal-close" aria-label="Cerrar" onClick={onClose}>Cerrar <span aria-hidden="true">×</span></button><h2 id="stock-title">Stock de {product.name}</h2>
    <form onSubmit={submit}>{activeVariants.length > 0 && <label>Combinación<select aria-label="Combinación" value={variantId} onChange={e => { setVariantId(e.target.value); setQuantity(''); setNotice('') }} required><option value="">Seleccionar combinación</option>{activeVariants.map(v => <option key={v.id} value={v.id}>{Object.entries(v.attributes).map(([name, value]) => `${name}: ${value}`).join(' · ')} · {v.sku} ({v.stock})</option>)}</select></label>}<label>Cantidad (+ suma / − resta)<input aria-label="Cantidad" type="number" step="1" value={quantity} placeholder="Ej.: 5 o -2" onChange={e => setQuantity(e.target.value)} required /></label><small>Usá un número positivo para ingresar unidades o negativo para descontarlas.</small><label>Motivo<textarea value={reason} maxLength={500} onChange={e => setReason(e.target.value)} required /></label><div className="stock-preview"><span>Anterior <strong>{activeVariants.length && !variant ? '—' : currentStock}</strong></span><span>Variación <strong>{Number.isFinite(delta) && numeric !== 0 ? `${delta > 0 ? '+' : ''}${delta}` : '—'}</strong></span><span>Resultante <strong className={resulting < 0 ? 'no-stock' : ''}>{activeVariants.length && !variant ? '—' : resulting}</strong></span></div>{resulting < 0 && <p className="no-stock" role="alert">El stock resultante no puede ser negativo.</p>}<button className="primary" disabled={pending || (activeVariants.length > 0 && !variant) || !Number.isInteger(numeric) || numeric === 0 || resulting < 0 || !reason.trim()}>{pending ? 'Guardando…' : 'Confirmar ajuste'}</button>{notice && <p role="status">{notice}</p>}</form>
    <h3>Historial</h3>{historyLoading && <p role="status">Cargando historial…</p>}{historyError && <p role="alert">{historyError} <button onClick={loadHistory}>Reintentar</button></p>}{!historyLoading && !historyError && movements.length === 0 && <p className="admin-empty">Todavía no hay movimientos.</p>}{movements.length > 0 && <><MovementTable movements={movements} />{totalPages > 1 && <div className="pagination"><button disabled={page === 1 || historyLoading} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page} de {totalPages}</span><button disabled={page === totalPages || historyLoading} onClick={() => setPage(value => value + 1)}>Siguiente</button></div>}</>}</section></div>
}

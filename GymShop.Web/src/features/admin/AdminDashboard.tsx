import { useCallback, useEffect, useId, useState } from 'react'
import { api } from '../../api/gymshop'
import type { DashboardFilters, DashboardStatistics, OrderStatus } from '../../api/types'
import { money, storefront } from '../../config/storefront'
import { describeAdminError } from './adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from './adminUi'

const statusLabels: Record<OrderStatus, string> = {
  Pending: 'Pendientes', Paid: 'Pagados', Preparing: 'En preparación', Shipped: 'Enviados / listos para retirar',
  Delivered: 'Entregados / retirados', Canceled: 'Cancelados', Refunded: 'Reembolsados',
}

export function AdminDashboard() {
  const [filters, setFilters] = useState<DashboardFilters>({ period: '30d' })
  const [data, setData] = useState<DashboardStatistics | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const load = useCallback(async (nextFilters: DashboardFilters) => {
    setLoading(true); setError('')
    try { setData(await api.dashboard(nextFilters)) }
    catch (value) { setError(describeAdminError(value)) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { void load({ period: '30d' }) }, [load])

  return <section className="admin-page dashboard-page">
    <div className="admin-page-heading"><div><p className="eyebrow">PANEL COMERCIAL</p><h1>Resumen</h1><p>Ventas confirmadas, pedidos y salud del catálogo.</p></div></div>
    <PeriodFilters filters={filters} onChange={next => { setFilters(next); void load(next) }} disabled={loading} />
    <AdminFeedback error={error} />
    {loading && !data && <AdminLoading label="Cargando estadísticas…" />}
    {error && <button className="secondary dashboard-retry" type="button" onClick={() => void load(filters)}>Reintentar</button>}
    {data && <div aria-busy={loading} className={loading ? 'dashboard-content is-loading' : 'dashboard-content'}>
      <div className="dashboard-kpis">
        <Kpi label="Ventas confirmadas" value={money(data.totalSales)} />
        <Kpi label="Pedidos pagados" value={String(data.paidOrders)} />
        <Kpi label="Ticket promedio" value={money(data.averageTicket)} />
      </div>
      <div className="dashboard-grid">
        <section className="dashboard-panel"><div className="dashboard-panel-heading"><h2>Ventas por día</h2><span>Sólo días con ventas</span></div><SalesChart data={data.salesByDay} timeZone={data.timeZoneId} /></section>
        <section className="dashboard-panel"><h2>Pedidos por estado</h2><StatusSummary data={data.ordersByStatus} /></section>
      </div>
      <section className="dashboard-panel"><div className="dashboard-panel-heading"><h2>Productos más vendidos</h2><span>Hasta 10 en el período · <a href="/admin/pedidos">Ver pedidos</a></span></div><TopProducts data={data.topProducts} /></section>
      <div className="dashboard-grid">
        <StockPanel title="Sin stock" kind="danger" products={data.outOfStockProducts} empty="No hay productos activos sin stock." filter="none" />
        <StockPanel title={`Stock bajo (1–${data.lowStockThreshold})`} kind="warning" products={data.lowStockProducts} empty="No hay productos activos con stock bajo." filter="low" />
      </div>
      <p className="admin-footnote">Período y días de venta según {data.timeZoneId}. Los reembolsos totales y los pagos marcados con reembolso parcial se excluyen por completo.</p>
    </div>}
  </section>
}

function PeriodFilters({ filters, onChange, disabled }: { filters: DashboardFilters; onChange(filters: DashboardFilters): void; disabled: boolean }) {
  const fromId = useId(); const toId = useId()
  const [from, setFrom] = useState(''); const [to, setTo] = useState('')
  return <div className="dashboard-filters" aria-label="Período de estadísticas">
    <div className="dashboard-periods" role="group" aria-label="Períodos rápidos">
      {([['7d', '7 días'], ['30d', '30 días'], ['month', 'Mes actual']] as const).map(([period, label]) =>
        <button key={period} type="button" disabled={disabled} className={filters.period === period ? 'is-active' : ''} aria-pressed={filters.period === period} onClick={() => onChange({ period })}>{label}</button>)}
    </div>
    <form onSubmit={event => { event.preventDefault(); if (from && to) onChange({ from, to }) }}>
      <label htmlFor={fromId}>Desde</label><input id={fromId} type="date" required value={from} onChange={event => setFrom(event.target.value)} />
      <label htmlFor={toId}>Hasta</label><input id={toId} type="date" required value={to} onChange={event => setTo(event.target.value)} />
      <button className="secondary" type="submit" disabled={disabled || !from || !to}>Aplicar</button>
    </form>
  </div>
}

function Kpi({ label, value }: { label: string; value: string }) { return <article><span>{label}</span><strong>{value}</strong></article> }

function SalesChart({ data, timeZone }: { data: DashboardStatistics['salesByDay']; timeZone: string }) {
  if (!data.length) return <AdminEmpty>No hay ventas confirmadas en este período.</AdminEmpty>
  const max = Math.max(...data.map(item => item.amount), 1)
  return <div className="sales-chart" role="img" aria-label="Gráfico de ventas confirmadas por día">
    {data.map(item => <div className={`sales-bar-column ${item.amount === max ? 'is-peak' : ''}`} key={item.date} title={`${formatDay(item.date, timeZone)}: ${money(item.amount)}`}>
      <span>{money(item.amount)}</span><div className="sales-bar-track"><i style={{ height: `${Math.max(4, item.amount / max * 100)}%` }} /></div><small>{formatDay(item.date, timeZone)}</small>
    </div>)}
  </div>
}

function StatusSummary({ data }: { data: DashboardStatistics['ordersByStatus'] }) {
  if (!data.length) return <AdminEmpty>No hay pedidos creados en este período.</AdminEmpty>
  const total = data.reduce((sum, item) => sum + item.count, 0)
  return <ul className="status-summary">{data.map(item => <li key={item.status}><span>{statusLabels[item.status]}</span><i><b style={{ width: `${item.count / total * 100}%` }} /></i><strong>{item.count}</strong></li>)}</ul>
}

function TopProducts({ data }: { data: DashboardStatistics['topProducts'] }) {
  if (!data.length) return <AdminEmpty>No hay productos vendidos en este período.</AdminEmpty>
  return <div className="dashboard-table-wrap"><table className="dashboard-table"><thead><tr><th>Producto</th><th>Cantidad</th><th>Importe</th></tr></thead><tbody>{data.map(item => <tr key={item.productId}><td>{item.productName}</td><td>{item.quantity}</td><td>{money(item.amount)}</td></tr>)}</tbody></table></div>
}

function StockPanel({ title, products, empty, kind, filter }: { title: string; products: DashboardStatistics['lowStockProducts']; empty: string; kind: string; filter: 'low' | 'none' }) {
  return <section className={`dashboard-panel stock-panel ${kind}`}><h2>{title}<span>{products.length}</span></h2>{products.length ? <ul>{products.map(product => <li key={product.productId}><span>{product.productName}</span><strong>{product.stock} u.</strong></li>)}</ul> : <AdminEmpty>{empty}</AdminEmpty>}<a className="stock-panel-link" href={`/admin/stock?estado=${filter}`}>Ver y gestionar todos →</a></section>
}

function formatDay(value: string, timeZone: string) {
  return new Intl.DateTimeFormat(storefront.market.locale, { day: '2-digit', month: '2-digit', timeZone }).format(new Date(`${value}T12:00:00Z`))
}

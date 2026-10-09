import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AdminDashboard } from './AdminDashboard'

const dashboard = {
  fromUtc: '2026-09-01T03:00:00Z', toUtc: '2026-09-22T03:00:00Z', timeZoneId: 'America/Argentina/Buenos_Aires', totalSales: 250, paidOrders: 2, averageTicket: 125,
  ordersByStatus: [{ status: 'Paid', count: 2 }],
  salesByDay: [{ date: '2026-09-20', amount: 250, orders: 2 }],
  topProducts: [{ productId: 1, productName: 'Mancuerna', quantity: 3, amount: 250 }],
  outOfStockProducts: [{ productId: 2, productName: 'Banco', stock: 0, isActive: true }],
  lowStockProducts: [{ productId: 3, productName: 'Banda', stock: 2, isActive: true }], lowStockThreshold: 5,
}

const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

describe('AdminDashboard', () => {
  afterEach(() => vi.restoreAllMocks())

  it('muestra carga y luego todas las métricas comerciales', async () => {
    let resolveRequest!: (value: Response) => void
    vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise(resolve => { resolveRequest = resolve }))
    render(<AdminDashboard />)
    expect(screen.getByRole('status')).toHaveTextContent('Cargando estadísticas')
    resolveRequest(await response(dashboard))
    expect(await screen.findByText('Ventas confirmadas')).toBeInTheDocument()
    expect(screen.getAllByText('$ 250,00')).toHaveLength(3)
    expect(screen.getByText('Mancuerna')).toBeInTheDocument()
    expect(screen.getByText('Banco')).toBeInTheDocument()
    expect(screen.getByText('Banda')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /ventas confirmadas por día/i })).toBeInTheDocument()
  })

  it('distingue un período vacío de un error', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => response({ ...dashboard, totalSales: 0, paidOrders: 0, averageTicket: 0, ordersByStatus: [], salesByDay: [], topProducts: [], outOfStockProducts: [], lowStockProducts: [] }))
    render(<AdminDashboard />)
    expect(await screen.findByText('No hay ventas confirmadas en este período.')).toBeInTheDocument()
    expect(screen.getByText('No hay pedidos creados en este período.')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('muestra el error y permite reintentar', async () => {
    let calls = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => ++calls === 1 ? response({ message: 'Falló el dashboard.' }, 500) : response(dashboard))
    render(<AdminDashboard />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Falló el dashboard.')
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByText('Mancuerna')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(calls).toBe(2)
  })

  it('aplica períodos rápidos y fechas personalizadas', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => response(dashboard))
    render(<AdminDashboard />)
    await screen.findByText('Mancuerna')
    await userEvent.click(screen.getByRole('button', { name: '7 días' }))
    await waitFor(() => expect(fetchMock).toHaveBeenLastCalledWith(expect.stringContaining('period=7d'), expect.anything()))
    await userEvent.type(screen.getByLabelText('Desde'), '2026-09-01')
    await userEvent.type(screen.getByLabelText('Hasta'), '2026-09-20')
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }))
    await waitFor(() => expect(fetchMock).toHaveBeenLastCalledWith(expect.stringContaining('from=2026-09-01&to=2026-09-20'), expect.anything()))
  })
})

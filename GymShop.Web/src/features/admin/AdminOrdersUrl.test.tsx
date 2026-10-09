import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'

const json = (body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
const order = { id: 9, userId: 2, userEmail: 'cliente@email.com', userName: 'Cliente', createdAt: '2026-09-10T12:00:00Z', updatedAt: null, total: 100, status: 'Paid', lastPaymentStatus: 'Approved', lastPaymentId: 1 }

function signIn() { localStorage.setItem('gymshop.token', 'jwt'); localStorage.setItem('gymshop.user', JSON.stringify({ id: 1, email: 'admin@test.com', name: 'Admin', role: 'Admin' })) }
function setup(path: string) {
  signIn(); window.history.replaceState(null, '', path)
  const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(input => {
    const url = String(input)
    if (url.includes('/api/cart')) return json({ id: 1, userId: 1, total: 0, items: [] })
    if (url.includes('/api/orders?')) { const page = Number(new URL(url).searchParams.get('page')) || 1; return json({ items: [order], page, pageSize: 20, totalItems: 60, totalPages: 3 }) }
    return json([])
  })
  render(<App />)
  return { fetchMock, calls: () => fetchMock.mock.calls.filter(([input]) => String(input).includes('/api/orders?')) }
}

describe('filtros URL de AdminOrders', () => {
  beforeEach(() => { localStorage.clear(); window.history.replaceState(null, '', '/'); vi.restoreAllMocks() })

  it('inicializa controles y API desde una URL válida', async () => {
    const { calls } = setup('/admin/pedidos?status=Paid&from=2026-09-01&to=2026-09-30&page=2&search=cliente%40email.com')
    await screen.findByText('#9'); expect(screen.getByRole('searchbox', { name: 'Buscar' })).toHaveValue('cliente@email.com'); expect(screen.getByLabelText('Estado')).toHaveValue('Paid'); expect(screen.getByLabelText('Desde')).toHaveValue('2026-09-01'); expect(screen.getByLabelText('Hasta')).toHaveValue('2026-09-30')
    const request = new URL(String(calls()[0][0])); expect(request.searchParams.get('page')).toBe('2'); expect(request.searchParams.get('status')).toBe('Paid'); expect(request.searchParams.get('fromUtc')).toBe(new Date('2026-09-01T00:00:00').toISOString()); expect(request.searchParams.get('toUtc')).toBe(new Date('2026-09-30T23:59:59.999').toISOString())
  })

  it('descarta from=abc sin romper ni enviarlo al backend', async () => { const { calls } = setup('/admin/pedidos?from=abc'); await screen.findByText('#9'); expect(window.location.search).toBe(''); expect(new URL(String(calls()[0][0])).searchParams.has('fromUtc')).toBe(false); expect(calls()).toHaveLength(1) })
  it('descarta una fecha inexistente', async () => { const { calls } = setup('/admin/pedidos?from=2026-02-30'); await screen.findByText('#9'); expect(window.location.search).toBe(''); expect(new URL(String(calls()[0][0])).searchParams.has('fromUtc')).toBe(false) })

  it.each(['0', '-1', '1.5', 'abc'])('normaliza page=%s a la primera página', async value => {
    const { calls } = setup(`/admin/pedidos?page=${encodeURIComponent(value)}`); await screen.findByText('#9'); expect(window.location.search).toBe(''); expect(new URL(String(calls()[0][0])).searchParams.get('page')).toBe('1'); expect(calls()).toHaveLength(1)
  })

  it('elimina estados desconocidos y parámetros repetidos', async () => { const { calls } = setup('/admin/pedidos?status=Unknown&search=uno&search=dos'); await screen.findByText('#9'); expect(window.location.search).toBe(''); const request = new URL(String(calls()[0][0])); expect(request.searchParams.has('status')).toBe(false); expect(request.searchParams.has('search')).toBe(false) })
  it('descarta completamente un rango invertido', async () => { const { calls } = setup('/admin/pedidos?from=2026-09-30&to=2026-09-01'); await screen.findByText('#9'); expect(window.location.search).toBe(''); const request = new URL(String(calls()[0][0])); expect(request.searchParams.has('fromUtc')).toBe(false); expect(request.searchParams.has('toUtc')).toBe(false) })

  it('conserva filtros válidos al paginar', async () => {
    const { calls } = setup('/admin/pedidos?search=%239&status=Paid&from=2026-09-01&to=2026-09-30&page=2'); await screen.findByText('#9'); await userEvent.click(screen.getByRole('button', { name: 'Siguiente' })); await waitFor(() => expect(calls()).toHaveLength(2)); expect(window.location.search).toContain('search=%239'); expect(window.location.search).toContain('status=Paid'); expect(window.location.search).toContain('from=2026-09-01'); expect(window.location.search).toContain('to=2026-09-30'); expect(window.location.search).toContain('page=3')
  })

  it('Limpiar elimina todos los parámetros', async () => { const { calls } = setup('/admin/pedidos?search=cliente%40email.com&status=Paid&page=2'); await screen.findByText('#9'); await userEvent.click(screen.getByRole('button', { name: 'Limpiar' })); await waitFor(() => expect(calls()).toHaveLength(2)); expect(window.location.search).toBe(''); expect(screen.getByRole('searchbox', { name: 'Buscar' })).toHaveValue('') })
  it('normaliza una URL inválida con una sola consulta', async () => { const { calls } = setup('/admin/pedidos?from=abc&page=-1&status=Nope&search='); await screen.findByText('#9'); await waitFor(() => expect(window.location.search).toBe('')); expect(calls()).toHaveLength(1) })
})

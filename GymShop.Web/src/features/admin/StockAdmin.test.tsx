import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { StockAdmin } from './StockAdmin'

const product = { id: 1, name: 'Mancuerna', description: null, price: 100, stock: 4, imageUrl: null, isActive: true, category: null }
const variantProduct = { ...product, id: 2, name: 'Remera', stock: 7, variants: [{ id: 21, sku: 'REM-NEG-M', price: 100, stock: 2, isActive: true, attributes: { Color: 'Negro', Talle: 'M' } }, { id: 22, sku: 'REM-AZU-L', price: 100, stock: 5, isActive: true, attributes: { Color: 'Azul', Talle: 'L' } }] }
const movement = { id: 4, productId: 1, productName: 'Mancuerna', type: 'InitialStock', quantity: 4, previousStock: 0, resultingStock: 4, reason: 'Stock inicial del producto', actorUserId: 2, actorName: 'Admin', orderId: null, createdAtUtc: '2026-09-21T12:00:00Z' }
const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

describe('StockAdmin', () => {
  afterEach(() => vi.restoreAllMocks())

  it('filtra productos y completa un ajuste mostrando la previsualización', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/movements')) return response({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 })
      if (url.includes('/adjustments') && init?.method === 'POST') return response({ productId: 1, previousStock: 4, resultingStock: 7, movement: { id: 9, productId: 1, productName: 'Mancuerna', type: 'ManualEntry', quantity: 3, previousStock: 4, resultingStock: 7, reason: 'Ingreso', actorUserId: 2, actorName: 'Admin', orderId: null, createdAtUtc: new Date().toISOString() } })
      return response([product])
    })
    render(<MemoryRouter><StockAdmin /></MemoryRouter>)
    expect(await screen.findByRole('button', { name: 'Gestionar' })).toBeInTheDocument()
    await userEvent.type(screen.getByPlaceholderText('Nombre del producto'), 'manc')
    await userEvent.click(screen.getByRole('button', { name: 'Gestionar' }))
    await userEvent.type(screen.getByLabelText('Cantidad'), '3')
    await userEvent.type(screen.getByLabelText('Motivo'), 'Ingreso')
    expect(screen.getByText('7', { selector: '.stock-preview strong' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar ajuste' }))
    expect(await screen.findByText('Stock actualizado correctamente.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/adjustments'))).toHaveLength(1)
  })

  it('exige una combinación y envía el ajuste a la variante seleccionada', async () => {
    let body: Record<string, unknown> | undefined
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/adjustments')) { body = JSON.parse(String(init?.body)); return response({ productId: 2, previousStock: 2, resultingStock: 5, movement: { ...movement, id: 11, productId: 2, productName: 'Remera', productVariantId: 21, variantSku: 'REM-NEG-M', quantity: 3, previousStock: 2, resultingStock: 5 } }) }
      if (url.includes('/movements')) return response({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 })
      return response([variantProduct])
    })
    render(<MemoryRouter><StockAdmin /></MemoryRouter>); await userEvent.click(await screen.findByRole('button', { name: 'Gestionar' }))
    expect(screen.getByRole('button', { name: 'Confirmar ajuste' })).toBeDisabled()
    await userEvent.selectOptions(screen.getByLabelText('Combinación'), '21'); await userEvent.type(screen.getByLabelText('Cantidad'), '3'); await userEvent.type(screen.getByLabelText('Motivo'), 'Reposición'); await userEvent.click(screen.getByRole('button', { name: 'Confirmar ajuste' }))
    await waitFor(() => expect(body).toEqual(expect.objectContaining({ productVariantId: 21, quantity: 3 })))
  })

  it('evita doble envío y muestra el error del servidor', async () => {
    let reject!: (error: Error) => void
    const pending = new Promise<Response>((_, rejectPromise) => { reject = rejectPromise })
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input) => String(input).includes('/adjustments') ? pending : String(input).includes('/movements') ? response({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 }) : response([product]))
    render(<MemoryRouter><StockAdmin /></MemoryRouter>)
    await screen.findByRole('button', { name: 'Gestionar' }); await userEvent.click(screen.getByRole('button', { name: 'Gestionar' }))
    await userEvent.type(screen.getByLabelText('Cantidad'), '2'); await userEvent.type(screen.getByLabelText('Motivo'), 'Control')
    const submit = screen.getByRole('button', { name: 'Confirmar ajuste' }); await userEvent.dblClick(submit)
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/adjustments'))).toHaveLength(1)
    reject(new Error('Falló el ajuste'))
    expect(await screen.findByText('Falló el ajuste')).toBeInTheDocument()
    await waitFor(() => expect(submit).toBeEnabled())
  })

  it('refresca una sola vez el historial global con los filtros y la página aplicados después de un ajuste exitoso', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/adjustments') && init?.method === 'POST') return response({ productId: 1, previousStock: 4, resultingStock: 6, movement: { ...movement, id: 10, type: 'ManualEntry', quantity: 2, previousStock: 4, resultingStock: 6, reason: 'Ingreso' } })
      if (url.includes('/api/stock/movements')) return response({ items: [movement], page: 1, pageSize: 20, totalItems: 21, totalPages: 2 })
      if (url.includes('/api/stock/products/1/movements')) return response({ items: [movement], page: 1, pageSize: 20, totalItems: 1, totalPages: 1 })
      return response([product])
    })
    render(<MemoryRouter><StockAdmin /></MemoryRouter>)
    await screen.findByText('Stock inicial')
    const movementCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).includes('/api/stock/movements'))
    await userEvent.selectOptions(screen.getByLabelText('Producto', { selector: '.movement-filters select' }), '1')
    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'InitialStock')
    await userEvent.type(screen.getByLabelText('Movimientos desde'), '2026-09-01')
    await userEvent.type(screen.getByLabelText('Movimientos hasta'), '2026-09-20')
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }))
    await waitFor(() => expect(movementCalls()).toHaveLength(2))
    await userEvent.click(screen.getByRole('button', { name: 'Siguiente' }))
    await waitFor(() => expect(movementCalls()).toHaveLength(3))
    await userEvent.click(screen.getByRole('button', { name: 'Gestionar' }))
    await userEvent.type(screen.getByLabelText('Cantidad'), '2')
    await userEvent.type(screen.getByLabelText('Motivo'), 'Ingreso')
    await userEvent.dblClick(screen.getByRole('button', { name: 'Confirmar ajuste' }))
    await waitFor(() => expect(movementCalls()).toHaveLength(4))
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/adjustments'))).toHaveLength(1)
    const refreshedUrl = String(movementCalls()[3][0])
    expect(refreshedUrl).toContain('page=2')
    expect(refreshedUrl).toContain('productId=1')
    expect(refreshedUrl).toContain('type=InitialStock')
    expect(refreshedUrl).toContain('fromUtc=2026-09-01')
    expect(refreshedUrl).toContain('toUtc=2026-09-20')
  })

  it('no refresca el historial global cuando el ajuste falla', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/adjustments') && init?.method === 'POST') return Promise.reject(new Error('Falló el ajuste'))
      if (url.includes('/movements')) return response({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 })
      return response([product])
    })
    render(<MemoryRouter><StockAdmin /></MemoryRouter>)
    await screen.findByRole('button', { name: 'Gestionar' })
    const movementCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).includes('/api/stock/movements'))
    await waitFor(() => expect(movementCalls()).toHaveLength(1))
    const globalCallsBefore = movementCalls().length
    await userEvent.click(screen.getByRole('button', { name: 'Gestionar' }))
    await userEvent.type(screen.getByLabelText('Cantidad'), '2')
    await userEvent.type(screen.getByLabelText('Motivo'), 'Control')
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar ajuste' }))
    expect(await screen.findByText('Falló el ajuste')).toBeInTheDocument()
    expect(movementCalls()).toHaveLength(globalCallsBefore)
  })

  it('aplica, pagina y limpia filtros globales sin consultar por cada pulsación', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(input => String(input).includes('/api/stock/movements') ? response({ items: [movement], page: 1, pageSize: 20, totalItems: 21, totalPages: 2 }) : response([product]))
    render(<MemoryRouter><StockAdmin /></MemoryRouter>)
    const movementCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).includes('/api/stock/movements'))
    await waitFor(() => expect(movementCalls()).toHaveLength(1))
    await userEvent.selectOptions(screen.getByLabelText('Producto', { selector: '.movement-filters select' }), '1')
    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'InitialStock')
    await userEvent.type(screen.getByLabelText('Movimientos desde'), '2026-09-01')
    expect(movementCalls()).toHaveLength(1)
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }))
    await waitFor(() => expect(movementCalls()).toHaveLength(2))
    expect(String(movementCalls()[1][0])).toContain('productId=1')
    expect(String(movementCalls()[1][0])).toContain('type=InitialStock')
    expect(String(movementCalls()[1][0])).toContain('fromUtc=2026-09-01')
    await userEvent.click(screen.getByRole('button', { name: 'Siguiente' }))
    await waitFor(() => expect(String(movementCalls().at(-1)?.[0])).toContain('page=2'))
    await userEvent.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(screen.getByLabelText('Tipo')).toHaveValue(''))
    expect(screen.getByLabelText('Movimientos desde')).toHaveValue('')
  })

  it('muestra carga, error, reintento y estado vacío del historial global', async () => {
    let movementCalls = 0; let resolveFirst!: (value: Response) => void
    const first = new Promise<Response>(resolve => { resolveFirst = resolve })
    vi.spyOn(globalThis, 'fetch').mockImplementation(input => {
      if (!String(input).includes('/api/stock/movements')) return response([product])
      movementCalls += 1
      if (movementCalls === 1) return first
      if (movementCalls === 2) return response({ message: 'No se pudo cargar el historial.' }, 500)
      return response({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 })
    })
    render(<MemoryRouter><StockAdmin /></MemoryRouter>)
    expect(await screen.findByText('Cargando movimientos…')).toBeInTheDocument()
    resolveFirst(await response({ message: 'No se pudo cargar el historial.' }, 500))
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el historial.')
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el historial.')
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByText('No hay movimientos para los filtros seleccionados.')).toBeInTheDocument()
  })
})

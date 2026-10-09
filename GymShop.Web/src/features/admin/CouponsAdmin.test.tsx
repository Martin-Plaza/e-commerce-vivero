import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { CouponsAdmin } from './CouponsAdmin'

const coupon = { id: 1, code: 'SAVE10', name: 'Ahorro', type: 'Percentage', value: 10, minimumPurchase: null, maximumDiscount: null, startsAtUtc: '2026-09-22T15:30:00.000Z', endsAtUtc: null, totalUsageLimit: 5, usageLimitPerUser: 1, isActive: true, reservedUses: 1, consumedUses: 2, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null }
const json = (body: unknown, status = 200) => Promise.resolve(new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const page = { items: [coupon], page: 1, pageSize: 20, totalItems: 21, totalPages: 2 }

describe('administración de cupones', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lista, filtra y pagina usando la API', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => json(page))
    render(<CouponsAdmin />)
    expect(await screen.findByText('SAVE10')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Buscar'), 'save')
    await userEvent.selectOptions(screen.getByLabelText('Estado'), 'true')
    await userEvent.selectOptions(screen.getByLabelText('Tipo'), 'Percentage')
    await userEvent.selectOptions(screen.getByLabelText('Vigencia'), 'current')
    await userEvent.click(screen.getByRole('button', { name: 'Siguiente' }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([value]) => { const url = String(value); return url.includes('search=save') && url.includes('status=true') && url.includes('type=Percentage') && url.includes('validity=current') && url.includes('page=2') })).toBe(true))
  })

  it('ignora una respuesta vieja que llega después de la consulta vigente', async () => {
    let resolveInitial!: (response: Response) => void
    let resolveLatest!: (response: Response) => void
    const oldCoupon = { ...coupon, id: 10, code: 'OLD' }
    const latestCoupon = { ...coupon, id: 11, code: 'LATEST' }
    let request = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => {
      request += 1
      if (request === 1) return new Promise<Response>(resolve => { resolveInitial = resolve })
      return new Promise<Response>(resolve => { resolveLatest = resolve })
    })
    render(<CouponsAdmin />)
    await userEvent.type(screen.getByLabelText('Buscar'), 'latest')
    resolveLatest(await json({ ...page, items: [latestCoupon] }))
    expect(await screen.findByText('LATEST')).toBeInTheDocument()
    resolveInitial(await json({ ...page, items: [oldCoupon] }))
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(screen.getByText('LATEST')).toBeInTheDocument()
    expect(screen.queryByText('OLD')).not.toBeInTheDocument()
  })

  it('refresca con filtros vigentes después de un guardado pendiente e ignora la respuesta anterior', async () => {
    let resolveSave!: (response: Response) => void
    let resolveFiltered!: (response: Response) => void
    let resolveRefresh!: (response: Response) => void
    const staleCoupon = { ...coupon, id: 20, code: 'STALE' }
    const refreshedCoupon = { ...coupon, id: 21, code: 'CURRENT' }
    const urls: string[] = []
    let getCount = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); urls.push(url)
      if (init?.method === 'POST') return new Promise<Response>(resolve => { resolveSave = resolve })
      getCount += 1
      if (getCount === 1) return json(page)
      if (getCount === 2) return new Promise<Response>(resolve => { resolveFiltered = resolve })
      return new Promise<Response>(resolve => { resolveRefresh = resolve })
    })
    render(<CouponsAdmin />); await screen.findByText('SAVE10')
    await userEvent.click(screen.getByRole('button', { name: 'Nuevo cupón' })); await userEvent.type(screen.getByLabelText('Código'), 'NEW'); await userEvent.type(screen.getByLabelText('Nombre'), 'Nuevo'); await userEvent.type(screen.getByLabelText('Valor'), '10')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }))
    await userEvent.type(screen.getByLabelText('Buscar'), 'current')
    resolveSave(await json(coupon))
    await waitFor(() => expect(urls.filter(url => url.includes('/api/coupons?')).at(-1)).toContain('search=current'))
    resolveRefresh(await json({ ...page, items: [refreshedCoupon] })); expect(await screen.findByText('CURRENT')).toBeInTheDocument()
    resolveFiltered(await json({ ...page, items: [staleCoupon] })); await new Promise(resolve => setTimeout(resolve, 0))
    expect(screen.getByText('CURRENT')).toBeInTheDocument(); expect(screen.queryByText('STALE')).not.toBeInTheDocument()
  })

  it('crea y edita incluyendo fechas locales convertidas a UTC', async () => {
    const calls: RequestInit[] = []
    vi.spyOn(globalThis, 'fetch').mockImplementation((_, init) => { if (init?.method === 'POST' || init?.method === 'PUT') { calls.push(init); return json(coupon) } return json(page) })
    render(<CouponsAdmin />); await screen.findByText('SAVE10')
    await userEvent.click(screen.getByRole('button', { name: 'Nuevo cupón' }))
    await userEvent.type(screen.getByLabelText('Código'), 'new10'); await userEvent.type(screen.getByLabelText('Nombre'), 'Nuevo'); await userEvent.type(screen.getByLabelText('Valor'), '10')
    await userEvent.type(screen.getByLabelText('Inicio (hora local)'), '2026-10-01T12:45')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(calls).toHaveLength(1))
    const created = JSON.parse(String(calls[0].body)); expect(created.startsAtUtc).toBe(new Date(2026, 9, 1, 12, 45).toISOString())
    await userEvent.click(screen.getByRole('button', { name: 'Editar' })); expect((screen.getByLabelText('Inicio (hora local)') as HTMLInputElement).value).toMatch(/^2026-/)
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' })); await waitFor(() => expect(calls).toHaveLength(2)); expect(calls[1].method).toBe('PUT')
  })

  it('confirma cambios de estado, evita doble envío y muestra errores API', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    let resolvePost!: (response: Response) => void
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_, init) => {
      if (init?.method === 'PATCH') return json(null, 204)
      if (init?.method === 'POST') return new Promise<Response>(resolve => { resolvePost = resolve })
      return json(page)
    })
    render(<CouponsAdmin />); await screen.findByText('SAVE10'); await userEvent.click(screen.getByRole('button', { name: 'Desactivar' })); await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')).toHaveLength(1))
    await userEvent.click(screen.getByRole('button', { name: 'Nuevo cupón' })); await userEvent.type(screen.getByLabelText('Código'), 'FAIL'); await userEvent.type(screen.getByLabelText('Nombre'), 'Falla'); await userEvent.type(screen.getByLabelText('Valor'), '10')
    const save = screen.getByRole('button', { name: 'Guardar' }); await userEvent.dblClick(save); expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
    resolvePost(await json({ message: 'Código duplicado.' }, 409)); expect(await screen.findByRole('alert')).toHaveTextContent('Código duplicado')
  })
})

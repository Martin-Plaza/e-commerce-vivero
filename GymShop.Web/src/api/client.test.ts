import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, request, requestBlob } from './client'
import { api } from './gymshop'
import { session } from '../auth/session'

const response = (body: unknown, status = 200, headers?: HeadersInit) => new Response(body === undefined ? undefined : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

describe('cliente HTTP y contrato GymShop', () => {
  beforeEach(() => { vi.restoreAllMocks(); localStorage.clear(); sessionStorage.clear(); document.cookie = 'XSRF-TOKEN=; Max-Age=0; Path=/' })

  it('usa cookies del navegador sin exponer un Bearer', async () => {
    session.save({ id: 1, email: 'u@gym.com', name: 'U', role: 'User' })
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ ok: true }))
    await request('/api/test')
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers)
    expect(headers.get('Authorization')).toBeNull()
    expect(fetchMock.mock.calls[0][1]?.credentials).toBe('include')
  })

  it('envía el token CSRF en operaciones mutables', async () => {
    document.cookie = 'XSRF-TOKEN=csrf-seguro; Path=/'
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ ok: true }))
    await request('/api/test', { method: 'POST', body: '{}' })
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('X-CSRF-TOKEN')).toBe('csrf-seguro')
  })

  it('un 401 invalida la sesión', async () => {
    session.save({ id: 1, email: 'u@gym.com', name: 'U', role: 'User' })
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(undefined, { status: 401 }))
    await expect(request('/api/private')).rejects.toMatchObject({ status: 401 })
    expect(session.user()).toBeNull()
  })

  it('un 403 informa permisos insuficientes sin cerrar sesión', async () => {
    session.save({ id: 1, email: 'u@gym.com', name: 'U', role: 'User' })
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(undefined, { status: 403 }))
    await expect(request('/api/admin')).rejects.toMatchObject({ status: 403 })
    expect(session.user()?.email).toBe('u@gym.com')
  })

  it('normaliza ProblemDetails y conserva traceId', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ title: 'Conflicto', detail: 'La orden ya tiene un pago.', traceId: 'trace-409' }, 409))
    await expect(request('/api/test')).rejects.toMatchObject({ status: 409, message: 'La orden ya tiene un pago.', traceId: 'trace-409' })
  })

  it('normaliza ValidationProblemDetails', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ title: 'Validation', errors: { Email: ['El email no es válido.'], Password: ['La clave es requerida.'] } }, 400))
    const error = await request('/api/test').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    if (!(error instanceof ApiError)) throw new Error('Se esperaba ApiError')
    expect(error.message).toContain('El email no es válido.')
    expect(error.validationErrors).toHaveProperty('Password')
  })

  it('normaliza 429 y Retry-After', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ detail: 'Esperá antes de reintentar.' }, 429, { 'Retry-After': '45' }))
    await expect(request('/api/test')).rejects.toMatchObject({ status: 429, retryAfter: 45 })
  })

  it('normaliza un 500 seguro con traceId', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ title: 'Error interno', detail: 'Ocurrió un error inesperado.', traceId: 'server-trace' }, 500))
    await expect(request('/api/test')).rejects.toMatchObject({ status: 500, traceId: 'server-trace' })
  })

  it('distingue una falla de red de una respuesta HTTP', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    await expect(request('/api/test')).rejects.toMatchObject({
      status: 0,
      code: 'network_unavailable',
      message: 'No pudimos conectarnos con el servicio. Revisá tu conexión e intentá nuevamente.',
    })
  })

  it('solicita solo el catálogo activo por defecto', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response([]))
    await api.products()
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/api\/products$/)
    expect(String(fetchMock.mock.calls[0][0])).not.toContain('includeInactive')
  })

  it('usa los endpoints actuales de carrito y checkout', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => Promise.resolve(response({ id: 5 })))
    await api.addCartItem(2, 3)
    await api.checkout({ deliveryMethod: 'HomeDelivery', shippingAddress: 'Calle 123', expectedShippingCost: 10, expectedSubtotal: 100, expectedDiscount: 0, idempotencyKey: 'checkout-key' })
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/cart/items')
    expect(String(fetchMock.mock.calls[1][0])).toContain('/api/cart/checkout')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body)).idempotencyKey).toBe('checkout-key')
  })

  it('crea pagos por orderId con el medio elegido e idempotencia estable', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ id: 7, status: 'Creating', checkoutUrl: null }))
    const payment = await api.createPayment(42, 'BankTransfer', 'stable-key')
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/orders/42/payments')
    expect(String(fetchMock.mock.calls[0][0])).not.toContain('/api/payments/current')
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ provider: 'BankTransfer', idempotencyKey: 'stable-key' })
    expect(payment).toMatchObject({ status: 'Creating', checkoutUrl: null })
  })

  it('descarga PDFs autenticados sin intentar interpretarlos como JSON', async () => {
    session.save({ id: 1, email: 'admin@gym.com', name: 'Admin', role: 'Admin' })
    const expected = new Blob(['%PDF-1.4'], { type: 'application/pdf' })
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(expected, { headers: { 'Content-Type': 'application/pdf' } }))

    const result = await requestBlob('/api/admin/billing/orders/42/documents/id/pdf')

    expect(result.type).toBe('application/pdf')
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers)
    expect(headers.get('Accept')).toBe('application/pdf')
    expect(headers.get('Authorization')).toBeNull()
    expect(fetchMock.mock.calls[0][1]?.credentials).toBe('include')
  })
})

import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'

const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const products = [
  { id: 1, name: 'Mancuerna 10kg', description: null, price: 15000, stock: 3, imageUrl: null, isActive: true, category: { id: 1, name: 'Fuerza', slug: 'fuerza' } },
  { id: 2, name: 'Colchoneta', description: null, price: 9000, stock: 0, imageUrl: null, isActive: false, category: { id: 2, name: 'Movilidad', slug: 'movilidad' } },
]
const orders = [{ id: 10, userId: 7, userEmail: 'cliente@gym.com', userName: 'Cliente Gym', createdAt: '2026-09-20T12:00:00Z', updatedAt: null, total: 15000, status: 'Pending', lastPaymentStatus: null, lastPaymentId: null }]
const orderPage = { items: orders, page: 1, pageSize: 20, totalItems: 1, totalPages: 1 }
const orderDetail = { ...orders[0], shippingAddress: 'Av. Siempre Viva 742', cancellationReason: null, items: [{ productId: 1, productName: 'Mancuerna 10kg', unitPrice: 15000, quantity: 1, subtotal: 15000 }], payments: [] }
const orderHistory = [
  { id: 1, action: 'OrderStatusChanged', previousStatus: 'Paid', newStatus: 'Preparing', reason: null, createdAtUtc: '2026-09-20T13:00:00Z', actorUserId: 1, actorName: 'Admin Gym', actorEmail: 'admin@gym.com', source: 'Manual' },
  { id: 2, action: 'PaymentPartialRefundFlagged', previousStatus: 'Approved', newStatus: 'Approved', reason: 'Reembolso parcial; requiere gestión manual.', createdAtUtc: '2026-09-20T14:00:00Z', actorUserId: null, actorName: null, actorEmail: null, source: 'Provider' },
]
const dashboard = { fromUtc: '2026-09-01T03:00:00Z', toUtc: '2026-09-22T03:00:00Z', timeZoneId: 'America/Argentina/Buenos_Aires', totalSales: 15000, paidOrders: 1, averageTicket: 15000, ordersByStatus: [{ status: 'Pending', count: 1 }], salesByDay: [{ date: '2026-09-20', amount: 15000, orders: 1 }], topProducts: [{ productId: 1, productName: 'Mancuerna 10kg', quantity: 1, amount: 15000 }], outOfStockProducts: [], lowStockProducts: [{ productId: 1, productName: 'Mancuerna 10kg', stock: 3, isActive: true }], lowStockThreshold: 5 }

function signIn(role: 'User' | 'Admin' | 'SuperAdmin') {
  localStorage.setItem('gymshop.token', 'jwt')
  localStorage.setItem('gymshop.user', JSON.stringify({ id: 1, email: `${role.toLowerCase()}@gym.com`, name: role, role }))
}

function apiMock(input: RequestInfo | URL, init?: RequestInit) {
  const url = String(input)
  if (url.includes('/api/admin/dashboard')) return response(dashboard)
  if (url.includes('/api/cart')) return response({ id: 1, userId: 1, total: 0, items: [] })
  if (url.includes('/api/products')) return init?.method === 'PATCH' ? response(null) : response(products)
  if (url.includes('/api/orders/10/history')) return response(orderHistory)
  if (url.includes('/api/orders/10/cancel')) return response({ ...orderDetail, status: 'Canceled', cancellationReason: 'Pedido duplicado' })
  if (/\/api\/orders\/10$/.test(url)) return response(orderDetail)
  if (url.includes('/api/orders')) return init?.method === 'PATCH' ? Promise.resolve(new Response(null, { status: 204 })) : response(orderPage)
  if (url.includes('/api/users')) return response([])
  if (url.includes('/api/audit')) return response({ items: [], page: 1, pageSize: 50, totalItems: 0, totalPages: 0 })
  return response([])
}


describe('panel administrativo', () => {
  beforeEach(() => { localStorage.clear(); window.history.replaceState(null, '', '/'); vi.restoreAllMocks() })

  it('redirige visitantes al login conservando la ruta administrativa', async () => {
    window.history.replaceState(null, '', '/admin/productos?estado=activo')
    vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    expect(await screen.findByRole('status')).toHaveTextContent('Iniciá sesión para continuar.')
    expect(window.location.pathname).toBe('/login')
    expect(window.history.state.usr.returnTo).toBe('/admin/productos?estado=activo')
  })

  it('deniega el panel a un usuario común', async () => {
    signIn('User'); window.history.replaceState(null, '', '/admin/productos')
    vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    expect(await screen.findByText('No tenés permisos para acceder a esta sección.')).toBeInTheDocument()
  })

  it('da a Admin acceso a resumen, productos y pedidos, pero no muestra secciones de SuperAdmin', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin')
    vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    expect(await screen.findByText('Ventas confirmadas')).toBeInTheDocument()
    expect(screen.getByText('Pedidos pagados')).toBeInTheDocument()
    const navigation = screen.getByRole('navigation', { name: 'Navegación administrativa' })
    expect(within(navigation).getByRole('link', { name: 'Productos' })).toBeInTheDocument()
    expect(within(navigation).getByRole('link', { name: 'Pedidos' })).toBeInTheDocument()
    expect(within(navigation).queryByRole('link', { name: 'Usuarios' })).not.toBeInTheDocument()
    expect(within(navigation).queryByRole('link', { name: 'Auditoría' })).not.toBeInTheDocument()
  })

  it('bloquea usuarios y auditoría para Admin aunque conozca la URL', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/usuarios')
    vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    expect(await screen.findByText('No tenés permisos para acceder a esta sección.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalledWith(expect.stringContaining('/api/users'), expect.anything())
  })

  it('muestra navegación completa a SuperAdmin', async () => {
    signIn('SuperAdmin'); window.history.replaceState(null, '', '/admin')
    vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    const navigation = await screen.findByRole('navigation', { name: 'Navegación administrativa' })
    expect(within(navigation).getByRole('link', { name: 'Usuarios' })).toBeInTheDocument()
    expect(within(navigation).getByRole('link', { name: 'Auditoría' })).toBeInTheDocument()
  })

  it('consulta pedidos con búsqueda y filtros solo al aplicar', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    await screen.findByText('#10')
    const orderCalls = () => fetchMock.mock.calls.filter(([input]) => String(input).includes('/api/orders'))
    expect(orderCalls()).toHaveLength(1)
    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar' }), 'cliente@gym.com')
    await userEvent.selectOptions(screen.getByLabelText('Estado'), 'Paid')
    expect(orderCalls()).toHaveLength(1)
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }))
    await waitFor(() => expect(orderCalls()).toHaveLength(2))
    expect(String(orderCalls()[1][0])).toContain('search=cliente%40gym.com')
    expect(String(orderCalls()[1][0])).toContain('status=Paid')
    await userEvent.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(orderCalls()).toHaveLength(3))
    expect(String(orderCalls()[2][0])).toContain('page=1')
  })

  it('muestra el detalle y confirma un cambio de estado permitido', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))
    expect(await screen.findByRole('heading', { name: /15\.000,00/ })).toBeInTheDocument()
    expect(screen.getByText(/Podés cancelarlo indicando un motivo/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /pagado/i })).not.toBeInTheDocument()
    expect(await screen.findByText('Estado del pedido actualizado')).toBeInTheDocument()
    expect(screen.getByText('Admin Gym · admin@gym.com')).toBeInTheDocument()
    expect(screen.getByText('Reembolso parcial informado por el proveedor')).toBeInTheDocument()
    expect(screen.getByText('Reembolso parcial; requiere gestión manual.')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Motivo de cancelación'), 'Pedido duplicado')
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar pedido' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/api/orders/10/cancel'), expect.objectContaining({ method: 'POST' })))
    expect(window.confirm).toHaveBeenCalled()
  })

  it('muestra una incidencia destacada cuando MP aprobó después de cancelar', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const incidentOrder = { ...orderDetail, status: 'Canceled', cancellationReason: 'Fraude', payments: [{ id: 44, provider: 'MercadoPago', amount: 15000, currency: 'ARS', status: 'Approved', createdAt: '2026-09-20T12:00:00Z', paidAt: '2026-09-20T15:00:00Z', failureReason: 'Requiere revisión y devolución.', requiresReview: true }] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(incidentOrder)
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Pago aprobado después de cancelar')
    expect(alert).toHaveTextContent('Requiere revisión y devolución')
  })

  it('ofrece cancelar un pedido gratuito pagado sin intentos de pago', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const freeOrder = { ...orderDetail, total: 0, status: 'Paid', payments: [] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(freeOrder)
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))

    expect(await screen.findByText(/Este pedido gratuito puede cancelarse/)).toBeInTheDocument()
    expect(screen.getByLabelText('Motivo de cancelación')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancelar pedido' })).toBeDisabled()
  })

  it('no ofrece cancelar un pedido de total cero con un pago pendiente', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const orderWithPayment = { ...orderDetail, total: 0, status: 'Paid', payments: [{ id: 51, provider: 'MercadoPago', amount: 100, currency: 'ARS', status: 'Pending', createdAt: '2026-09-20T12:00:00Z' }] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(orderWithPayment)
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))

    await screen.findByText('El pago ya fue confirmado. Podés iniciar la preparación del pedido.')
    expect(screen.queryByLabelText('Motivo de cancelación')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancelar pedido' })).not.toBeInTheDocument()
  })

  it('no ofrece cancelación administrativa a pedidos pagos o gratuitos enviados', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const paidOrder = { ...orderDetail, status: 'Paid', payments: [{ id: 50, provider: 'MercadoPago', amount: 15000, currency: 'ARS', status: 'Approved', createdAt: '2026-09-20T12:00:00Z' }] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(paidOrder)
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))

    await screen.findByText('El pago ya fue confirmado. Podés iniciar la preparación del pedido.')
    expect(screen.queryByLabelText('Motivo de cancelación')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancelar pedido' })).not.toBeInTheDocument()
  })

  it('ubica los datos de seguimiento debajo del historial al preparar el envío', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const preparingOrder = { ...orderDetail, status: 'Preparing', deliveryMethod: 'HomeDelivery' }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(preparingOrder)
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))

    const historyHeading = await screen.findByRole('heading', { name: 'Historial' })
    const trackingHeading = screen.getByRole('heading', { name: 'Datos para marcar el pedido como enviado' })
    expect(historyHeading.compareDocumentPosition(trackingHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByLabelText('Empresa transportista')).toBeInTheDocument()
    expect(screen.getByLabelText('Número de seguimiento')).toBeInTheDocument()
  })

  it('genera una sola constancia interna para un pedido cobrado', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const paidOrder = { ...orderDetail, status: 'Paid', payments: [{ id: 50, provider: 'MercadoPago', amount: 15000, currency: 'ARS', status: 'Approved', createdAt: '2026-09-20T12:00:00Z', paidAt: '2026-09-20T12:05:00Z' }] }
    const receipt = { id: 'fd6c39f0-1b67-4b95-9edf-78a37db693cd', orderId: 10, paymentId: 50, category: 'Receipt', type: 'PurchaseReceipt', status: 'Authorized', currency: 'ARS', issuerBusinessName: 'GymShop', recipientName: 'Cliente Gym', recipientEmail: 'cliente@gym.com', recipientAddress: 'Av. Siempre Viva 742', subtotal: 15000, discountAmount: 0, shippingAmount: 0, total: 15000, createdAtUtc: '2026-09-20T12:10:00Z', authorizedAtUtc: '2026-09-20T12:10:00Z', items: [] }
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/admin/billing/orders/10/receipts')) return response(receipt)
      if (url.includes('/api/admin/billing/orders/10/documents')) return response([])
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(paidOrder)
      return apiMock(input, init)
    })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))

    await userEvent.click(await screen.findByRole('button', { name: 'Generar comprobante' }))

    expect(await screen.findByText('Comprobante interno', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getByText(/no reemplaza una factura fiscal/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ver PDF' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Descargar PDF' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Generar comprobante' })).not.toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/admin/billing/orders/10/receipts'),
      expect.objectContaining({ method: 'POST', headers: expect.any(Headers) }))
    const receiptCall = fetchMock.mock.calls.find(([input]) => String(input).includes('/receipts'))
    expect((receiptCall?.[1]?.headers as Headers).get('Idempotency-Key')).toBe('receipt-order-10')
  })

  it('muestra la factura fiscal autorizada con su numeracion, CAE y acciones de PDF fiscal', async () => {
    signIn('SuperAdmin'); window.history.replaceState(null, '', '/admin/pedidos')
    const paidOrder = { ...orderDetail, status: 'Paid', payments: [{ id: 50, provider: 'MercadoPago', amount: 15000, currency: 'ARS', status: 'Approved', createdAt: '2026-09-20T12:00:00Z', paidAt: '2026-09-20T12:05:00Z' }] }
    const invoice = { id: '81d53082-a463-4bbc-a4c3-7e6b2d4c8f80', orderId: 10, paymentId: 50, category: 'Invoice', type: 'InvoiceC', status: 'Authorized', currency: 'ARS', issuerBusinessName: 'GymShop Homologacion', recipientName: 'Cliente Gym', recipientEmail: 'cliente@gym.com', recipientAddress: 'Av. Siempre Viva 742', subtotal: 15000, discountAmount: 0, shippingAmount: 0, total: 15000, pointOfSale: 1, documentNumber: 1, authorizationProvider: 'ARCA-Homologation', cae: '86400947232722', caeExpiresOn: '2026-10-15', rejectionCode: null, rejectionReason: null, createdAtUtc: '2026-10-05T12:10:00Z', authorizedAtUtc: '2026-10-05T12:10:01Z', items: [] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/admin/billing/orders/10/documents')) return response([invoice])
      if (url.includes('/api/orders/10/history')) return response(orderHistory)
      if (/\/api\/orders\/10$/.test(url)) return response(paidOrder)
      return apiMock(input, init)
    })

    render(<App />)
    await userEvent.click(await screen.findByText('#10'))

    expect(await screen.findByText('Factura C · Homologación')).toBeInTheDocument()
    expect(screen.getByText('Autorizado')).toBeInTheDocument()
    expect(screen.getByText('Punto de venta 00001 · Número 00000001')).toBeInTheDocument()
    expect(screen.getByText(/CAE 86400947232722/)).toBeInTheDocument()
    expect(screen.getByText(/representación fiscal incluye CAE, vencimiento y código QR/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ver PDF' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Descargar PDF' })).toBeInTheDocument()
  })

  it('mantiene el detalle visible si falla el historial y permite reintentarlo', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    let historyLoads = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (String(input).includes('/api/orders/10/history')) return ++historyLoads === 1 ? response({ message: 'No se pudo cargar el historial.' }, 500) : response(orderHistory)
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))
    expect(await screen.findByText('Av. Siempre Viva 742')).toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar el historial.')
    expect(screen.getByRole('heading', { name: 'Cliente' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar historial' }))
    expect(await screen.findByText('Estado del pedido actualizado')).toBeInTheDocument()
    expect(historyLoads).toBe(2)
  })

  it('ignora el historial tardío de un pedido anterior', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    const secondSummary = { ...orders[0], id: 11, userName: 'Segundo Cliente', userEmail: 'segundo@gym.com' }
    const secondDetail = { ...orderDetail, ...secondSummary, shippingAddress: 'Calle B 456' }
    let resolveFirstHistory!: (value: Response) => void
    const firstHistory = new Promise<Response>(resolve => { resolveFirstHistory = resolve })
    const historyB = [{ ...orderHistory[0], id: 20, action: 'OrderCanceled', previousStatus: 'Pending', newStatus: 'Canceled', reason: 'Evento exclusivo de B' }]
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.includes('/api/orders/10/history')) return firstHistory
      if (url.includes('/api/orders/11/history')) return response(historyB)
      if (/\/api\/orders\/10$/.test(url)) return response(orderDetail)
      if (/\/api\/orders\/11$/.test(url)) return response(secondDetail)
      if (url.includes('/api/orders')) return response({ items: [orders[0], secondSummary], page: 1, pageSize: 20, totalItems: 2, totalPages: 1 })
      return apiMock(input, init)
    })
    render(<App />)
    await userEvent.click(await screen.findByText('#10'))
    expect(await screen.findByText('Av. Siempre Viva 742')).toBeInTheDocument()
    await userEvent.click(screen.getByText('#11'))
    expect(await screen.findByText('Calle B 456')).toBeInTheDocument()
    expect(await screen.findByText('Evento exclusivo de B')).toBeInTheDocument()
    resolveFirstHistory(new Response(JSON.stringify([{ ...orderHistory[0], id: 99, reason: 'Evento tardío de A' }]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    await waitFor(() => expect(screen.queryByText('Evento tardío de A')).not.toBeInTheDocument())
    expect(screen.getByText('Evento exclusivo de B')).toBeInTheDocument()
  })

  it('muestra errores de API del listado de pedidos y permite reintentar', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/pedidos')
    let loads = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => String(input).includes('/api/orders?') && ++loads === 1 ? response({ message: 'No se pudieron cargar los pedidos.' }, 500) : apiMock(input, init))
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar los pedidos.')
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByText('#10')).toBeInTheDocument()
  })

  it('busca, filtra y distingue lista vacía de ausencia de resultados', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/productos')
    vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    render(<App />)
    expect(await screen.findByText('Mancuerna 10kg')).toBeInTheDocument()
    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar por nombre' }), 'colch')
    expect(screen.queryByText('Mancuerna 10kg')).not.toBeInTheDocument()
    expect(screen.getByText('Colchoneta')).toBeInTheDocument()
    await userEvent.clear(screen.getByRole('searchbox', { name: 'Buscar por nombre' }))
    await userEvent.selectOptions(screen.getByLabelText('Categoría'), 'Fuerza')
    expect(screen.getByText('Mancuerna 10kg')).toBeInTheDocument()
    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar por nombre' }), 'inexistente')
    expect(screen.getByText(/No hay productos que coincidan/)).toBeInTheDocument()
  })

  it('muestra un estado vacío cuando todavía no existen productos', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/productos')
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => String(input).includes('/api/products') ? response([]) : apiMock(input, init))
    render(<App />)
    expect(await screen.findByText('No hay productos cargados.')).toBeInTheDocument()
  })

  it('muestra el error de carga inicial y permite reintentar sin confundirlo con una lista vacía', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/productos')
    let productLoads = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (String(input).includes('/api/products') && init?.method !== 'PATCH') return ++productLoads === 1 ? response({ message: 'No se pudo cargar productos.' }, 500) : response(products)
      return apiMock(input, init)
    })
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar productos.')
    expect(screen.queryByText('No hay productos cargados.')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByText('Mancuerna 10kg')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(productLoads).toBe(2)
  })

  it('confirma cambios de estado del producto', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/productos')
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(apiMock)
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<App />)
    await screen.findByText('Mancuerna 10kg')
    await userEvent.click(screen.getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/api/products/1/status'), expect.objectContaining({ method: 'PATCH' })))
    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('Mancuerna 10kg'))
  })

  it('evita envíos duplicados mientras una mutación está pendiente', async () => {
    signIn('Admin'); window.history.replaceState(null, '', '/admin/productos')
    let resolveMutation!: (value: Response) => void
    const mutation = new Promise<Response>(resolve => { resolveMutation = resolve })
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => String(input).includes('/status') && init?.method === 'PATCH' ? mutation : apiMock(input, init))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<App />)
    const button = await screen.findByRole('button', { name: 'Desactivar' })
    await userEvent.click(button)
    expect(button).toBeDisabled()
    await userEvent.click(button)
    expect(fetchMock.mock.calls.filter(([input]) => String(input).includes('/status'))).toHaveLength(1)
    resolveMutation(new Response('null', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Desactivar' })).toBeEnabled())
  })
})

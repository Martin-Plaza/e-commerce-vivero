import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import type { BillingDocument, CheckoutSession, Order, Payment } from '../../api/types'

const product = { id: 4, name: 'Kettlebell 16kg', price: 42000, stock: 12, imageUrl: '/kettlebell.webp' }
const cart = { id: 1, userId: 7, subtotal: 84000, discount: 0, couponCode: null, total: 84000, items: [{ productId: 4, productName: product.name, unitPrice: product.price, quantity: 2, subtotal: 84000, stock: product.stock, imageUrl: product.imageUrl }] }
const emptyCart = { ...cart, total: 0, items: [] }
const order: Order = { id: 81, userId: 7, userEmail: 'u@gym.com', userName: 'Usuario', createdAt: '2026-08-11T10:00:00Z', updatedAt: null, total: 84000, status: 'Pending', shippingAddress: 'Av. Siempre Viva 742, Córdoba', cancellationReason: null, items: [{ productId: 4, productName: product.name, unitPrice: product.price, quantity: 2, subtotal: 84000 }], payments: [] }
const quote = { id: '4ad310db-2407-4d8e-b114-52a59a620b67', providerCode: 'OwnFleet', serviceCode: 'standard', serviceName: 'Envío estándar', price: 6500, estimatedDeliveryFrom: '2026-08-12T10:00:00Z', estimatedDeliveryTo: '2026-08-14T10:00:00Z', expiresAtUtc: '2099-08-11T10:15:00Z' }
const payment = (status: Payment['status']): Payment => ({ id: 91, orderId: 81, provider: 'BankTransfer', externalReference: 'order-81', providerPreferenceId: null, providerPaymentId: null, idempotencyKey: 'key', amount: 84000, currency: 'ARS', status, checkoutUrl: null, failureReason: status === 'Rejected' ? 'Transferencia rechazada.' : null, createdAt: '2026-08-11T10:00:01Z', updatedAt: null, paidAt: null })
const pendingCheckout: CheckoutSession = {
  id: 501, orderId: null, userId: 7, createdAt: '2026-08-11T10:00:00Z', expiresAt: '2026-08-12T10:00:00Z',
  subtotal: 84000, couponCode: null, discountAmount: 0, deliveryMethod: 'HomeDelivery', shippingCost: 6500, total: 90500,
  status: 'AwaitingPayment', shippingAddress: 'Av. Corrientes 5500, CABA', pickupAddress: '', pickupHours: '', pickupInstructions: '',
  items: order.items,
  payment: { ...payment('Pending'), orderId: null, checkoutSessionId: 501, externalReference: 'checkout-501-payment-91', amount: 90500 },
}
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } }))

function authenticate() {
  localStorage.setItem('gymshop.token', 'jwt')
  localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'Usuario', role: 'User' }))
}

async function fillAddressAndQuote() {
  await userEvent.type(await screen.findByLabelText('Código postal'), 'C1414ABC')
  await userEvent.type(screen.getByLabelText('Provincia'), 'CABA')
  await userEvent.type(screen.getByLabelText('Ciudad o localidad'), 'Villa Crespo')
  await userEvent.type(screen.getByLabelText('Calle'), 'Av. Corrientes')
  await userEvent.type(screen.getByLabelText('Número'), '5500')
  await userEvent.click(screen.getByRole('button', { name: 'Calcular envío' }))
  await screen.findByText(/Envío estándar:/)
}

describe('checkout y pago por orderId', () => {
  beforeEach(() => { localStorage.clear(); sessionStorage.clear(); window.history.replaceState(null, '', '/checkout'); vi.restoreAllMocks(); authenticate() })

  it('permite elegir Mercado Pago sin ofrecer Mock como medio comercial', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/payments/methods')) return json({ bankTransferAvailable: true, mercadoPagoAvailable: true, mercadoPagoUnavailableReason: null })
      if (url.endsWith('/api/cart')) return json(cart)
      return json([])
    })
    render(<App />)
    const mercadoPago = await screen.findByRole('radio', { name: /Mercado Pago/ })
    expect(screen.getByRole('radio', { name: /Transferencia bancaria/ })).toBeChecked()
    expect(screen.queryByRole('radio', { name: /Mock/ })).not.toBeInTheDocument()
    await userEvent.click(mercadoPago)
    expect(mercadoPago).toBeChecked()
  })

  it('informa y deshabilita Mercado Pago cuando la API lo declara no disponible antes de crear el pedido', async () => {
    let checkoutPosts = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/payments/methods')) return json({ bankTransferAvailable: true, mercadoPagoAvailable: false, mercadoPagoUnavailableReason: 'Mercado Pago no está disponible en este momento.' })
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkoutPosts++; return json(order) }
      if (url.endsWith('/api/cart')) return json(cart)
      return json([])
    })
    render(<App />)
    const mercadoPago = await screen.findByRole('radio', { name: /Mercado Pago/ })
    expect(mercadoPago).toBeDisabled()
    expect(screen.getByText('Mercado Pago no está disponible en este momento.')).toBeInTheDocument()
    expect(checkoutPosts).toBe(0)
  })

  it('recupera Mercado Pago tras una recarga cuando la creación falló antes de guardar un intento', async () => {
    let checkedOut = false
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart') && method === 'GET') return json(checkedOut ? emptyCart : cart)
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkedOut = true; return json(order) }
      if (url.endsWith('/api/orders/81/payments') && method === 'POST') return json({ detail: 'No se pudo crear la preferencia.' }, 503)
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) return json([])
      if (url.endsWith('/api/payments/methods')) return json({ bankTransferAvailable: true, mercadoPagoAvailable: true, mercadoPagoUnavailableReason: null })
      if (url.endsWith('/api/payments/bank-transfer-details')) return json({ bankName: '', accountHolder: '', cbu: '', alias: '', cuit: '' })
      return json([])
    })
    const first = render(<App />)
    await userEvent.click(await screen.findByRole('radio', { name: /Mercado Pago/ }))
    await fillAddressAndQuote()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar y pagar' }))
    expect(await screen.findByRole('radio', { name: /Mercado Pago/ })).toBeChecked()
    expect(localStorage.getItem('gymshop.payment-provider.81')).toBe('MercadoPago')
    first.unmount()
    render(<App />)
    expect(await screen.findByRole('radio', { name: /Mercado Pago/ })).toBeChecked()
  })

  it('desde Mis órdenes pide elegir el medio si no existe un intento ni una elección recuperable', async () => {
    window.history.replaceState(null, '', '/ordenes')
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/my')) return json([{ id: 81, userId: 7, userEmail: 'u@gym.com', userName: 'Usuario', createdAt: order.createdAt, total: order.total, status: 'Pending', updatedAt: null, lastPaymentStatus: null, lastPaymentId: null }])
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) return json([])
      if (url.endsWith('/api/payments/methods')) return json({ bankTransferAvailable: true, mercadoPagoAvailable: true, mercadoPagoUnavailableReason: null })
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: /#81/ }))
    await userEvent.click(await screen.findByRole('link', { name: /Crear \/ consultar pago/ }))
    expect(await screen.findByText('Todavía no hay intentos de pago. Elegí cómo continuar:')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Transferencia bancaria/ })).not.toBeChecked()
    expect(screen.getByRole('radio', { name: /Mercado Pago/ })).not.toBeChecked()
  })

  it('revisa, crea una sola orden y crea la transferencia por orderId con clave estable', async () => {
    let checkedOut = false
    const calls: Array<{ url: string; method: string; body: string | null }> = []
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'; calls.push({ url, method, body: init?.body ? String(init.body) : null })
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart') && method === 'GET') return json(checkedOut ? emptyCart : cart)
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkedOut = true; return json(order) }
      if (url.endsWith('/api/orders/81/payments') && method === 'POST') return json(payment('Creating'), 202)
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) return json([payment('Creating')])
      return json([])
    })
    render(<App />)
    await screen.findByRole('heading', { name: 'Confirmá tu compra' })
    await fillAddressAndQuote()
    const confirm = screen.getByRole('button', { name: 'Confirmar y pagar' })
    await userEvent.dblClick(confirm)
    expect(await screen.findByRole('heading', { name: 'Estamos confirmando tu pago' })).toBeInTheDocument()
    expect(screen.getByText(/todavía no tiene un enlace/)).toBeInTheDocument()
    expect(calls.filter(call => call.url.endsWith('/api/cart/checkout') && call.method === 'POST')).toHaveLength(1)
    const checkoutCall = calls.find(call => call.url.endsWith('/api/cart/checkout') && call.method === 'POST')
    expect(JSON.parse(checkoutCall!.body || '{}')).toMatchObject({ deliveryMethod: 'HomeDelivery', expectedShippingCost: 6500, expectedSubtotal: 84000, expectedDiscount: 0, shippingQuoteId: quote.id, shippingDestination: { postalCode: 'C1414ABC', streetNumber: '5500' } })
    expect(JSON.parse(checkoutCall!.body || '{}').idempotencyKey).toEqual(expect.any(String))
    expect(localStorage.getItem('gymshop.checkout-key.7')).toBeNull()
    const paymentCall = calls.find(call => call.url.endsWith('/api/orders/81/payments') && call.method === 'POST')
    expect(paymentCall).toBeTruthy()
    expect(JSON.parse(paymentCall!.body || '{}').provider).toBe('BankTransfer')
    expect(JSON.parse(paymentCall!.body || '{}').idempotencyKey).toBe(localStorage.getItem('gymshop.payment-key.81'))
    expect(calls.some(call => call.url.includes('/api/payments/current'))).toBe(false)
  })

  it('libera el carrito y rota la clave cuando la transferencia queda pendiente', async () => {
    let checkedOut = false
    let checkoutBody: Record<string, string> = {}
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/payments/methods')) return json({ bankTransferAvailable: true, mercadoPagoAvailable: true, mercadoPagoUnavailableReason: null })
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkedOut = true; checkoutBody = JSON.parse(String(init?.body)); return json(pendingCheckout) }
      if (url.endsWith('/api/cart/checkout/501')) return json(pendingCheckout)
      if (url.endsWith('/api/payments/bank-transfer-details')) return json({ bankName: 'Banco', accountHolder: 'Tienda', cbu: '123', alias: 'tienda', cuit: '' })
      if (url.endsWith('/api/cart')) return json(checkedOut ? emptyCart : cart)
      return json([])
    })
    render(<App />)
    expect(await screen.findByText(/La disponibilidad se confirma al acreditar el pago/)).toBeInTheDocument()
    await fillAddressAndQuote()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar y pagar' }))

    expect(await screen.findByRole('heading', { name: 'Estamos esperando el pago' })).toBeInTheDocument()
    expect(screen.getByText(/Podés seguir comprando mientras verificamos tu transferencia/)).toBeInTheDocument()
    expect(window.location.pathname).toBe('/checkout/pago/501')
    expect(checkoutBody.paymentIdempotencyKey).toBe(checkoutBody.idempotencyKey)
    expect(localStorage.getItem('gymshop.checkout-key.7')).toBeNull()
  })

  it('confirma una orden gratuita con retiro sin crear un pago y tolera doble click', async () => {
    const freeCart = { ...cart, discount: cart.subtotal, couponCode: 'FREE100', total: 0 }
    const freeOrder = { ...order, status: 'Paid' as const, subtotal: cart.subtotal, discountAmount: cart.subtotal, couponCode: 'FREE100', deliveryMethod: 'StorePickup' as const, shippingAddress: '', shippingCost: 0, total: 0, pickupAddress: 'Av. Demo 123' }
    let checkedOut = false; let checkoutPosts = 0; let paymentPosts = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart') && method === 'GET') return json(checkedOut ? emptyCart : freeCart)
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkedOut = true; checkoutPosts++; return json(freeOrder) }
      if (url.endsWith('/api/orders/81/payments') && method === 'POST') { paymentPosts++; return json(payment('Pending')) }
      if (url.endsWith('/api/orders/81')) return json(freeOrder)
      if (url.endsWith('/api/payments/orders/81')) return json([])
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('radio', { name: /Retiro en tienda/ }))
    expect(screen.queryByRole('group', { name: 'Medio de pago' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('no requiere pago')

    await userEvent.dblClick(screen.getByRole('button', { name: 'Confirmar pedido' }))

    expect(await screen.findByRole('heading', { name: 'Pedido confirmado' })).toBeInTheDocument()
    expect(screen.getByText('Esta orden no requiere pago.')).toBeInTheDocument()
    expect(checkoutPosts).toBe(1)
    expect(paymentPosts).toBe(0)
  })

  it('muestra 409, refresca el carrito y recupera la orden pendiente', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart/checkout') && method === 'POST') return json({ title: 'Conflicto', detail: 'Ya tenes una orden pendiente.', code: 'pending_order_exists' }, 409)
      if (url.endsWith('/api/orders/my')) return json([{ id: 70, userId: 7, createdAt: '2026-08-11T09:00:00Z', total: 84000, status: 'Pending', lastPaymentStatus: null, lastPaymentId: null }])
      if (url.endsWith('/api/cart')) return json(cart)
      return json([])
    })
    render(<App />)
    await fillAddressAndQuote()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar y pagar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ya tenes una orden pendiente')
    expect(await screen.findByRole('link', { name: 'Ver orden' })).toHaveAttribute('href', '/checkout/orden/70')
  })

  it('conserva la orden y muestra Retry-After cuando falla la creación del pago', async () => {
    let checkedOut = false
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart') && method === 'GET') return json(checkedOut ? emptyCart : cart)
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkedOut = true; return json(order) }
      if (url.endsWith('/api/orders/81/payments') && method === 'POST') return json({ title: 'Demasiadas solicitudes' }, 429, { 'Retry-After': '9' })
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) return json([])
      return json([])
    })
    render(<App />)
    await fillAddressAndQuote()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar y pagar' }))
    expect(await screen.findByRole('heading', { name: 'Orden creada' })).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('9 segundos')
    expect(sessionStorage.getItem('gymshop.last-order')).toBe('81')
  })

  it('bloquea domicilio si fallan las opciones, permite reintentar y luego cotizar', async () => {
    let attempts = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) {
        attempts += 1
        return attempts === 1 ? json({ message: 'sin tarifa' }, 503) : json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      }
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart')) return json(cart)
      return json([])
    })
    render(<App />)
    const confirm = await screen.findByRole('button', { name: 'Confirmar y pagar' })
    expect(confirm).toBeDisabled()
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos obtener las opciones de entrega')
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar opciones de entrega' }))
    expect(screen.queryByText('No pudimos obtener las opciones de entrega')).not.toBeInTheDocument()
    await fillAddressAndQuote()
    await waitFor(() => expect(confirm).toBeEnabled())
    expect(screen.getByText(/90\.500,00/)).toBeInTheDocument()
  })

  it('bloquea retiro si fallan las opciones y permite confirmarlo después de reintentar', async () => {
    let checkoutBody: Record<string, unknown> | null = null
    let optionAttempts = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) {
        optionAttempts += 1
        return optionAttempts === 1
          ? json({ message: 'sin opciones' }, 503)
          : json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: 'Traé tu documento.', pickupHours: 'Lunes a viernes de 9 a 17' })
      }
      if (url.endsWith('/api/cart') && method === 'GET') return json(checkoutBody ? emptyCart : cart)
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkoutBody = JSON.parse(String(init?.body)); return json({ ...order, deliveryMethod: 'StorePickup', shippingAddress: '', shippingCost: 0, pickupAddress: 'Av. Demo 123', pickupInstructions: 'Traé tu documento.', pickupHours: 'Lunes a viernes de 9 a 17' }) }
      if (url.endsWith('/api/orders/81/payments')) return json(payment('Pending'))
      if (url.endsWith('/api/orders/81')) return json({ ...order, deliveryMethod: 'StorePickup', shippingAddress: '', shippingCost: 0, pickupAddress: 'Av. Demo 123', pickupInstructions: 'Traé tu documento.', pickupHours: 'Lunes a viernes de 9 a 17' })
      if (url.endsWith('/api/payments/orders/81')) return json([payment('Pending')])
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('radio', { name: /Retiro en tienda/ }))
    const confirm = screen.getByRole('button', { name: 'Confirmar y pagar' })
    expect(confirm).toBeDisabled()
    expect(screen.queryByLabelText('Código postal')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar opciones de entrega' }))
    await waitFor(() => expect(confirm).toBeEnabled())
    await userEvent.click(confirm)
    await waitFor(() => expect(checkoutBody).toMatchObject({ deliveryMethod: 'StorePickup', shippingAddress: null, expectedShippingCost: 0 }))
    expect(await screen.findByText('Av. Demo 123')).toBeInTheDocument()
    expect(screen.getByText('Lunes a viernes de 9 a 17')).toBeInTheDocument()
    expect(screen.getByText('Traé tu documento.')).toBeInTheDocument()
  })

  it('bloquea retiro cuando falta la dirección configurada', async () => {
    let checkoutPosts = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: '', pickupInstructions: 'Indicaciones', pickupHours: 'Horario' })
      if (url.endsWith('/api/cart') && method === 'GET') return json(cart)
      if (url.endsWith('/api/cart/checkout') && method === 'POST') checkoutPosts += 1
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('radio', { name: /Retiro en tienda/ }))
    expect(screen.getByRole('button', { name: 'Confirmar y pagar' })).toBeDisabled()
    expect(await screen.findByRole('alert')).toHaveTextContent('falta configurar su dirección')
    expect(screen.getByRole('button', { name: 'Reintentar opciones de entrega' })).toBeInTheDocument()
    expect(checkoutPosts).toBe(0)
  })

  it('muestra los datos de retiro provistos por la configuración', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: 'Traé tu documento y número de orden.', pickupHours: 'Lunes a viernes de 9 a 17' })
      if (url.endsWith('/api/cart')) return json(cart)
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('radio', { name: /Retiro en tienda/ }))
    expect(screen.getByText('Av. Demo 123')).toBeInTheDocument()
    expect(screen.getByText('Lunes a viernes de 9 a 17')).toBeInTheDocument()
    expect(screen.getByText('Traé tu documento y número de orden.')).toBeInTheDocument()
  })

  it('ante un 409 comercial refresca carrito y tarifa sin buscar ni crear una orden', async () => {
    const updatedCart = { ...cart, subtotal: 90000, discount: 10000, couponCode: 'NUEVO', total: 80000 }
    let cartReads = 0; let shippingReads = 0; let checkoutPosts = 0; let pendingOrderReads = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart/shipping-options')) { shippingReads += 1; return json({ homeDeliveryCost: shippingReads === 1 ? 6500 : 8000, pickupAddress: 'Local', pickupInstructions: '', pickupHours: '' }) }
      if (url.endsWith('/api/cart/shipping-quotes') && method === 'POST') return json([quote])
      if (url.endsWith('/api/cart') && method === 'GET') { cartReads += 1; return json(cartReads === 1 ? cart : updatedCart) }
      if (url.endsWith('/api/cart/checkout') && method === 'POST') { checkoutPosts += 1; return json({ message: 'El precio, descuento o costo de envio cambio. Revisa el resumen antes de confirmar.', code: 'checkout_pricing_changed' }, 409) }
      if (url.endsWith('/api/orders/my')) { pendingOrderReads += 1; return json([]) }
      return json([])
    })
    render(<App />)
    await fillAddressAndQuote()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar y pagar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Revisá el nuevo total y confirmá nuevamente')
    expect(screen.getByText(/80\.000,00/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirmar y pagar' })).toBeDisabled()
    expect(screen.getByText(/Descuento \(NUEVO\)/)).toBeInTheDocument()
    expect(checkoutPosts).toBe(1)
    expect(shippingReads).toBe(2)
    expect(cartReads).toBe(2)
    expect(pendingOrderReads).toBe(0)
    expect(screen.queryByRole('link', { name: 'Ver orden' })).not.toBeInTheDocument()
  })

  it('genera otra clave solo al iniciar un nuevo intento después de Rejected', async () => {
    window.history.replaceState(null, '', '/checkout/orden/81')
    localStorage.setItem('gymshop.payment-key.81', 'old-key')
    let current = payment('Rejected')
    let sentKey = ''
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) return json([current])
      if (url.endsWith('/api/orders/81/payments') && method === 'POST') { sentKey = JSON.parse(String(init?.body)).idempotencyKey; current = payment('Pending'); return json(current) }
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: 'Intentar pagar nuevamente' }))
    await waitFor(() => expect(screen.getAllByText('Pendiente').length).toBeGreaterThan(0))
    expect(sentKey).not.toBe('old-key')
    expect(sentKey).toBe(localStorage.getItem('gymshop.payment-key.81'))
  })

  it('actualiza un pago Pending sin crear otro intento', async () => {
    window.history.replaceState(null, '', '/checkout/orden/81')
    localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'admin@gym.com', name: 'Admin', role: 'Admin' }))
    let paymentPosts = 0
    let paymentReads = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) { paymentReads++; return json([payment('Pending')]) }
      if (url.endsWith('/api/orders/81/payments') && method === 'POST') { paymentPosts++; return json(payment('Pending')) }
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: 'Actualizar estado' }))
    await waitFor(() => expect(paymentReads).toBeGreaterThan(1))
    expect(paymentPosts).toBe(0)
  })

  it('no muestra Actualizar estado a un usuario común', async () => {
    window.history.replaceState(null, '', '/checkout/orden/81')
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/81')) return json(order)
      if (url.endsWith('/api/payments/orders/81')) return json([payment('Pending')])
      return json([])
    })
    render(<App />)
    await screen.findAllByText('Pendiente')
    expect(screen.queryByRole('button', { name: 'Actualizar estado' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancelar orden' })).not.toBeInTheDocument()
  })

  async function renderResult(orderStatus: Order['status'], attempts: Payment[]) {
    window.history.replaceState(null, '', '/checkout/orden/81')
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/81')) return json({ ...order, status: orderStatus })
      if (url.endsWith('/api/payments/orders/81')) return json(attempts)
      if (url.endsWith('/api/payments/bank-transfer-details')) return json({ bankName: 'Banco de prueba', accountHolder: 'Tienda', cbu: '123456789', alias: 'tienda.test', cuit: '' })
      return json([])
    })
    render(<App />)
    await screen.findByText('Estado del pago')
  }

  it('prioriza Canceled cuando un intento anterior de Mercado Pago fue Approved', async () => {
    await renderResult('Canceled', [
      { ...payment('Pending'), id: 92, provider: 'MercadoPago' },
      { ...payment('Approved'), id: 91, provider: 'MercadoPago' },
    ])
    expect(screen.getByRole('heading', { name: 'Pedido cancelado' })).toBeInTheDocument()
    expect(screen.getByText('Detectamos un pago aprobado para este pedido cancelado. La situación está en revisión.')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: '¡Pago aprobado!' })).not.toBeInTheDocument()
    expect(screen.queryByText('La compra quedó confirmada.')).not.toBeInTheDocument()
  })

  it('muestra la cancelación cuando ningún intento de Mercado Pago fue aprobado', async () => {
    await renderResult('Canceled', [
      { ...payment('Pending'), id: 92, provider: 'MercadoPago' },
      { ...payment('Rejected'), id: 91, provider: 'MercadoPago' },
    ])
    expect(screen.getByRole('heading', { name: 'Pedido cancelado' })).toBeInTheDocument()
    expect(screen.getByText('El pedido fue cancelado.')).toBeInTheDocument()
    expect(screen.queryByText('La orden permanece pendiente hasta que se apruebe un pago.')).not.toBeInTheDocument()
    expect(screen.queryByText('La compra quedó confirmada.')).not.toBeInTheDocument()
  })

  it('mantiene informativa una orden cancelada con un pago Pending y checkoutUrl', async () => {
    await renderResult('Canceled', [{ ...payment('Pending'), provider: 'MercadoPago', checkoutUrl: 'https://provider.test/checkout/81' }])
    expect(screen.getByRole('heading', { name: 'Pedido cancelado' })).toBeInTheDocument()
    expect(screen.getByText('Pendiente')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Continuar con el proveedor' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Iniciar pago' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Actualizar estado' })).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Transferencia bancaria/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Mercado Pago/ })).not.toBeInTheDocument()
  })

  it('mantiene informativa una orden cancelada sin intentos de pago', async () => {
    await renderResult('Canceled', [])
    expect(screen.getByRole('heading', { name: 'Pedido cancelado' })).toBeInTheDocument()
    expect(screen.getByText('Todavía no hay intentos de pago.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Continuar con el proveedor' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Iniciar pago' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Actualizar estado' })).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Transferencia bancaria/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Mercado Pago/ })).not.toBeInTheDocument()
  })

  it('no indica transferir ni muestra datos bancarios en una orden cancelada', async () => {
    await renderResult('Canceled', [payment('Pending')])
    expect(screen.getByText('Este intento de pago pertenece a un pedido cancelado. No realices pagos para esta orden.')).toBeInTheDocument()
    expect(screen.queryByText(/Transferí el monto exacto/)).not.toBeInTheDocument()
    expect(screen.queryByText('Banco de prueba')).not.toBeInTheDocument()
    expect(screen.queryByText('123456789')).not.toBeInTheDocument()
  })

  it('no invita a reintentar un pago rechazado de una orden cancelada', async () => {
    await renderResult('Canceled', [{ ...payment('Rejected'), provider: 'MercadoPago' }])
    expect(screen.getByText('Rechazado')).toBeInTheDocument()
    expect(screen.getByText('Este intento de pago pertenece a un pedido cancelado. No realices pagos para esta orden.')).toBeInTheDocument()
    expect(screen.queryByText(/Podés iniciar un nuevo intento/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Intentar pagar nuevamente' })).not.toBeInTheDocument()
  })

  it('confirma el flujo normal Paid con un pago Approved de Mercado Pago', async () => {
    await renderResult('Paid', [{ ...payment('Approved'), provider: 'MercadoPago' }])
    expect(screen.getByRole('heading', { name: '¡Pago aprobado!' })).toBeInTheDocument()
    expect(screen.getByText('La compra quedó confirmada.')).toBeInTheDocument()
  })

  it('muestra el snapshot de retiro en el detalle de Mis órdenes', async () => {
    window.history.replaceState(null, '', '/ordenes')
    const pickupOrder = { ...order, deliveryMethod: 'StorePickup' as const, shippingAddress: '', shippingCost: 0, pickupAddress: 'Sucursal histórica 456', pickupHours: 'Sábados de 10 a 13', pickupInstructions: 'Presentá el código de compra.' }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/my')) return json([{ id: 81, userId: 7, userEmail: 'u@gym.com', userName: 'Usuario', createdAt: order.createdAt, total: order.total, deliveryMethod: 'StorePickup', status: 'Pending', updatedAt: null, lastPaymentStatus: null, lastPaymentId: null }])
      if (url.endsWith('/api/orders/81')) return json(pickupOrder)
      if (url.endsWith('/api/payments/orders/81')) return json([])
      return json([])
    })
    render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: /#81/ }))
    expect(await screen.findByText('Sucursal histórica 456')).toBeInTheDocument()
    expect(screen.getByText('Sábados de 10 a 13')).toBeInTheDocument()
    expect(screen.getByText('Presentá el código de compra.')).toBeInTheDocument()
  })

  it('muestra facturas y notas de crédito autorizadas en Mis órdenes', async () => {
    window.history.replaceState(null, '', '/ordenes')
    const baseDocument = {
      orderId: 81, paymentId: 91, relatedDocumentId: null, status: 'Authorized', currency: 'ARS',
      issuerBusinessName: 'GymShop', recipientName: 'Usuario', recipientEmail: 'u@gym.com', recipientAddress: null,
      subtotal: 84000, discountAmount: 0, shippingAmount: 0, total: 84000, pointOfSale: 1,
      authorizationProvider: 'ARCA-Homologation', caeExpiresOn: '2026-10-15', rejectionCode: null,
      rejectionReason: null, createdAtUtc: '2026-10-05T10:00:00Z', authorizedAtUtc: '2026-10-05T10:01:00Z', items: []
    } satisfies Partial<BillingDocument>
    const documents: BillingDocument[] = [
      { ...baseDocument, id: '00000000-0000-0000-0000-000000000001', category: 'Invoice', type: 'InvoiceC', documentNumber: 12, cae: '74123456789012' } as BillingDocument,
      { ...baseDocument, id: '00000000-0000-0000-0000-000000000002', category: 'CreditNote', type: 'CreditNoteC', documentNumber: 3, cae: '74123456789013' } as BillingDocument
    ]
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = String(input)
      if (url.endsWith('/api/cart')) return json(emptyCart)
      if (url.endsWith('/api/orders/my')) return json([{ id: 81, userId: 7, userEmail: 'u@gym.com', userName: 'Usuario', createdAt: order.createdAt, total: order.total, status: 'Paid', updatedAt: null, lastPaymentStatus: 'Approved', lastPaymentId: 91 }])
      if (url.endsWith('/api/orders/81/billing-documents')) return json(documents)
      if (url.endsWith('/api/orders/81')) return json({ ...order, status: 'Paid' })
      if (url.endsWith('/api/payments/orders/81')) return json([payment('Approved')])
      return json([])
    })

    render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: /#81/ }))

    expect(await screen.findByText('Factura C')).toBeInTheDocument()
    expect(screen.getByText('Nota de crédito C')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Ver PDF' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Descargar PDF' })).toHaveLength(2)
  })
})

describe('checkout invitado', () => {
  beforeEach(() => {
    localStorage.clear(); sessionStorage.clear(); vi.restoreAllMocks()
    window.history.replaceState(null, '', '/checkout')
    localStorage.setItem('gymshop.guest-cart.v1', JSON.stringify(cart.items))
  })

  it('crea una orden pendiente por transferencia con datos de contacto y sin requerir cuenta', async () => {
    const token = '3c44e646-2f94-4e88-aad7-794f2e2f61eb'
    const guestOrder: Order = {
      ...order, id: 701, userId: null, userEmail: 'ana@example.com', userName: 'Ana Prueba',
      customerPhone: '+54 341 555 0101', expiresAt: '2026-08-12T10:00:00Z', shippingCost: 6500, total: 90500,
      payments: [{ id: 702, provider: 'BankTransfer', externalReference: '481726395', amount: 90500, currency: 'ARS', status: 'Pending', createdAt: '2026-08-11T10:00:00Z', paidAt: null }]
    }
    let requestBody: Record<string, unknown> | null = null
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 6500, pickupAddress: 'Av. Demo 123', pickupInstructions: '', pickupHours: '' })
      if (url.endsWith('/api/payments/methods')) return json({ bankTransferAvailable: true, mercadoPagoAvailable: true, mercadoPagoUnavailableReason: null })
      if (url.endsWith('/api/guest-checkout') && init?.method === 'POST') {
        requestBody = JSON.parse(String(init.body))
        return json({ kind: 'Order', accessToken: token, expiresAt: guestOrder.expiresAt, order: guestOrder, checkout: null })
      }
      if (url.includes('/api/guest-checkout/orders/701?')) return json(guestOrder)
      if (url.endsWith('/api/payments/bank-transfer-details')) return json({ bankName: 'Banco', accountHolder: 'GymShop', cbu: '123', alias: 'GYMSHOP', cuit: '' })
      return json([])
    })

    render(<App />)
    await userEvent.type(await screen.findByLabelText('Nombre'), 'Ana')
    await userEvent.type(screen.getByLabelText('Apellido'), 'Prueba')
    await userEvent.type(screen.getByLabelText('Email'), 'ana@example.com')
    await userEvent.type(screen.getByLabelText('Teléfono'), '+54 341 555 0101')
    await userEvent.type(screen.getByLabelText('Código postal'), '2000')
    await userEvent.type(screen.getByLabelText('Provincia'), 'Santa Fe')
    await userEvent.type(screen.getByLabelText('Ciudad o localidad'), 'Rosario')
    await userEvent.type(screen.getByLabelText('Calle'), 'Córdoba')
    await userEvent.type(screen.getByLabelText('Número'), '1234')
    expect(screen.getByText(/24 horas para transferir/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Generar orden de transferencia' }))

    await waitFor(() => expect(window.location.pathname).toBe('/checkout/orden/701'))
    expect(requestBody).toMatchObject({
      customer: { firstName: 'Ana', lastName: 'Prueba', email: 'ana@example.com', phone: '+54 341 555 0101' },
      paymentProvider: 'BankTransfer', expectedSubtotal: 84000, expectedShippingCost: 6500,
      items: [{ productId: 4, quantity: 2 }]
    })
    expect(await screen.findByText(/El stock se validará cuando se acredite el pago/)).toBeInTheDocument()
    expect(screen.getByText('481726395')).toBeInTheDocument()
    expect(localStorage.getItem('gymshop.guest-cart.v1')).toBeNull()
  })
})

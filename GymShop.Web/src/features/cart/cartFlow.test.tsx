import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'

const product = { id: 42, name: 'Mancuerna Pro', description: 'Acero', price: 1000, stock: 5, imageUrl: '/product.webp', isActive: true, category: null }
const cartItem = (quantity: number) => ({ productId: 42, productName: product.name, unitPrice: product.price, quantity, subtotal: product.price * quantity, stock: product.stock, imageUrl: product.imageUrl })
const json = (body: unknown, status = 200) => Promise.resolve(new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

describe('carrito visitante y fusión autenticada', () => {
  beforeEach(() => { localStorage.clear(); window.history.replaceState(null, '', '/'); vi.restoreAllMocks() })

  it('agrega como visitante desde una URL de producto y limita por stock', async () => {
    window.history.replaceState(null, '', '/catalogo/42')
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => json(product))
    render(<App />)
    expect(await screen.findByRole('heading', { name: product.name })).toBeInTheDocument()
    const quantity = screen.getByLabelText('Cantidad')
    expect(quantity).toHaveValue(1)
    expect(screen.getByRole('button', { name: 'Restar una unidad' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Sumar una unidad' }))
    expect(quantity).toHaveValue(2)
    await userEvent.clear(quantity)
    await userEvent.type(quantity, '5')
    expect(screen.getByRole('button', { name: 'Sumar una unidad' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Agregar al carrito' }))
    expect(await screen.findByRole('dialog', { name: 'Carrito' })).toBeInTheDocument()
    expect(screen.getByLabelText(`Cantidad de ${product.name}`)).toHaveTextContent('5')
    expect(screen.getByRole('button', { name: `Sumar una unidad de ${product.name}` })).toBeDisabled()
    expect(JSON.parse(localStorage.getItem('gymshop.guest-cart.v1') || '[]')[0].quantity).toBe(5)
  })

  it('combina cantidades con el carrito existente usando un objetivo absoluto', async () => {
    localStorage.setItem('gymshop.guest-cart.v1', JSON.stringify([cartItem(4)]))
    localStorage.setItem('gymshop.token', 'jwt')
    localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' }))
    window.history.replaceState(null, '', '/carrito')
    const calls: Array<{ url: string; method: string; body: string | null }> = []
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input); const method = init?.method || 'GET'
      calls.push({ url, method, body: init?.body ? String(init.body) : null })
      if (url.endsWith('/api/cart') && method === 'GET') return json({ id: 1, userId: 7, total: 2000, items: [cartItem(2)] })
      if (url.endsWith('/api/products/42')) return json(product)
      if (url.endsWith('/api/cart/items/42') && method === 'PUT') return json({ id: 1, userId: 7, total: 5000, items: [cartItem(5)] })
      return json([])
    })
    render(<App />)
    await waitFor(() => expect(screen.getByLabelText(`Cantidad de ${product.name}`)).toHaveValue(5))
    expect(calls.find(call => call.method === 'PUT')?.body).toBe(JSON.stringify({ quantity: 5 }))
    expect(localStorage.getItem('gymshop.guest-cart.v1')).toBe('[]')
    expect(await screen.findByRole('status')).toHaveTextContent('Ajustamos al stock disponible')
  })

  it('avisa y conserva un producto visitante que ya no existe', async () => {
    localStorage.setItem('gymshop.guest-cart.v1', JSON.stringify([cartItem(1)]))
    localStorage.setItem('gymshop.token', 'jwt')
    localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' }))
    window.history.replaceState(null, '', '/carrito')
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => String(input).endsWith('/api/products/42') ? json({ message: 'No existe.' }, 404) : json({ id: 1, userId: 7, total: 0, items: [] }))
    render(<App />)
    expect(await screen.findByRole('status')).toHaveTextContent('inexistentes, inactivos o sin stock')
    expect(JSON.parse(localStorage.getItem('gymshop.guest-cart.v1') || '[]')).toHaveLength(1)
  })

  it('aplica y quita un cupón mostrando subtotal, descuento y total confirmados por API', async () => {
    localStorage.setItem('gymshop.token', 'jwt'); localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' })); window.history.replaceState(null, '', '/carrito')
    const base = { id: 1, userId: 7, subtotal: 2000, discount: 0, total: 2000, couponCode: null, items: [cartItem(2)] }
    const discounted = { ...base, discount: 200, total: 1800, couponCode: 'SAVE10' }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => { const url = String(input); if (url.endsWith('/api/cart/coupon') && init?.method === 'POST') return json(discounted); if (url.endsWith('/api/cart/coupon') && init?.method === 'DELETE') return json(base); return json(base) })
    render(<App />); await screen.findByText('Mancuerna Pro')
    await userEvent.type(screen.getByLabelText('Código de descuento'), 'save10'); await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }))
    expect(await screen.findByText(/−.*200,00/)).toBeInTheDocument(); expect(screen.getAllByText(/1\.800,00/).length).toBeGreaterThan(0); expect(screen.getAllByText(/2\.000,00/).length).toBeGreaterThan(0)
    await userEvent.click(screen.getByRole('button', { name: 'Quitar SAVE10' })); await waitFor(() => expect(screen.queryByText(/−.*200,00/)).not.toBeInTheDocument())
  })

  it('remueve visualmente un cupón invalidado por un cambio del carrito', async () => {
    localStorage.setItem('gymshop.token', 'jwt'); localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' })); window.history.replaceState(null, '', '/carrito')
    const discounted = { id: 1, userId: 7, subtotal: 2000, discount: 200, total: 1800, couponCode: 'MINIMUM', items: [cartItem(2)] }
    const invalidated = { id: 1, userId: 7, subtotal: 1000, discount: 0, total: 1000, couponCode: null, items: [cartItem(1)] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => String(input).endsWith('/api/cart/items/42') && init?.method === 'PUT' ? json(invalidated) : json(discounted))
    render(<App />); expect(await screen.findByRole('button', { name: 'Quitar MINIMUM' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Quitar una unidad de Mancuerna Pro' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Quitar MINIMUM' })).not.toBeInTheDocument()); expect(screen.queryByText(/−.*200,00/)).not.toBeInTheDocument()
  })

  it('continúa al checkout sin validar cuando el cupón está vacío', async () => {
    localStorage.setItem('gymshop.token', 'jwt'); localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' })); window.history.replaceState(null, '', '/carrito')
    const base = { id: 1, userId: 7, subtotal: 2000, discount: 0, total: 2000, couponCode: null, items: [cartItem(2)] }
    let couponPosts = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => { if (String(input).endsWith('/api/cart/coupon') && init?.method === 'POST') couponPosts++; return json(base) })
    render(<App />); await screen.findByText('Mancuerna Pro')

    await userEvent.click(screen.getByRole('button', { name: 'Continuar al checkout' }))

    await waitFor(() => expect(window.location.pathname).toBe('/checkout'))
    expect(couponPosts).toBe(0)
  })

  it('valida y aplica una sola vez el cupón escrito antes de continuar', async () => {
    localStorage.setItem('gymshop.token', 'jwt'); localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' })); window.history.replaceState(null, '', '/carrito')
    const base = { id: 1, userId: 7, subtotal: 2000, discount: 0, total: 2000, couponCode: null, items: [cartItem(2)] }
    const discounted = { ...base, discount: 200, total: 1800, couponCode: 'SAVE10' }
    let couponPosts = 0
    let resolveCoupon!: (response: Response) => void
    const pendingCoupon = new Promise<Response>(resolve => { resolveCoupon = resolve })
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.endsWith('/api/cart/coupon') && init?.method === 'POST') { couponPosts++; return pendingCoupon }
      if (url.endsWith('/api/cart/shipping-options')) return json({ homeDeliveryCost: 0, pickupAddress: 'Local', pickupInstructions: '', pickupHours: '' })
      return json(couponPosts > 0 ? discounted : base)
    })
    render(<App />); await screen.findByText('Mancuerna Pro')
    await userEvent.type(screen.getByLabelText('Código de descuento'), 'SAVE10')
    const continueButton = screen.getByRole('button', { name: 'Continuar al checkout' })

    await userEvent.dblClick(continueButton)
    expect(couponPosts).toBe(1)
    expect(window.location.pathname).toBe('/carrito')
    resolveCoupon(await json(discounted))

    await waitFor(() => expect(window.location.pathname).toBe('/checkout'))
  })

  it.each([
    ['código inventado', 404, 'El código de descuento no es válido.'],
    ['error de API', 503, 'No pudimos validar el cupón.'],
  ])('permanece en el carrito ante %s', async (_, status, detail) => {
    localStorage.setItem('gymshop.token', 'jwt'); localStorage.setItem('gymshop.user', JSON.stringify({ id: 7, email: 'u@gym.com', name: 'U', role: 'User' })); window.history.replaceState(null, '', '/carrito')
    const base = { id: 1, userId: 7, subtotal: 2000, discount: 0, total: 2000, couponCode: null, items: [cartItem(2)] }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => String(input).endsWith('/api/cart/coupon') && init?.method === 'POST' ? json({ detail }, status) : json(base))
    render(<App />); await screen.findByText('Mancuerna Pro')
    await userEvent.type(screen.getByLabelText('Código de descuento'), status === 404 ? 'INVENTADO' : 'ERROR')

    await userEvent.click(screen.getByRole('button', { name: 'Continuar al checkout' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(detail)
    expect(window.location.pathname).toBe('/carrito')
  })
})

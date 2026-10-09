import { useCallback, useEffect, useMemo, useState } from 'react'
import { ApiError } from '../../api/client'
import { api } from '../../api/gymshop'
import type { Cart, CartItem, Product, ProductVariant, User } from '../../api/types'
import { session } from '../../auth/session'
import { CartContext, type CartContextValue } from './cartContextValue'
import { guestCartStore, mergePlanStore, type MergePlan } from './guestCart'
const describe = (error: unknown) => error instanceof ApiError ? error.message : 'No pudimos actualizar el carrito.'

export function CartProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(() => session.user())
  const [cart, setCart] = useState<CartItem[]>(() => user ? [] : guestCartStore.read())
  const [pricing, setPricing] = useState<Pick<Cart, 'subtotal' | 'discount' | 'total' | 'couponCode'>>({ subtotal: 0, discount: 0, total: 0, couponCode: null })
  const acceptServerCart = useCallback((result: Cart) => { setCart(result.items); setPricing({ subtotal: result.subtotal, discount: result.discount, total: result.total, couponCode: result.couponCode }) }, [])
  const [loading, setLoading] = useState(Boolean(user))
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [drawerOpen, setDrawerOpen] = useState(false)

  const refresh = useCallback(async () => {
    const currentUser = session.user()
    if (!currentUser) {
      setCart(guestCartStore.read())
      setLoading(false)
      return
    }
    setLoading(true)
    try { acceptServerCart(await api.cart()) }
    catch (value) { setError(describe(value)) }
    finally { setLoading(false) }
  }, [acceptServerCart])

  const mergeGuestCart = useCallback(async (currentUser: User) => {
    const guestItems = guestCartStore.read()
    let serverCart = await api.cart()
    let plan = mergePlanStore.read()

    if (!plan || plan.userId !== currentUser.id) {
      const planned = []
      const unavailable: string[] = []
      const capped: string[] = []
      for (const guest of guestItems) {
        try {
          const product = await api.product(guest.productId)
          const variants = product.variants ?? []
          const variant = guest.productVariantId ? variants.find(item => item.id === guest.productVariantId && item.isActive) : undefined
          if (variants.length && !variant) { unavailable.push(guest.productName); continue }
          const serverQuantity = serverCart.items.find(item => item.productId === product.id && (item.productVariantId ?? null) === (guest.productVariantId ?? null))?.quantity ?? 0
          const requested = serverQuantity + guest.quantity
          const targetQuantity = Math.min(requested, variant?.stock ?? product.stock)
          if (targetQuantity < requested) capped.push(product.name)
          if (targetQuantity > 0) planned.push({ productId: product.id, productVariantId: guest.productVariantId, productName: product.name, guestQuantity: guest.quantity, targetQuantity })
          else unavailable.push(product.name)
        } catch (value) {
          if (value instanceof ApiError && value.status === 404) unavailable.push(guest.productName)
          else throw value
        }
      }
      plan = { userId: currentUser.id, items: planned } satisfies MergePlan
      mergePlanStore.write(plan)
      const messages = []
      if (planned.length) messages.push('Combinamos tu carrito de visitante con tu carrito guardado.')
      if (capped.length) messages.push(`Ajustamos al stock disponible: ${capped.join(', ')}.`)
      if (unavailable.length) messages.push(`No pudimos sumar productos inexistentes, inactivos o sin stock: ${unavailable.join(', ')}.`)
      setNotice(messages.join(' '))
    }

    for (const item of [...plan.items]) {
      const current = serverCart.items.find(serverItem => serverItem.productId === item.productId && (serverItem.productVariantId ?? null) === (item.productVariantId ?? null))
      if (current?.quantity !== item.targetQuantity) {
        serverCart = current
          ? await api.updateCartItem(item.productId, item.targetQuantity, item.productVariantId)
          : await api.addCartItem(item.productId, item.targetQuantity, item.productVariantId)
      }
      guestCartStore.remove(item.productId, item.productVariantId)
      plan = { ...plan, items: plan.items.filter(candidate => candidate.productId !== item.productId) }
      mergePlanStore.write(plan)
    }
    mergePlanStore.clear()
    acceptServerCart(serverCart)
  }, [acceptServerCart])

  useEffect(() => {
    const changed = () => setUser(session.user())
    window.addEventListener('gymshop:session', changed)
    return () => window.removeEventListener('gymshop:session', changed)
  }, [])

  useEffect(() => {
    let active = true
    setError('')
    if (!user) {
      setCart(guestCartStore.read())
      setLoading(false)
      return () => { active = false }
    }
    setLoading(true)
    mergeGuestCart(user).catch(value => {
      if (active) setError(`${describe(value)} Tu carrito de visitante se conservó para reintentarlo.`)
    }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [mergeGuestCart, user])

  const add = useCallback(async (product: Product, quantity: number, variant?: ProductVariant) => {
    setError('')
    setNotice('')
    const safeQuantity = Math.max(1, Math.floor(quantity))
    if (session.user()) {
      const existing = cart.find(item => item.productId === product.id && (item.productVariantId ?? null) === (variant?.id ?? null))?.quantity ?? 0
      const available = variant?.stock ?? product.stock
      const accepted = Math.min(safeQuantity, Math.max(available - existing, 0))
      if (accepted < 1) { setNotice(`Ya alcanzaste el stock disponible de ${product.name}.`); setDrawerOpen(true); return }
      const result = await api.addCartItem(product.id, accepted, variant?.id)
      acceptServerCart(result)
      if (accepted < safeQuantity) setNotice(`Sumamos ${accepted}; alcanzaste el límite de stock de ${product.name}.`)
    } else {
      const items = guestCartStore.read()
      const existing = items.find(item => item.productId === product.id && (item.productVariantId ?? null) === (variant?.id ?? null))
      const available = variant?.stock ?? product.stock
      const target = Math.min((existing?.quantity ?? 0) + safeQuantity, available)
      const next = existing
        ? items.map(item => item === existing ? { ...item, quantity: target, subtotal: item.unitPrice * target, stock: available } : item)
        : [...items, guestCartStore.fromProduct(product, target, variant)]
      guestCartStore.write(next)
      setCart(next)
      if (target < (existing?.quantity ?? 0) + safeQuantity) setNotice(`Ajustamos la cantidad al stock disponible de ${product.name}.`)
    }
    setDrawerOpen(true)
  }, [cart])

  const update = useCallback(async (productId: number, quantity: number, productVariantId?: number | null) => {
    const item = cart.find(candidate => candidate.productId === productId && (candidate.productVariantId ?? null) === (productVariantId ?? null))
    if (!item) return
    const target = Math.min(Math.max(1, Math.floor(quantity)), item.stock)
    if (session.user()) acceptServerCart(await api.updateCartItem(productId, target, productVariantId))
    else {
      const next = cart.map(candidate => candidate === item ? { ...candidate, quantity: target, subtotal: candidate.unitPrice * target } : candidate)
      guestCartStore.write(next); setCart(next)
    }
    if (target !== quantity) setNotice(`La cantidad máxima disponible de ${item.productName} es ${item.stock}.`)
  }, [acceptServerCart, cart])

  const remove = useCallback(async (productId: number, productVariantId?: number | null) => {
    if (session.user()) acceptServerCart(await api.removeCartItem(productId, productVariantId))
    else setCart(guestCartStore.remove(productId, productVariantId))
  }, [acceptServerCart])

  const clear = useCallback(async () => {
    if (session.user()) { await api.clearCart(); setCart([]); setPricing({ subtotal: 0, discount: 0, total: 0, couponCode: null }) }
    else { guestCartStore.clear(); setCart([]) }
  }, [])

  const value = useMemo<CartContextValue>(() => ({
    items: cart,
    subtotal: session.user() ? pricing.subtotal : cart.reduce((sum, item) => sum + item.subtotal, 0),
    discount: session.user() ? pricing.discount : 0,
    total: session.user() ? pricing.total : cart.reduce((sum, item) => sum + item.subtotal, 0),
    couponCode: session.user() ? pricing.couponCode : null,
    count: cart.reduce((sum, item) => sum + item.quantity, 0),
    loading, error, notice, drawerOpen, add, update, remove, clear, refresh,
    applyCoupon: async code => { setError(''); try { const result = await api.applyCoupon(code); acceptServerCart(result); if (!result.couponCode) { setError('El cupón no quedó aplicado.'); return false } setNotice('Cupón aplicado.'); return true } catch (value) { setError(describe(value)); throw value } },
    removeCoupon: async () => { setError(''); acceptServerCart(await api.removeCoupon()); setNotice('Cupón quitado.') },
    openDrawer: () => setDrawerOpen(true),
    closeDrawer: () => setDrawerOpen(false),
    dismissMessages: () => { setError(''); setNotice('') },
  }), [acceptServerCart, add, cart, clear, drawerOpen, error, loading, notice, pricing, refresh, remove, update])

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

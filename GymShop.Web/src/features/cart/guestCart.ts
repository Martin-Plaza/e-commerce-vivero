import type { CartItem, Product, ProductVariant } from '../../api/types'

const CART_KEY = 'gymshop.guest-cart.v1'
const MERGE_KEY = 'gymshop.cart-merge.v1'

export type GuestCartItem = CartItem

export interface MergePlanItem {
  productId: number
  productVariantId?: number | null
  productName: string
  guestQuantity: number
  targetQuantity: number
}

export interface MergePlan {
  userId: number
  items: MergePlanItem[]
}

function readJson<T>(key: string, fallback: T): T {
  try {
    const value = localStorage.getItem(key)
    return value ? JSON.parse(value) as T : fallback
  } catch {
    return fallback
  }
}

export const guestCartStore = {
  read: () => readJson<GuestCartItem[]>(CART_KEY, []),
  write: (items: GuestCartItem[]) => localStorage.setItem(CART_KEY, JSON.stringify(items)),
  remove: (productId: number, productVariantId?: number | null) => {
    const items = guestCartStore.read().filter(item => !(item.productId === productId && (item.productVariantId ?? null) === (productVariantId ?? null)))
    guestCartStore.write(items)
    return items
  },
  clear: () => localStorage.removeItem(CART_KEY),
  fromProduct: (product: Product, quantity: number, variant?: ProductVariant): GuestCartItem => ({
    productId: product.id,
    productName: product.name,
    unitPrice: variant?.price ?? product.price,
    quantity,
    subtotal: (variant?.price ?? product.price) * quantity,
    stock: variant?.stock ?? product.stock,
    imageUrl: product.imageUrl,
    productVariantId: variant?.id ?? null,
    variantSku: variant?.sku ?? null,
    variantAttributes: variant?.attributes ?? null,
  }),
}

export const mergePlanStore = {
  read: () => readJson<MergePlan | null>(MERGE_KEY, null),
  write: (plan: MergePlan) => localStorage.setItem(MERGE_KEY, JSON.stringify(plan)),
  clear: () => localStorage.removeItem(MERGE_KEY),
}

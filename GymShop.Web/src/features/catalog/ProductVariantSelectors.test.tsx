import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../api/gymshop'
import type { Product } from '../../api/types'
import { CartContext, type CartContextValue } from '../cart/cartContextValue'
import { ProductDetailPage } from './ProductDetailPage'

const product: Product = {
  id: 12, name: 'Remera técnica', description: null, price: 100, stock: 3, imageUrl: '/general.webp', isActive: true, category: null,
  colorImages: { Negro: '/negro.webp', Blanco: '/blanco.webp' },
  variants: [
    { id: 1, sku: 'REM-NEG-M', price: 110, stock: 2, isActive: true, attributes: { Color: 'Negro', Talle: 'M' } },
    { id: 2, sku: 'REM-NEG-L', price: 110, stock: 0, isActive: true, attributes: { Color: 'Negro', Talle: 'L' } },
    { id: 3, sku: 'REM-BLA-L', price: 120, stock: 1, isActive: true, attributes: { Color: 'Blanco', Talle: 'L' } },
  ],
}

function setup() {
  const add = vi.fn().mockResolvedValue(undefined)
  const cart: CartContextValue = { items: [], total: 0, subtotal: 0, discount: 0, couponCode: null, count: 0, loading: false, error: '', notice: '', drawerOpen: false, add, update: vi.fn(), remove: vi.fn(), clear: vi.fn(), refresh: vi.fn(), applyCoupon: vi.fn(), removeCoupon: vi.fn(), openDrawer: vi.fn(), closeDrawer: vi.fn(), dismissMessages: vi.fn() }
  render(<CartContext.Provider value={cart}><MemoryRouter initialEntries={['/catalogo/12']}><Routes><Route path="/catalogo/:productId" element={<ProductDetailPage />} /></Routes></MemoryRouter></CartContext.Provider>)
  return { add }
}

describe('selectores de variantes', () => {
  beforeEach(() => { vi.restoreAllMocks(); vi.spyOn(api, 'product').mockResolvedValue(product); vi.spyOn(api, 'products').mockResolvedValue([]) })

  it('cambia la imagen por color y actualiza la disponibilidad de talles', async () => {
    setup(); const negro = await screen.findByRole('radio', { name: 'Negro' }); await userEvent.click(negro)
    expect(screen.getByRole('img', { name: 'Remera técnica, color Negro' })).toHaveAttribute('src', '/negro.webp')
    expect(screen.getByRole('radio', { name: 'M' })).toBeEnabled()
    expect(screen.getByRole('radio', { name: 'L' })).toBeDisabled()
  })

  it('limpia un talle incompatible al cambiar color y solo agrega una combinación disponible', async () => {
    const { add } = setup(); const blanco = await screen.findByRole('radio', { name: 'Blanco' }); await userEvent.click(blanco); await userEvent.click(screen.getByRole('radio', { name: 'L' }))
    expect(screen.getByRole('button', { name: 'Agregar al carrito' })).toBeEnabled()
    await userEvent.click(screen.getByRole('radio', { name: 'Negro' }))
    expect(screen.getByRole('radio', { name: 'L' })).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByRole('button', { name: 'Agregar al carrito' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('radio', { name: 'M' })); await userEvent.click(screen.getByRole('button', { name: 'Agregar al carrito' }))
    expect(add).toHaveBeenCalledWith(expect.objectContaining({ id: 12 }), 1, expect.objectContaining({ sku: 'REM-NEG-M' }))
  })

  it('permite seleccionar con teclado y mantiene selección única', async () => {
    setup(); await screen.findByRole('radio', { name: 'Negro' }); screen.getByRole('radio', { name: 'Blanco' }).focus(); await userEvent.keyboard('{Enter}')
    expect(screen.getByRole('radio', { name: 'Blanco' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('radio', { name: 'Negro' })).toHaveAttribute('aria-checked', 'false')
  })
})

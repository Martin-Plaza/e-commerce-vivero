import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'

const categories = [
  { id: 1, name: 'Fuerza', slug: 'fuerza', description: 'Pesas y bancos', displayOrder: 10 },
  { id: 2, name: 'Cardio', slug: 'cardio', description: 'Acondicionamiento', displayOrder: 20 },
]
const products = [
  { id: 1, name: 'Mancuerna Pro', description: 'Acero cromado', price: 300, stock: 5, imageUrl: null, isActive: true, category: { id: 1, name: 'Fuerza', slug: 'fuerza' } },
  { id: 2, name: 'Banco plano', description: 'Entrenamiento con pesas', price: 700, stock: 0, imageUrl: null, isActive: true, category: { id: 1, name: 'Fuerza', slug: 'fuerza' } },
  { id: 3, name: 'Soga rápida', description: 'Cardio intenso', price: 100, stock: 8, imageUrl: null, isActive: true, category: { id: 2, name: 'Cardio', slug: 'cardio' } },
]
const json = (body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
const mockCatalog = () => vi.spyOn(globalThis, 'fetch').mockImplementation(input => String(input).includes('/categories') ? json(categories) : json(products))

describe('experiencia pública del catálogo', () => {
  beforeEach(() => { localStorage.clear(); window.history.replaceState(null, '', '/'); vi.restoreAllMocks() })

  it('navega desde una categoría visual y abre el catálogo filtrado', async () => {
    mockCatalog(); render(<App />)
    await userEvent.click(await screen.findByRole('button', { name: /Fuerza/ }))
    await waitFor(() => expect(window.location.search).toBe('?categoria=fuerza'))
    expect(await screen.findByRole('radio', { name: 'Fuerza' })).toBeChecked()
    expect(screen.getByRole('heading', { name: 'Mancuerna Pro' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Soga rápida' })).not.toBeInTheDocument()
  })

  it('combina filtros y los limpia para recuperar todos los productos', async () => {
    window.history.replaceState(null, '', '/catalogo'); mockCatalog(); render(<App />)
    expect(await screen.findByRole('heading', { name: 'Mancuerna Pro' })).toBeInTheDocument()
    await userEvent.type(screen.getByPlaceholderText('¿Qué necesitás para entrenar?'), 'banco')
    await userEvent.click(screen.getByRole('radio', { name: 'Sin stock' }))
    expect(screen.getByRole('heading', { name: 'Banco plano' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Mancuerna Pro' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }))
    expect(screen.getByPlaceholderText('¿Qué necesitás para entrenar?')).toHaveValue('')
    expect(screen.getByRole('radio', { name: 'Todos' })).toBeChecked()
    expect(screen.getAllByRole('heading', { level: 3 })).toHaveLength(3)
  })

  it('vuelve arriba al abrir el detalle de un producto desde el catálogo', async () => {
    window.history.replaceState(null, '', '/catalogo')
    mockCatalog()
    const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined)
    render(<App />)

    await userEvent.click(await screen.findByRole('link', { name: 'Ver Mancuerna Pro' }))

    expect(window.location.pathname).toBe('/catalogo/1')
    expect(scrollTo).toHaveBeenCalledWith(0, 0)
  })

  it('cierra los filtros móviles con Escape y devuelve el foco al disparador', async () => {
    window.history.replaceState(null, '', '/catalogo'); mockCatalog(); render(<App />)
    await screen.findByRole('heading', { name: 'Mancuerna Pro' })
    const trigger = screen.getByRole('button', { name: 'Filtros' })
    await userEvent.click(trigger)
    const dialog = screen.getByRole('dialog', { name: 'Filtros del catálogo' })
    expect(within(dialog).getByRole('button', { name: 'Cerrar filtros' })).toHaveFocus()
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Filtros del catálogo' })).not.toBeInTheDocument())
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('muestra sólo productos relacionados de la misma categoría', async () => {
    window.history.replaceState(null, '', '/catalogo/1')
    vi.spyOn(globalThis, 'fetch').mockImplementation(input => String(input).endsWith('/api/products/1') ? json(products[0]) : json(products))
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'También te puede interesar' })).toBeInTheDocument()
    const related = screen.getByRole('region', { name: 'También te puede interesar' })
    expect(within(related).getByRole('heading', { name: 'Banco plano' })).toBeInTheDocument()
    expect(within(related).queryByRole('heading', { name: 'Soga rápida' })).not.toBeInTheDocument()
    expect(within(related).getByRole('link', { name: 'Ver categoría →' })).toHaveAttribute('href', '/catalogo?categoria=fuerza')
  })

  it('no busca relacionados ni crea enlaces de categoría para un producto sin categoría', async () => {
    window.history.replaceState(null, '', '/catalogo/9')
    const uncategorized = { ...products[0], id: 9, name: 'Producto libre', category: null }
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => json(uncategorized))
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Producto libre' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'También te puede interesar' })).not.toBeInTheDocument()
    expect(document.querySelector('a[href*="categoria=undefined"]')).not.toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})

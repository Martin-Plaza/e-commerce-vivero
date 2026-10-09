import { describe, expect, it } from 'vitest'
import type { Product } from '../../api/types'
import { filterAndSortProducts, type CatalogFilters } from './catalogFilters'

const products: Product[] = [
  { id: 1, name: 'Mancuerna Pro', description: 'Acero cromado', price: 300, stock: 5, imageUrl: null, isActive: true, category: { id: 1, name: 'Fuerza', slug: 'fuerza' } },
  { id: 2, name: 'Banco', description: 'Para usar con mancuerna', price: 700, stock: 0, imageUrl: null, isActive: true, category: { id: 1, name: 'Fuerza', slug: 'fuerza' } },
  { id: 3, name: 'Banda', description: 'Resistencia', price: 100, stock: 8, imageUrl: null, isActive: true, category: { id: 2, name: 'Funcional', slug: 'funcional' } },
]
const defaults: CatalogFilters = { query: '', category: '', availability: 'all', minPrice: null, maxPrice: null, sort: 'relevance' }

describe('filtros y orden del catálogo', () => {
  it('busca por nombre y descripción y prioriza el nombre', () => {
    expect(filterAndSortProducts(products, { ...defaults, query: 'mancuerna' }).map(product => product.id)).toEqual([1, 2])
  })

  it('filtra disponibilidad y rango inclusivo', () => {
    expect(filterAndSortProducts(products, { ...defaults, availability: 'available', minPrice: 100, maxPrice: 300 }).map(product => product.id)).toEqual([1, 3])
  })

  it('ordena por precio y alfabéticamente', () => {
    expect(filterAndSortProducts(products, { ...defaults, sort: 'price-desc' }).map(product => product.id)).toEqual([2, 1, 3])
    expect(filterAndSortProducts(products, { ...defaults, sort: 'name-asc' }).map(product => product.name)).toEqual(['Banco', 'Banda', 'Mancuerna Pro'])
  })

  it('filtra por slug de categoría', () => {
    expect(filterAndSortProducts(products, { ...defaults, category: 'funcional' }).map(product => product.id)).toEqual([3])
  })
})

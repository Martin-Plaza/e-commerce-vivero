import type { Product } from '../../api/types'

export type AvailabilityFilter = 'all' | 'available' | 'unavailable'
export type CatalogSort = 'relevance' | 'price-asc' | 'price-desc' | 'name-asc' | 'name-desc'

export interface CatalogFilters {
  query: string
  availability: AvailabilityFilter
  minPrice: number | null
  maxPrice: number | null
  sort: CatalogSort
  category: string
}

const relevance = (product: Product, normalizedQuery: string) => {
  if (!normalizedQuery) return 0
  const name = product.name.toLocaleLowerCase('es')
  const description = (product.description ?? '').toLocaleLowerCase('es')
  if (name === normalizedQuery) return 0
  if (name.startsWith(normalizedQuery)) return 1
  if (name.includes(normalizedQuery)) return 2
  if (description.includes(normalizedQuery)) return 3
  return 4
}

export function filterAndSortProducts(products: Product[], filters: CatalogFilters) {
  const query = filters.query.trim().toLocaleLowerCase('es')
  const filtered = products.filter(product => {
    const searchable = `${product.name} ${product.description ?? ''}`.toLocaleLowerCase('es')
    if (query && !searchable.includes(query)) return false
    if (filters.category && product.category?.slug !== filters.category) return false
    if (filters.availability === 'available' && product.stock < 1) return false
    if (filters.availability === 'unavailable' && product.stock > 0) return false
    if (filters.minPrice !== null && product.price < filters.minPrice) return false
    if (filters.maxPrice !== null && product.price > filters.maxPrice) return false
    return true
  })

  return filtered
    .map((product, originalIndex) => ({ product, originalIndex }))
    .sort((left, right) => {
      const byName = left.product.name.localeCompare(right.product.name, 'es')
      if (filters.sort === 'price-asc') return left.product.price - right.product.price || byName
      if (filters.sort === 'price-desc') return right.product.price - left.product.price || byName
      if (filters.sort === 'name-asc') return byName
      if (filters.sort === 'name-desc') return -byName
      return relevance(left.product, query) - relevance(right.product, query) || left.originalIndex - right.originalIndex
    })
    .map(entry => entry.product)
}

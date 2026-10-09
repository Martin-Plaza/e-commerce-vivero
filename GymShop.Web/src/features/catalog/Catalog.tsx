import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/gymshop'
import type { Category, Product } from '../../api/types'
import { storefront } from '../../config/storefront'
import { useCart } from '../cart/useCart'
import { filterAndSortProducts, type AvailabilityFilter, type CatalogSort } from './catalogFilters'
import { ProductCard } from './ProductCard'

const numberOrNull = (value: string) => value === '' ? null : Number(value)

export function Catalog() {
  const cart = useCart(); const [searchParams] = useSearchParams()
  const [products, setProducts] = useState<Product[]>([]); const [categories, setCategories] = useState<Category[]>([])
  const [query, setQuery] = useState(() => searchParams.get('buscar') ?? ''); const [category, setCategory] = useState(() => searchParams.get('categoria') ?? '')
  const [availability, setAvailability] = useState<AvailabilityFilter>('all'); const [minPrice, setMinPrice] = useState(''); const [maxPrice, setMaxPrice] = useState('')
  const [sort, setSort] = useState<CatalogSort>('relevance'); const [filtersOpen, setFiltersOpen] = useState(false); const [loading, setLoading] = useState(true); const [loadError, setLoadError] = useState('')
  const filterTriggerRef = useRef<HTMLButtonElement>(null); const filterCloseRef = useRef<HTMLButtonElement>(null)
  const load = () => { setLoading(true); setLoadError(''); Promise.all([api.products(false), api.categories()]).then(([result, categoryList]) => { setProducts(result.filter(product => product.isActive)); setCategories(categoryList) }).catch(() => setLoadError('Revisá tu conexión e intentá nuevamente.')).finally(() => setLoading(false)) }
  useEffect(load, [])
  useEffect(() => {
    setQuery(searchParams.get('buscar') ?? '')
    setCategory(searchParams.get('categoria') ?? '')
  }, [searchParams])
  useEffect(() => {
    if (!filtersOpen) return
    filterCloseRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') { setFiltersOpen(false); window.setTimeout(() => filterTriggerRef.current?.focus(), 0) } }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [filtersOpen])
  const invalidRange = minPrice !== '' && maxPrice !== '' && Number(minPrice) > Number(maxPrice)
  const visible = useMemo(() => invalidRange ? [] : filterAndSortProducts(products, { query, category, availability, minPrice: numberOrNull(minPrice), maxPrice: numberOrNull(maxPrice), sort }), [availability, category, invalidRange, maxPrice, minPrice, products, query, sort])
  const activeFilterCount = [category, availability !== 'all' ? availability : '', minPrice, maxPrice].filter(Boolean).length
  const clearFilters = () => { setQuery(''); setCategory(''); setAvailability('all'); setMinPrice(''); setMaxPrice(''); setSort('relevance') }
  const closeFilters = () => { setFiltersOpen(false); window.setTimeout(() => filterTriggerRef.current?.focus(), 0) }
  const filters = <div className="filter-fields"><div className="filter-heading"><h2>{storefront.copy.filtersTitle}</h2>{activeFilterCount > 0 && <button className="text-button" onClick={clearFilters}>{storefront.copy.clearFilters}</button>}</div>
    <fieldset><legend>Categoría</legend><label className="radio-option"><input type="radio" name="category" checked={category === ''} onChange={() => setCategory('')} /> Todas las categorías</label>{categories.map(item => <label className="radio-option" key={item.id}><input type="radio" name="category" checked={category === item.slug} onChange={() => setCategory(item.slug)} /> {item.name}</label>)}</fieldset>
    <fieldset><legend>Disponibilidad</legend>{([['all', 'Todos'], ['available', 'Con stock'], ['unavailable', 'Sin stock']] as const).map(option => <label className="radio-option" key={option[0]}><input type="radio" name="availability" checked={availability === option[0]} onChange={() => setAvailability(option[0])} /> {option[1]}</label>)}</fieldset>
    <fieldset><legend>Precio</legend><div className="price-inputs"><label>Mínimo<input type="number" min="0" placeholder="$ 0" value={minPrice} onChange={event => setMinPrice(event.target.value)} /></label><label>Máximo<input type="number" min="0" placeholder="Sin límite" value={maxPrice} onChange={event => setMaxPrice(event.target.value)} /></label></div></fieldset></div>
  return <section className="catalog-page"><div className="catalog-intro"><p className="eyebrow">{storefront.copy.catalogEyebrow}</p><h1>{storefront.copy.catalogTitle}</h1><p>Especies, macetas y cuidados seleccionados para cada tipo de espacio.</p></div>
    <label className="catalog-search"><span className="search-icon" aria-hidden="true">⌕</span><span className="sr-only">Buscar productos</span><input type="search" placeholder={storefront.copy.searchPlaceholder} value={query} onChange={event => setQuery(event.target.value)} />{query && <button type="button" aria-label="Limpiar búsqueda" onClick={() => setQuery('')}>×</button>}</label>
    <div className="mobile-filter-bar"><button ref={filterTriggerRef} aria-expanded={filtersOpen} aria-controls="mobile-catalog-filters" onClick={() => setFiltersOpen(true)}>{storefront.copy.filtersAction}{activeFilterCount > 0 && <b>{activeFilterCount}</b>}</button><Sort value={sort} onChange={setSort} /></div>
    <div className="catalog-layout"><aside className="desktop-filters">{filters}</aside><div className="catalog-results"><div className="results-bar"><span aria-live="polite"><strong>{visible.length}</strong> {storefront.copy.resultsLabel}</span><Sort value={sort} onChange={setSort} /></div>
      {invalidRange && <div className="error" role="alert">El precio mínimo no puede superar al máximo.</div>}
      {loading ? <Skeleton count={6} label="Cargando catálogo" /> : loadError ? <div className="empty state-card"><span aria-hidden="true">!</span><h2>No pudimos cargar los productos</h2><p>{loadError}</p><button className="primary" onClick={load}>Reintentar</button></div> : products.length === 0 ? <div className="empty state-card"><h2>El catálogo se está preparando</h2><p>Volvé pronto para descubrir nuevos productos.</p></div> : visible.length === 0 ? <div className="empty state-card"><span aria-hidden="true">⌕</span><h2>No encontramos coincidencias</h2><p>Probá con otra búsqueda o quitá algunos filtros.</p><button onClick={clearFilters}>{storefront.copy.clearFilters}</button></div> : <div className="catalog-grid">{visible.map(product => <ProductCard product={product} key={product.id} onAdd={item => void cart.add(item, 1)} />)}</div>}
    </div></div>
    {filtersOpen && <div id="mobile-catalog-filters" className="filter-drawer-backdrop" role="dialog" aria-modal="true" aria-label="Filtros del catálogo" onMouseDown={event => { if (event.currentTarget === event.target) closeFilters() }}><div className="filter-drawer"><button ref={filterCloseRef} className="close" aria-label="Cerrar filtros" onClick={closeFilters}>×</button>{filters}<button className="primary apply-filters" onClick={closeFilters}>Ver {visible.length} productos</button></div></div>}
  </section>
}

function Sort({ value, onChange }: { value: CatalogSort; onChange(value: CatalogSort): void }) { return <label>Ordenar por<select value={value} onChange={event => onChange(event.target.value as CatalogSort)}><option value="relevance">Relevancia</option><option value="price-asc">Menor precio</option><option value="price-desc">Mayor precio</option><option value="name-asc">Nombre A–Z</option><option value="name-desc">Nombre Z–A</option></select></label> }
function Skeleton({ count, label }: { count: number; label: string }) { return <div className="skeleton-grid" aria-label={label}>{Array.from({ length: count }, (_, index) => <div className="product-skeleton" key={index} />)}</div> }

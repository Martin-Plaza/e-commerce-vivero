import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { api } from '../../api/gymshop'
import type { Product, ProductAttributeDefinition, ProductVariant } from '../../api/types'
import { money, storefront } from '../../config/storefront'
import { absoluteSeoUrl } from '../../config/seo'
import { useCart } from '../cart/useCart'
import { Seo } from '../seo/Seo'
import { ProductCard } from './ProductCard'
import { ProductImage } from './ProductImage'
import { QuantitySelector } from './QuantitySelector'

function legacyDefinitions(variants: ProductVariant[]): ProductAttributeDefinition[] {
  const names = [...new Set(variants.flatMap(v => Object.keys(v.attributes)))]; let next = -1
  return names.map((name, index) => ({ id: -(index + 1), name, presentation: name.toLowerCase() === 'color' ? 'ColorSwatch' : 'Button', displayOrder: index, options: [...new Set(variants.map(v => v.attributes[name]).filter(Boolean))].map((value, order) => ({ id: next--, value, visualValue: null, displayOrder: order })) }))
}

export function ProductDetailPage() {
  const { productId } = useParams(); const cart = useCart()
  const [product, setProduct] = useState<Product | null>(null); const [selected, setSelected] = useState<Record<number, number>>({})
  const [related, setRelated] = useState<Product[]>([]); const [quantity, setQuantity] = useState(1); const [loading, setLoading] = useState(true); const [error, setError] = useState(''); const [notFound, setNotFound] = useState(false)
  useEffect(() => { const id = Number(productId); setLoading(true); setError(''); setNotFound(false); setRelated([]); if (!Number.isInteger(id) || id < 1) { setNotFound(true); setError('El producto solicitado no es válido.'); setLoading(false); return }
    api.product(id).then(result => { const normalized = { ...result, variants: result.variants ?? [], colorImages: result.colorImages ?? {} }; setProduct(normalized); setSelected({}); setQuantity(normalized.variants.length ? 1 : normalized.stock > 0 ? 1 : 0); const slug = normalized.category?.slug; if (slug) return api.products(false).then(products => setRelated(products.filter(item => item.id !== result.id && item.category?.slug === slug).slice(0, 3))).catch(() => undefined) }).catch(value => { const missing = value instanceof ApiError && value.status === 404; setNotFound(missing); setError(missing ? 'El producto no existe o ya no está activo.' : value instanceof ApiError ? value.message : 'No pudimos cargar el producto.') }).finally(() => setLoading(false)) }, [productId])
  const variants = useMemo(() => (product?.variants ?? []).filter(v => v.isActive), [product])
  const definitions = useMemo(() => product?.productAttributes?.length ? product.productAttributes : legacyDefinitions(variants), [product, variants])
  const optionById = useMemo(() => new Map(definitions.flatMap(a => a.options.map(o => [o.id, { ...o, attributeId: a.id }] as const))), [definitions])
  const variantOptions = (variant: ProductVariant) => variant.optionIds?.length ? variant.optionIds : definitions.flatMap(a => { const value = Object.entries(variant.attributes).find(([name]) => name.toLowerCase() === a.name.toLowerCase())?.[1]; return a.options.filter(o => o.value === value).map(o => o.id) })
  const matches = (variant: ProductVariant, choices: Record<number, number>) => Object.entries(choices).every(([attributeId, optionId]) => variantOptions(variant).includes(optionId) && optionById.get(optionId)?.attributeId === Number(attributeId))
  const selectedVariant = variants.find(v => definitions.length > 0 && definitions.every(a => selected[a.id]) && matches(v, selected))
  const choose = (attributeId: number, optionId: number) => { const next = { ...selected, [attributeId]: optionId }; for (const key of Object.keys(next).map(Number)) if (key !== attributeId && !variants.some(v => v.stock > 0 && matches(v, next))) delete next[key]; setSelected(next); setQuantity(1) }
  if (loading) return <><Seo metadata={{ title: 'Producto', path: `/catalogo/${productId}`, noIndex: true }} /><div className="product-detail-skeleton" aria-label="Cargando producto"><div /><div /></div></>
  if (error || !product) return <><Seo metadata={{ title: notFound ? 'Producto no encontrado' : 'Producto no disponible', path: `/catalogo/${productId}`, noIndex: true }} /><div className="empty state-card"><h1>{notFound ? 'No encontramos ese producto' : 'El servicio no está disponible'}</h1><p>{error}</p><div className="page-state-actions">{!notFound && <button className="primary" onClick={() => window.location.reload()}>Reintentar</button>}<Link className="link-button" to="/catalogo">Volver al catálogo</Link></div></div></>
  const colorAttribute = definitions.find(a => a.presentation === 'ColorSwatch'); const selectedColor = colorAttribute ? selected[colorAttribute.id] : undefined
  const colorImage = selectedColor ? product.colorImages?.[String(selectedColor)] ?? (selectedColor < 0 ? product.colorImages?.[optionById.get(selectedColor)?.value ?? ''] : undefined) : undefined
  const stock = selectedVariant?.stock ?? (variants.length ? 0 : product.stock); const price = selectedVariant?.price ?? product.price
  const seoDescription = product.description?.trim() || `${product.name}, equipamiento disponible en ${storefront.identity.name}.`
  return <><Seo metadata={{
    title: product.name,
    description: seoDescription,
    path: `/catalogo/${product.id}`,
    image: product.imageUrl,
    type: 'product',
    jsonLd: {
      '@context': 'https://schema.org',
      '@type': 'Product',
      name: product.name,
      description: seoDescription,
      image: product.imageUrl ? [absoluteSeoUrl(product.imageUrl)] : undefined,
      category: product.category?.name,
      offers: {
        '@type': 'Offer',
        url: absoluteSeoUrl(`/catalogo/${product.id}`),
        priceCurrency: storefront.market.currency,
        price,
        availability: stock > 0 ? 'https://schema.org/InStock' : 'https://schema.org/OutOfStock',
        itemCondition: 'https://schema.org/NewCondition',
      },
    },
  }} /><section className="product-detail-page"><nav className="breadcrumbs" aria-label="Ruta de navegación"><Link to="/">Inicio</Link><span>/</span><Link to="/catalogo">Catálogo</Link>{product.category && <><span>/</span><Link to={`/catalogo?categoria=${product.category.slug}`}>{product.category.name}</Link></>}<span>/</span><span aria-current="page">{product.name}</span></nav>
    <div className="product-detail-layout"><div className="product-detail-image"><ProductImage src={colorImage ?? product.imageUrl} alt={selectedColor ? `${product.name}, color ${optionById.get(selectedColor)?.value}` : product.name} /></div><div className="product-detail-copy">{product.category && <Link className="detail-category" to={`/catalogo?categoria=${product.category.slug}`}>{product.category.name}</Link>}<h1>{product.name}</h1>{product.description?.trim() && <p className="product-description">{product.description}</p>}<strong className="product-detail-price">{money(price)}</strong>
      {definitions.map((attribute, attributeIndex) => <fieldset className="variant-selector" key={attribute.id}><legend>{attribute.name}{selected[attribute.id] && <span>: {optionById.get(selected[attribute.id])?.value}</span>}</legend><div className={`variant-options ${attribute.presentation === 'ColorSwatch' ? 'color-options' : 'size-options'}`} role="radiogroup" aria-label={attribute.name}>{attribute.options.map(option => { const priorChoices = Object.fromEntries(definitions.slice(0, attributeIndex).filter(a => selected[a.id]).map(a => [a.id, selected[a.id]])); const enabled = variants.some(v => v.stock > 0 && matches(v, { ...priorChoices, [attribute.id]: option.id })); return <button type="button" role="radio" aria-checked={selected[attribute.id] === option.id} aria-label={option.value} title={option.value} disabled={!enabled} className={attribute.presentation === 'ColorSwatch' ? 'color-swatch' : 'size-option'} style={attribute.presentation === 'ColorSwatch' ? { '--swatch-color': option.visualValue || option.value } as React.CSSProperties : undefined} onClick={() => choose(attribute.id, option.id)} key={option.id}>{attribute.presentation === 'ColorSwatch' ? <span aria-hidden="true" /> : option.value}</button> })}</div></fieldset>)}
      <p className={stock > 0 ? 'in-stock' : 'no-stock'}><span aria-hidden="true">●</span> {variants.length && !selectedVariant ? 'Seleccioná una combinación disponible' : stock > 0 ? `Disponible · ${stock} unidades` : 'Producto sin stock'}</p>{(!variants.length ? product.stock > 0 : Boolean(selectedVariant && stock > 0)) && <div className="product-buy"><QuantitySelector value={quantity} max={stock} onChange={setQuantity} /><button className="primary" onClick={() => void cart.add(product, quantity, selectedVariant)}>{storefront.copy.addToCart}</button></div>}<div className="purchase-benefits"><p><b>Envíos a todo el país</b><span>Coordinamos la entrega de tu equipo.</span></p><p><b>Compra protegida</b><span>Tu pedido y tu cuenta, siempre seguros.</span></p></div><div className="trust-seal" aria-label="Compra segura"><svg viewBox="0 0 48 48" aria-hidden="true"><path d="M24 4 40 10v12c0 10-6.5 18.2-16 22-9.5-3.8-16-12-16-22V10L24 4Z"/><path d="m16.5 24 5 5 10-11"/></svg><div><strong>Sitio y compra segura</strong><span>Datos protegidos · Pago validado · Seguimiento de tu pedido</span></div></div></div></div>
    {related.length > 0 && <section className="related-products" aria-labelledby="related-title"><p className="eyebrow">{storefront.copy.relatedEyebrow}</p><div className="section-title"><h2 id="related-title">{storefront.copy.relatedTitle}</h2><Link to={`/catalogo?categoria=${product.category?.slug}`}>Ver categoría →</Link></div><div className="product-grid">{related.map(item => <ProductCard key={item.id} product={item} onAdd={p => void cart.add(p, 1)} />)}</div></section>}
  </section></>
}

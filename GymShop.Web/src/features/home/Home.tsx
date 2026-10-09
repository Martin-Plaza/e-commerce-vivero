import { useEffect, useState } from 'react'
import { api } from '../../api/gymshop'
import type { Category, Product } from '../../api/types'
import { ProductCard } from '../catalog/ProductCard'
import { storefront } from '../../config/storefront'

const hasSupportedImage = (product: Product) => Boolean(product.imageUrl && (product.imageUrl.startsWith('/') || /^https?:\/\//i.test(product.imageUrl)))

export function Home({ onCatalog, onProduct }: { onCatalog(category?: string): void; onProduct(id: number): void }) {
  const [products, setProducts] = useState<Product[]>([])
  const [categories, setCategories] = useState<Category[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    setLoadError(false)
    Promise.all([api.products(false), api.categories()]).then(([result, categoryList]) => {
      const ordered = [...result.filter(hasSupportedImage), ...result.filter(product => !hasSupportedImage(product))]
      setProducts(ordered.slice(0, 6))
      setCategories(Array.isArray(categoryList) ? categoryList : [])
    }).catch(() => setLoadError(true)).finally(() => setLoading(false))
  }, [])

  const campaignProduct = products.find(product => product.name.toLocaleLowerCase('es').includes('monstera')) ?? products[0]
  return <>
    <section className="home-hero">
      <img src={storefront.assets.hero} alt={storefront.copy.heroImageAlt} />
      <div className="home-hero-overlay">
        <p className="eyebrow">{storefront.copy.heroEyebrow}</p>
        <h1>{storefront.copy.heroTitle}</h1>
        <p>{storefront.copy.heroDescription}</p>
        <button className="primary hero-cta" onClick={() => onCatalog()}>{storefront.copy.heroAction}</button>
      </div>
      <button className="hero-product-link" onClick={() => onCatalog()}>{storefront.copy.heroSecondaryAction} <span>→</span></button>
    </section>

    <section className="category-section" aria-labelledby="category-title">
      <div className="section-title"><div><p className="eyebrow">{storefront.copy.categoriesEyebrow}</p><h2 id="category-title">{storefront.copy.categoriesTitle}</h2><p>{storefront.copy.categoriesDescription}</p></div></div>
      <div className="category-grid">{categories.map((item, index) => {
        const visual = storefront.categoryVisuals[item.slug] ?? { symbol: String(index + 1).padStart(2, '0'), color: storefront.theme.colors.accent }
        const count = products.filter(product => product.category?.slug === item.slug).length
        return <button key={item.id} className="category-tile" style={{ '--category-color': item.color || visual.color } as React.CSSProperties} onClick={() => onCatalog(item.slug)}>
          <span className="category-number">{visual.symbol}</span><span className="category-copy"><strong>{item.name}</strong><small>{item.description || `${count} productos para descubrir`}</small><b>{storefront.copy.categoryAction} →</b></span>
        </button>
      })}</div>
    </section>

    <section className="featured-products">
      <div className="section-title"><div><p className="eyebrow">{storefront.copy.featuredEyebrow}</p><h2>{storefront.copy.featuredTitle}</h2></div><button onClick={() => onCatalog()}>{storefront.copy.featuredAction}</button></div>
      {loading ? <div className="skeleton-grid" aria-label={storefront.copy.featuredLoading}>{[1, 2, 3].map(item => <div className="product-skeleton" key={item} />)}</div> : loadError ? <div className="empty"><p>{storefront.copy.featuredLoadError}</p><button onClick={() => window.location.reload()}>Reintentar</button></div> : products.length === 0 ? <div className="empty">{storefront.copy.featuredEmpty}</div> : <div className="product-grid">{products.map(product => <ProductCard product={product} key={product.id} />)}</div>}
    </section>

    {campaignProduct && <section className="editorial-section" aria-label={storefront.copy.campaignAriaLabel}>
      <button className="editorial-banner" onClick={() => onProduct(campaignProduct.id)}>
        <img src={storefront.assets.campaign} alt={storefront.copy.campaignImageAlt} loading="lazy" />
        <span className="editorial-copy">
          <span className="eyebrow">{storefront.copy.campaignEyebrow}</span>
          <strong>{storefront.copy.campaignTitleLine1}<br />{storefront.copy.campaignTitleLine2}</strong>
          <span>{storefront.copy.campaignPrefix} {campaignProduct.name} <b>→</b></span>
        </span>
      </button>
    </section>}

    <section className="trust-section" aria-label={`Beneficios de comprar en ${storefront.identity.name}`}>
      {storefront.copy.benefits.map(benefit => <article key={benefit.title}><span>{benefit.icon}</span><div><h3>{benefit.title}</h3><p>{benefit.description}</p></div></article>)}
    </section>

    <section className="closing-cta" aria-label="Inspiración para crear un rincón verde">
      <img src="/images/nursery/potting-editorial.png" alt="Manos trasplantando una planta con cuidado" loading="lazy" />
      <div><p className="eyebrow">TU RINCÓN, MÁS VIVO</p><h2>Empezá con una planta</h2><p>No hace falta saberlo todo. Elegí una especie para tu espacio y acompañala a crecer.</p><button className="primary" onClick={() => onCatalog()}>Encontrá la indicada</button></div>
    </section>

  </>
}

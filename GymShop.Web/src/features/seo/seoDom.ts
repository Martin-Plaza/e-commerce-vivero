import { absoluteSeoUrl, seoConfig } from '../../config/seo'
import { storefront } from '../../config/storefront'

type JsonLd = Record<string, unknown>

export interface SeoMetadata {
  title?: string
  description?: string
  path?: string
  image?: string | null
  type?: 'website' | 'product'
  noIndex?: boolean
  jsonLd?: JsonLd | JsonLd[]
}

const upsertMeta = (selector: string, attributes: Record<string, string>) => {
  let element = document.head.querySelector<HTMLMetaElement>(selector)
  if (!element) {
    element = document.createElement('meta')
    document.head.appendChild(element)
  }
  Object.entries(attributes).forEach(([name, value]) => element!.setAttribute(name, value))
}

const upsertLink = (rel: string, href: string) => {
  let element = document.head.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`)
  if (!element) {
    element = document.createElement('link')
    element.rel = rel
    document.head.appendChild(element)
  }
  element.href = href
}

const organizationSchema = (): JsonLd => ({
  '@context': 'https://schema.org',
  '@type': 'OnlineStore',
  name: storefront.identity.name,
  url: absoluteSeoUrl('/'),
  image: absoluteSeoUrl(seoConfig.defaultImage),
  email: storefront.legal.email,
  address: storefront.legal.address || undefined,
  areaServed: storefront.market.region,
  currenciesAccepted: storefront.market.currency,
})

const websiteSchema = (): JsonLd => ({
  '@context': 'https://schema.org',
  '@type': 'WebSite',
  name: storefront.identity.name,
  url: absoluteSeoUrl('/'),
  inLanguage: storefront.market.locale,
  potentialAction: {
    '@type': 'SearchAction',
    target: `${absoluteSeoUrl('/catalogo')}?buscar={search_term_string}`,
    'query-input': 'required name=search_term_string',
  },
})

export function applySeo(metadata: SeoMetadata) {
  const title = metadata.title
    ? seoConfig.titleTemplate.replace('%s', metadata.title)
    : seoConfig.defaultTitle
  const description = metadata.description?.trim() || seoConfig.defaultDescription
  const canonical = absoluteSeoUrl(metadata.path || window.location.pathname)
  const image = absoluteSeoUrl(metadata.image || seoConfig.defaultImage)
  const robots = metadata.noIndex ? 'noindex, nofollow' : 'index, follow, max-image-preview:large'

  document.title = title
  document.documentElement.lang = storefront.market.locale
  upsertMeta('meta[name="description"]', { name: 'description', content: description })
  upsertMeta('meta[name="robots"]', { name: 'robots', content: robots })
  upsertMeta('meta[property="og:title"]', { property: 'og:title', content: title })
  upsertMeta('meta[property="og:description"]', { property: 'og:description', content: description })
  upsertMeta('meta[property="og:type"]', { property: 'og:type', content: metadata.type || 'website' })
  upsertMeta('meta[property="og:url"]', { property: 'og:url', content: canonical })
  upsertMeta('meta[property="og:image"]', { property: 'og:image', content: image })
  upsertMeta('meta[property="og:locale"]', { property: 'og:locale', content: seoConfig.locale })
  upsertMeta('meta[property="og:site_name"]', { property: 'og:site_name', content: storefront.identity.name })
  upsertMeta('meta[name="twitter:card"]', { name: 'twitter:card', content: 'summary_large_image' })
  upsertMeta('meta[name="twitter:title"]', { name: 'twitter:title', content: title })
  upsertMeta('meta[name="twitter:description"]', { name: 'twitter:description', content: description })
  upsertMeta('meta[name="twitter:image"]', { name: 'twitter:image', content: image })
  if (seoConfig.twitterHandle) upsertMeta('meta[name="twitter:site"]', { name: 'twitter:site', content: seoConfig.twitterHandle })
  else document.head.querySelector('meta[name="twitter:site"]')?.remove()
  upsertLink('canonical', canonical)

  const pageSchemas = metadata.jsonLd ? (Array.isArray(metadata.jsonLd) ? metadata.jsonLd : [metadata.jsonLd]) : []
  let script = document.head.querySelector<HTMLScriptElement>('script[data-seo-json-ld]')
  if (!script) {
    script = document.createElement('script')
    script.type = 'application/ld+json'
    script.dataset.seoJsonLd = 'true'
    document.head.appendChild(script)
  }
  script.textContent = JSON.stringify([organizationSchema(), websiteSchema(), ...pageSchemas])
}

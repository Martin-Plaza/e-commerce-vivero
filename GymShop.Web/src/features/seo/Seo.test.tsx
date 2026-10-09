import { afterEach, describe, expect, it, vi } from 'vitest'
import { applySeo } from './seoDom'

describe('SEO', () => {
  afterEach(() => {
    document.head.querySelectorAll('meta[name="description"], meta[name="robots"], meta[property^="og:"], meta[name^="twitter:"], link[rel="canonical"], script[data-seo-json-ld]').forEach(element => element.remove())
    vi.unstubAllEnvs()
  })

  it('actualiza metadatos públicos y datos estructurados', () => {
    applySeo({
      title: 'Mancuerna 10 kg',
      description: 'Mancuerna para entrenamiento de fuerza.',
      path: '/catalogo/10',
      image: '/producto.webp',
      type: 'product',
      jsonLd: { '@context': 'https://schema.org', '@type': 'Product', name: 'Mancuerna 10 kg' },
    })

    expect(document.title).toBe('Mancuerna 10 kg | GymShop')
    expect(document.querySelector('meta[name="description"]')).toHaveAttribute('content', 'Mancuerna para entrenamiento de fuerza.')
    expect(document.querySelector('meta[name="robots"]')).toHaveAttribute('content', 'index, follow, max-image-preview:large')
    expect(document.querySelector('meta[property="og:type"]')).toHaveAttribute('content', 'product')
    expect(document.querySelector('link[rel="canonical"]')).toHaveAttribute('href', `${window.location.origin}/catalogo/10`)
    expect(document.querySelector('script[data-seo-json-ld]')?.textContent).toContain('Mancuerna 10 kg')
  })

  it('marca como no indexables las pantallas privadas', () => {
    applySeo({ title: 'Checkout', path: '/checkout', noIndex: true })

    expect(document.querySelector('meta[name="robots"]')).toHaveAttribute('content', 'noindex, nofollow')
  })
})

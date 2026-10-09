import { storefront } from './storefront'

const configuredSiteUrl = import.meta.env.VITE_SITE_URL?.trim().replace(/\/$/, '') || ''

export const seoConfig = {
  siteUrl: configuredSiteUrl,
  defaultTitle: storefront.identity.name,
  titleTemplate: `%s | ${storefront.identity.name}`,
  defaultDescription: import.meta.env.VITE_STORE_DESCRIPTION?.trim() || storefront.copy.heroDescription,
  defaultImage: import.meta.env.VITE_SEO_IMAGE?.trim() || '/images/nursery/hero-raiz-viva.png',
  twitterHandle: import.meta.env.VITE_TWITTER_HANDLE?.trim() || '',
  locale: storefront.market.locale.replace('-', '_'),
} as const

export const absoluteSeoUrl = (value: string) => {
  if (/^https?:\/\//i.test(value)) return value
  const origin = seoConfig.siteUrl || (typeof window !== 'undefined' ? window.location.origin : '')
  return origin ? `${origin}${value.startsWith('/') ? value : `/${value}`}` : value
}

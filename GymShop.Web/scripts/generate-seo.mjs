/* global process */
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { loadEnv } from 'vite'

const mode = process.argv[2] || 'production'
const root = resolve(import.meta.dirname, '..')
const output = resolve(root, 'dist')
const env = loadEnv(mode, root, '')
const siteUrl = (env.VITE_SITE_URL || '').trim().replace(/\/$/, '')
const storeName = (env.VITE_STORE_NAME || 'GymShop').trim()
const description = (env.VITE_STORE_DESCRIPTION || 'Equipamiento seleccionado para construir fuerza, constancia y resultados.').trim()
const socialImage = (env.VITE_SEO_IMAGE || '/gymshop-linkedin-thumbnail.png').trim()
const publicPaths = ['/', '/catalogo', '/terminos', '/privacidad', '/envios-cambios-y-devoluciones', '/contacto']
const extraPaths = (env.VITE_SEO_EXTRA_PATHS || '').split(',').map(value => value.trim()).filter(Boolean)
const indexable = /^https:\/\//i.test(siteUrl) && !siteUrl.includes('<')
const escapeMarkup = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')

await mkdir(output, { recursive: true })

const robots = indexable
  ? `User-agent: *\nAllow: /\nDisallow: /admin\nDisallow: /checkout\nDisallow: /ordenes\nDisallow: /carrito\nDisallow: /login\nSitemap: ${siteUrl}/sitemap.xml\n`
  : 'User-agent: *\nDisallow: /\n'
await writeFile(resolve(output, 'robots.txt'), robots, 'utf8')

const indexPath = resolve(output, 'index.html')
let index = await readFile(indexPath, 'utf8')
const imageUrl = /^https?:\/\//i.test(socialImage) ? socialImage : `${siteUrl}${socialImage.startsWith('/') ? socialImage : `/${socialImage}`}`
index = index
  .replace(/<title>.*?<\/title>/, `<title>${escapeMarkup(storeName)}</title>`)
  .replace(/<meta name="description" content="[^"]*"\s*\/?>/, `<meta name="description" content="${escapeMarkup(description)}" />`)
  .replace(/<meta name="robots" content="[^"]*"\s*\/?>/, `<meta name="robots" content="${indexable ? 'index, follow, max-image-preview:large' : 'noindex, nofollow'}" />`)
  .replace(/<meta property="og:title" content="[^"]*"\s*\/?>/, `<meta property="og:title" content="${escapeMarkup(storeName)}" />`)
  .replace(/<meta property="og:description" content="[^"]*"\s*\/?>/, `<meta property="og:description" content="${escapeMarkup(description)}" />`)
  .replace(/<meta property="og:image" content="[^"]*"\s*\/?>/, `<meta property="og:image" content="${escapeMarkup(imageUrl)}" />`)
await writeFile(indexPath, index, 'utf8')

if (indexable) {
  const urls = [...new Set([...publicPaths, ...extraPaths])]
    .map(path => `${siteUrl}${path.startsWith('/') ? path : `/${path}`}`)
  const sitemap = `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls.map(url => `  <url><loc>${escapeMarkup(url)}</loc></url>`).join('\n')}\n</urlset>\n`
  await writeFile(resolve(output, 'sitemap.xml'), sitemap, 'utf8')
}

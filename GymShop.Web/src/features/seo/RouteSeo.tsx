import { useMemo } from 'react'
import { useLocation } from 'react-router-dom'
import type { SeoMetadata } from './Seo'
import { Seo } from './Seo'

const publicPages: Record<string, SeoMetadata> = {
  '/': { description: 'Equipamiento para entrenar, ganar fuerza y alcanzar tus objetivos.', path: '/' },
  '/catalogo': { title: 'Catálogo', description: 'Explorá equipamiento de entrenamiento, fuerza, movilidad y cardio.', path: '/catalogo' },
  '/terminos': { title: 'Términos y condiciones', description: 'Términos y condiciones de compra.', path: '/terminos' },
  '/privacidad': { title: 'Privacidad', description: 'Política de privacidad y tratamiento de datos personales.', path: '/privacidad' },
  '/envios-cambios-y-devoluciones': { title: 'Envíos, cambios y devoluciones', description: 'Información sobre entregas, cambios y devoluciones.', path: '/envios-cambios-y-devoluciones' },
  '/contacto': { title: 'Contacto', description: 'Contactate con nuestro equipo.', path: '/contacto' },
}

export function RouteSeo() {
  const { pathname } = useLocation()
  const productRoute = /^\/catalogo\/\d+$/.test(pathname)
  const metadata = useMemo<SeoMetadata>(() => {
    if (publicPages[pathname]) return publicPages[pathname]
    if (pathname === '/arrepentimiento') return { title: 'Botón de arrepentimiento', path: pathname, noIndex: true }
    if (pathname.startsWith('/admin')) return { title: 'Administración', path: pathname, noIndex: true }
    if (pathname === '/carrito') return { title: 'Carrito', path: pathname, noIndex: true }
    if (pathname === '/login') return { title: 'Ingresar', path: pathname, noIndex: true }
    if (pathname.startsWith('/checkout')) return { title: 'Checkout', path: pathname, noIndex: true }
    if (pathname === '/ordenes') return { title: 'Mis órdenes', path: pathname, noIndex: true }
    return { title: 'Página no encontrada', path: pathname, noIndex: true }
  }, [pathname])

  if (productRoute) return null
  return <Seo metadata={metadata} />
}

const storeName = import.meta.env.VITE_STORE_NAME?.trim() || 'GymShop'

export const storefront = {
  identity: { name: storeName, logoUrl: null as string | null, monogram: storeName.charAt(0).toUpperCase() || 'G' },
  theme: {
    colors: { accent: '#d7ff45', background: '#101210', panel: '#1a1d1a', text: '#eef0ed', muted: '#9da39b' },
    fonts: { body: 'Inter, sans-serif', display: "'Barlow Condensed', sans-serif" },
  },
  market: { locale: 'es-AR', currency: 'ARS', region: 'AR' },
  contact: { email: 'hola@gymshop.demo', phone: '+54 11 5555 0101', whatsapp: '+54 9 11 5555 0101' },
  social: {
    instagramUrl: import.meta.env.VITE_INSTAGRAM_URL?.trim() || '',
    facebookUrl: import.meta.env.VITE_FACEBOOK_URL?.trim() || '',
    email: import.meta.env.VITE_SOCIAL_EMAIL?.trim() || '',
  },
  legal: {
    businessName: import.meta.env.VITE_LEGAL_BUSINESS_NAME?.trim() || 'Comercio de demostración',
    cuit: import.meta.env.VITE_LEGAL_CUIT?.trim() || '',
    address: import.meta.env.VITE_LEGAL_ADDRESS?.trim() || '',
    email: import.meta.env.VITE_LEGAL_EMAIL?.trim() || 'hola@gymshop.demo',
    lastUpdated: '5 de octubre de 2026',
  },
  copy: {
    catalogNav: 'Catálogo', heroEyebrow: 'EQUIPÁ TU MEJOR VERSIÓN', heroTitle: 'Entrená sin límites.',
    heroDescription: 'Equipamiento seleccionado para construir fuerza, constancia y resultados.', heroAction: 'Ver catálogo', heroSecondaryAction: 'Explorar productos',
    heroImageAlt: 'Atleta entrenando con mancuernas', campaignImageAlt: 'Atleta entrenando con una kettlebell',
    featuredEyebrow: 'SELECCIÓN GYMSHOP', featuredTitle: 'Productos destacados', catalogEyebrow: 'CATÁLOGO ACTIVO',
    catalogTitle: 'Elegí tu próximo desafío',
    featuredAction: 'Ver todos', featuredLoading: 'Cargando productos destacados…',
    featuredLoadError: 'No pudimos cargar los destacados.', featuredEmpty: 'Todavía no hay productos destacados.',
    productAction: 'Ver producto', featuredProductEyebrow: 'PRODUCTO DESTACADO',
    campaignAriaLabel: 'Producto en acción', campaignEyebrow: 'FUERZA QUE SE SIENTE',
    campaignTitleLine1: 'Entrená fuerte.', campaignTitleLine2: 'Donde quieras.', campaignPrefix: 'Descubrí la',
    benefits: [
      { icon: 'AR', title: 'Envíos a todo el país', description: 'Recibí tu equipamiento estés donde estés.' },
      { icon: '✓', title: 'Compra protegida', description: 'Tu cuenta y tus órdenes siempre bajo control.' },
      { icon: '$', title: 'Opciones de pago', description: 'Elegí la alternativa disponible que mejor te resulte.' },
    ],
    registrationTitle: 'Empezá a entrenar',
    orderPaymentAction: 'Crear / consultar pago',
    cartAuthenticatedExplanation: 'En el siguiente paso confirmarás la entrega y elegirás el medio de pago.',
      cartGuestExplanation: 'Podés finalizar la compra sin crear una cuenta. Te pediremos tus datos de contacto en el siguiente paso.',
    localCodeLabel: 'Código Mock local',
    footer: 'GymShop · Compra segura',
    categoriesEyebrow: 'ENTRENÁ A TU MANERA', categoriesTitle: 'Explorá por categoría',
    categoriesDescription: 'Encontrá el equipo indicado para cada objetivo.', categoryAction: 'Ver productos',
    searchPlaceholder: '¿Qué necesitás para entrenar?', filtersTitle: 'Filtrar productos',
    filtersAction: 'Filtros', clearFilters: 'Limpiar filtros', relatedTitle: 'También te puede interesar',
    relatedEyebrow: 'SEGUÍ ENTRENANDO', resultsLabel: 'resultados', addToCart: 'Agregar al carrito',
  },
  assets: { hero: '/images/home/hero-training.webp', campaign: '/images/home/lifestyle-kettlebell.webp' },
  categoryVisuals: {
    fuerza: { symbol: '01', color: '#d7ff45' },
    'entrenamiento-funcional': { symbol: '02', color: '#ff8a5b' },
    'yoga-movilidad': { symbol: '03', color: '#9f8cff' },
    cardio: { symbol: '04', color: '#57d7ff' },
  } as Record<string, { symbol: string; color: string }>,
} as const

export const legalIdentityConfigured = Boolean(
  storefront.legal.businessName && storefront.legal.cuit && storefront.legal.address && storefront.legal.email
)

export const money = (value: number, currency: string = storefront.market.currency) =>
  new Intl.NumberFormat(storefront.market.locale, { style: 'currency', currency }).format(value)

export function applyStorefrontTheme() {
  const root = document.documentElement
  root.style.setProperty('--accent', storefront.theme.colors.accent)
  root.style.setProperty('--background', storefront.theme.colors.background)
  root.style.setProperty('--panel', storefront.theme.colors.panel)
  root.style.setProperty('--text', storefront.theme.colors.text)
  root.style.setProperty('--muted', storefront.theme.colors.muted)
  root.style.setProperty('--font-body', storefront.theme.fonts.body)
  root.style.setProperty('--font-display', storefront.theme.fonts.display)
  document.title = storefront.identity.name
}

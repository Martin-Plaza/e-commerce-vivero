const storeName = import.meta.env.VITE_STORE_NAME?.trim() || 'Raíz Viva'

export const storefront = {
  identity: { name: storeName, logoUrl: null as string | null, monogram: 'RV' },
  theme: {
    colors: { accent: '#c66a3d', background: '#f4efe5', panel: '#fffbf4', text: '#18372a', muted: '#6c796f' },
    fonts: { body: "'Manrope', sans-serif", display: "'Cormorant Garamond', serif" },
  },
  market: { locale: 'es-AR', currency: 'ARS', region: 'AR' },
  contact: { email: 'hola@raizviva.demo', phone: '+54 11 5555 0101', whatsapp: '+54 9 11 5555 0101' },
  social: {
    instagramUrl: import.meta.env.VITE_INSTAGRAM_URL?.trim() || '',
    facebookUrl: import.meta.env.VITE_FACEBOOK_URL?.trim() || '',
    email: import.meta.env.VITE_SOCIAL_EMAIL?.trim() || '',
  },
  legal: {
    businessName: import.meta.env.VITE_LEGAL_BUSINESS_NAME?.trim() || 'Comercio de demostración',
    cuit: import.meta.env.VITE_LEGAL_CUIT?.trim() || '',
    address: import.meta.env.VITE_LEGAL_ADDRESS?.trim() || '',
    email: import.meta.env.VITE_LEGAL_EMAIL?.trim() || 'hola@raizviva.demo',
    lastUpdated: '5 de octubre de 2026',
  },
  copy: {
    catalogNav: 'Tienda', heroEyebrow: 'VIVÍ MÁS CERCA DE LO NATURAL', heroTitle: 'Hacé crecer tu lugar.',
    heroDescription: 'Plantas nobles, macetas y cuidados elegidos para llenar de vida cada rincón.', heroAction: 'Explorar plantas', heroSecondaryAction: 'Descubrir el vivero',
    heroImageAlt: 'Vivero luminoso con plantas de interior y macetas de terracota', campaignImageAlt: 'Manos trasplantando una planta en un vivero',
    featuredEyebrow: 'ELEGIDOS DE LA TEMPORADA', featuredTitle: 'Verde para llevar', catalogEyebrow: 'NUESTRO VIVERO',
    catalogTitle: 'Encontrá tu próxima planta',
    featuredAction: 'Ver todos', featuredLoading: 'Cargando productos destacados…',
    featuredLoadError: 'No pudimos cargar los destacados.', featuredEmpty: 'Todavía no hay productos destacados.',
    productAction: 'Ver producto', featuredProductEyebrow: 'PRODUCTO DESTACADO',
    campaignAriaLabel: 'Inspiración para cuidar tus plantas', campaignEyebrow: 'CUIDAR TAMBIÉN ES CRECER',
    campaignTitleLine1: 'Un ritual verde.', campaignTitleLine2: 'Todos los días.', campaignPrefix: 'Conocé',
    benefits: [
      { icon: '♧', title: 'Plantas seleccionadas', description: 'Cada ejemplar se prepara con cuidado antes de salir.' },
      { icon: 'AR', title: 'Envíos responsables', description: 'Embalaje seguro y opciones de entrega para cada zona.' },
      { icon: '✓', title: 'Te acompañamos', description: 'Consejos simples para que tus plantas se adapten mejor.' },
    ],
    registrationTitle: 'Sumate a Raíz Viva',
    orderPaymentAction: 'Crear / consultar pago',
    cartAuthenticatedExplanation: 'En el siguiente paso confirmarás la entrega y elegirás el medio de pago.',
      cartGuestExplanation: 'Podés finalizar la compra sin crear una cuenta. Te pediremos tus datos de contacto en el siguiente paso.',
    localCodeLabel: 'Código Mock local',
    footer: 'Raíz Viva · Vivero urbano',
    categoriesEyebrow: 'CADA ESPACIO TIENE SU VERDE', categoriesTitle: 'Explorá el vivero',
    categoriesDescription: 'Elegí por ambiente, especie o forma de cuidado.', categoryAction: 'Descubrir',
    searchPlaceholder: 'Buscá plantas, macetas o sustratos', filtersTitle: 'Filtrar productos',
    filtersAction: 'Filtros', clearFilters: 'Limpiar filtros', relatedTitle: 'También te puede interesar',
    relatedEyebrow: 'SEGUÍ CULTIVANDO', resultsLabel: 'resultados', addToCart: 'Agregar al carrito',
  },
  assets: { hero: '/images/nursery/hero-raiz-viva.png', campaign: '/images/nursery/potting-editorial.png' },
  categoryVisuals: {
    'plantas-interior': { symbol: '01', color: '#315c45' },
    'plantas-exterior': { symbol: '02', color: '#6f8f55' },
    'aromaticas-huerta': { symbol: '03', color: '#a66b3f' },
    'cactus-suculentas': { symbol: '04', color: '#9aaa72' },
    'macetas-accesorios': { symbol: '05', color: '#c57955' },
    'sustratos-cuidado': { symbol: '06', color: '#c89a4b' },
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

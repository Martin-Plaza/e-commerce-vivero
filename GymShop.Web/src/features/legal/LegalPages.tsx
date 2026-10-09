import { Link } from 'react-router-dom'
import { legalIdentityConfigured, storefront } from '../../config/storefront'

function LegalPage({ eyebrow, title, children }: { eyebrow: string; title: string; children: React.ReactNode }) {
  return <article className="legal-page">
    <header className="legal-heading"><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p>Última actualización: {storefront.legal.lastUpdated}</p></header>
    {!legalIdentityConfigured && <div className="demo-warning" role="note"><strong>Entorno de demostración.</strong> Los datos identificatorios del vendedor deben configurarse antes de operar comercialmente.</div>}
    {children}
    <section><h2>Identificación y contacto</h2><MerchantIdentity /></section>
  </article>
}

function MerchantIdentity() {
  const legal = storefront.legal
  return <dl className="merchant-identity">
    <div><dt>Razón social</dt><dd>{legal.businessName}</dd></div>
    <div><dt>CUIT</dt><dd>{legal.cuit || 'Pendiente de configuración'}</dd></div>
    <div><dt>Domicilio</dt><dd>{legal.address || 'Pendiente de configuración'}</dd></div>
    <div><dt>Email</dt><dd><a href={`mailto:${legal.email}`}>{legal.email}</a></dd></div>
  </dl>
}

export function TermsPage() {
  return <LegalPage eyebrow="INFORMACIÓN LEGAL" title="Términos y condiciones">
    <section><h2>Alcance</h2><p>Estos términos regulan el uso de la tienda y las compras realizadas en ella. Al confirmar un pedido, la persona compradora acepta las condiciones informadas durante el checkout.</p></section>
    <section><h2>Productos, precios y disponibilidad</h2><p>Las características, el precio final, el stock y las promociones aplicables se muestran antes de confirmar la compra. Agregar un producto al carrito no reserva stock. La orden se crea con los productos, cantidades, entrega y precio confirmados en el checkout.</p></section>
    <section><h2>Pagos</h2><p>La orden queda pendiente hasta que el medio elegido confirme el pago. Si una operación es rechazada o no se completa, no se considera abonada. Los comprobantes disponibles pueden consultarse desde “Órdenes”.</p></section>
    <section><h2>Entrega y cancelaciones</h2><p>Los costos y plazos estimados se informan antes de finalizar la compra. Las cancelaciones, devoluciones y reintegros se procesan conforme al estado del pedido, al medio de pago y a la normativa aplicable.</p><p>Consultá la <Link to="/envios-cambios-y-devoluciones">política de envíos, cambios y devoluciones</Link> y el <Link to="/arrepentimiento">derecho de arrepentimiento</Link>.</p></section>
    <section><h2>Responsabilidad de la cuenta</h2><p>La persona usuaria debe proporcionar datos correctos y proteger sus credenciales. Ante un uso no autorizado, debe contactar al comercio cuanto antes.</p></section>
  </LegalPage>
}

export function PrivacyPage() {
  return <LegalPage eyebrow="TUS DATOS" title="Privacidad y datos personales">
    <section><h2>Qué datos tratamos</h2><p>Tratamos los datos necesarios para registrar la cuenta, gestionar carrito y pedidos, procesar pagos, coordinar entregas, emitir comprobantes, prevenir fraude y responder solicitudes. Esto puede incluir identidad, contacto, domicilio, historial de compras y referencias del pago. La tienda no almacena los datos completos de la tarjeta.</p></section>
    <section><h2>Finalidad y destinatarios</h2><p>Los datos se usan para prestar el servicio y cumplir obligaciones legales. Solo se comparten en la medida necesaria con proveedores de pago, logística, facturación, infraestructura y correo transaccional.</p></section>
    <section><h2>Conservación y seguridad</h2><p>Se conservan durante el plazo necesario para operar, atender reclamos y cumplir obligaciones legales y fiscales. Se aplican controles de acceso, trazabilidad y medidas técnicas razonables; ningún sistema puede garantizar riesgo cero.</p></section>
    <section><h2>Derechos</h2><p>Podés solicitar acceso, actualización, rectificación o supresión de tus datos escribiendo a <a href={`mailto:${storefront.legal.email}`}>{storefront.legal.email}</a>. La solicitud puede requerir validación de identidad para proteger la cuenta.</p><p>La <a href="https://www.argentina.gob.ar/aaip" target="_blank" rel="noreferrer">Agencia de Acceso a la Información Pública</a> es el órgano de control de la Ley 25.326 y recibe denuncias y reclamos vinculados con datos personales.</p></section>
  </LegalPage>
}

export function ShippingReturnsPage() {
  return <LegalPage eyebrow="DESPUÉS DE COMPRAR" title="Envíos, cambios y devoluciones">
    <section><h2>Cotización y alcance</h2><p>Las opciones disponibles dependen del código postal, el peso y las dimensiones embaladas del pedido. El checkout muestra el proveedor, modalidad, costo y plazo estimado vigentes al cotizar.</p></section>
    <section><h2>Preparación y seguimiento</h2><p>El plazo de entrega comienza una vez confirmado el pago y preparado el pedido. Cuando corresponda, el número de seguimiento y el estado se muestran en “Órdenes”. Los plazos del operador logístico son estimados.</p></section>
    <section><h2>Recepción</h2><p>Revisá el paquete al recibirlo. Si presenta faltantes, daños o un producto distinto al comprado, conservá el embalaje y contactanos indicando el número de pedido y adjuntando evidencia.</p></section>
    <section><h2>Cambios y devoluciones</h2><p>Para solicitar un cambio o devolución, escribí a <a href={`mailto:${storefront.legal.email}`}>{storefront.legal.email}</a> con el número de pedido. Evaluaremos la solicitud y comunicaremos las instrucciones, costos y plazos que correspondan según la causa y la normativa aplicable.</p><p>Esta política no limita el derecho legal de arrepentimiento ni las garantías por productos defectuosos.</p></section>
  </LegalPage>
}

export function WithdrawalPage() {
  const subject = encodeURIComponent('Solicitud de arrepentimiento de compra')
  const body = encodeURIComponent('Número de pedido:\nNombre y apellido:\nEmail usado en la compra:\nFecha de recepción (si corresponde):\nDetalle opcional:\n')
  return <LegalPage eyebrow="BOTÓN DE ARREPENTIMIENTO" title="Arrepentimiento de compra">
    <section className="withdrawal-lead"><h2>Podés revocar tu compra online</h2><p>Solicitalo dentro de los diez días corridos desde la entrega del producto o la celebración del contrato, según corresponda. No necesitás iniciar sesión ni explicar el motivo.</p><a className="primary link-button withdrawal-action" href={`mailto:${storefront.legal.email}?subject=${subject}&body=${body}`}>Iniciar solicitud por email</a><p className="legal-help">Incluí el número de pedido y el email usado en la compra. El comercio debe confirmar la recepción y comunicar un código identificatorio dentro de las 24 horas.</p></section>
    <section><h2>Devolución</h2><p>Conservá el producto y sus accesorios. El comercio indicará cómo coordinar la devolución; el costo de restitución no corresponde a la persona consumidora cuando ejerce este derecho en plazo.</p></section>
    <section><h2>Si no podés usar el botón</h2><p>Escribí directamente a <a href={`mailto:${storefront.legal.email}`}>{storefront.legal.email}</a> con el asunto “Arrepentimiento” o comunicate por los canales informados por el comercio.</p></section>
  </LegalPage>
}

export function ContactPage() {
  return <LegalPage eyebrow="ESTAMOS PARA AYUDARTE" title="Contacto">
    <section><h2>Consultas sobre compras</h2><p>Para ayudarnos a responder, indicá el número de pedido y el email con el que realizaste la compra. No envíes contraseñas, códigos de acceso ni datos completos de tarjetas.</p><p><a className="primary link-button" href={`mailto:${storefront.legal.email}`}>Escribir al comercio</a></p></section>
    <section><h2>Reclamos de consumo</h2><p>Primero contactá al comercio para buscar una solución. También podés <a href="https://www.argentina.gob.ar/servicio/iniciar-un-reclamo-ante-defensa-del-consumidor" target="_blank" rel="noreferrer">iniciar un reclamo gratuito ante Defensa del Consumidor</a> mediante los canales oficiales.</p></section>
  </LegalPage>
}

import { useEffect, useState } from 'react'
import { api } from '../../api/gymshop'
import type { ArcaConnectionStatus, BillingDocument, BillingProfile } from '../../api/types'
import { describeAdminError } from './adminErrors'
import { AdminFeedback, AdminLoading } from './adminUi'

export function BillingAdmin() {
  const [profile, setProfile] = useState<BillingProfile | null>(null)
  const [status, setStatus] = useState<ArcaConnectionStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [checking, setChecking] = useState(false)
  const [issuing, setIssuing] = useState(false)
  const [issuingCreditNote, setIssuingCreditNote] = useState(false)
  const [orderId, setOrderId] = useState('')
  const [creditNoteOrderId, setCreditNoteOrderId] = useState('')
  const [invoice, setInvoice] = useState<BillingDocument | null>(null)
  const [creditNote, setCreditNote] = useState<BillingDocument | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api.billingProfile().then(setProfile).catch(value => setError(describeAdminError(value))).finally(() => setLoading(false))
  }, [])

  const checkConnection = async () => {
    if (checking) return
    setChecking(true); setError('')
    try { setStatus(await api.arcaStatus()) }
    catch (value) { setError(describeAdminError(value)) }
    finally { setChecking(false) }
  }

  const issueTestCreditNote = async () => {
    const parsedOrderId = Number(creditNoteOrderId)
    if (issuingCreditNote || !Number.isSafeInteger(parsedOrderId) || parsedOrderId <= 0) {
      setError('Ingresá un número de pedido válido.')
      return
    }
    if (!window.confirm(`¿Solicitar a ARCA una Nota de Crédito C de homologación para anular la factura del pedido #${parsedOrderId}? El pedido debe estar totalmente reembolsado.`)) return
    setIssuingCreditNote(true); setError(''); setCreditNote(null)
    try { setCreditNote(await api.createArcaHomologationCreditNote(parsedOrderId)) }
    catch (value) { setError(describeAdminError(value)) }
    finally { setIssuingCreditNote(false) }
  }

  const issueTestInvoice = async () => {
    const parsedOrderId = Number(orderId)
    if (issuing || !Number.isSafeInteger(parsedOrderId) || parsedOrderId <= 0) {
      setError('Ingresá un número de pedido válido.')
      return
    }
    if (!window.confirm(`¿Solicitar a ARCA una Factura C de homologación para el pedido #${parsedOrderId}? No tendrá validez fiscal productiva.`)) return
    setIssuing(true); setError(''); setInvoice(null)
    try { setInvoice(await api.createArcaHomologationInvoice(parsedOrderId)) }
    catch (value) { setError(describeAdminError(value)) }
    finally { setIssuing(false) }
  }

  return <section className="admin-page billing-admin">
    <div className="admin-page-heading"><div><p className="eyebrow">CONFIGURACIÓN PROTEGIDA</p><h1>Facturación</h1><p>Estado del perfil fiscal y de la conexión de homologación con ARCA.</p></div></div>
    <AdminFeedback error={error} />
    {loading && <AdminLoading label="Cargando configuración fiscal…" />}
    {profile && <div className="dashboard-grid">
      <section className="dashboard-panel"><h2>Perfil fiscal</h2><dl className="billing-status-list">
        <div><dt>Modo</dt><dd>{profile.mode}</dd></div><div><dt>Condición</dt><dd>{profile.taxCondition}</dd></div><div><dt>Razón social</dt><dd>{profile.businessName || 'Sin configurar'}</dd></div><div><dt>CUIT</dt><dd>{profile.cuit || 'Sin configurar'}</dd></div><div><dt>Punto de venta</dt><dd>{profile.pointOfSale ?? 'Sin configurar'}</dd></div>
      </dl></section>
      <section className="dashboard-panel"><h2>ARCA</h2><p><strong>{profile.arcaEnabled ? 'Homologación habilitada' : 'Conector deshabilitado'}</strong></p><p>{profile.electronicInvoicingReady ? 'El perfil fiscal está completo.' : 'El perfil fiscal todavía no está listo para facturación electrónica.'}</p><button className="primary" type="button" disabled={checking} onClick={() => void checkConnection()}>{checking ? 'Verificando…' : 'Probar conexión de homologación'}</button></section>
    </div>}
    {status && <section className="dashboard-panel arca-diagnostic" aria-live="polite"><h2>Último diagnóstico</h2><dl className="billing-status-list">
      <div><dt>Ambiente</dt><dd>{status.environment}</dd></div><div><dt>Configuración</dt><dd>{status.configurationReady ? 'Completa' : 'Incompleta'}</dd></div><div><dt>WSFE</dt><dd>{status.wsfeReachable ? 'Disponible' : 'No disponible'}</dd></div><div><dt>WSAA</dt><dd>{status.wsaaAuthenticated ? 'Autenticado' : 'Sin autenticar'}</dd></div><div><dt>Puntos de venta</dt><dd>{status.pointsOfSale.length ? status.pointsOfSale.join(', ') : 'Ninguno informado'}</dd></div>
    </dl><p className={status.errorCode ? 'error' : 'notice'}>{status.message}</p>{status.errorCode && <small>Código técnico: {status.errorCode}</small>}</section>}
    {status?.wsaaAuthenticated && <section className="dashboard-panel"><h2>Factura C de prueba</h2><p>Solicita manualmente un CAE en homologación para un pedido pagado. Este comprobante no pertenece al ambiente productivo.</p><form className="admin-filters" onSubmit={event => { event.preventDefault(); void issueTestInvoice() }}><label>Número de pedido<input type="number" min="1" step="1" value={orderId} onChange={event => setOrderId(event.target.value)} /></label><button className="primary" type="submit" disabled={issuing}>{issuing ? 'Solicitando CAE…' : 'Emitir factura de prueba'}</button></form>{invoice && <div className={invoice.status === 'Authorized' ? 'notice' : 'error'} role="status"><strong>{invoice.status === 'Authorized' ? 'Factura autorizada en homologación' : 'Factura rechazada por ARCA'}</strong><p>Pedido #{invoice.orderId} · Punto de venta {invoice.pointOfSale ?? '—'} · Número {invoice.documentNumber ?? '—'}</p>{invoice.cae && <p>CAE: {invoice.cae} · Vencimiento: {invoice.caeExpiresOn}</p>}{invoice.rejectionReason && <p>{invoice.rejectionCode ? `${invoice.rejectionCode}: ` : ''}{invoice.rejectionReason}</p>}</div>}</section>}
    {status?.wsaaAuthenticated && <section className="dashboard-panel"><h2>Nota de Crédito C de prueba</h2><p>Anula fiscalmente una Factura C de homologación después de que el proveedor haya confirmado el reembolso total del pedido.</p><form className="admin-filters" onSubmit={event => { event.preventDefault(); void issueTestCreditNote() }}><label>Número de pedido reembolsado<input type="number" min="1" step="1" value={creditNoteOrderId} onChange={event => setCreditNoteOrderId(event.target.value)} /></label><button className="primary" type="submit" disabled={issuingCreditNote}>{issuingCreditNote ? 'Solicitando CAE…' : 'Emitir nota de crédito de prueba'}</button></form>{creditNote && <div className={creditNote.status === 'Authorized' ? 'notice' : 'error'} role="status"><strong>{creditNote.status === 'Authorized' ? 'Nota de crédito autorizada en homologación' : 'Nota de crédito rechazada por ARCA'}</strong><p>Pedido #{creditNote.orderId} · Punto de venta {creditNote.pointOfSale ?? '—'} · Número {creditNote.documentNumber ?? '—'}</p>{creditNote.cae && <p>CAE: {creditNote.cae} · Vencimiento: {creditNote.caeExpiresOn}</p>}{creditNote.rejectionReason && <p>{creditNote.rejectionCode ? `${creditNote.rejectionCode}: ` : ''}{creditNote.rejectionReason}</p>}</div>}</section>}
    <p className="admin-footnote">Esta pantalla no permite cargar secretos ni cambiar proveedores. La configuración se administra únicamente mediante Railway.</p>
  </section>
}

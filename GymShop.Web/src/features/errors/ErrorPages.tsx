import { Component, type ErrorInfo, type ReactNode } from 'react'
import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return <section className="page-state" aria-labelledby="not-found-title"><span aria-hidden="true">404</span><p className="eyebrow">PÁGINA NO ENCONTRADA</p><h1 id="not-found-title">Esta hoja no estaba en el mapa</h1><p>La dirección puede estar incompleta o la página pudo haber cambiado.</p><div className="page-state-actions"><Link className="primary link-button" to="/">Volver al inicio</Link><Link className="link-button" to="/catalogo">Ver catálogo</Link></div></section>
}

export function UnexpectedErrorPage({ onRetry }: { onRetry(): void }) {
  return <section className="page-state" role="alert"><span aria-hidden="true">!</span><p className="eyebrow">ERROR INESPERADO</p><h1>No pudimos mostrar esta pantalla</h1><p>Tu información no se perdió. Intentá nuevamente o volvé al inicio.</p><div className="page-state-actions"><button className="primary" onClick={onRetry}>Reintentar</button><a className="link-button" href="/">Volver al inicio</a></div></section>
}

export class AppErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch(error: Error, info: ErrorInfo) { console.error('Unhandled storefront error', error, info.componentStack) }
  render() { return this.state.failed ? <UnexpectedErrorPage onRetry={() => this.setState({ failed: false })} /> : this.props.children }
}

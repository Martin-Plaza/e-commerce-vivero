export function AdminEmpty({ children }: { children: React.ReactNode }) {
  return <div className="admin-empty">{children}</div>
}

export function AdminFeedback({ error, success }: { error?: string; success?: string }) {
  return <>{error && <div className="error" role="alert">{error}</div>}{success && <div className="notice" role="status">{success}</div>}</>
}

export function AdminLoading({ label = 'Cargando…' }: { label?: string }) {
  return <div className="admin-loading" role="status"><span aria-hidden="true" />{label}</div>
}

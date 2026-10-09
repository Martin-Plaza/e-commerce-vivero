import { Navigate, useLocation } from 'react-router-dom'
import type { User } from '../../api/types'
import { AdminEmpty } from './adminUi'
import { isAdmin, isSuperAdmin } from './adminConfig'

export function AdminRoute({ user, superAdmin = false, children }: { user: User | null; superAdmin?: boolean; children: React.ReactNode }) {
  const location = useLocation()
  if (!user) return <Navigate to="/login" replace state={{ returnTo: `${location.pathname}${location.search}`, message: 'Iniciá sesión para continuar.' }} />
  const allowed = superAdmin ? isSuperAdmin(user) : isAdmin(user)
  return allowed ? <>{children}</> : <AdminEmpty>No tenés permisos para acceder a esta sección.</AdminEmpty>
}


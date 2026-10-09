import type { User } from '../../api/types'

export const LOW_STOCK_THRESHOLD = 5

export const isAdmin = (user: User | null) => user?.role === 'Admin' || user?.role === 'SuperAdmin'
export const isSuperAdmin = (user: User | null) => user?.role === 'SuperAdmin'


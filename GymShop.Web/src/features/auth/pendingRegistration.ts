const STORAGE_KEY = 'gymshop.pending-registration'

export interface PendingRegistration {
  email: string
  expiresAt: number
}

export function loadPendingRegistration(): PendingRegistration | null {
  try {
    const value = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null') as Partial<PendingRegistration> | null
    if (!value || typeof value.email !== 'string' || typeof value.expiresAt !== 'number') return null
    return { email: value.email, expiresAt: value.expiresAt }
  } catch {
    return null
  }
}

export function savePendingRegistration(email: string, expiresInSeconds: number): PendingRegistration {
  const value = { email, expiresAt: Date.now() + expiresInSeconds * 1000 }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(value))
  return value
}

export function clearPendingRegistration() {
  localStorage.removeItem(STORAGE_KEY)
}

export function remainingSeconds(pending: PendingRegistration) {
  return Math.max(0, Math.ceil((pending.expiresAt - Date.now()) / 1000))
}

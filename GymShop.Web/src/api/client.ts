import { session } from '../auth/session'
import type { ApiErrorShape, AuthResponse } from './types'

// Production requests stay on the storefront origin and are proxied to the API
// by Vercel. This keeps session cookies first-party on mobile browsers.
export const API_URL = import.meta.env.DEV
  ? (import.meta.env.VITE_API_URL || 'http://localhost:5093').replace(/\/$/, '')
  : ''

export class ApiError extends Error implements ApiErrorShape {
  status: number
  code?: string
  traceId?: string
  retryAfter?: number
  validationErrors?: Record<string, string[]>
  constructor(data: ApiErrorShape) {
    super(data.message)
    this.name = 'ApiError'
    this.status = data.status
    this.code = data.code
    this.traceId = data.traceId
    this.retryAfter = data.retryAfter
    this.validationErrors = data.validationErrors
  }
}

const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])
let csrfToken: string | undefined

function cookie(name: string) {
  const prefix = `${encodeURIComponent(name)}=`
  const value = document.cookie.split('; ').find(item => item.startsWith(prefix))?.slice(prefix.length)
  return value ? decodeURIComponent(value) : undefined
}

async function ensureCsrfToken() {
  const cookieToken = cookie('XSRF-TOKEN')
  if (cookieToken) return (csrfToken = cookieToken)
  if (csrfToken) return csrfToken
  if (new URL(API_URL, window.location.href).hostname === window.location.hostname) return undefined

  const response = await fetch(`${API_URL}/api/auth/csrf`, { credentials: 'include', headers: { Accept: 'application/json' } })
  if (!response.ok) return undefined
  const body = await response.json() as { token?: string }
  csrfToken = response.headers.get('X-CSRF-TOKEN') || body.token
  return csrfToken
}

function captureCsrfToken(response: Response) {
  csrfToken = response.headers.get('X-CSRF-TOKEN') || cookie('XSRF-TOKEN') || csrfToken
}

async function prepareHeaders(init: RequestInit, accept: string) {
  const headers = new Headers(init.headers)
  headers.set('Accept', accept)
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const method = (init.method || 'GET').toUpperCase()
  const csrf = unsafeMethods.has(method) ? await ensureCsrfToken() : undefined
  if (unsafeMethods.has(method) && csrf) headers.set('X-CSRF-TOKEN', csrf)
  return headers
}

async function refreshBrowserSession() {
  const csrf = await ensureCsrfToken()
  if (!csrf) return false
  const response = await fetch(`${API_URL}/api/auth/refresh`, {
    method: 'POST', credentials: 'include', headers: { 'X-CSRF-TOKEN': csrf, Accept: 'application/json' },
  })
  captureCsrfToken(response)
  if (response.ok) session.save((await response.json() as AuthResponse).user)
  return response.ok
}

async function fetchWithSession(path: string, init: RequestInit, retry = true) {
  const response = await fetch(`${API_URL}${path}`, { ...init, credentials: 'include' })
  captureCsrfToken(response)
  if (response.status !== 401 || !retry || !session.user() || path.startsWith('/api/auth/')) return response
  if (!await refreshBrowserSession()) return response
  const retried = await fetch(`${API_URL}${path}`, { ...init, credentials: 'include' })
  captureCsrfToken(retried)
  return retried
}

function errorMessage(status: number) {
  if (status === 0) return 'No pudimos conectarnos con el servicio. Revisá tu conexión e intentá nuevamente.'
  if (status === 401) return 'Tu sesión no es válida. Iniciá sesión nuevamente.'
  if (status === 403) return 'No tenés permisos para realizar esta acción.'
  if (status === 409) return 'La operación entra en conflicto con el estado actual.'
  if (status === 429) return 'Demasiadas solicitudes. Intentá nuevamente más tarde.'
  if (status === 404) return 'El recurso solicitado no existe o ya no está disponible.'
  if (status >= 500) return 'El servicio no está disponible en este momento. Intentá nuevamente más tarde.'
  return 'No se pudo completar la solicitud.'
}

async function normalizeError(response: Response): Promise<ApiError> {
  let body: Record<string, unknown> = {}
  try { body = await response.json() as Record<string, unknown> } catch { /* empty response */ }
  const errors = body.errors && typeof body.errors === 'object' ? body.errors as Record<string, string[]> : undefined
  const validationMessage = errors ? Object.values(errors).flat().join(' ') : undefined
  const message = validationMessage || (typeof body.message === 'string' && body.message) || (typeof body.detail === 'string' && body.detail) || (typeof body.title === 'string' && body.title) || errorMessage(response.status)
  const retry = Number(response.headers.get('Retry-After'))
  return new ApiError({
    status: response.status,
    message,
    code: typeof body.code === 'string' ? body.code : undefined,
    traceId: typeof body.traceId === 'string' ? body.traceId : undefined,
    retryAfter: Number.isFinite(retry) && retry > 0 ? retry : undefined,
    validationErrors: errors,
  })
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = await prepareHeaders(init, 'application/json')
  let response: Response
  try { response = await fetchWithSession(path, { ...init, headers }) }
  catch (error) {
    if (error instanceof TypeError) throw new ApiError({ status: 0, code: 'network_unavailable', message: errorMessage(0) })
    throw error
  }
  if (!response.ok) {
    const error = await normalizeError(response)
    if (response.status === 401) session.clear()
    throw error
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function requestBlob(path: string, init: RequestInit = {}): Promise<Blob> {
  const headers = await prepareHeaders(init, 'application/pdf')
  let response: Response
  try { response = await fetchWithSession(path, { ...init, headers }) }
  catch (error) {
    if (error instanceof TypeError) throw new ApiError({ status: 0, code: 'network_unavailable', message: errorMessage(0) })
    throw error
  }
  if (!response.ok) {
    const error = await normalizeError(response)
    if (response.status === 401) session.clear()
    throw error
  }
  return response.blob()
}

export const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) })

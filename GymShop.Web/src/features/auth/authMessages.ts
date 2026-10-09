import { ApiError } from '../../api/client'

export function authErrorMessage(value: unknown) {
  if (!(value instanceof ApiError)) return 'Ocurrió un error inesperado. Intentá nuevamente.'
  const extras = [
    value.retryAfter ? `Reintentá en ${value.retryAfter} segundos.` : '',
    value.traceId ? `Referencia: ${value.traceId}` : '',
  ].filter(Boolean)
  return [value.message, ...extras].join(' ')
}

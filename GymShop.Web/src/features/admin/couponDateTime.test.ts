import { describe, expect, it } from 'vitest'
import { localDateTimeInputToUtcIso, utcIsoToLocalDateTimeInput } from './couponDateTime'

describe('fechas locales de cupones', () => {
  it('round-trips an API UTC instant through datetime-local without shifting it', () => {
    const iso = '2026-09-22T15:30:00.000Z'
    const local = utcIsoToLocalDateTimeInput(iso)
    expect(localDateTimeInputToUtcIso(local)).toBe(iso)
  })
  it('supports empty and rejects malformed values', () => {
    expect(utcIsoToLocalDateTimeInput(null)).toBe('')
    expect(localDateTimeInputToUtcIso('')).toBeNull()
    expect(localDateTimeInputToUtcIso('invalid')).toBeNull()
  })
})

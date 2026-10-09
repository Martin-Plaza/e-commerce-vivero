import { beforeEach, describe, expect, it, vi } from 'vitest'
import { clearPendingRegistration, loadPendingRegistration, remainingSeconds, savePendingRegistration } from './pendingRegistration'

describe('registro pendiente', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.setSystemTime(new Date('2026-08-10T12:00:00Z'))
  })

  it('guarda solamente email y vencimiento para retomar la verificación', () => {
    const pending = savePendingRegistration('persona@gym.com', 60)
    expect(loadPendingRegistration()).toEqual(pending)
    expect(remainingSeconds(pending)).toBe(60)
    expect(localStorage.getItem('gymshop.pending-registration')).not.toContain('123456')
  })

  it('permite limpiar el registro pendiente', () => {
    savePendingRegistration('persona@gym.com', 60)
    clearPendingRegistration()
    expect(loadPendingRegistration()).toBeNull()
  })
})

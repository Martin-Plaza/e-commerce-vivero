import { describe, expect, it } from 'vitest'
import type { PaymentStatus } from '../../api/types'
import { paymentExplanation, paymentLabels, terminalRetryablePayments } from './checkoutPresentation'

describe('presentación de estados de pago', () => {
  it('cubre todos los estados del contrato sin inventar transiciones', () => {
    const statuses: PaymentStatus[] = ['Creating', 'Pending', 'CreationFailed', 'Approved', 'Rejected', 'Canceled', 'Expired', 'Refunded']
    for (const status of statuses) {
      expect(paymentLabels[status]).toBeTruthy()
      expect(paymentExplanation(status, false)).toBeTruthy()
    }
  })

  it('explica Creating sin asumir que existe CheckoutUrl', () => {
    expect(paymentExplanation('Creating', false)).toContain('todavía no tiene un enlace')
  })

  it('solo habilita un intento nuevo para estados terminales recuperables', () => {
    expect(terminalRetryablePayments).toEqual(['CreationFailed', 'Rejected', 'Canceled', 'Expired'])
  })
})

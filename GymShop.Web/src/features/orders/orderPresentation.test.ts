import { describe, expect, it } from 'vitest'
import { orderStatusLabel } from './orderPresentation'

describe('estados visibles según modalidad', () => {
  it('presenta retiro sin lenguaje de transporte', () => {
    expect(orderStatusLabel('Shipped', 'StorePickup')).toBe('Listo para retirar')
    expect(orderStatusLabel('Delivered', 'StorePickup')).toBe('Retirado')
  })

  it('mantiene los textos de envío a domicilio', () => {
    expect(orderStatusLabel('Shipped', 'HomeDelivery')).toBe('Enviado')
    expect(orderStatusLabel('Delivered', 'HomeDelivery')).toBe('Entregado')
    expect(orderStatusLabel('Shipped', 'HomeDelivery', true)).toBe('Enviada')
    expect(orderStatusLabel('Delivered', 'HomeDelivery', true)).toBe('Entregada')
  })
})

import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BillingAdmin } from './BillingAdmin'

const profile = {
  mode: 'ElectronicInvoice', taxCondition: 'Monotributo', businessName: 'GymShop', cuit: '20-12345678-9',
  fiscalAddress: 'Rosario', grossIncomeNumber: 'Exento', activityStartDate: '2026-01-01', pointOfSale: 4,
  arcaEnabled: true, electronicInvoicingReady: true,
}
const status = {
  environment: 'Homologation', configurationReady: true, wsaaAuthenticated: true, wsfeReachable: true,
  pointsOfSale: [4, 12], errorCode: null, message: 'Conexion de homologacion validada correctamente.', checkedAtUtc: '2026-10-03T15:00:00Z',
}
const response = (body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
const invoice = {
  id: '82af6a55-813c-40f3-8d48-081adb563c36', orderId: 23, paymentId: 8, category: 'Invoice', type: 'InvoiceC', status: 'Authorized',
  currency: 'ARS', issuerBusinessName: 'GymShop Homologacion', recipientName: 'Cliente Prueba', recipientEmail: 'cliente@example.com',
  recipientAddress: 'Rosario', subtotal: 30000, discountAmount: 0, shippingAmount: 5000, total: 35000,
  pointOfSale: 1, documentNumber: 1, authorizationProvider: 'ARCA-Homologation', cae: '74123456789012', caeExpiresOn: '2026-10-13',
  rejectionCode: null, rejectionReason: null, createdAtUtc: '2026-10-03T15:00:00Z', authorizedAtUtc: '2026-10-03T15:00:01Z', items: [],
}
const creditNote = {
  ...invoice,
  id: '92bf6a55-813c-40f3-8d48-081adb563c37', relatedDocumentId: invoice.id,
  category: 'CreditNote', type: 'CreditNoteC', documentNumber: 2, cae: '74123456789015',
}

describe('BillingAdmin', () => {
  afterEach(() => vi.restoreAllMocks())

  it('muestra el perfil y ejecuta el diagnostico solo cuando se solicita', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(input =>
      response(String(input).endsWith('/arca/status') ? status : profile))

    render(<BillingAdmin />)

    expect(await screen.findByText('GymShop')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(1)
    await userEvent.click(screen.getByRole('button', { name: 'Probar conexión de homologación' }))
    expect(await screen.findByText('Autenticado')).toBeInTheDocument()
    expect(screen.getByText('4, 12')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
  })

  it('emite manualmente una factura de homologacion solo despues de confirmar', async () => {
    const confirmMock = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.endsWith('/arca/status')) return response(status)
      if (url.includes('/arca/homologation/orders/23/invoice')) {
        expect(init?.method).toBe('POST')
        expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('arca-homologation-order-23')
        return response(invoice)
      }
      return response(profile)
    })

    render(<BillingAdmin />)
    await screen.findByText('GymShop')
    await userEvent.click(screen.getByRole('button', { name: 'Probar conexión de homologación' }))
    await screen.findByText('Autenticado')
    await userEvent.type(screen.getByLabelText('Número de pedido'), '23')
    await userEvent.click(screen.getByRole('button', { name: 'Emitir factura de prueba' }))

    expect(confirmMock).toHaveBeenCalledWith(expect.stringContaining('pedido #23'))
    expect(await screen.findByText('Factura autorizada en homologación')).toBeInTheDocument()
    expect(screen.getByText(/CAE: 74123456789012/)).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
  })

  it('emite una nota de credito de homologacion para un pedido reembolsado', async () => {
    const confirmMock = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input)
      if (url.endsWith('/arca/status')) return response(status)
      if (url.includes('/arca/homologation/orders/23/credit-note')) {
        expect(init?.method).toBe('POST')
        expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('arca-homologation-credit-note-order-23')
        return response(creditNote)
      }
      return response(profile)
    })

    render(<BillingAdmin />)
    await screen.findByText('GymShop')
    await userEvent.click(screen.getByRole('button', { name: 'Probar conexión de homologación' }))
    await screen.findByText('Autenticado')
    await userEvent.type(screen.getByLabelText('Número de pedido reembolsado'), '23')
    await userEvent.click(screen.getByRole('button', { name: 'Emitir nota de crédito de prueba' }))

    expect(confirmMock).toHaveBeenCalledWith(expect.stringContaining('pedido #23'))
    expect(await screen.findByText('Nota de crédito autorizada en homologación')).toBeInTheDocument()
    expect(screen.getByText(/CAE: 74123456789015/)).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
  })
})

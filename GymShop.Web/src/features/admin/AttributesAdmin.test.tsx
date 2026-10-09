import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../api/gymshop'
import { AttributesAdmin } from './AttributesAdmin'

describe('ABM de atributos', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('crea un atributo reutilizable y agrega una opción visual', async () => {
    const color = { id: 1, name: 'Color', presentation: 'ColorSwatch' as const, displayOrder: 0, isActive: true, options: [] }
    vi.spyOn(api, 'adminAttributes').mockResolvedValueOnce([]).mockResolvedValueOnce([color]).mockResolvedValueOnce([{ ...color, options: [{ id: 4, value: 'Azul', visualValue: '#0000ff', displayOrder: 0, isActive: true, usageCount: 0 }] }])
    const create = vi.spyOn(api, 'createAttribute').mockResolvedValue(color); const add = vi.spyOn(api, 'addAttributeOption').mockResolvedValue({})
    render(<AttributesAdmin />); await waitFor(() => expect(api.adminAttributes).toHaveBeenCalled())
    await userEvent.type(screen.getByLabelText('Nombre'), 'Color'); await userEvent.selectOptions(screen.getByLabelText('Presentación'), 'ColorSwatch'); await userEvent.click(screen.getByRole('button', { name: 'Crear atributo' }))
    await screen.findByRole('heading', { name: 'Color' }); await userEvent.type(screen.getByLabelText('Nueva opción de Color'), 'Azul'); await userEvent.click(screen.getByRole('button', { name: 'Agregar opción' }))
    await waitFor(() => expect(add).toHaveBeenCalledWith(1, expect.objectContaining({ value: 'Azul' }))); expect(create).toHaveBeenCalledWith(expect.objectContaining({ name: 'Color', presentation: 'ColorSwatch' }))
  })
})

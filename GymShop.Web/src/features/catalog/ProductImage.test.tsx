import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProductImage } from './ProductImage'

describe('ProductImage', () => {
  it('acepta rutas locales y muestra un fallback si la imagen falla', () => {
    render(<ProductImage src="/images/products/inexistente.webp" alt="Producto de prueba" />)
    fireEvent.error(screen.getByRole('img', { name: 'Producto de prueba' }))
    expect(screen.getByLabelText('Imagen no disponible para Producto de prueba')).toHaveTextContent('GS')
  })
})

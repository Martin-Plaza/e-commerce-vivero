import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProductImage } from './ProductImage'

describe('ProductImage', () => {
  it('reintenta la imagen sin caché antes de mostrar el reemplazo', () => {
    render(<ProductImage src="/images/products/lavanda.webp" alt="Lavanda" />)

    const firstAttempt = screen.getByRole('img', { name: 'Lavanda' })
    fireEvent.error(firstAttempt)

    const retry = screen.getByRole('img', { name: 'Lavanda' })
    expect(retry).toHaveAttribute('src', '/images/products/lavanda.webp?image-retry=1')
    expect(retry).toHaveAttribute('loading', 'eager')

    fireEvent.error(retry)
    expect(screen.getByLabelText('Imagen no disponible para Lavanda')).toHaveTextContent('GS')
  })
})

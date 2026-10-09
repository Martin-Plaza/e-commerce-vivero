import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'

if (!URL.createObjectURL) URL.createObjectURL = () => 'blob:test-preview'
if (!URL.revokeObjectURL) URL.revokeObjectURL = () => undefined

afterEach(() => {
  cleanup()
  localStorage.clear()
  sessionStorage.clear()
})

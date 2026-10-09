import type { CreateProductInput, Product, ProductVariant, UpdateProductInput } from '../../api/types'

export const PRODUCT_LIMITS = { name: 150, description: 1000, imageUrl: 500, maxPrice: Number('9999999999999999.99') } as const

export interface ProductFormValues {
  name: string
  description: string
  price: string
  stock: string
  categoryId: string
  imageUrl: string
  packageWeightGrams: string
  packageLengthCm: string
  packageWidthCm: string
  packageHeightCm: string
  isActive: boolean
  variants: ProductVariant[]
  colorImages: Record<string, string>
}

export type ProductField = keyof Omit<ProductFormValues, 'isActive' | 'variants' | 'colorImages'>
export type ProductFormErrors = Partial<Record<ProductField, string>>

export const emptyProductValues = (): ProductFormValues => ({ name: '', description: '', price: '', stock: '', categoryId: '', imageUrl: '', packageWeightGrams: '', packageLengthCm: '', packageWidthCm: '', packageHeightCm: '', isActive: true, variants: [], colorImages: {} })

export const productToFormValues = (product: Product): ProductFormValues => ({
  name: product.name,
  description: product.description || '',
  price: String(product.price),
  stock: String(product.stock),
  categoryId: product.category ? String(product.category.id) : '',
  imageUrl: product.imageUrl || '',
  packageWeightGrams: product.packageWeightGrams == null ? '' : String(product.packageWeightGrams),
  packageLengthCm: product.packageLengthCm == null ? '' : String(product.packageLengthCm),
  packageWidthCm: product.packageWidthCm == null ? '' : String(product.packageWidthCm),
  packageHeightCm: product.packageHeightCm == null ? '' : String(product.packageHeightCm),
  isActive: product.isActive,
  variants: product.variants ?? [],
  colorImages: product.colorImages ?? {},
})

export function isValidProductImageUrl(value: string) {
  const url = value.trim()
  if (!url) return true
  if (url.startsWith('/') && !url.startsWith('//') && !url.includes('..')) return true
  try { const parsed = new URL(url); return parsed.protocol === 'http:' || parsed.protocol === 'https:' } catch { return false }
}

export function validateProduct(values: ProductFormValues): ProductFormErrors {
  const errors: ProductFormErrors = {}
  const name = values.name.trim()
  const description = values.description.trim()
  const imageUrl = values.imageUrl.trim()
  const price = Number(values.price)
  const stock = Number(values.stock)
  const packageWeightGrams = Number(values.packageWeightGrams)
  const packageDimensions = [values.packageLengthCm, values.packageWidthCm, values.packageHeightCm].map(Number)
  if (!name) errors.name = 'El nombre es obligatorio.'
  else if (name.length > PRODUCT_LIMITS.name) errors.name = `El nombre no puede superar los ${PRODUCT_LIMITS.name} caracteres.`
  if (description.length > PRODUCT_LIMITS.description) errors.description = `La descripción no puede superar los ${PRODUCT_LIMITS.description} caracteres.`
  if (!values.price.trim() || !Number.isFinite(price) || price <= 0) errors.price = 'El precio debe ser mayor a cero.'
  else if (price > PRODUCT_LIMITS.maxPrice || !/^\d+(\.\d{1,2})?$/.test(values.price.trim())) errors.price = 'El precio debe tener hasta 16 dígitos enteros y 2 decimales.'
  if (!values.stock.trim() || !Number.isInteger(stock) || stock < 0) errors.stock = 'El stock debe ser un número entero mayor o igual a cero.'
  if (!values.categoryId) errors.categoryId = 'Seleccioná una categoría.'
  if (!values.packageWeightGrams.trim() || !Number.isInteger(packageWeightGrams) || packageWeightGrams <= 0 || packageWeightGrams > 1_000_000) errors.packageWeightGrams = 'Ingresá el peso empaquetado en gramos.'
  ;(['packageLengthCm', 'packageWidthCm', 'packageHeightCm'] as const).forEach((field, index) => {
    const value = packageDimensions[index]
    if (!values[field].trim() || !Number.isFinite(value) || value <= 0 || value > 1000 || !/^\d+(\.\d{1,2})?$/.test(values[field].trim())) errors[field] = 'Ingresá una medida válida en cm (hasta 2 decimales).'
  })
  if (imageUrl.length > PRODUCT_LIMITS.imageUrl) errors.imageUrl = `La URL no puede superar los ${PRODUCT_LIMITS.imageUrl} caracteres.`
  else if (!isValidProductImageUrl(imageUrl)) errors.imageUrl = 'Ingresá una URL http/https o una ruta local que comience con “/”.'
  return errors
}

export function toProductInput(values: ProductFormValues): CreateProductInput {
  return {
    name: values.name.trim(), description: values.description.trim() || null,
    price: Number(values.price), stock: Number(values.stock),
    imageUrl: values.imageUrl.trim() || null, categoryId: Number(values.categoryId), variants: values.variants, colorImages: values.colorImages,
    packageWeightGrams: Number(values.packageWeightGrams), packageLengthCm: Number(values.packageLengthCm), packageWidthCm: Number(values.packageWidthCm), packageHeightCm: Number(values.packageHeightCm),
  }
}

export function toUpdateProductInput(values: ProductFormValues): UpdateProductInput {
  return {
    name: values.name.trim(), description: values.description.trim() || null,
    price: Number(values.price), imageUrl: values.imageUrl.trim() || null,
    categoryId: Number(values.categoryId), isActive: values.isActive, variants: values.variants, colorImages: values.colorImages,
    packageWeightGrams: Number(values.packageWeightGrams), packageLengthCm: Number(values.packageLengthCm), packageWidthCm: Number(values.packageWidthCm), packageHeightCm: Number(values.packageHeightCm),
  }
}

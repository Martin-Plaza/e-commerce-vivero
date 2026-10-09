import type { AdminCategory, CategoryInput } from '../../api/types'

export const CATEGORY_COLORS = [
  { name: 'Lima', value: '#d7ff45' }, { name: 'Coral', value: '#ff8a5b' }, { name: 'Violeta', value: '#9f8cff' },
  { name: 'Celeste', value: '#57d7ff' }, { name: 'Rosa', value: '#ff7eb6' }, { name: 'Verde', value: '#77d8a3' },
] as const
export type CategoryField = 'name' | 'slug' | 'description' | 'displayOrder' | 'color'
export type CategoryFormErrors = Partial<Record<CategoryField, string>>
export interface CategoryFormValues { name: string; slug: string; description: string; displayOrder: string; color: string }
export const CATEGORY_LIMITS = { name: 100, slug: 120, description: 500 }
export const slugify = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')
export const emptyCategoryValues = (): CategoryFormValues => ({ name: '', slug: '', description: '', displayOrder: '0', color: '' })
export const categoryToFormValues = (category: AdminCategory): CategoryFormValues => ({ name: category.name, slug: category.slug, description: category.description || '', displayOrder: String(category.displayOrder), color: category.color || '' })
export function validateCategory(values: CategoryFormValues): CategoryFormErrors {
  const errors: CategoryFormErrors = {}; const name = values.name.trim(); const slug = slugify(values.slug); const order = Number(values.displayOrder)
  if (!name) errors.name = 'Ingresá un nombre.'; else if (name.length > CATEGORY_LIMITS.name) errors.name = `Usá hasta ${CATEGORY_LIMITS.name} caracteres.`
  if (!slug) errors.slug = 'Ingresá un slug válido.'; else if (slug.length > CATEGORY_LIMITS.slug) errors.slug = `Usá hasta ${CATEGORY_LIMITS.slug} caracteres.`
  if (values.description.trim().length > CATEGORY_LIMITS.description) errors.description = `Usá hasta ${CATEGORY_LIMITS.description} caracteres.`
  if (!Number.isInteger(order) || order < 0) errors.displayOrder = 'Ingresá un entero mayor o igual a cero.'
  if (values.color && !CATEGORY_COLORS.some(color => color.value === values.color)) errors.color = 'Seleccioná un color disponible.'
  return errors
}
export const toCategoryInput = (values: CategoryFormValues): CategoryInput => ({ name: values.name.trim(), slug: slugify(values.slug), description: values.description.trim() || null, displayOrder: Number(values.displayOrder), color: values.color || null })

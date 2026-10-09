import { useState } from 'react'
import { CATEGORY_COLORS, CATEGORY_LIMITS, slugify, type CategoryField, type CategoryFormErrors, type CategoryFormValues, validateCategory } from './categoryFormValidation'

export function CategoryForm({ mode, values: initialValues, busy, serverErrors = {}, onSubmit }: { mode: 'create' | 'edit'; values: CategoryFormValues; busy: boolean; serverErrors?: CategoryFormErrors; onSubmit(values: CategoryFormValues): void }) {
  const [values, setValues] = useState(initialValues); const [errors, setErrors] = useState<CategoryFormErrors>({}); const [slugTouched, setSlugTouched] = useState(mode === 'edit'); const allErrors = { ...serverErrors, ...errors }
  const change = (field: CategoryField, value: string) => { setValues(current => ({ ...current, [field]: value, ...(field === 'name' && !slugTouched ? { slug: slugify(value) } : {}) })); setErrors(current => ({ ...current, [field]: undefined })) }
  const error = (field: CategoryField) => allErrors[field] ? <small className="field-error" id={`${field}-error`}>{allErrors[field]}</small> : null
  return <form className="category-form" noValidate onSubmit={event => { event.preventDefault(); if (busy) return; const next = validateCategory(values); setErrors(next); if (!Object.keys(next).length) onSubmit(values) }}>
    <label>Nombre<input aria-label="Nombre" value={values.name} maxLength={CATEGORY_LIMITS.name} aria-invalid={Boolean(allErrors.name)} onChange={event => change('name', event.target.value)} />{error('name')}</label>
    <label>Slug<input aria-label="Slug" value={values.slug} maxLength={CATEGORY_LIMITS.slug} aria-invalid={Boolean(allErrors.slug)} onChange={event => { setSlugTouched(true); change('slug', slugify(event.target.value)) }} />{error('slug')}<small className="field-hint">Cambiarlo modifica la URL pública; no se crearán redirecciones.</small></label>
    <label>Descripción<textarea aria-label="Descripción" value={values.description} maxLength={CATEGORY_LIMITS.description} aria-invalid={Boolean(allErrors.description)} onChange={event => change('description', event.target.value)} />{error('description')}</label>
    <label>Orden de presentación<input aria-label="Orden de presentación" type="number" min="0" step="1" value={values.displayOrder} aria-invalid={Boolean(allErrors.displayOrder)} onChange={event => change('displayOrder', event.target.value)} />{error('displayOrder')}</label>
    <label>Color de categoría<select aria-label="Color de categoría" value={values.color} aria-invalid={Boolean(allErrors.color)} onChange={event => change('color', event.target.value)}><option value="">Color de la tienda</option>{CATEGORY_COLORS.map(color => <option key={color.value} value={color.value}>{color.name}</option>)}</select>{error('color')}</label>
    <div className="product-form-actions"><button className="primary" type="submit" disabled={busy}>{busy ? 'Guardando…' : mode === 'create' ? 'Crear categoría' : 'Guardar cambios'}</button></div>
  </form>
}

import { useEffect, useState } from 'react'
import type { Category, ProductAttributeDefinition } from '../../api/types'
import { ProductImage } from '../catalog/ProductImage'
import { PRODUCT_LIMITS, type ProductField, type ProductFormErrors, type ProductFormValues, validateProduct } from './productFormValidation'

export function ProductForm({ mode, values: initialValues, categories, attributes, busy, busyLabel, serverErrors = {}, onSubmit }: {
  mode: 'create' | 'edit'
  values: ProductFormValues
  categories: Category[]
  attributes: ProductAttributeDefinition[]
  busy: boolean
  busyLabel?: string
  serverErrors?: ProductFormErrors
  onSubmit(values: ProductFormValues, imageFile: File | null, colorImageFiles: Record<string, File>): void
}) {
  const [values, setValues] = useState({ ...initialValues, variants: initialValues.variants ?? [] })
  const optionAttribute = new Map(attributes.flatMap(attribute => attribute.options.map(option => [option.id, attribute.id] as const)))
  const [selectedAttributeIds, setSelectedAttributeIds] = useState<number[]>(() => [...new Set((initialValues.variants ?? []).flatMap(v => v.optionIds ?? []).map(id => optionAttribute.get(id)).filter((id): id is number => Boolean(id)))])
  const [clientErrors, setClientErrors] = useState<ProductFormErrors>({})
  const [imageFile, setImageFile] = useState<File | null>(null)
  const [colorImageFiles, setColorImageFiles] = useState<Record<string, File>>({})
  const [fileError, setFileError] = useState('')
  const [variantError, setVariantError] = useState('')
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!imageFile) { setPreviewUrl(null); return }
    const url = URL.createObjectURL(imageFile); setPreviewUrl(url)
    return () => URL.revokeObjectURL(url)
  }, [imageFile])
  const errors = { ...serverErrors, ...clientErrors }
  const change = (field: ProductField, value: string) => { setValues(current => ({ ...current, [field]: value })); setClientErrors(current => ({ ...current, [field]: undefined })) }
  const updateVariant = (index: number, patch: Partial<(typeof values.variants)[number]>) => setValues(current => ({ ...current, variants: current.variants.map((item, i) => i === index ? { ...item, ...patch } : item) }))
  const updateVariantOption = (index: number, definition: ProductAttributeDefinition, optionId: number) => setValues(current => ({ ...current, variants: current.variants.map((item, i) => i !== index ? item : { ...item, optionIds: [...(item.optionIds ?? []).filter(id => !definition.options.some(option => option.id === id)), ...(optionId ? [optionId] : [])] }) }))
  const fieldError = (field: ProductField) => errors[field] ? <small className="field-error" id={`${field}-error`}>{errors[field]}</small> : null
  return <form className="product-form" noValidate onSubmit={event => {
    event.preventDefault()
    if (busy) return
    const nextErrors = validateProduct(values)
    const invalidVariant = values.variants.some(variant => {
      const dimensions = [variant.packageLengthCm, variant.packageWidthCm, variant.packageHeightCm]
      return !variant.sku.trim() || variant.stock < 0 || !variant.optionIds?.length || variant.optionIds.length !== selectedAttributeIds.length
        || (variant.packageWeightGrams != null && (!Number.isInteger(variant.packageWeightGrams) || variant.packageWeightGrams <= 0 || variant.packageWeightGrams > 1_000_000))
        || dimensions.some(value => value != null && (!Number.isFinite(value) || value <= 0 || value > 1000))
    })
    if (!imageFile && !values.imageUrl.trim()) nextErrors.imageUrl = 'Agregá una imagen o su URL.'
    setClientErrors(nextErrors)
    if (invalidVariant) { setVariantError('Cada variante necesita SKU, stock y medidas opcionales válidas, además de una opción de cada atributo elegido.'); return }
    setVariantError('')
    if (Object.keys(nextErrors).length === 0 && !fileError) {
      const colorOptionIds = new Set(values.variants.flatMap(variant => variant.optionIds ?? []).filter(id => attributes.some(a => a.presentation === 'ColorSwatch' && a.options.some(o => o.id === id))))
      const colorImages = Object.fromEntries(Object.entries(values.colorImages).filter(([id]) => colorOptionIds.has(Number(id))))
      onSubmit({ ...values, colorImages }, imageFile, colorImageFiles)
    }
  }}>
    <div className="product-form-fields">
      <label>Nombre<input aria-label="Nombre" name="name" value={values.name} maxLength={PRODUCT_LIMITS.name} aria-invalid={Boolean(errors.name)} aria-describedby={errors.name ? 'name-error' : undefined} onChange={event => change('name', event.target.value)} />{fieldError('name')}</label>
      <label>Descripción<textarea aria-label="Descripción" name="description" value={values.description} maxLength={PRODUCT_LIMITS.description} aria-invalid={Boolean(errors.description)} aria-describedby={errors.description ? 'description-error' : undefined} onChange={event => change('description', event.target.value)} />{fieldError('description')}</label>
      <div className="product-form-row">
        <label>Precio<input aria-label="Precio" name="price" type="number" min="0.01" step="0.01" value={values.price} aria-invalid={Boolean(errors.price)} aria-describedby={errors.price ? 'price-error' : undefined} onChange={event => change('price', event.target.value)} />{fieldError('price')}</label>
        <label>Stock<input aria-label="Stock" name="stock" type="number" min="0" step="1" value={values.variants.length ? values.variants.reduce((sum, variant) => sum + variant.stock, 0) : values.stock} disabled={mode === 'edit' || values.variants.length > 0} aria-invalid={Boolean(errors.stock)} aria-describedby={errors.stock ? 'stock-error' : 'stock-managed-help'} onChange={event => change('stock', event.target.value)} />{fieldError('stock')}{(mode === 'edit' || values.variants.length > 0) && <small id="stock-managed-help">{values.variants.length ? 'Se calcula sumando el stock de las variantes.' : 'Los cambios de stock se realizan desde la sección Stock para conservar su historial.'}</small>}</label>
      </div>
      <label>Categoría<select aria-label="Categoría" name="categoryId" value={values.categoryId} aria-invalid={Boolean(errors.categoryId)} aria-describedby={errors.categoryId ? 'categoryId-error' : undefined} onChange={event => change('categoryId', event.target.value)}><option value="">Seleccionar categoría</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}{'isActive' in category && category.isActive === false ? ' (inactiva, categoría actual)' : ''}</option>)}</select>{fieldError('categoryId')}</label>
      <fieldset><legend>Paquete para envío</legend><p>Ingresá el peso y las medidas del producto ya embalado. Se usan para cotizar con la empresa de logística.</p><div className="product-form-row"><label>Peso (g)<input aria-label="Peso empaquetado" name="packageWeightGrams" type="number" min="1" max="1000000" step="1" value={values.packageWeightGrams} aria-invalid={Boolean(errors.packageWeightGrams)} onChange={event => change('packageWeightGrams', event.target.value)} />{fieldError('packageWeightGrams')}</label><label>Largo (cm)<input aria-label="Largo empaquetado" name="packageLengthCm" type="number" min="0.01" max="1000" step="0.01" value={values.packageLengthCm} aria-invalid={Boolean(errors.packageLengthCm)} onChange={event => change('packageLengthCm', event.target.value)} />{fieldError('packageLengthCm')}</label><label>Ancho (cm)<input aria-label="Ancho empaquetado" name="packageWidthCm" type="number" min="0.01" max="1000" step="0.01" value={values.packageWidthCm} aria-invalid={Boolean(errors.packageWidthCm)} onChange={event => change('packageWidthCm', event.target.value)} />{fieldError('packageWidthCm')}</label><label>Alto (cm)<input aria-label="Alto empaquetado" name="packageHeightCm" type="number" min="0.01" max="1000" step="0.01" value={values.packageHeightCm} aria-invalid={Boolean(errors.packageHeightCm)} onChange={event => change('packageHeightCm', event.target.value)} />{fieldError('packageHeightCm')}</label></div></fieldset>
      <label>URL de imagen<input aria-label="URL de imagen" name="imageUrl" type="url" value={values.imageUrl} maxLength={PRODUCT_LIMITS.imageUrl} placeholder="https://… o /images/…" aria-invalid={Boolean(errors.imageUrl)} aria-describedby={errors.imageUrl ? 'imageUrl-error' : undefined} onChange={event => change('imageUrl', event.target.value)} />{fieldError('imageUrl')}</label>
      <label>Subir imagen<input aria-label="Subir imagen" name="imageFile" type="file" accept="image/jpeg,image/png,image/webp" disabled={busy} aria-invalid={Boolean(fileError)} aria-describedby={fileError ? 'imageFile-error' : 'imageFile-help'} onChange={event => {
        const file = event.target.files?.[0] ?? null
        if (!file) { setImageFile(null); setFileError(''); return }
        if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { setImageFile(null); setFileError('Solo se permiten imágenes JPEG, PNG o WebP.'); return }
        if (file.size > 5 * 1024 * 1024) { setImageFile(null); setFileError('La imagen no puede superar los 5 MB.'); return }
        setImageFile(file); setFileError('')
      }} />{fileError ? <small className="field-error" id="imageFile-error">{fileError}</small> : <small id="imageFile-help">JPEG, PNG o WebP, hasta 5 MB. La imagen subida tendrá prioridad sobre la URL manual.</small>}
        {imageFile && <small role="status">Seleccionada: {imageFile.name} ({(imageFile.size / 1024).toFixed(1)} KB). Podés elegir otra antes de guardar.</small>}
      </label>
      <fieldset><legend>Variantes (opcional)</legend><p>Elegí los atributos del producto y agregá únicamente las combinaciones que vendés. Las medidas vacías heredan las del producto.</p><div className="attribute-picker">{attributes.filter(a => a.isActive || selectedAttributeIds.includes(a.id)).map(a => <label key={a.id}><input type="checkbox" checked={selectedAttributeIds.includes(a.id)} disabled={values.variants.length > 0} onChange={e => setSelectedAttributeIds(current => e.target.checked ? [...current, a.id] : current.filter(id => id !== a.id))} /> {a.name}</label>)}</div>{values.variants.map((variant, index) => <div className="variant-admin-row" key={variant.id ?? index}><input aria-label={`SKU variante ${index + 1}`} placeholder="SKU" value={variant.sku} onChange={event => updateVariant(index, { sku: event.target.value })} /><input aria-label={`Stock variante ${index + 1}`} type="number" min="0" placeholder="Stock" value={variant.stock} onChange={event => updateVariant(index, { stock: Number(event.target.value) })} /><input aria-label={`Precio variante ${index + 1}`} type="number" min="0.01" step="0.01" placeholder="Precio opcional" value={variant.price ?? ''} onChange={event => updateVariant(index, { price: event.target.value ? Number(event.target.value) : null })} /><input aria-label={`Peso empaquetado variante ${index + 1}`} type="number" min="1" step="1" placeholder="Peso heredado" value={variant.packageWeightGrams ?? ''} onChange={event => updateVariant(index, { packageWeightGrams: event.target.value ? Number(event.target.value) : null })} /><input aria-label={`Largo empaquetado variante ${index + 1}`} type="number" min="0.01" step="0.01" placeholder="Largo heredado" value={variant.packageLengthCm ?? ''} onChange={event => updateVariant(index, { packageLengthCm: event.target.value ? Number(event.target.value) : null })} /><input aria-label={`Ancho empaquetado variante ${index + 1}`} type="number" min="0.01" step="0.01" placeholder="Ancho heredado" value={variant.packageWidthCm ?? ''} onChange={event => updateVariant(index, { packageWidthCm: event.target.value ? Number(event.target.value) : null })} /><input aria-label={`Alto empaquetado variante ${index + 1}`} type="number" min="0.01" step="0.01" placeholder="Alto heredado" value={variant.packageHeightCm ?? ''} onChange={event => updateVariant(index, { packageHeightCm: event.target.value ? Number(event.target.value) : null })} />{selectedAttributeIds.map(attributeId => { const definition = attributes.find(a => a.id === attributeId)!; const selected = (variant.optionIds ?? []).find(id => definition.options.some(o => o.id === id)); return <label key={attributeId}>{definition.name}<select aria-label={`${definition.name} variante ${index + 1}`} value={selected ?? ''} onChange={e => updateVariantOption(index, definition, Number(e.target.value))}><option value="">Seleccionar</option>{definition.options.filter(o => o.isActive || o.id === selected).map(o => <option key={o.id} value={o.id}>{o.value}</option>)}</select></label>})}<label><input type="checkbox" checked={variant.isActive} onChange={event => updateVariant(index, { isActive: event.target.checked })} /> Activa</label><button type="button" onClick={() => setValues(current => ({ ...current, variants: current.variants.filter((_, i) => i !== index) }))}>Quitar</button></div>)}<button type="button" disabled={!selectedAttributeIds.length} onClick={() => setValues(current => ({ ...current, stock: '0', variants: [...current.variants, { sku: '', price: null, stock: 0, isActive: true, attributes: {}, optionIds: [], packageWeightGrams: null, packageLengthCm: null, packageWidthCm: null, packageHeightCm: null }] }))}>Agregar combinación</button>{!attributes.length && <small>Primero creá atributos desde Administración → Atributos.</small>}</fieldset>
      <ColorImagesEditor values={values} attributes={attributes} files={colorImageFiles} onFile={(color, file) => setColorImageFiles(current => file ? { ...current, [color]: file } : Object.fromEntries(Object.entries(current).filter(([key]) => key !== color)))} onUrl={(color, url) => setValues(current => ({ ...current, colorImages: { ...current.colorImages, [color]: url } }))} />
      {mode === 'edit' && <label className="product-active"><input type="checkbox" checked={values.isActive} onChange={event => setValues(current => ({ ...current, isActive: event.target.checked }))} /> Producto activo</label>}
    </div>
    <aside className="product-form-preview"><span>Vista previa</span><div><ProductImage src={previewUrl ?? (values.imageUrl.trim() && !errors.imageUrl ? values.imageUrl.trim() : null)} alt={values.name.trim() || 'Nuevo producto'} /></div><small>Si la imagen no puede cargarse, se mostrará el reemplazo visual de la tienda.</small></aside>
    <div className="product-form-actions">{variantError && <small className="field-error" role="alert">{variantError}</small>}<button className="primary" type="submit" disabled={busy || categories.length === 0}>{busy ? busyLabel || 'Guardando…' : mode === 'create' ? 'Crear producto' : 'Guardar cambios'}</button></div>
  </form>
}

function ColorImagesEditor({ values, attributes, files, onFile, onUrl }: { values: ProductFormValues; attributes: ProductAttributeDefinition[]; files: Record<string, File>; onFile(color: string, file: File | null): void; onUrl(color: string, url: string): void }) {
  const optionIds = new Set(values.variants.flatMap(variant => variant.optionIds ?? []))
  const colors = attributes.filter(a => a.presentation === 'ColorSwatch').flatMap(a => a.options).filter(option => optionIds.has(option.id))
  if (!colors.length) return null
  return <fieldset className="color-image-editor"><legend>Imágenes por color (opcional)</legend><p>Una imagen se reutiliza para todas las combinaciones del mismo color. Si no asignás una, se usa la imagen general.</p>{colors.map(color => { const key = String(color.id); return <div className="color-image-row" key={key}><strong>{color.value}</strong><input aria-label={`URL de imagen para ${color.value}`} type="url" value={values.colorImages[key] ?? ''} placeholder="Usar imagen general" onChange={event => onUrl(key, event.target.value)} /><input aria-label={`Subir imagen para ${color.value}`} type="file" accept="image/jpeg,image/png,image/webp" onChange={event => onFile(key, event.target.files?.[0] ?? null)} />{files[key] && <small>{files[key].name}</small>}</div> })}</fieldset>
}

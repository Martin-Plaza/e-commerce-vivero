import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { api } from '../../api/gymshop'
import type { Category, Product, ProductAttributeDefinition } from '../../api/types'
import { describeAdminError } from './adminErrors'
import { AdminEmpty, AdminFeedback, AdminLoading } from './adminUi'
import { ProductForm } from './ProductForm'
import { emptyProductValues, productToFormValues, toProductInput, toUpdateProductInput, type ProductField, type ProductFormErrors, type ProductFormValues } from './productFormValidation'

const fields: Record<string, ProductField> = { name: 'name', description: 'description', price: 'price', stock: 'stock', imageurl: 'imageUrl', categoryid: 'categoryId' }

function normalizeLegacyVariants(product: Product | null, attributes: ProductAttributeDefinition[]): Product | null {
  if (!product) return null
  const optionFor = (name: string, value: string) => attributes.find(a => a.name.toLocaleLowerCase() === name.toLocaleLowerCase())?.options.find(o => o.value.toLocaleLowerCase() === value.toLocaleLowerCase())
  const variants = (product.variants ?? []).map(variant => ({ ...variant, optionIds: variant.optionIds?.length ? variant.optionIds : Object.entries(variant.attributes).map(([name, value]) => optionFor(name, value)?.id).filter((id): id is number => id !== undefined) }))
  const colorImages = { ...(product.colorImages ?? {}) }
  for (const [key, url] of Object.entries(product.colorImages ?? {})) {
    if (/^\d+$/.test(key)) continue
    const option = attributes.filter(a => a.presentation === 'ColorSwatch').flatMap(a => a.options).find(o => o.value.toLocaleLowerCase() === key.toLocaleLowerCase())
    if (option) { colorImages[String(option.id)] = url; delete colorImages[key] }
  }
  return { ...product, variants, colorImages }
}

function splitValidationErrors(error: ApiError): { fieldErrors: ProductFormErrors; generalError: string } {
  const mapped: ProductFormErrors = {}
  const unmapped: string[] = []
  for (const [key, messages] of Object.entries(error.validationErrors || {})) {
    const field = fields[key.toLowerCase()]
    if (field && messages.length) mapped[field] = messages.join(' ')
    else unmapped.push(...messages)
  }
  return { fieldErrors: mapped, generalError: unmapped.join(' ') }
}

export function ProductEditorPage({ mode }: { mode: 'create' | 'edit' }) {
  const { productId } = useParams(); const navigate = useNavigate()
  const [categories, setCategories] = useState<Category[]>([]); const [product, setProduct] = useState<Product | null>(null)
  const [attributes, setAttributes] = useState<ProductAttributeDefinition[]>([])
  const [loading, setLoading] = useState(true); const [loadError, setLoadError] = useState(''); const [notFound, setNotFound] = useState(false); const [retryKey, setRetryKey] = useState(0); const [submitError, setSubmitError] = useState(''); const [fieldErrors, setFieldErrors] = useState<ProductFormErrors>({}); const [busy, setBusy] = useState(false); const [busyLabel, setBusyLabel] = useState('Guardando…')
  const submitting = useRef(false)
  const numericId = Number(productId)
  const hasValidProductId = Number.isInteger(numericId) && numericId > 0

  useEffect(() => {
    let active = true; setLoading(true); setLoadError(''); setNotFound(false)
    if (mode === 'edit' && !hasValidProductId) {
      setNotFound(true); setLoading(false)
      return () => { active = false }
    }
    const requests = mode === 'edit' ? Promise.all([api.categories(), api.product(numericId), api.adminAttributes()]) : Promise.all([api.categories(), Promise.resolve(null), api.adminAttributes()])
    requests.then(async ([categoryList, loadedProduct, attributeList]) => {
      let options: Category[] = categoryList
      if (mode === 'edit' && loadedProduct?.category && !categoryList.some(item => item.id === loadedProduct.category?.id)) {
        const all = await api.adminCategories(); options = all.filter(item => item.isActive || item.id === loadedProduct.category?.id)
      }
      if (active) { setCategories(options); setProduct(normalizeLegacyVariants(loadedProduct, attributeList)); setAttributes(attributeList) }
    }).catch(error => { if (active) { if (mode === 'edit' && error instanceof ApiError && error.status === 404) setNotFound(true); else setLoadError(describeAdminError(error)) } }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [mode, numericId, hasValidProductId, retryKey])

  const save = async (values: ProductFormValues, imageFile: File | null, colorImageFiles: Record<string, File>) => {
    if (submitting.current) return
    if (!imageFile && !values.imageUrl.trim()) { setFieldErrors({ imageUrl: 'Agregá una imagen o su URL.' }); return }
    submitting.current = true; setBusy(true); setSubmitError(''); setFieldErrors({})
    let completed = false; const uploadedKeys: string[] = []
    try {
      let nextValues = values
      if (imageFile) {
        setBusyLabel('Subiendo imagen…')
        const uploaded = await api.uploadProductImage(imageFile, mode === 'edit' ? numericId : undefined)
        uploadedKeys.push(uploaded.key); nextValues = { ...values, imageUrl: uploaded.url }; setBusyLabel('Guardando…')
      }
      if (Object.keys(colorImageFiles).length) {
        setBusyLabel('Subiendo imágenes por color…')
        const colorImages = { ...nextValues.colorImages }
        for (const [color, file] of Object.entries(colorImageFiles)) {
          const uploaded = await api.uploadProductImage(file, mode === 'edit' ? numericId : undefined)
          uploadedKeys.push(uploaded.key); colorImages[color] = uploaded.url
        }
        nextValues = { ...nextValues, colorImages }; setBusyLabel('Guardando…')
      }
      const saved = mode === 'create' ? await api.createProduct(toProductInput(nextValues)) : await api.updateProduct(numericId, toUpdateProductInput(nextValues))
      completed = true
      if (imageFile && product?.imageUrl && product.imageUrl !== saved.imageUrl) {
        try { await api.deleteProductImage({ url: product.imageUrl }) } catch { /* replacement is saved; cleanup can be retried safely */ }
      }
      const savedColorUrls = new Set(Object.values(saved.colorImages ?? {}))
      for (const oldUrl of Object.values(product?.colorImages ?? {})) {
        if (!savedColorUrls.has(oldUrl)) { try { await api.deleteProductImage({ url: oldUrl }) } catch { /* product update already succeeded */ } }
      }
      navigate('/admin/productos', { replace: true, state: { productNotice: `${saved.name} fue ${mode === 'create' ? 'creado' : 'actualizado'} correctamente.` } })
    } catch (error) {
      for (const key of uploadedKeys) { try { await api.deleteProductImage({ key }) } catch { /* preserve original error */ } }
      if (error instanceof ApiError) {
        const validation = splitValidationErrors(error)
        setFieldErrors(validation.fieldErrors)
        setSubmitError(Object.keys(validation.fieldErrors).length ? validation.generalError : describeAdminError(error))
      } else setSubmitError(describeAdminError(error))
    } finally {
      if (!completed) { submitting.current = false; setBusy(false); setBusyLabel('Guardando…') }
    }
  }

  return <section className="admin-page"><div className="admin-page-heading"><div><p className="eyebrow">CATÁLOGO</p><h1>{mode === 'create' ? 'Nuevo producto' : 'Editar producto'}</h1><p>{mode === 'create' ? 'Completá los datos del producto.' : 'Actualizá los datos principales del producto.'}</p></div><Link to="/admin/productos">← Volver al listado</Link></div>
    <AdminFeedback error={loadError || submitError} />
    {loading ? <AdminLoading label={mode === 'create' ? 'Cargando catálogo…' : 'Cargando producto…'} /> : notFound ? <AdminEmpty>Producto no encontrado.</AdminEmpty> : loadError ? <AdminEmpty>No se pudo preparar el formulario. <button type="button" onClick={() => setRetryKey(value => value + 1)}>Reintentar</button></AdminEmpty> : categories.length === 0 ? <AdminEmpty>No hay categorías disponibles. No es posible guardar productos hasta que exista una categoría activa.</AdminEmpty> : mode === 'edit' && !product ? <AdminEmpty>Producto no encontrado.</AdminEmpty> : <ProductForm mode={mode} values={product ? productToFormValues(product) : emptyProductValues()} categories={categories} attributes={attributes} busy={busy} busyLabel={busyLabel} serverErrors={fieldErrors} onSubmit={(values, imageFile, colorImageFiles) => void save(values, imageFile, colorImageFiles)} />}
  </section>
}

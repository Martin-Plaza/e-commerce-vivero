import { useEffect, useState } from 'react'
import { api } from '../../api/gymshop'
import type { ProductAttributeDefinition } from '../../api/types'
import { describeAdminError } from './adminErrors'
import { AdminFeedback, AdminLoading } from './adminUi'

export function AttributesAdmin() {
  const [items, setItems] = useState<ProductAttributeDefinition[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState({ name: '', presentation: 'Button', displayOrder: 0 })
  const [option, setOption] = useState<Record<number, { value: string; visualValue: string }>>({})
  const load = async () => {
    setLoading(true)
    try { setItems(await api.adminAttributes()) }
    catch (value) { setError(describeAdminError(value)) }
    finally { setLoading(false) }
  }
  useEffect(() => { void load() }, [])
  const run = async (action: () => Promise<unknown>) => {
    setError('')
    try { await action(); await load() }
    catch (value) { setError(describeAdminError(value)) }
  }

  return <section className="admin-page">
    <div className="admin-page-heading"><div><p className="eyebrow">CATÁLOGO</p><h1>Atributos</h1><p>Definí atributos reutilizables y sus opciones.</p></div></div>
    <AdminFeedback error={error} />
    <form className="attribute-create" onSubmit={event => {
      event.preventDefault()
      void run(async () => { await api.createAttribute(form); setForm({ name: '', presentation: 'Button', displayOrder: 0 }) })
    }}>
      <label>Nombre<input required value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} /></label>
      <label>Presentación<select value={form.presentation} onChange={event => setForm({ ...form, presentation: event.target.value })}><option value="Button">Botones</option><option value="ColorSwatch">Muestras de color</option></select></label>
      <label>Orden<input type="number" min="0" value={form.displayOrder} onChange={event => setForm({ ...form, displayOrder: Number(event.target.value) })} /></label>
      <button className="primary">Crear atributo</button>
    </form>
    {loading ? <AdminLoading label="Cargando atributos…" /> : <div className="attribute-admin-list">{items.map(attribute =>
      <article key={attribute.id} className="attribute-card">
        <header>
          <div><h2>{attribute.name}</h2><small>{attribute.presentation === 'ColorSwatch' ? 'Muestras de color' : 'Botones'}</small></div>
          <div className="attribute-actions">
            <button className="secondary-action" onClick={() => {
              const name = window.prompt('Nombre del atributo', attribute.name)?.trim()
              if (name) void run(() => api.updateAttribute(attribute.id, { name, presentation: attribute.presentation, displayOrder: attribute.displayOrder }))
            }}>Editar</button>
            <button className={attribute.isActive ? 'danger-action' : 'success-action'} onClick={() => void run(() => api.setAttributeStatus(attribute.id, !attribute.isActive))}>{attribute.isActive ? 'Desactivar' : 'Activar'}</button>
          </div>
        </header>
        <div className="attribute-option-list">{attribute.options.map(item =>
          <div key={item.id}>
            <span className={attribute.presentation === 'ColorSwatch' ? 'attribute-color-preview' : ''} style={attribute.presentation === 'ColorSwatch' ? { background: item.visualValue || 'transparent' } : undefined}>{attribute.presentation === 'ColorSwatch' ? '' : item.value}</span>
            <strong>{item.value}</strong><small>{item.usageCount || 0} usos</small>
            <span className="attribute-actions">
              <button className="secondary-action" onClick={() => {
                const value = window.prompt('Valor de la opción', item.value)?.trim()
                if (!value) return
                const visualValue = attribute.presentation === 'ColorSwatch' ? window.prompt('Color CSS o hexadecimal', item.visualValue || '#000000')?.trim() || item.visualValue : null
                void run(() => api.updateAttributeOption(attribute.id, item.id, { value, visualValue, displayOrder: item.displayOrder }))
              }}>Editar</button>
              <button className={item.isActive ? 'danger-action' : 'success-action'} onClick={() => void run(() => api.setAttributeOptionStatus(attribute.id, item.id, !item.isActive))}>{item.isActive ? 'Desactivar' : 'Activar'}</button>
            </span>
          </div>)}</div>
        <form className="attribute-option-form" onSubmit={event => {
          event.preventDefault()
          const value = option[attribute.id] ?? { value: '', visualValue: '' }
          void run(async () => {
            await api.addAttributeOption(attribute.id, { value: value.value, visualValue: attribute.presentation === 'ColorSwatch' ? value.visualValue : null, displayOrder: attribute.options.length })
            setOption(current => ({ ...current, [attribute.id]: { value: '', visualValue: '' } }))
          })
        }}>
          <input aria-label={`Nueva opción de ${attribute.name}`} required placeholder="Nueva opción" value={option[attribute.id]?.value ?? ''} onChange={event => setOption(current => ({ ...current, [attribute.id]: { value: event.target.value, visualValue: current[attribute.id]?.visualValue ?? '' } }))} />
          {attribute.presentation === 'ColorSwatch' && <input aria-label={`Color visual de ${attribute.name}`} type="color" value={option[attribute.id]?.visualValue || '#000000'} onChange={event => setOption(current => ({ ...current, [attribute.id]: { value: current[attribute.id]?.value ?? '', visualValue: event.target.value } }))} />}
          <button className="secondary-action">Agregar opción</button>
        </form>
      </article>)}</div>}
  </section>
}

interface QuantitySelectorProps {
  value: number
  max: number
  onChange(value: number): void
  label?: string
}

export function QuantitySelector({ value, max, onChange, label = 'Cantidad' }: QuantitySelectorProps) {
  const update = (next: number) => onChange(Math.min(max, Math.max(1, Number.isFinite(next) ? next : 1)))
  return <div className="quantity-picker">
    <span>{label}</span>
    <div className="quantity-control">
      <button type="button" aria-label="Restar una unidad" disabled={value <= 1} onClick={() => update(value - 1)}>−</button>
      <input aria-label={label} type="number" min="1" max={max} value={value} onChange={event => update(Number(event.target.value))} />
      <button type="button" aria-label="Sumar una unidad" disabled={value >= max} onClick={() => update(value + 1)}>+</button>
    </div>
  </div>
}

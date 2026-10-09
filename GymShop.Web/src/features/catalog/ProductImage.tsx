import { useEffect, useState } from 'react'

export function ProductImage({ src, alt }: { src: string | null; alt: string }) {
  const [failed, setFailed] = useState(false)
  useEffect(() => setFailed(false), [src])

  if (!src || failed) return <span className="image-fallback" aria-label={`Imagen no disponible para ${alt}`}>GS</span>
  return <img src={src} alt={alt} loading="lazy" onError={() => setFailed(true)} />
}

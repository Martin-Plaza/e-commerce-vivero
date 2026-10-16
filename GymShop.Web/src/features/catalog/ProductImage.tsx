import { useEffect, useState } from 'react'

function retryUrl(src: string) {
  const separator = src.includes('?') ? '&' : '?'
  return `${src}${separator}image-retry=1`
}

export function ProductImage({ src, alt }: { src: string | null; alt: string }) {
  const [failed, setFailed] = useState(false)
  const [retrying, setRetrying] = useState(false)
  useEffect(() => {
    setFailed(false)
    setRetrying(false)
  }, [src])

  if (!src || failed) return <span className="image-fallback" aria-label={`Imagen no disponible para ${alt}`}>GS</span>
  return <img
    src={retrying ? retryUrl(src) : src}
    alt={alt}
    loading={retrying ? 'eager' : 'lazy'}
    onError={() => retrying ? setFailed(true) : setRetrying(true)}
  />
}

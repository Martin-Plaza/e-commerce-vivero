import { useEffect } from 'react'
import { applySeo, type SeoMetadata } from './seoDom'

export type { SeoMetadata } from './seoDom'

export function Seo({ metadata }: { metadata: SeoMetadata }) {
  useEffect(() => { applySeo(metadata) }, [metadata])
  return null
}

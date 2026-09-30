import { useState } from 'react'

interface PosterProps {
  src: string | null
  title: string
  label?: string
  className?: string
}

export function Poster({ src, title, label = 'FILM', className = '' }: PosterProps) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)

  return <span className={`poster ${className}`}>
    {src && src !== failedSrc ? (
      <img alt={`Poster for ${title}`} loading="lazy" onError={() => setFailedSrc(src)} src={src} />
    ) : (
      <span aria-hidden="true" className="poster-fallback"><span>{label}</span></span>
    )}
  </span>
}
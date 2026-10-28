import { useEffect, useState, type ReactNode } from 'react'
import { getMovieAwards } from '../api/client'
import type { MovieAwardRecognition } from '../api/types'
import { formatWords } from '../shared/format'
import { MovieAwardsList, MovieDetailsIntro } from './MoviePresentation'

export interface MovieMetadataDetails {
  imdbId: string | null
  title: string
  year: number | null
  mediaType: string | null
  imdbRating: number | null
  posterUrl: string | null
  imdbVotes?: number | null
  metascore?: number | null
  genres?: string[]
  countries?: string[]
  director?: string | null
  plot?: string | null
  runtime?: string | null
  awards?: string | null
  boxOffice?: string | null
}

interface AwardsResponse {
  imdbId: string
  awards: MovieAwardRecognition[]
  error: boolean
}

export function MovieMetadataDetailsDialog({
  eyebrow,
  title,
  onClose,
  children,
}: {
  eyebrow: string
  title: MovieMetadataDetails
  onClose: () => void
  children?: ReactNode
}) {
  const [awardsResponse, setAwardsResponse] = useState<AwardsResponse | null>(null)
  const imdbId = title.imdbId

  useEffect(() => {
    let active = true
    if (!imdbId) return () => { active = false }

    getMovieAwards([imdbId])
      .then((result) => { if (active) setAwardsResponse({ imdbId, awards: result, error: false }) })
      .catch(() => { if (active) setAwardsResponse({ imdbId, awards: [], error: true }) })
    return () => { active = false }
  }, [imdbId])

  const hasCurrentAwards = awardsResponse?.imdbId === imdbId
  const awards = hasCurrentAwards ? awardsResponse.awards : []
  const awardsError = hasCurrentAwards && awardsResponse.error

  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget) onClose()
  }}>
    <section
      aria-labelledby="movie-metadata-details-heading"
      aria-modal="true"
      className="detail-dialog"
      onKeyDown={(event) => { if (event.key === 'Escape') onClose() }}
      role="dialog"
      tabIndex={-1}
    >
      <header className="detail-header">
        <div>
          <p className="eyebrow">{eyebrow}</p>
          <h2 id="movie-metadata-details-heading">{title.title}</h2>
          <div className="detail-meta">{title.year ?? 'Year unknown'} | {title.mediaType ? formatWords(title.mediaType) : 'Type unknown'}</div>
        </div>
        <button aria-label={`Close ${eyebrow.toLowerCase()} details`} className="button button-secondary" onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        <MovieDetailsIntro imdbId={title.imdbId} imdbRating={title.imdbRating} posterLabel={title.mediaType?.slice(0, 3).toUpperCase() ?? 'FIL'} posterUrl={title.posterUrl} title={title.title}>
          <span>{title.genres?.join(' · ') || 'Genres unavailable'}</span>
          {title.plot && <p className="detail-description">{title.plot}</p>}
        </MovieDetailsIntro>
        <dl className="detail-facts">
          <div><dt>IMDb votes</dt><dd>{title.imdbVotes?.toLocaleString() ?? 'Not listed'}</dd></div>
          <div><dt>Metascore</dt><dd>{title.metascore ?? 'Not listed'}</dd></div>
          <div><dt>Director</dt><dd>{title.director || 'Not listed'}</dd></div>
          <div><dt>Runtime</dt><dd>{title.runtime || 'Not listed'}</dd></div>
          <div><dt>Genres</dt><dd>{title.genres?.join(', ') || 'Not tagged'}</dd></div>
          <div><dt>Countries</dt><dd>{title.countries?.join(', ') || 'Not tagged'}</dd></div>
          <div><dt>Provider awards</dt><dd>{title.awards || 'Not listed'}</dd></div>
          <div><dt>Box office</dt><dd>{title.boxOffice || 'Not listed'}</dd></div>
        </dl>
        {children}
        {awardsError && <p className="movie-awards-warning" role="status">Linked awards are temporarily unavailable.</p>}
        <MovieAwardsList awards={awards} />
      </div>
    </section>
  </div>
}
import { useEffect, useState, type ReactNode } from 'react'
import { getMovieAwards } from '../api/client'
import type { MovieAwardRecognition } from '../api/types'
import { formatWords } from '../shared/format'
import { MovieAwardsList } from './MoviePresentation'
import { MovieDetailsTemplate } from './MovieDetailsTemplate'

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
  details,
  facts,
  actions,
  actionsBeforeFacts = false,
  children,
}: {
  eyebrow: string
  title: MovieMetadataDetails
  onClose: () => void
  details?: ReactNode
  facts?: ReactNode
  actions?: ReactNode
  actionsBeforeFacts?: boolean
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
  const metadataFacts = (title.metascore !== undefined || title.awards !== undefined || title.boxOffice !== undefined) && <>
    {title.metascore !== undefined && <div><dt>Metascore</dt><dd>{title.metascore ?? 'Not listed'}</dd></div>}
    {title.awards !== undefined && <div><dt>Provider awards</dt><dd>{title.awards || 'Not listed'}</dd></div>}
    {title.boxOffice !== undefined && <div><dt>Box office</dt><dd>{title.boxOffice || 'Not listed'}</dd></div>}
  </>
  const allFacts = (facts || metadataFacts) ? <>{facts}{metadataFacts}</> : undefined

  return <MovieDetailsTemplate
    actions={actions}
    actionsBeforeFacts={actionsBeforeFacts}
    details={details}
    eyebrow={eyebrow}
    facts={allFacts}
    genres={title.genres}
    imdbId={title.imdbId}
    imdbRating={title.imdbRating}
    imdbVotes={title.imdbVotes}
    posterLabel={title.mediaType?.slice(0, 3).toUpperCase() ?? 'FIL'}
    posterUrl={title.posterUrl}
    plot={title.plot}
    runtime={title.runtime}
    title={title.title}
    typeLabel={title.mediaType ? formatWords(title.mediaType) : 'Type unknown'}
    year={title.year}
    director={title.director}
    countries={title.countries}
    onClose={onClose}
  >
    {children}
    {awardsError && <p className="movie-awards-warning" role="status">Linked awards are temporarily unavailable.</p>}
    <MovieAwardsList awards={awards} />
  </MovieDetailsTemplate>
}
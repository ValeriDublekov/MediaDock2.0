import { useEffect, useRef, useState } from 'react'
import { getMovieAwards, getTitle, getTitleOccurrences, getTitleOscars } from '../../api/client'
import type { MovieAwardRecognition, Occurrence, OscarFilm, PageResponse, TitleDetails } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { MovieAwardsList, MovieImdbLink } from '../../components/MoviePresentation'
import { Poster } from '../../components/Poster'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions } from '../../shared/movieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'

interface TitleDetailsDialogProps {
  titleId: number
  initialSection?: 'details' | 'torrents'
  onClose: () => void
}

export function TitleDetailsDialog({ titleId, initialSection = 'details', onClose }: TitleDetailsDialogProps) {
  const [title, setTitle] = useState<TitleDetails | null>(null)
  const [occurrences, setOccurrences] = useState<PageResponse<Occurrence> | null>(null)
  const [oscars, setOscars] = useState<OscarFilm[]>([])
  const [awards, setAwards] = useState<MovieAwardRecognition[]>([])
  const [awardsError, setAwardsError] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const torrentSectionRef = useRef<HTMLElement>(null)

  useEffect(() => {
    let current = true
    setLoading(true)
    setError(null)
    Promise.all([getTitle(titleId), getTitleOccurrences(titleId, { page: 1, pageSize: 5 }), getTitleOscars(titleId)])
      .then(async ([details, page, oscarFilms]) => {
        let combinedAwards: MovieAwardRecognition[] = []
        let lookupFailed = false
        if (details.imdbId) {
          try {
            combinedAwards = await getMovieAwards([details.imdbId])
          } catch {
            lookupFailed = true
          }
        }
        if (!current) return
        setTitle(details)
        setOccurrences(page)
        setOscars(oscarFilms)
        setAwards(combineMovieAwards(oscarFilms.flatMap(getOscarRecognitions), combinedAwards, details.imdbId))
        setAwardsError(lookupFailed)
      })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The title request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [titleId, attempt])

  useEffect(() => {
    if (initialSection === 'torrents' && !loading && title) {
      torrentSectionRef.current?.scrollIntoView?.({ block: 'start' })
    }
  }, [initialSection, loading, title])

  return (
    <div className="detail-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <section aria-labelledby="title-details-heading" aria-modal="true" className="detail-dialog" role="dialog">
        <header className="detail-header">
          <div>
            <p className="eyebrow">TITLE DETAILS</p>
            <h2 id="title-details-heading">{title?.title ?? 'Catalog title'}</h2>
            {title && <div className="detail-meta">{title.year ?? 'Year unknown'} | {formatWords(title.mediaType)} | {title.occurrenceCount} observations</div>}
          </div>
          <button aria-label="Close title details" className="button button-secondary" onClick={onClose} type="button">Close</button>
        </header>

        <div className="detail-body">
          {loading && <LoadingState label="Loading title details" />}
          {!loading && error && <ErrorState message={error} onRetry={() => setAttempt((current) => current + 1)} />}
          {!loading && !error && title && (
            <>
              <div className="film-details-intro">
                <Poster className="detail-poster" label={title.mediaType.slice(0, 3).toUpperCase()} src={title.posterUrl} title={title.title} />
                <div className="film-details-summary">
                  <MovieImdbLink imdbId={title.imdbId} rating={title.imdbRating} title={title.title} />
                  <span>{title.genres.join(' · ') || 'Genres unavailable'}</span>
                  {title.plot && <p className="detail-description">{title.plot}</p>}
                </div>
              </div>
              <dl className="detail-facts">
                <div><dt>IMDb votes</dt><dd>{title.imdbVotes?.toLocaleString() ?? 'Not listed'}</dd></div>
                <div><dt>Director</dt><dd>{title.director ?? 'Not listed'}</dd></div>
                <div><dt>Runtime</dt><dd>{title.runtime ?? 'Not listed'}</dd></div>
                <div><dt>Genres</dt><dd>{title.genres.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>Countries</dt><dd>{title.countries.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>First seen</dt><dd>{formatDate(title.firstSeenAt)}</dd></div>
              </dl>
              <FavoriteControls from={oscars.length ? 'oscar' : 'catalog'} mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} titleId={titleId} />
              {awardsError && <p className="movie-awards-warning" role="status">Combined awards are temporarily unavailable.</p>}
              <MovieAwardsList awards={awards} />
              <section ref={torrentSectionRef}>
                <div className="section-title-row"><div><h2>Torrent sources</h2><p>Most recently seen occurrences</p></div></div>
                {occurrences?.items.length ? (
                  <div className="occurrence-list">{occurrences.items.map((item) => <OccurrenceRow item={item} key={item.id} />)}</div>
                ) : (
                  <EmptyState title="No observations recorded" message="This title has no linked feed occurrences yet." />
                )}
              </section>
            </>
          )}
        </div>
      </section>
    </div>
  )
}

function OccurrenceRow({ item }: { item: Occurrence }) {
  return (
    <div className="occurrence-row">
      <div>
        <strong>{item.sourceName} | {item.sourceFeedName}</strong>
        <span>{item.rawTitle}</span>
        <span>Last seen {formatDate(item.lastSeenAt)}</span>
      </div>
      <a href={item.torrentUrl} rel="noreferrer" target="_blank">Open source</a>
    </div>
  )
}
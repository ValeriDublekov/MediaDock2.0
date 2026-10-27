import { useEffect, useRef, useState } from 'react'
import { deleteCatalogTitle, getMovieAwards, getTitle, getTitleOccurrences, getTitleOscars } from '../../api/client'
import type { DeleteTitleResponse, MovieAwardRecognition, Occurrence, OscarFilm, PageResponse, TitleDetails } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { MovieAwardsList, MovieDetailsIntro } from '../../components/MoviePresentation'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions } from '../../shared/movieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'

interface TitleDetailsDialogProps {
  titleId: number
  initialSection?: 'details' | 'torrents'
  onClose: () => void
  onFindImdbMatch?: (titleId: number, title: string, rawTitle: string) => void
  onDeleted?: (result: DeleteTitleResponse, deleteImdbInfo: boolean, hadImdbInfo: boolean) => void
  onFavoriteAdded?: () => void
  onFavoriteRemoved?: () => void
  onFavoriteUpdated?: (input: { toWatch?: boolean; toDownload?: boolean }) => void
  onIgnored?: () => void
}

export function TitleDetailsDialog({ titleId, initialSection = 'details', onClose, onFindImdbMatch, onDeleted, onFavoriteAdded, onFavoriteRemoved, onFavoriteUpdated, onIgnored }: TitleDetailsDialogProps) {
  const [title, setTitle] = useState<TitleDetails | null>(null)
  const [occurrences, setOccurrences] = useState<PageResponse<Occurrence> | null>(null)
  const [oscars, setOscars] = useState<OscarFilm[]>([])
  const [awards, setAwards] = useState<MovieAwardRecognition[]>([])
  const [awardsError, setAwardsError] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deletingTitle, setDeletingTitle] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
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

  async function deleteTitle(deleteImdbInfo: boolean) {
    const handleDeleted = onDeleted
    if (!handleDeleted) return
    setDeletingTitle(true)
    setDeleteError(null)
    try {
      const result = await deleteCatalogTitle(titleId, deleteImdbInfo)
      handleDeleted(result, deleteImdbInfo, Boolean(title?.imdbId))
      onClose()
    } catch (requestError: unknown) {
      setDeleteError(requestError instanceof Error ? requestError.message : 'Could not delete the catalog title.')
    } finally {
      setDeletingTitle(false)
    }
  }

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
              <MovieDetailsIntro imdbId={title.imdbId} imdbRating={title.imdbRating} posterLabel={title.mediaType.slice(0, 3).toUpperCase()} posterUrl={title.posterUrl} title={title.title}>
                <span>{title.genres.join(' · ') || 'Genres unavailable'}</span>
                {title.plot && <p className="detail-description">{title.plot}</p>}
              </MovieDetailsIntro>
              <dl className="detail-facts">
                <div><dt>IMDb votes</dt><dd>{title.imdbVotes?.toLocaleString() ?? 'Not listed'}</dd></div>
                <div><dt>Director</dt><dd>{title.director ?? 'Not listed'}</dd></div>
                <div><dt>Runtime</dt><dd>{title.runtime ?? 'Not listed'}</dd></div>
                <div><dt>Genres</dt><dd>{title.genres.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>Countries</dt><dd>{title.countries.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>First seen</dt><dd>{formatDate(title.firstSeenAt)}</dd></div>
              </dl>
              {onFindImdbMatch && <div className="imdb-match-trigger">
                <button className="button button-secondary" onClick={() => onFindImdbMatch(title.id, title.title, occurrences?.items[0]?.rawTitle ?? title.title)} type="button">
                  {title.imdbId ? 'Change IMDb match' : 'Find IMDb match'}
                </button>
              </div>}
              {onDeleted && <div className="title-delete-trigger">
                {!confirmingDelete ? (
                  <button className="button button-danger" onClick={() => { setDeleteError(null); setConfirmingDelete(true) }} type="button">Delete from catalog</button>
                ) : (
                  <div aria-labelledby="delete-title-question" className="delete-confirmation" role="group">
                    <p id="delete-title-question">Delete {title.title} from the torrent catalog?</p>
                    <p>Should its linked IMDb information also be deleted? Shared information used by other records will be kept.</p>
                    <div className="delete-confirmation-actions">
                      <button className="button button-danger" disabled={deletingTitle} onClick={() => void deleteTitle(true)} type="button">Yes</button>
                      <button className="button button-secondary" disabled={deletingTitle} onClick={() => void deleteTitle(false)} type="button">No</button>
                      <button className="text-button" disabled={deletingTitle} onClick={() => setConfirmingDelete(false)} type="button">Cancel</button>
                    </div>
                    {deletingTitle && <p role="status">Deleting title…</p>}
                    {deleteError && <p className="form-error" role="alert">{deleteError}</p>}
                  </div>
                )}
              </div>}
              <FavoriteControls from={oscars.length ? 'oscar' : 'catalog'} imdbId={title.imdbId} metadataStatus={title.metadataStatus} mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} onAdded={onFavoriteAdded} onRemoved={onFavoriteRemoved} onUpdated={onFavoriteUpdated} />
              <IgnoredMovieControl imdbId={title.imdbId} mediaType={title.mediaType} onIgnored={onIgnored} />
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
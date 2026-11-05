import { useEffect, useRef, useState } from 'react'
import { deleteCatalogTitle, getMovieAwards, getTitle, getTitleOccurrences, getTitleOscars } from '../../api/client'
import type { DeleteTitleResponse, MovieAwardRecognition, Occurrence, PageResponse, TitleDetails } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { MovieAwardsSummary } from '../../components/MoviePresentation'
import { MovieDetailsTemplate } from '../../components/MovieDetailsTemplate'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions } from '../../shared/movieAwards'
import { isHttpUrl } from '../../shared/urls'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'

interface TitleDetailsDialogProps {
  titleId: number
  initialSection?: 'details' | 'torrents'
  onClose: () => void
  onFindImdbMatch?: (titleId: number, title: string, rawTitle: string) => void
  onDeleted?: (result: DeleteTitleResponse, deleteImdbInfo: boolean, hadImdbInfo: boolean) => void
  onFavoriteRemoved?: () => void
  onFavoriteUpdated?: (input: { toWatch?: boolean; toDownload?: boolean }) => void
  onIgnored?: () => void
}

export function TitleDetailsDialog({ titleId, initialSection = 'details', onClose, onFindImdbMatch, onDeleted, onFavoriteRemoved, onFavoriteUpdated, onIgnored }: TitleDetailsDialogProps) {
  const [title, setTitle] = useState<TitleDetails | null>(null)
  const [occurrences, setOccurrences] = useState<PageResponse<Occurrence> | null>(null)
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

  return <MovieDetailsTemplate
    actions={title && !loading && !error && <>
      {onFindImdbMatch && <button className="button button-secondary" onClick={() => onFindImdbMatch(title.id, title.title, occurrences?.items[0]?.rawTitle ?? title.title)} type="button">
        {title.imdbId ? 'Change IMDb match' : 'Find IMDb match'}
      </button>}
      <FavoriteControls from="catalog" imdbId={title.imdbId} metadataStatus={title.metadataStatus} mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} onRemoved={onFavoriteRemoved} onUpdated={onFavoriteUpdated} />
      <IgnoredMovieControl imdbId={title.imdbId} mediaType={title.mediaType} onIgnored={onIgnored} />
      {onDeleted && <div className="title-action-delete">
        {!confirmingDelete ? (
          <button className="button button-danger" onClick={() => { setDeleteError(null); setConfirmingDelete(true) }} type="button">Delete from catalog</button>
        ) : (
          <div aria-labelledby="delete-title-question" className="delete-confirmation" role="group">
            <p id="delete-title-question">Delete {title.title} from the torrent catalog?</p>
            <p>Should its linked IMDb information also be deleted? Shared information used by other records will be kept.</p>
            <div className="delete-confirmation-actions">
              <button className="button button-danger" disabled={deletingTitle} onClick={() => void deleteTitle(true)} type="button">Yes</button>
              <button className="button button-secondary" disabled={deletingTitle} onClick={() => void deleteTitle(false)} type="button">No</button>
              <button className="button button-secondary" disabled={deletingTitle} onClick={() => setConfirmingDelete(false)} type="button">Cancel</button>
            </div>
            {deletingTitle && <p role="status">Deleting title…</p>}
            {deleteError && <p className="form-error" role="alert">{deleteError}</p>}
          </div>
        )}
      </div>}
    </>}
    className="title-detail-dialog"
    countries={title?.countries}
    director={title?.director}
    eyebrow="TORRENT CATALOG"
    genres={title?.genres}
    imdbId={title?.imdbId ?? null}
    imdbRating={title?.imdbRating ?? null}
    imdbVotes={title?.imdbVotes}
    onClose={onClose}
    plot={title?.plot}
    posterLabel={title?.mediaType.slice(0, 3).toUpperCase() ?? 'FIL'}
    posterPreviewOnHover
    posterUrl={title?.posterUrl ?? null}
    runtime={title?.runtime}
    showOverview={!loading && !error && Boolean(title)}
    title={title?.title ?? 'Catalog title'}
    typeLabel={title?.mediaType === 'movie' ? null : title ? formatWords(title.mediaType) : null}
    year={title?.year}
  >
    {loading && <LoadingState label="Loading title details" />}
    {!loading && error && <ErrorState message={error} onRetry={() => setAttempt((current) => current + 1)} />}
    {!loading && !error && title && <>
      {awardsError && <p className="movie-awards-warning" role="status">Combined awards are temporarily unavailable.</p>}
      {awards.length > 0 && <section aria-labelledby="title-awards-heading" className="title-detail-awards">
        <div className="section-title-row"><h2 id="title-awards-heading">Awards and nominations</h2></div>
        <MovieAwardsSummary awards={awards} compact />
      </section>}
      <section ref={torrentSectionRef}>
        <div className="section-title-row"><h2>Torrent sources</h2></div>
        {occurrences?.items.length ? (
          <div className="occurrence-list">{occurrences.items.map((item) => <OccurrenceRow item={item} key={item.id} />)}</div>
        ) : (
          <EmptyState title="No observations recorded" message="This title has no linked feed occurrences yet." />
        )}
      </section>
    </>}
  </MovieDetailsTemplate>
}

function OccurrenceRow({ item }: { item: Occurrence }) {
  const torrentUrl = isHttpUrl(item.torrentUrl) ? item.torrentUrl : null
  const sourceLabel = item.sourceName === item.sourceFeedName ? item.sourceName : `${item.sourceName} | ${item.sourceFeedName}`

  return (
    <div className="occurrence-row">
      <div>
        <strong>{sourceLabel}</strong>
        {torrentUrl
          ? <a className="occurrence-title" href={torrentUrl} rel="noopener noreferrer" target="_blank">{item.rawTitle}</a>
          : <span className="occurrence-title">{item.rawTitle}</span>}
        <span>Last seen {formatDate(item.lastSeenAt)}</span>
      </div>
    </div>
  )
}
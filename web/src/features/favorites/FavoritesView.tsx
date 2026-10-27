import { useEffect, useState } from 'react'
import { getFavorites } from '../../api/client'
import type { FavoriteTitle, PageResponse } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { MovieDetailsIntro, MoviePosterCard } from '../../components/MoviePresentation'
import { titlePageSize } from '../../shared/titlePagination'
import { removePageItems } from '../../shared/removePageItems'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'
import { TitleDetailsDialog } from '../catalog/TitleDetailsDialog'
import { FavoriteControls } from './FavoriteControls'
import { useFavorites } from './FavoriteContext'

type Status = 'all' | 'to_watch' | 'to_download'

export function FavoritesView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [status, setStatus] = useState<Status>('all')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PageResponse<FavoriteTitle> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selected, setSelected] = useState<FavoriteTitle | null>(null)
  const { available } = useFavorites()

  useEffect(() => {
    if (!available) return
    let active = true

    getFavorites({ status, page, pageSize: titlePageSize })
      .then((response) => { if (active) setResult(response) })
      .catch((reason: unknown) => { if (active) setError(reason instanceof Error ? reason.message : 'Favorites could not be loaded.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [available, status, page, attempt])

  function removeFavoriteTitle(imdbId: string) {
    setResult((current) => removePageItems(current, (item) => item.imdbId === imdbId))
  }

  function updateFavoriteTitle(imdbId: string, input: { toWatch?: boolean; toDownload?: boolean }) {
    setResult((current) => {
      if (!current) return current
      const item = current.items.find((candidate) => candidate.imdbId === imdbId)
      if (!item) return current
      const updated = { ...item, ...input }
      const noLongerMatches = (status === 'to_watch' && !updated.toWatch)
        || (status === 'to_download' && !updated.toDownload)
      if (noLongerMatches) return removePageItems(current, (candidate) => candidate.imdbId === imdbId)
      return { ...current, items: current.items.map((candidate) => candidate.imdbId === imdbId ? updated : candidate) }
    })
  }

  function changeStatus(next: Status) {
    setStatus(next)
    setPage(1)
    setLoading(true)
    setError(null)
  }

  return <section aria-label="Favorites" className="media-view">
    <div className="section-toolbar">
      <div className="favorite-filters" role="group" aria-label="Favorite status">
        {(['all', 'to_watch', 'to_download'] as const).map((option) =>
          <button aria-pressed={status === option} className={`button ${status === option ? '' : 'button-secondary'}`} key={option}
            onClick={() => changeStatus(option)} type="button">{option === 'all' ? 'All' : option === 'to_watch' ? 'To watch' : 'To download'}</button>)}
      </div>
      <div className="toolbar-actions">{!error && result && <span className="result-count">{result.totalCount} {result.totalCount === 1 ? 'title' : 'titles'}</span>}<ViewModeControl onChange={setViewMode} value={viewMode} /></div>
    </div>
    {!available && <EmptyState title="Sign in to view favorites" message="An active linked account is required." />}
    {available && loading && <LoadingState label="Loading favorites" />}
    {available && !loading && error && <ErrorState message={error} onRetry={() => { setLoading(true); setError(null); setAttempt((value) => value + 1) }} />}
    {available && !loading && !error && result?.items.length === 0 && <EmptyState title="No favorites here" message="Add movies from supported catalogs or titles from recommendations." />}
    {available && !loading && !error && result && result.items.length > 0 && <>
      {viewMode === 'posters' ? <div className="poster-grid">
        {result.items.map((item) => <MoviePosterCard
          awards={[]}
          footer={<FavoriteControls allowAddFromHere={false} alwaysShowRemove from={item.addedFromOscar ? 'oscar' : 'catalog'} imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={() => removeFavoriteTitle(item.imdbId)} onUpdated={(input) => updateFavoriteTitle(item.imdbId, input)} placement="status" />}
          imdbId={item.imdbId}
          imdbRating={item.imdbRating}
          key={item.imdbId}
          mediaType={item.mediaType}
          onOpen={() => setSelected(item)}
          openLabel={`View ${item.title} details`}
          posterActions={<>
            <IgnoredMovieControl imdbId={item.imdbId} mediaType={item.mediaType} placement="poster" />
          </>}
          posterLabel={item.mediaType.slice(0, 3).toUpperCase()}
          posterUrl={item.posterUrl}
          showAwards={false}
          title={item.title}
          year={item.year}
        >
          <div className="tile-observations">
            <span>{[item.addedFromOscar && 'Oscar', item.addedFromCatalog && 'Catalog', item.addedFromRecommendations && 'Recommendations'].filter(Boolean).join(' / ')}</span>
            <span>{item.mediaType === 'movie' ? item.winCount > 0 ? `${item.winCount} Oscar ${item.winCount === 1 ? 'win' : 'wins'}` : `${item.nominationCount} nominations` : 'Series watch list'}</span>
            <span>{item.occurrenceCount ? `${item.occurrenceCount} torrent ${item.occurrenceCount === 1 ? 'source' : 'sources'}` : 'No torrent source available'}</span>
          </div>
        </MoviePosterCard>)}
      </div> : <div className="table-wrap"><table className="data-table"><thead><tr><th>TITLE</th><th>ORIGIN</th><th>OSCAR</th><th>TORRENTS</th><th>STATUS</th></tr></thead>
        <tbody>{result.items.map((item) => <tr key={item.imdbId}>
          <td><button className="title-link" onClick={() => setSelected(item)} type="button">{item.title}</button><span className="subtle-line">{item.year ?? 'Year unknown'}</span></td>
          <td>{[item.addedFromOscar && 'Oscar', item.addedFromCatalog && 'Catalog', item.addedFromRecommendations && 'Recommendations'].filter(Boolean).join(' / ')}</td>
          <td>{item.mediaType === 'movie' ? `${item.nominationCount} nominations, ${item.winCount} wins` : 'No Oscar records'}</td>
          <td>{item.occurrenceCount ? `${item.occurrenceCount} sources` : 'No source available'}</td>
          <td><FavoriteControls allowAddFromHere={false} from={item.addedFromOscar ? 'oscar' : 'catalog'} imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={() => removeFavoriteTitle(item.imdbId)} onUpdated={(input) => updateFavoriteTitle(item.imdbId, input)} /></td>
        </tr>)}</tbody>
      </table></div>}
      <Pagination onPageChange={(next) => { setPage(next); setLoading(true) }} page={result} />
    </>}
    {selected && (selected.torrentMovieId !== null
      ? <TitleDetailsDialog
        onClose={() => setSelected(null)}
        onFavoriteRemoved={() => { removeFavoriteTitle(selected.imdbId); setSelected(null) }}
        onFavoriteUpdated={(input) => {
          updateFavoriteTitle(selected.imdbId, input)
          setSelected((current) => current ? { ...current, ...input } : current)
        }}
        titleId={selected.torrentMovieId}
      />
      : <FavoriteDetailsDialog
        item={selected}
        onClose={() => setSelected(null)}
        onRemoved={() => { removeFavoriteTitle(selected.imdbId); setSelected(null) }}
        onUpdated={(input) => {
          updateFavoriteTitle(selected.imdbId, input)
          setSelected((current) => current ? { ...current, ...input } : current)
        }}
      />)}
  </section>
}

function FavoriteDetailsDialog({ item, onClose, onRemoved, onUpdated }: {
  item: FavoriteTitle
  onClose: () => void
  onRemoved: () => void
  onUpdated: (input: { toWatch?: boolean; toDownload?: boolean }) => void
}) {
  const origin = [item.addedFromOscar && 'Oscar', item.addedFromCatalog && 'Catalog', item.addedFromRecommendations && 'Recommendations'].filter(Boolean).join(' / ')
  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget) onClose()
  }}>
    <section aria-labelledby="favorite-title-details-heading" aria-modal="true" className="detail-dialog" role="dialog">
      <header className="detail-header">
        <div>
          <p className="eyebrow">FAVORITE</p>
          <h2 id="favorite-title-details-heading">{item.title}</h2>
          <div className="detail-meta">{item.year ?? 'Year unknown'} | {item.mediaType}</div>
        </div>
        <button aria-label="Close favorite details" className="button button-secondary" onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        <MovieDetailsIntro imdbId={item.imdbId} imdbRating={item.imdbRating} posterLabel={item.mediaType.slice(0, 3).toUpperCase()} posterUrl={item.posterUrl} title={item.title} />
        <dl className="detail-facts">
          <div><dt>Saved from</dt><dd>{origin || 'Favorites'}</dd></div>
          <div><dt>Oscar nominations</dt><dd>{item.nominationCount}</dd></div>
          <div><dt>Oscar wins</dt><dd>{item.winCount}</dd></div>
          <div><dt>Torrent sources</dt><dd>{item.occurrenceCount}</dd></div>
          <div><dt>Watch status</dt><dd>{item.toWatch ? 'To watch' : 'Not marked to watch'}</dd></div>
          {item.mediaType === 'movie' && <div><dt>Download status</dt><dd>{item.toDownload ? 'To download' : 'Not marked to download'}</dd></div>}
        </dl>
        <FavoriteControls allowAddFromHere={false} from={item.addedFromOscar ? 'oscar' : 'catalog'} imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={onRemoved} onUpdated={onUpdated} />
        <IgnoredMovieControl imdbId={item.imdbId} mediaType={item.mediaType} />
      </div>
    </section>
  </div>
}
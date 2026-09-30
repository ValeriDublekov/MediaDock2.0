import { useEffect, useState } from 'react'
import { getFavorites } from '../../api/client'
import type { FavoriteMovie, PageResponse } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { Poster } from '../../components/Poster'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { TitleDetailsDialog } from '../catalog/TitleDetailsDialog'
import { FavoriteControls } from './FavoriteControls'
import { useFavorites } from './FavoriteContext'

type Status = 'all' | 'to_watch' | 'to_download'

export function FavoritesView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [status, setStatus] = useState<Status>('all')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PageResponse<FavoriteMovie> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selected, setSelected] = useState<number | null>(null)
  const { movies } = useFavorites()

  useEffect(() => {
    let active = true
    getFavorites({ status, page, pageSize: 20 })
      .then((response) => { if (active) setResult(response) })
      .catch((reason: unknown) => { if (active) setError(reason instanceof Error ? reason.message : 'Favorites could not be loaded.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [status, page, attempt, movies])

  function changeStatus(next: Status) {
    setStatus(next)
    setPage(1)
    setLoading(true)
    setError(null)
  }

  return <section aria-label="Favorites">
    <div className="section-toolbar">
      <div className="favorite-filters" role="group" aria-label="Favorite status">
        {(['all', 'to_watch', 'to_download'] as const).map((option) =>
          <button aria-pressed={status === option} className={`button ${status === option ? '' : 'button-secondary'}`} key={option}
            onClick={() => changeStatus(option)} type="button">{option === 'all' ? 'All' : option === 'to_watch' ? 'To watch' : 'To download'}</button>)}
      </div>
      <div className="toolbar-actions">{!error && result && <span className="result-count">{result.totalCount} movies</span>}<ViewModeControl onChange={setViewMode} value={viewMode} /></div>
    </div>
    {loading && <LoadingState label="Loading favorites" />}
    {!loading && error && <ErrorState message={error} onRetry={() => { setLoading(true); setError(null); setAttempt((value) => value + 1) }} />}
    {!loading && !error && result?.items.length === 0 && <EmptyState title="No favorites here" message="Add a movie from the catalog or Oscar catalog." />}
    {!loading && !error && result && result.items.length > 0 && <>
      {viewMode === 'posters' ? <div className="poster-grid">
        {result.items.map((item) => <article className="movie-tile" key={item.titleId}>
          <button aria-label={`View ${item.title} details`} className="poster-action" onClick={() => setSelected(item.titleId)} type="button">
            <Poster src={item.posterUrl} title={item.title} />
          </button>
          <div className="movie-tile-info">
            <button className="tile-title" onClick={() => setSelected(item.titleId)} type="button">{item.title}</button>
            <div className="tile-meta">{item.year ?? 'Year unknown'} <span aria-hidden="true">·</span> <span>{[item.addedFromOscar && 'Oscar', item.addedFromCatalog && 'Catalog'].filter(Boolean).join(' / ')}</span></div>
            <div className="tile-genres">{item.winCount > 0 ? `${item.winCount} Oscar ${item.winCount === 1 ? 'win' : 'wins'}` : `${item.nominationCount} nominations`}</div>
            <div className="tile-footer"><span className="tile-rating">{item.imdbRating === null ? 'Not rated' : `IMDb ${item.imdbRating.toFixed(1)}`}</span></div>
            <FavoriteControls from={item.addedFromOscar ? 'oscar' : 'catalog'} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} titleId={item.titleId} />
          </div>
        </article>)}
      </div> : <div className="table-wrap"><table className="data-table"><thead><tr><th>MOVIE</th><th>ORIGIN</th><th>OSCAR</th><th>TORRENTS</th><th>STATUS</th></tr></thead>
        <tbody>{result.items.map((item) => <tr key={item.titleId}>
          <td><button className="title-link" onClick={() => setSelected(item.titleId)} type="button">{item.title}</button><span className="subtle-line">{item.year ?? 'Year unknown'}</span></td>
          <td>{[item.addedFromOscar && 'Oscar', item.addedFromCatalog && 'Catalog'].filter(Boolean).join(' / ')}</td>
          <td>{item.nominationCount} nominations, {item.winCount} wins</td>
          <td>{item.occurrenceCount ? `${item.occurrenceCount} sources` : 'No source available'}</td>
          <td><FavoriteControls from={item.addedFromOscar ? 'oscar' : 'catalog'} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} titleId={item.titleId} /></td>
        </tr>)}</tbody>
      </table></div>}
      <Pagination onPageChange={(next) => { setPage(next); setLoading(true) }} page={result} />
    </>}
    {selected !== null && <TitleDetailsDialog onClose={() => setSelected(null)} titleId={selected} />}
  </section>
}
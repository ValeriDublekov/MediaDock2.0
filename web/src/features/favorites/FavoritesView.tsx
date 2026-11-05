import { useEffect, useState } from 'react'
import { Plus } from 'lucide-react'
import { getFavorites } from '../../api/client'
import type { FavoriteTitle, PageResponse } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { MovieImdbLink, MoviePosterCard, MovieTableTitle } from '../../components/MoviePresentation'
import { MovieMetadataDetailsDialog } from '../../components/MovieMetadataDetailsDialog'
import { titlePageSize } from '../../shared/titlePagination'
import { formatWords } from '../../shared/format'
import { removePageItems } from '../../shared/removePageItems'
import { combineMovieAwards, movieAwardsRequestKey } from '../../shared/movieAwards'
import { useMovieAwards } from '../../shared/useMovieAwards'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { ImdbCandidatePicker } from '../../components/ImdbCandidatePicker'
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
  const [addTitleOpen, setAddTitleOpen] = useState(false)
  const { available, add, retry } = useFavorites()
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((item) => item.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)

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
      <div className="toolbar-actions">{!error && result && <span className="result-count">{result.totalCount} {result.totalCount === 1 ? 'title' : 'titles'}</span>}{available && <button className="button button-secondary" onClick={() => setAddTitleOpen(true)} type="button"><Plus aria-hidden="true" size={16} />Add title</button>}<ViewModeControl onChange={setViewMode} value={viewMode} /></div>
    </div>
    {!available && <EmptyState title="Sign in to view favorites" message="An active linked account is required." />}
    {available && loading && <LoadingState label="Loading favorites" />}
    {available && !loading && error && <ErrorState message={error} onRetry={() => { setLoading(true); setError(null); setAttempt((value) => value + 1) }} />}
    {available && !loading && !error && result?.items.length === 0 && <EmptyState title="No favorites here" message="Add titles from supported catalogs, recommendations, or awards." />}
    {available && !loading && !error && result && result.items.length > 0 && <>
      {awardsError && <p className="movie-awards-warning" role="status">Combined awards are temporarily unavailable.</p>}
      {viewMode === 'posters' ? <div className="poster-grid">
        {result.items.map((item) => <MoviePosterCard
          awards={combineMovieAwards([], pageAwards, item.imdbId)}
          footer={<FavoriteControls allowAddFromHere={false} showFavoriteStatus={false} showRemoveButton={false} from="catalog" imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={() => removeFavoriteTitle(item.imdbId)} onUpdated={(input) => updateFavoriteTitle(item.imdbId, input)} placement="status" />}
          genres={item.genres}
          imdbId={item.imdbId}
          imdbRating={item.imdbRating}
          key={item.imdbId}
          metadata={{ director: item.director ?? null, plot: item.plot ?? null }}
          mediaType={item.mediaType}
          onOpen={() => setSelected(item)}
          openLabel={`View ${item.title} details`}
          posterActions={<FavoriteControls allowAddFromHere={false} from="catalog" imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={() => removeFavoriteTitle(item.imdbId)} placement="poster" showFavoriteStatus={false} />}
          posterLabel={item.mediaType?.slice(0, 3).toUpperCase() ?? 'IMDb'}
          posterUrl={item.posterUrl}
          title={item.title}
          year={item.year}
        >
          {item.occurrenceCount > 0 && <div className="tile-observations"><span>{item.occurrenceCount} torrent {item.occurrenceCount === 1 ? 'source' : 'sources'}</span></div>}
        </MoviePosterCard>)}
      </div> : <div className="table-wrap"><table className="data-table catalog-data-table"><thead><tr><th>TITLE</th><th>TYPE</th><th>IMDB</th><th>OSCAR</th><th>GENRES</th><th>TORRENTS</th><th>STATUS</th></tr></thead>
        <tbody>{result.items.map((item) => <tr key={item.imdbId}>
          <td><MovieTableTitle
            metadata={{ director: item.director ?? null, plot: item.plot ?? null }}
            onOpen={() => setSelected(item)}
            openLabel={`View ${item.title} details`}
            posterLabel={item.mediaType?.slice(0, 3).toUpperCase() ?? 'IMDb'}
            posterUrl={item.posterUrl}
            subtitle={item.year ?? 'Year unknown'}
            title={item.title}
          /></td>
          <td><span className="type-label">{formatWords(item.mediaType ?? 'unknown')}</span></td>
          <td><MovieImdbLink imdbId={item.imdbId} rating={item.imdbRating} title={item.title} /></td>
          <td>{[item.nominationCount > 0 && `${item.nominationCount} nominations`, item.winCount > 0 && `${item.winCount} wins`].filter(Boolean).join(', ')}</td>
          <td>{item.genres?.slice(0, 2).join(', ') || 'Not tagged'}</td>
          <td>{item.occurrenceCount > 0 ? `${item.occurrenceCount} sources` : '0 sources'}</td>
          <td><FavoriteControls allowAddFromHere={false} showFavoriteStatus={false} from="catalog" imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={() => removeFavoriteTitle(item.imdbId)} onUpdated={(input) => updateFavoriteTitle(item.imdbId, input)} /></td>
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
    {addTitleOpen && <ImdbCandidatePicker
      onApplied={async (candidate) => {
        if (!candidate?.imdbId) return
        if (!await add(candidate.imdbId, 'manual')) throw new Error('Could not add the title to favorites.')
        retry()
        setStatus('all')
        setPage(1)
        setLoading(true)
        setError(null)
        setAttempt((current) => current + 1)
        setAddTitleOpen(false)
      }}
      onClose={() => setAddTitleOpen(false)}
      target={{ source: 'manual', list: 'favorites' }}
    />}
  </section>
}

function FavoriteDetailsDialog({ item, onClose, onRemoved, onUpdated }: {
  item: FavoriteTitle
  onClose: () => void
  onRemoved: () => void
  onUpdated: (input: { toWatch?: boolean; toDownload?: boolean }) => void
}) {
  return <MovieMetadataDetailsDialog
    actions={<>
      <FavoriteControls allowAddFromHere={false} showFavoriteStatus={false} from="catalog" imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={item.occurrenceCount} onRemoved={onRemoved} onUpdated={onUpdated} />
      <IgnoredMovieControl imdbId={item.imdbId} mediaType={item.mediaType} />
    </>}
    eyebrow="FAVORITE"
    facts={(item.nominationCount > 0 || item.winCount > 0 || item.occurrenceCount > 0) && <>
    {item.nominationCount > 0 && <div><dt>Oscar nominations</dt><dd>{item.nominationCount}</dd></div>}
    {item.winCount > 0 && <div><dt>Oscar wins</dt><dd>{item.winCount}</dd></div>}
    {item.occurrenceCount > 0 && <div><dt>Torrent sources</dt><dd>{item.occurrenceCount}</dd></div>}
    </>}
    onClose={onClose}
    title={item}
  />
}
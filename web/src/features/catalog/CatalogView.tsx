import { useEffect, useState, type FormEvent } from 'react'
import { getCatalog } from '../../api/client'
import type { CatalogQuery, CatalogTitle, MediaType, PageResponse } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { Poster } from '../../components/Poster'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { formatDate, formatWords } from '../../shared/format'
import { TitleDetailsDialog } from './TitleDetailsDialog'
import { FavoriteControls } from '../favorites/FavoriteControls'

interface CatalogFilters {
  search: string
  mediaType: string
  sourceType: string
  contentKind: string
  yearFrom: string
  yearTo: string
  genre: string
}

const emptyFilters: CatalogFilters = {
  search: '', mediaType: '', sourceType: '', contentKind: '', yearFrom: '', yearTo: '', genre: '',
}

function buildQuery(page: number, filters: CatalogFilters): CatalogQuery {
  return {
    page,
    pageSize: 20,
    ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
    ...(filters.mediaType ? { mediaType: filters.mediaType as MediaType } : {}),
    ...(filters.sourceType ? { sourceType: filters.sourceType as 'movie' | 'series' } : {}),
    ...(filters.contentKind ? { contentKind: filters.contentKind as 'standard' | 'documentary' | 'short' } : {}),
    ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}),
    ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}),
    ...(filters.genre.trim() ? { genre: filters.genre.trim() } : {}),
  }
}

function CatalogRow({ title, onSelect }: { title: CatalogTitle; onSelect: (id: number) => void }) {
  return (
    <tr>
      <td>
        <div className="title-cell">
          <Poster className="poster-small" label={title.mediaType.slice(0, 3).toUpperCase()} src={title.posterUrl} title={title.title} />
          <span>
            <button aria-label={`View ${title.title} details`} className="title-link" onClick={() => onSelect(title.id)} type="button">{title.title}</button>
            <span className="subtle-line">{title.year ?? 'Year unknown'} | {title.occurrenceCount} observations</span>
          </span>
        </div>
      </td>
      <td><span className="type-label">{formatWords(title.mediaType)}</span>{title.contentKind && <span className="subtle-line">{formatWords(title.contentKind)}</span>}</td>
      <td className="rating-value">{title.imdbRating === null ? 'Not rated' : title.imdbRating.toFixed(1)}</td>
      <td>{title.genres.slice(0, 2).join(', ') || 'Not tagged'}</td>
      <td>{formatDate(title.lastSeenAt)}</td>
      <td><FavoriteControls from="catalog" mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} titleId={title.id} /></td>
    </tr>
  )
}

export function CatalogView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [page, setPage] = useState(1)
  const [draftFilters, setDraftFilters] = useState<CatalogFilters>(emptyFilters)
  const [appliedFilters, setAppliedFilters] = useState<CatalogFilters>(emptyFilters)
  const [result, setResult] = useState<PageResponse<CatalogTitle> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedTitleId, setSelectedTitleId] = useState<number | null>(null)
  const [detailsSection, setDetailsSection] = useState<'details' | 'torrents'>('details')
  const activeFilterCount = Object.entries(draftFilters).filter(([key, value]) => key !== 'search' && value !== '').length

  function openTitle(titleId: number, section: 'details' | 'torrents') {
    setSelectedTitleId(titleId)
    setDetailsSection(section)
  }

  useEffect(() => {
    let current = true
    setLoading(true)
    setError(null)
    getCatalog(buildQuery(page, appliedFilters))
      .then((response) => { if (current) setResult(response) })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The catalog request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [page, appliedFilters, attempt])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPage(1)
    setAppliedFilters({ ...draftFilters })
  }

  function updateFilter<K extends keyof CatalogFilters>(key: K, value: CatalogFilters[K]) {
    setDraftFilters((current) => ({ ...current, [key]: value }))
  }

  return (
    <section aria-label="Catalog" className="media-view">
      <form className="filter-form browse-filters" onSubmit={applyFilters}>
        <div className="field filter-search">
          <label htmlFor="catalog-search">Search titles</label>
          <input id="catalog-search" onChange={(event) => updateFilter('search', event.target.value)} placeholder="Title or year" type="search" value={draftFilters.search} />
        </div>
        <details className="advanced-filters">
          <summary>More filters{activeFilterCount > 0 ? ` (${activeFilterCount} active)` : ''}</summary>
          <div className="advanced-fields">
            <div className="field">
              <label htmlFor="catalog-media-type">Media type</label>
              <select id="catalog-media-type" onChange={(event) => updateFilter('mediaType', event.target.value)} value={draftFilters.mediaType}>
                <option value="">All types</option><option value="movie">Movie</option><option value="series">Series</option><option value="documentary">Documentary</option><option value="short">Short</option>
              </select>
            </div>
            <div className="field">
              <label htmlFor="catalog-feed-type">Feed type</label>
              <select id="catalog-feed-type" onChange={(event) => updateFilter('sourceType', event.target.value)} value={draftFilters.sourceType}>
                <option value="">All feeds</option><option value="movie">Movie feed</option><option value="series">Series feed</option>
              </select>
            </div>
            <div className="field">
              <label htmlFor="catalog-content-kind">Content kind</label>
              <select id="catalog-content-kind" onChange={(event) => updateFilter('contentKind', event.target.value)} value={draftFilters.contentKind}>
                <option value="">All content</option><option value="standard">Standard</option><option value="documentary">Documentary</option><option value="short">Short</option>
              </select>
            </div>
            <div className="field">
              <label htmlFor="catalog-year-from">Year from</label>
              <input id="catalog-year-from" max="2200" min="1800" onChange={(event) => updateFilter('yearFrom', event.target.value)} type="number" value={draftFilters.yearFrom} />
            </div>
            <div className="field">
              <label htmlFor="catalog-year-to">Year to</label>
              <input id="catalog-year-to" max="2200" min="1800" onChange={(event) => updateFilter('yearTo', event.target.value)} type="number" value={draftFilters.yearTo} />
            </div>
            <div className="field">
              <label htmlFor="catalog-genre">Genre</label>
              <input id="catalog-genre" onChange={(event) => updateFilter('genre', event.target.value)} placeholder="e.g. drama" value={draftFilters.genre} />
            </div>
          </div>
        </details>
        <div className="filter-actions">
          <button className="button" type="submit">Apply filters</button>
          <button className="button button-secondary" onClick={() => {
            setDraftFilters({ ...emptyFilters })
            setAppliedFilters({ ...emptyFilters })
            setPage(1)
          }} type="button">Clear</button>
        </div>
      </form>

      <div className="section-toolbar">
        <span className="result-count">{result ? `${result.totalCount.toLocaleString()} titles` : 'Catalog records'}</span>
        <div className="toolbar-actions">
          <ViewModeControl onChange={setViewMode} value={viewMode} />
          <button className="text-button" onClick={() => setAttempt((current) => current + 1)} type="button">Refresh results</button>
        </div>
      </div>

      {loading && <LoadingState label="Loading catalog" />}
      {!loading && error && <ErrorState message={error} onRetry={() => setAttempt((current) => current + 1)} />}
      {!loading && !error && result && result.items.length === 0 && <EmptyState title="No titles found" message="Try changing the search or filters, or clear them to browse the full catalog." />}
      {!loading && !error && result && result.items.length > 0 && (
        <>
          {viewMode === 'posters' ? <div className="poster-grid">
            {result.items.map((title) => <article className="movie-tile" key={title.id}>
              <button aria-label={`View ${title.title} details`} className="poster-action" onClick={() => openTitle(title.id, 'details')} type="button">
                <Poster label={title.mediaType.slice(0, 3).toUpperCase()} src={title.posterUrl} title={title.title} />
                <span className="poster-type-badge">{formatWords(title.mediaType)}</span>
              </button>
              <div className="movie-tile-info">
                <div className="tile-heading">
                  <button className="tile-title" onClick={() => openTitle(title.id, 'details')} type="button">{title.title}</button>
                  <span className="tile-year" title={title.year ? `Release year ${title.year}` : 'Release year unknown'}>{title.year ?? '—'}</span>
                </div>
                <div aria-label={title.imdbRating === null
                  ? `IMDb rating for ${title.title}: not rated`
                  : `IMDb rating for ${title.title}: ${title.imdbRating.toFixed(1)} out of 10`} className="tile-rating" role="group">
                  <span>IMDb</span>
                  <strong>{title.imdbRating === null ? 'Not rated' : title.imdbRating.toFixed(1)}</strong>
                  {title.imdbRating !== null && <span className="tile-rating-scale">/ 10</span>}
                </div>
                <div className="tile-genres">
                  {title.genres.length > 0
                    ? title.genres.slice(0, 3).map((genre) => <span className="tile-genre" key={genre}>{genre}</span>)
                    : <span>Genres unavailable</span>}
                </div>
                <div className="tile-observations">
                  <span>{title.occurrenceCount} feed observations</span>
                  <span>Last seen {formatDate(title.lastSeenAt)}</span>
                </div>
                <div className="tile-actions">
                  {title.imdbId
                    ? <a aria-label={`Open ${title.title} on IMDb (opens in new tab)`} className="tile-action tile-action-imdb" href={`https://www.imdb.com/title/${title.imdbId}/`} rel="noopener noreferrer" target="_blank">IMDb</a>
                    : <button aria-label={`Open details for ${title.title}`} className="tile-action" onClick={() => openTitle(title.id, 'details')} type="button">Details</button>}
                  <button aria-label={`View torrent sources for ${title.title} (${title.occurrenceCount})`} className="tile-action tile-action-primary" onClick={() => openTitle(title.id, 'torrents')} type="button">
                    Torrents <span>{title.occurrenceCount}</span>
                  </button>
                </div>
                <FavoriteControls from="catalog" mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} titleId={title.id} />
              </div>
            </article>)}
          </div> : <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>TITLE</th><th>TYPE</th><th>IMDB</th><th>GENRES</th><th>LAST SEEN</th><th>FAVORITES</th></tr></thead>
              <tbody>{result.items.map((title) => <CatalogRow key={title.id} onSelect={setSelectedTitleId} title={title} />)}</tbody>
            </table>
          </div>}
          <Pagination onPageChange={setPage} page={result} />
        </>
      )}
      {selectedTitleId !== null && <TitleDetailsDialog initialSection={detailsSection} onClose={() => setSelectedTitleId(null)} titleId={selectedTitleId} />}
    </section>
  )
}
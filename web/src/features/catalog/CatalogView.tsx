import { useEffect, useState, type FormEvent } from 'react'
import { getCatalog } from '../../api/client'
import type { CatalogQuery, CatalogTitle, FeedType, MediaType, PageResponse } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { YearRangeFilter } from '../../components/YearRangeFilter'
import { MovieAwardsSummary, MovieImdbLink, MoviePosterCard, MovieTableTitle } from '../../components/MoviePresentation'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, movieAwardsRequestKey } from '../../shared/movieAwards'
import { useMovieAwards } from '../../shared/useMovieAwards'
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

type CatalogCategory = 'main' | 'movie' | 'series' | 'series_ongoing' | 'all'

const categoryFeedTypes: Record<Exclude<CatalogCategory, 'all'>, FeedType[]> = {
  main: ['movie', 'series_complete', 'series_ongoing'],
  movie: ['movie'],
  series: ['series_complete'],
  series_ongoing: ['series_ongoing'],
}

const categories: Array<{ id: CatalogCategory; label: string }> = [
  { id: 'main', label: 'Main categories' },
  { id: 'movie', label: 'Movies' },
  { id: 'series', label: 'Series' },
  { id: 'series_ongoing', label: 'Series in progress' },
  { id: 'all', label: 'All' },
]

const emptyFilters: CatalogFilters = {
  search: '', mediaType: '', sourceType: '', contentKind: '', yearFrom: '', yearTo: '', genre: '',
}
const searchDebounceMs = 350

function buildQuery(page: number, filters: CatalogFilters, category: CatalogCategory): CatalogQuery {
  return {
    page,
    pageSize: 20,
    ...(category !== 'all' ? { feedTypes: categoryFeedTypes[category] } : {}),
    ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
    ...(filters.mediaType ? { mediaType: filters.mediaType as MediaType } : {}),
    ...(filters.sourceType ? { sourceType: filters.sourceType as 'movie' | 'series' } : {}),
    ...(filters.contentKind ? { contentKind: filters.contentKind as 'standard' | 'documentary' | 'short' } : {}),
    ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}),
    ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}),
    ...(filters.genre.trim() ? { genre: filters.genre.trim() } : {}),
  }
}

function CatalogRow({ title, onSelect, awards }: { title: CatalogTitle; onSelect: (id: number) => void; awards: ReturnType<typeof combineMovieAwards> }) {
  return (
    <tr>
      <td>
        <MovieTableTitle
          onOpen={() => onSelect(title.id)}
          openLabel={`View ${title.title} details`}
          posterLabel={title.mediaType.slice(0, 3).toUpperCase()}
          posterUrl={title.posterUrl}
          subtitle={`${title.year ?? 'Year unknown'} | ${title.occurrenceCount} observations`}
          title={title.title}
        />
      </td>
      <td><span className="type-label">{formatWords(title.mediaType)}</span>{title.contentKind && <span className="subtle-line">{formatWords(title.contentKind)}</span>}</td>
      <td><MovieImdbLink imdbId={title.imdbId} rating={title.imdbRating} title={title.title} /></td>
      <td><MovieAwardsSummary awards={awards} compact /></td>
      <td>{title.genres.slice(0, 2).join(', ') || 'Not tagged'}</td>
      <td>{formatDate(title.lastSeenAt)}</td>
      <td><FavoriteControls from="catalog" mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} titleId={title.id} /></td>
    </tr>
  )
}

export function CatalogView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [category, setCategory] = useState<CatalogCategory>('main')
  const [page, setPage] = useState(1)
  const [draftFilters, setDraftFilters] = useState<CatalogFilters>(emptyFilters)
  const [appliedFilters, setAppliedFilters] = useState<CatalogFilters>(emptyFilters)
  const [result, setResult] = useState<PageResponse<CatalogTitle> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedTitleId, setSelectedTitleId] = useState<number | null>(null)
  const [detailsSection, setDetailsSection] = useState<'details' | 'torrents'>('details')
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((title) => title.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)
  const activeFilterCount = Object.entries(draftFilters).filter(([key, value]) => key !== 'search' && value !== '').length

  function openTitle(titleId: number, section: 'details' | 'torrents') {
    setSelectedTitleId(titleId)
    setDetailsSection(section)
  }

  useEffect(() => {
    let current = true
    setLoading(true)
    setError(null)
    getCatalog(buildQuery(page, appliedFilters, category))
      .then((response) => { if (current) setResult(response) })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The catalog request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [page, appliedFilters, category, attempt])

  useEffect(() => {
    if (draftFilters.search === appliedFilters.search) return
    const timeoutId = window.setTimeout(() => {
      setLoading(true)
      setError(null)
      setPage(1)
      setAppliedFilters((current) => ({ ...current, search: draftFilters.search }))
    }, searchDebounceMs)
    return () => window.clearTimeout(timeoutId)
  }, [draftFilters.search, appliedFilters.search])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoading(true)
    setError(null)
    setPage(1)
    setAppliedFilters({ ...draftFilters })
  }

  function updateFilter<K extends keyof CatalogFilters>(key: K, value: CatalogFilters[K]) {
    setDraftFilters((current) => ({ ...current, [key]: value }))
    if (key !== 'search') commitFilterChanges({ [key]: value } as Partial<CatalogFilters>)
  }

  function commitFilterChanges(filters: Partial<CatalogFilters>) {
    setLoading(true)
    setError(null)
    setPage(1)
    setAppliedFilters((current) => ({ ...current, ...filters }))
  }

  function updateYearRange(from: number, to: number, apply: boolean) {
    const bounds = result?.yearBounds
    if (!bounds) return
    const years = {
      yearFrom: from === bounds.minYear ? '' : String(from),
      yearTo: to === bounds.maxYear ? '' : String(to),
    }
    setDraftFilters((current) => ({ ...current, ...years }))
    if (apply) commitFilterChanges(years)
  }

  return (
    <section aria-label="Catalog" className="media-view">
      <nav aria-label="Catalog categories" className="catalog-category-nav">
        {categories.map((item) => <button
          aria-pressed={category === item.id}
          className={category === item.id ? 'is-active' : ''}
          key={item.id}
          onClick={() => { setCategory(item.id); setPage(1) }}
          type="button"
        >{item.label}</button>)}
      </nav>
      <form className="filter-form browse-filters" onSubmit={applyFilters}>
        <div className="field filter-search">
          <label htmlFor="catalog-search">Search titles</label>
          <input id="catalog-search" onChange={(event) => updateFilter('search', event.target.value)} placeholder="Title or year" type="search" value={draftFilters.search} />
        </div>
        {result?.yearBounds && <YearRangeFilter
          bounds={result.yearBounds}
          id="catalog-year-range"
          label="Title year"
          onCommit={(from, to) => updateYearRange(from, to, true)}
          onPreview={(from, to) => updateYearRange(from, to, false)}
          valueFrom={draftFilters.yearFrom ? Number(draftFilters.yearFrom) : result.yearBounds.minYear}
          valueTo={draftFilters.yearTo ? Number(draftFilters.yearTo) : result.yearBounds.maxYear}
        />}
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
          <button className="button button-secondary" onClick={() => {
            setDraftFilters({ ...emptyFilters })
            setAppliedFilters({ ...emptyFilters })
            setPage(1)
            setLoading(true)
            setError(null)
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
          {awardsError && <p className="movie-awards-warning" role="status">Combined awards are temporarily unavailable.</p>}
          {viewMode === 'posters' ? <div className="poster-grid">
            {result.items.map((title) => <MoviePosterCard
              actions={<>
                <button aria-label={`Open details for ${title.title}`} className="tile-action" onClick={() => openTitle(title.id, 'details')} type="button">Details</button>
                <button aria-label={`View torrent sources for ${title.title} (${title.occurrenceCount})`} className="tile-action tile-action-primary" onClick={() => openTitle(title.id, 'torrents')} type="button">
                  Torrents <span>{title.occurrenceCount}</span>
                </button>
              </>}
              awards={combineMovieAwards([], pageAwards, title.imdbId)}
              footer={<FavoriteControls from="catalog" mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} titleId={title.id} />}
              genres={title.genres}
              imdbId={title.imdbId}
              imdbRating={title.imdbRating}
              key={title.id}
              mediaType={title.mediaType}
              onOpen={() => openTitle(title.id, 'details')}
              openLabel={`View ${title.title} details`}
              posterLabel={title.mediaType.slice(0, 3).toUpperCase()}
              posterUrl={title.posterUrl}
              title={title.title}
              year={title.year}
            >
              <div className="tile-observations">
                <span>{title.occurrenceCount} feed observations</span>
                <span>Last seen {formatDate(title.lastSeenAt)}</span>
              </div>
            </MoviePosterCard>)}
          </div> : <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>TITLE</th><th>TYPE</th><th>IMDB</th><th>AWARDS</th><th>GENRES</th><th>LAST SEEN</th><th>FAVORITES</th></tr></thead>
              <tbody>{result.items.map((title) => <CatalogRow awards={combineMovieAwards([], pageAwards, title.imdbId)} key={title.id} onSelect={setSelectedTitleId} title={title} />)}</tbody>
            </table>
          </div>}
          <Pagination onPageChange={setPage} page={result} />
        </>
      )}
      {selectedTitleId !== null && <TitleDetailsDialog initialSection={detailsSection} onClose={() => setSelectedTitleId(null)} titleId={selectedTitleId} />}
    </section>
  )
}
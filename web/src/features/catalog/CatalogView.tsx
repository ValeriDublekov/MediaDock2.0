import { useEffect, useState, type FormEvent } from 'react'
import { Download } from 'lucide-react'
import { getCatalog, getCatalogGenres } from '../../api/client'
import type { CatalogQuery, CatalogTitle, FeedType, MediaType, PageResponse } from '../../api/types'
import { CategoryMultiSelect } from '../../components/CategoryMultiSelect'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { ImdbRatingRangeFilter } from '../../components/ImdbRatingRangeFilter'
import { Pagination } from '../../components/Pagination'
import { ImdbCandidatePicker, type ImdbCandidateTarget } from '../../components/ImdbCandidatePicker'
import { YearRangeFilter } from '../../components/YearRangeFilter'
import { MovieAwardsSummary, MovieImdbLink, MoviePosterCard, MovieTableTitle } from '../../components/MoviePresentation'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { formatDate, formatWords } from '../../shared/format'
import { titlePageSize } from '../../shared/titlePagination'
import { removePageItems } from '../../shared/removePageItems'
import { combineMovieAwards, movieAwardsRequestKey } from '../../shared/movieAwards'
import { useMovieAwards } from '../../shared/useMovieAwards'
import { TitleDetailsDialog } from './TitleDetailsDialog'
import { RssFeedPreviewDialog } from './RssFeedPreviewDialog'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'
import { HistoryView } from '../history/HistoryView'

interface CatalogFilters {
  search: string
  mediaType: string
  yearFrom: string
  yearTo: string
  imdbRatingFrom: string
  imdbRatingTo: string
  genres: string[] | null
}

type CatalogCategory = 'movie' | 'series' | 'series_ongoing' | 'all'
type CatalogRatingScope = 'movie' | 'series' | 'all'
type ImdbRatingRange = Pick<CatalogFilters, 'imdbRatingFrom' | 'imdbRatingTo'>
type CatalogRatingRanges = Record<CatalogRatingScope, ImdbRatingRange>

const categoryFeedTypes: Record<Exclude<CatalogCategory, 'all'>, FeedType[]> = {
  movie: ['movie'],
  series: ['series_complete'],
  series_ongoing: ['series_ongoing'],
}

const categories: Array<{ id: CatalogCategory; label: string }> = [
  { id: 'movie', label: 'Movies' },
  { id: 'series', label: 'Series' },
  { id: 'series_ongoing', label: 'Series in progress' },
  { id: 'all', label: 'All' },
]

const emptyFilters: CatalogFilters = {
  search: '', mediaType: '', yearFrom: '', yearTo: '', imdbRatingFrom: '', imdbRatingTo: '', genres: null,
}
const emptyImdbRatingRange: ImdbRatingRange = { imdbRatingFrom: '', imdbRatingTo: '' }
const emptyImdbRatingRanges: CatalogRatingRanges = {
  movie: emptyImdbRatingRange,
  series: emptyImdbRatingRange,
  all: emptyImdbRatingRange,
}
const imdbRatingStorageKey = 'mediadock.catalog.imdbRatingRanges'
const searchDebounceMs = 350

function ratingScopeForCategory(category: CatalogCategory): CatalogRatingScope {
  if (category === 'series' || category === 'series_ongoing') return 'series'
  return category
}

function parseStoredImdbRatingRange(value: unknown): ImdbRatingRange {
  if (typeof value !== 'object' || value === null) return { ...emptyImdbRatingRange }
  const range = value as Record<string, unknown>
  const isRating = (candidate: unknown): candidate is string =>
    typeof candidate === 'string' && (candidate === '' || Number.isFinite(Number(candidate)))
  if (!isRating(range.imdbRatingFrom) || !isRating(range.imdbRatingTo)) return { ...emptyImdbRatingRange }
  return { imdbRatingFrom: range.imdbRatingFrom, imdbRatingTo: range.imdbRatingTo }
}

function readSavedImdbRatingRanges(): CatalogRatingRanges {
  if (typeof window === 'undefined') return emptyImdbRatingRanges
  try {
    const saved = window.localStorage.getItem(imdbRatingStorageKey)
    if (saved === null) return emptyImdbRatingRanges
    const parsed: unknown = JSON.parse(saved)
    if (typeof parsed !== 'object' || parsed === null) return emptyImdbRatingRanges
    const ranges = parsed as Partial<Record<CatalogRatingScope, unknown>>
    return {
      movie: parseStoredImdbRatingRange(ranges.movie),
      series: parseStoredImdbRatingRange(ranges.series),
      all: parseStoredImdbRatingRange(ranges.all),
    }
  } catch {
    return emptyImdbRatingRanges
  }
}

function saveImdbRatingRanges(ranges: CatalogRatingRanges) {
  if (typeof window === 'undefined') return
  try {
    window.localStorage.setItem(imdbRatingStorageKey, JSON.stringify(ranges))
  } catch {
    return
  }
}

function buildQuery(page: number, filters: CatalogFilters, category: CatalogCategory): CatalogQuery {
  return {
    page,
    pageSize: titlePageSize,
    ...(category !== 'all' ? { feedTypes: categoryFeedTypes[category] } : {}),
    ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
    ...(filters.mediaType ? { mediaType: filters.mediaType as MediaType } : {}),
    ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}),
    ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}),
    ...(filters.imdbRatingFrom ? { imdbRatingFrom: Number(filters.imdbRatingFrom) } : {}),
    ...(filters.imdbRatingTo ? { imdbRatingTo: Number(filters.imdbRatingTo) } : {}),
    ...(filters.genres?.length ? { genres: filters.genres } : {}),
  }
}

function CatalogRow({ title, onSelect, onRemoved, awards }: { title: CatalogTitle; onSelect: (id: number) => void; onRemoved: () => void; awards: ReturnType<typeof combineMovieAwards> }) {
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
          metadata={{ director: title.director, plot: title.plot }}
        />
      </td>
      <td><span className="type-label">{formatWords(title.mediaType)}</span>{title.contentKind && <span className="subtle-line">{formatWords(title.contentKind)}</span>}</td>
      <td><MovieImdbLink imdbId={title.imdbId} rating={title.imdbRating} title={title.title} /></td>
      <td><MovieAwardsSummary awards={awards} compact /></td>
      <td>{title.genres.slice(0, 2).join(', ') || 'Not tagged'}</td>
      <td>{formatDate(title.sourcePublishedAt ?? null)}</td>
      <td>
        <FavoriteControls from="catalog" imdbId={title.imdbId} metadataStatus={title.metadataStatus} mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} />
        <IgnoredMovieControl imdbId={title.imdbId} mediaType={title.mediaType} onIgnored={onRemoved} />
      </td>
    </tr>
  )
}

export function CatalogView({ canDeleteTitles = false, canViewHistory = false }: { canDeleteTitles?: boolean; canViewHistory?: boolean } = {}) {
  const [catalogView, setCatalogView] = useState<'titles' | 'history'>('titles')
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [category, setCategory] = useState<CatalogCategory>('movie')
  const [ratingRanges, setRatingRanges] = useState(readSavedImdbRatingRanges)
  const [page, setPage] = useState(1)
  const [draftFilters, setDraftFilters] = useState<CatalogFilters>(() => ({ ...emptyFilters, ...ratingRanges.movie }))
  const [appliedFilters, setAppliedFilters] = useState<CatalogFilters>(() => ({ ...emptyFilters, ...ratingRanges.movie }))
  const [result, setResult] = useState<PageResponse<CatalogTitle> | null>(null)
  const [genreOptions, setGenreOptions] = useState<string[]>([])
  const [genresLoading, setGenresLoading] = useState(true)
  const [genresError, setGenresError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [deleteNotice, setDeleteNotice] = useState<string | null>(null)
  const [selectedTitleId, setSelectedTitleId] = useState<number | null>(null)
  const [rssPreviewProfileId, setRssPreviewProfileId] = useState<FeedType | 'all' | null>(null)
  const [imdbMatchTarget, setImdbMatchTarget] = useState<ImdbCandidateTarget | null>(null)
  const [detailsSection, setDetailsSection] = useState<'details' | 'torrents'>('details')
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((title) => title.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)
  const activeFilterCount = Object.entries(draftFilters).filter(([key, value]) => key !== 'search' && key !== 'genres' && value !== '').length
    + (draftFilters.genres?.length ? 1 : 0)

  function removeCatalogTitle(titleId: number) {
    setResult((current) => removePageItems(current, (title) => title.id === titleId))
  }

  function openTitle(titleId: number, section: 'details' | 'torrents') {
    setDeleteNotice(null)
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
    let current = true
    getCatalogGenres()
      .then((options) => { if (current) setGenreOptions(options) })
      .catch((requestError: unknown) => {
        if (current) setGenresError(requestError instanceof Error ? requestError.message : 'Could not load genres.')
      })
      .finally(() => { if (current) setGenresLoading(false) })
    return () => { current = false }
  }, [])

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

  function updateImdbRatingRange(from: number, to: number, apply: boolean) {
    const bounds = result?.imdbRatingBounds
    if (!bounds) return
    const ratings = {
      imdbRatingFrom: from === bounds.minRating ? '' : String(from),
      imdbRatingTo: to === bounds.maxRating ? '' : String(to),
    }
    setDraftFilters((current) => ({ ...current, ...ratings }))
    if (apply) {
      const nextRanges = { ...ratingRanges, [ratingScopeForCategory(category)]: ratings }
      setRatingRanges(nextRanges)
      saveImdbRatingRanges(nextRanges)
      commitFilterChanges(ratings)
    }
  }

  function selectCategory(nextCategory: CatalogCategory) {
    const nextRatingRange = ratingRanges[ratingScopeForCategory(nextCategory)]
    setCategory(nextCategory)
    setPage(1)
    setDraftFilters((current) => ({ ...current, ...nextRatingRange }))
    setAppliedFilters((current) => ({ ...current, ...nextRatingRange }))
  }

  const showingCatalogHistory = canViewHistory && catalogView === 'history'

  return (
    <section aria-label="Catalog" className="media-view">
      <div className="page-heading">
        <div>
          <p className="eyebrow">{showingCatalogHistory ? 'CATALOG HISTORY' : 'LIBRARY'}</p>
          <h1>{showingCatalogHistory ? 'Catalog history' : 'Torents catalog'}</h1>
          <p className="page-description">{showingCatalogHistory ? 'Review parse decisions and scan activity for catalog feeds.' : 'Browse titles collected from your configured feeds.'}</p>
        </div>
      </div>
      {canViewHistory && <div aria-label="Catalog view" className="catalog-screen-switch segment-control" role="group">
        <button aria-pressed={catalogView === 'titles'} onClick={() => setCatalogView('titles')} type="button">Titles</button>
        <button aria-pressed={catalogView === 'history'} onClick={() => setCatalogView('history')} type="button">History</button>
      </div>}
      {showingCatalogHistory ? <HistoryView showCleanupControls={false} /> : <>
      {deleteNotice && <p className="form-message" role="status">{deleteNotice}</p>}
      <nav aria-label="Catalog categories" className="catalog-category-nav">
        {categories.map((item) => <button
          aria-pressed={category === item.id}
          className={category === item.id ? 'is-active' : ''}
          key={item.id}
          onClick={() => selectCategory(item.id)}
          type="button"
        >{item.label}</button>)}
      </nav>
      <form className="filter-form browse-filters" onSubmit={applyFilters}>
        <div className="field filter-search">
          <label htmlFor="catalog-search">Search titles</label>
          <input id="catalog-search" onChange={(event) => updateFilter('search', event.target.value)} placeholder="Title or year" type="search" value={draftFilters.search} />
        </div>
        <CategoryMultiSelect
          error={genresError}
          label="Genres"
          loading={genresLoading}
          onChange={(values) => updateFilter('genres', values)}
          options={genreOptions}
          optionsLabel="genres"
          selectedValues={draftFilters.genres}
        />
        {result?.yearBounds && <YearRangeFilter
          bounds={result.yearBounds}
          id="catalog-year-range"
          label="Title year"
          onCommit={(from, to) => updateYearRange(from, to, true)}
          onPreview={(from, to) => updateYearRange(from, to, false)}
          valueFrom={draftFilters.yearFrom ? Number(draftFilters.yearFrom) : result.yearBounds.minYear}
          valueTo={draftFilters.yearTo ? Number(draftFilters.yearTo) : result.yearBounds.maxYear}
        />}
        {result?.imdbRatingBounds && <ImdbRatingRangeFilter
          bounds={result.imdbRatingBounds}
          id="catalog-imdb-rating-range"
          onCommit={(from, to) => updateImdbRatingRange(from, to, true)}
          onPreview={(from, to) => updateImdbRatingRange(from, to, false)}
          valueFrom={draftFilters.imdbRatingFrom ? Number(draftFilters.imdbRatingFrom) : result.imdbRatingBounds.minRating}
          valueTo={draftFilters.imdbRatingTo ? Number(draftFilters.imdbRatingTo) : result.imdbRatingBounds.maxRating}
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
          </div>
        </details>
        <div className="filter-actions">
          <button className="button button-secondary" onClick={() => {
            const nextRanges = { ...ratingRanges, [ratingScopeForCategory(category)]: { ...emptyImdbRatingRange } }
            setRatingRanges(nextRanges)
            saveImdbRatingRanges(nextRanges)
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
          <button className="button button-secondary" onClick={() => setRssPreviewProfileId(category === 'all' ? 'all' : categoryFeedTypes[category][0])} type="button">RSS contents</button>
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
              awards={combineMovieAwards([], pageAwards, title.imdbId)}
                footer={<FavoriteControls from="catalog" imdbId={title.imdbId} metadataStatus={title.metadataStatus} mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} placement="status" />}
              genres={title.genres}
                metadata={{ director: title.director, plot: title.plot }}
              imdbId={title.imdbId}
              imdbRating={title.imdbRating}
              key={title.id}
              mediaType={title.mediaType}
              onOpen={() => openTitle(title.id, 'details')}
              openLabel={`View ${title.title} details`}
              posterLabel={title.mediaType.slice(0, 3).toUpperCase()}
              posterActions={<>
                <FavoriteControls from="catalog" imdbId={title.imdbId} metadataStatus={title.metadataStatus} mediaType={title.mediaType} occurrenceCount={title.occurrenceCount} placement="poster" />
                <IgnoredMovieControl imdbId={title.imdbId} mediaType={title.mediaType} onIgnored={() => removeCatalogTitle(title.id)} placement="poster" />
                <button
                  aria-label={`View torrent sources for ${title.title} (${title.occurrenceCount})`}
                  className="tile-icon-action torrent-icon"
                  data-tooltip={`${title.occurrenceCount} ${title.occurrenceCount === 1 ? 'torrent source' : 'torrent sources'}`}
                  onClick={() => openTitle(title.id, 'torrents')}
                  type="button"
                ><Download aria-hidden="true" size={16} /></button>
              </>}
              posterUrl={title.posterUrl}
              title={title.title}
              year={title.year}
            >
              <div className="tile-observations">
                <span>{title.occurrenceCount} feed observations</span>
                <span>RSS date {formatDate(title.sourcePublishedAt ?? null)}</span>
              </div>
            </MoviePosterCard>)}
          </div> : <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>TITLE</th><th>TYPE</th><th>IMDB</th><th>AWARDS</th><th>GENRES</th><th>RSS DATE</th><th>ACTIONS</th></tr></thead>
              <tbody>{result.items.map((title) => <CatalogRow awards={combineMovieAwards([], pageAwards, title.imdbId)} key={title.id} onRemoved={() => removeCatalogTitle(title.id)} onSelect={(id) => openTitle(id, 'details')} title={title} />)}</tbody>
            </table>
          </div>}
          <Pagination onPageChange={setPage} page={result} />
        </>
      )}
      {selectedTitleId !== null && <TitleDetailsDialog
        initialSection={detailsSection}
        onClose={() => setSelectedTitleId(null)}
        onIgnored={() => removeCatalogTitle(selectedTitleId)}
        onFindImdbMatch={(id, title, rawTitle) => {
          setSelectedTitleId(null)
          setImdbMatchTarget({ source: 'catalog', id, title, rawTitle })
        }}
        onDeleted={canDeleteTitles ? (result, deleteImdbInfo, hadImdbInfo) => {
          setSelectedTitleId(null)
          if (selectedTitleId !== null) removeCatalogTitle(selectedTitleId)
          setDeleteNotice(deleteImdbInfo
            ? result.imdbInfoDeleted
              ? 'Title and IMDb information deleted.'
              : hadImdbInfo
                ? 'Title deleted. IMDb information was kept because another record still uses it.'
                : 'Title deleted.'
            : hadImdbInfo
              ? 'Title deleted. IMDb information was kept.'
              : 'Title deleted.')
        } : undefined}
        titleId={selectedTitleId}
      />}
      {imdbMatchTarget && <ImdbCandidatePicker
        onApplied={() => {
          setImdbMatchTarget(null)
          setAttempt((current) => current + 1)
        }}
        onClose={() => setImdbMatchTarget(null)}
        target={imdbMatchTarget}
      />}
      {rssPreviewProfileId !== null && <RssFeedPreviewDialog
        onClose={() => setRssPreviewProfileId(null)}
        profileId={rssPreviewProfileId}
      />}
      </>}
    </section>
  )
}
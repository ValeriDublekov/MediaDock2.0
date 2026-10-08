import { useEffect, useState, type FormEvent } from 'react'
import { getOscarCategories, getOscarFilms } from '../../api/client'
import type {
  OscarCatalogQuery,
  OscarEnrichmentStatus,
  OscarFilm,
  PageResponse,
} from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { CategoryMultiSelect } from '../../components/CategoryMultiSelect'
import { Pagination } from '../../components/Pagination'
import { ImdbRatingRangeFilter } from '../../components/ImdbRatingRangeFilter'
import { YearRangeFilter } from '../../components/YearRangeFilter'
import { MovieAwardsSummary, MovieImdbLink, MoviePosterCard, MovieTableTitle } from '../../components/MoviePresentation'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions, movieAwardsRequestKey } from '../../shared/movieAwards'
import { useMovieAwards } from '../../shared/useMovieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { OscarFilmDetailsDialog } from './OscarFilmDetailsDialog'

interface OscarFilters {
  search: string
  yearFrom: string
  yearTo: string
  imdbRatingFrom: string
  imdbRatingTo: string
  categories: string[] | null
  result: string
  enrichmentStatus: string
}

const emptyFilters: OscarFilters = {
  search: '',
  yearFrom: '',
  yearTo: '',
  imdbRatingFrom: '',
  imdbRatingTo: '',
  categories: null,
  result: '',
  enrichmentStatus: '',
}
const searchDebounceMs = 350

function buildQuery(page: number, filters: OscarFilters): OscarCatalogQuery {
  return {
    page,
    pageSize: 20,
    ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
    ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}),
    ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}),
    ...(filters.imdbRatingFrom ? { imdbRatingFrom: Number(filters.imdbRatingFrom) } : {}),
    ...(filters.imdbRatingTo ? { imdbRatingTo: Number(filters.imdbRatingTo) } : {}),
    ...(filters.categories !== null ? { categories: filters.categories, categoryFilter: true } : {}),
    ...(filters.result ? { result: filters.result as OscarCatalogQuery['result'] } : {}),
    ...(filters.enrichmentStatus
      ? { enrichmentStatus: filters.enrichmentStatus as OscarEnrichmentStatus }
      : {}),
  }
}

function OscarRow({ film, onSelect, awards }: { film: OscarFilm; onSelect: (id: number) => void; awards: ReturnType<typeof combineMovieAwards> }) {
  const categories = [...new Set(film.nominations.map((nomination) => nomination.category))]
  const winCount = film.nominations.filter((nomination) => nomination.isWinner).length
  const nominationCount = film.nominations.length

  return (
    <tr>
      <td>
        <MovieTableTitle
          onOpen={() => onSelect(film.id)}
          openLabel={`View ${film.title} Oscar details`}
          posterLabel="OSC"
          posterUrl={film.posterUrl}
          subtitle={film.filmYear}
          title={film.title}
        >
            <span className="subtle-line">Director: {film.director ?? 'Not listed'}</span>
            <span className="subtle-line">{film.genres.join(' · ') || 'Genres unavailable'}</span>
            {film.plot && <span className="oscar-row-plot">{film.plot}</span>}
        </MovieTableTitle>
      </td>
      <td>
        <span className="oscar-categories">
          {categories.slice(0, 2).join(', ') || 'No categories listed'}
        </span>
        {categories.length > 2 && <span className="subtle-line">+{categories.length - 2} more</span>}
      </td>
      <td>
        <span className={`state-pill ${winCount > 0 ? 'is-winner' : 'is-muted'}`}>
          {winCount > 0 ? 'Winner' : 'Nominee'}
        </span>
        <span className="subtle-line">
          {winCount > 0 && `${winCount} ${winCount === 1 ? 'win' : 'wins'} · `}
          {nominationCount} {nominationCount === 1 ? 'nomination' : 'nominations'}
        </span>
      </td>
      <td>
        <MovieImdbLink imdbId={film.imdbId} rating={film.imdbRating} title={film.title} />
        {film.imdbVotes !== null && <span className="subtle-line">{film.imdbVotes.toLocaleString()} votes</span>}
      </td>
      <td><MovieAwardsSummary awards={awards} compact /></td>
      <td>
        <span className={`state-pill is-${film.enrichmentStatus.replaceAll('_', '-')}`}>
          {formatWords(film.enrichmentStatus)}
        </span>
        <span className="subtle-line">
          {film.enrichmentAttemptCount} {film.enrichmentAttemptCount === 1 ? 'attempt' : 'attempts'}
        </span>
      </td>
      <td><FavoriteControls from="oscar" mediaType={film.mediaType} occurrenceCount={0} titleId={film.titleId} /></td>
    </tr>
  )
}

export function OscarCatalogView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [page, setPage] = useState(1)
  const [draftFilters, setDraftFilters] = useState<OscarFilters>(emptyFilters)
  const [appliedFilters, setAppliedFilters] = useState<OscarFilters>(emptyFilters)
  const [categoryOptions, setCategoryOptions] = useState<string[]>([])
  const [categoryOptionsLoading, setCategoryOptionsLoading] = useState(true)
  const [categoryOptionsError, setCategoryOptionsError] = useState<string | null>(null)
  const [result, setResult] = useState<PageResponse<OscarFilm> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedFilmId, setSelectedFilmId] = useState<number | null>(null)
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((film) => film.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)
  const activeFilterCount = Object.entries(draftFilters)
    .filter(([key, value]) => key !== 'search' && key !== 'categories' && value !== '')
    .length + (draftFilters.categories === null ? 0 : 1)

  useEffect(() => {
    let current = true
    getOscarCategories()
      .then((categories) => { if (current) setCategoryOptions(categories) })
      .catch((requestError: unknown) => {
        if (current) setCategoryOptionsError(requestError instanceof Error ? requestError.message : 'The Oscar categories request failed.')
      })
      .finally(() => { if (current) setCategoryOptionsLoading(false) })
    return () => { current = false }
  }, [])

  useEffect(() => {
    let current = true
    getOscarFilms(buildQuery(page, appliedFilters))
      .then((response) => { if (current) setResult(response) })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The Oscar catalog request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [page, appliedFilters, attempt])

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

  function updateFilter<K extends keyof OscarFilters>(key: K, value: OscarFilters[K]) {
    setDraftFilters((current) => ({ ...current, [key]: value }))
    if (key !== 'search') commitFilterChanges({ [key]: value } as Partial<OscarFilters>)
  }

  function commitFilterChanges(filters: Partial<OscarFilters>) {
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
    if (apply) commitFilterChanges(ratings)
  }

  function applyCategories(categories: string[] | null) {
    const nextFilters = { ...draftFilters, categories }
    setDraftFilters(nextFilters)
    commitFilterChanges({ categories })
  }

  function clearFilters() {
    setLoading(true)
    setError(null)
    setDraftFilters({ ...emptyFilters })
    setAppliedFilters({ ...emptyFilters })
    setPage(1)
  }

  function changePage(nextPage: number) {
    setLoading(true)
    setError(null)
    setPage(nextPage)
  }

  function refresh() {
    setLoading(true)
    setError(null)
    setAttempt((current) => current + 1)
  }

  return (
    <section aria-label="Oscar catalog" className="media-view">
      <form className="filter-form oscar-filter-form browse-filters" onSubmit={applyFilters}>
        <div className="field filter-search">
          <label htmlFor="oscar-search">Search films</label>
          <input
            id="oscar-search"
            onChange={(event) => updateFilter('search', event.target.value)}
            placeholder="Film title"
            type="search"
            value={draftFilters.search}
          />
        </div>
        {result?.yearBounds && <YearRangeFilter
          bounds={result.yearBounds}
          id="oscar-year-range"
          label="Film year"
          onCommit={(from, to) => updateYearRange(from, to, true)}
          onPreview={(from, to) => updateYearRange(from, to, false)}
          valueFrom={draftFilters.yearFrom ? Number(draftFilters.yearFrom) : result.yearBounds.minYear}
          valueTo={draftFilters.yearTo ? Number(draftFilters.yearTo) : result.yearBounds.maxYear}
        />}
        {result?.imdbRatingBounds && <ImdbRatingRangeFilter
          bounds={result.imdbRatingBounds}
          id="oscar-imdb-rating-range"
          onCommit={(from, to) => updateImdbRatingRange(from, to, true)}
          onPreview={(from, to) => updateImdbRatingRange(from, to, false)}
          valueFrom={draftFilters.imdbRatingFrom ? Number(draftFilters.imdbRatingFrom) : result.imdbRatingBounds.minRating}
          valueTo={draftFilters.imdbRatingTo ? Number(draftFilters.imdbRatingTo) : result.imdbRatingBounds.maxRating}
        />}
        <details className="advanced-filters">
          <summary>More filters{activeFilterCount > 0 ? ` (${activeFilterCount} active)` : ''}</summary>
          <div className="advanced-fields">
            <CategoryMultiSelect
              error={categoryOptionsError}
              label="Categories"
              loading={categoryOptionsLoading}
              onChange={applyCategories}
              options={categoryOptions}
              selectedValues={draftFilters.categories}
            />
            <div className="field">
              <label htmlFor="oscar-result">Award result</label>
              <select id="oscar-result" onChange={(event) => updateFilter('result', event.target.value)} value={draftFilters.result}>
                <option value="">All results</option>
                <option value="winner">Winners</option>
                <option value="nominee">Nominees</option>
              </select>
            </div>
            <div className="field">
              <label htmlFor="oscar-enrichment-status">OMDb status</label>
              <select id="oscar-enrichment-status" onChange={(event) => updateFilter('enrichmentStatus', event.target.value)} value={draftFilters.enrichmentStatus}>
                <option value="">All statuses</option>
                <option value="pending">Pending</option>
                <option value="enriched">Enriched</option>
                <option value="not_found">Not found</option>
                <option value="temporary_error">Temporary error</option>
              </select>
            </div>
          </div>
        </details>
        <div className="filter-actions">
          <button className="button button-secondary" onClick={clearFilters} type="button">Clear</button>
        </div>
      </form>

      <div className="section-toolbar">
        <span className="result-count">
          {result ? `${result.totalCount.toLocaleString()} Oscar films` : 'Oscar records'}
        </span>
        <div className="toolbar-actions">
          <ViewModeControl onChange={setViewMode} value={viewMode} />
          <button className="text-button" onClick={refresh} type="button">Refresh results</button>
        </div>
      </div>

      {loading && <LoadingState label="Loading Oscar catalog" />}
      {!loading && error && <ErrorState message={error} onRetry={refresh} />}
      {!loading && !error && result && result.items.length === 0 && (
        <EmptyState title="No Oscar films found" message="Try changing the search or filters, or clear them to browse all imported films." />
      )}
      {!loading && !error && result && result.items.length > 0 && (
        <>
          {awardsError && <p className="movie-awards-warning" role="status">Combined awards are temporarily unavailable.</p>}
          {viewMode === 'posters' ? <div className="poster-grid">
            {result.items.map((film) => {
              return <MoviePosterCard
                awards={combineMovieAwards(getOscarRecognitions(film), pageAwards, film.imdbId)}
                footer={<FavoriteControls from="oscar" mediaType={film.mediaType} occurrenceCount={0} titleId={film.titleId} />}
                genres={film.genres}
                imdbId={film.imdbId}
                imdbRating={film.imdbRating}
                key={film.id}
                mediaType={film.mediaType}
                onOpen={() => setSelectedFilmId(film.id)}
                openLabel={`View ${film.title} Oscar details`}
                posterLabel="OSC"
                posterUrl={film.posterUrl}
                title={film.title}
                year={film.filmYear}
              >
                <div className="oscar-tile-director">Director: {film.director ?? 'Not listed'}</div>
                {film.plot && <p className="oscar-tile-plot">{film.plot}</p>}
                <span className={`state-pill is-${film.enrichmentStatus.replaceAll('_', '-')}`}>{formatWords(film.enrichmentStatus)}</span>
              </MoviePosterCard>
            })}
          </div> : <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th>FILM</th><th>CATEGORIES</th><th>RESULT</th><th>IMDB</th><th>AWARDS</th><th>OMDB</th><th>FAVORITES</th></tr></thead>
              <tbody>
                {result.items.map((film) => (
                  <OscarRow awards={combineMovieAwards(getOscarRecognitions(film), pageAwards, film.imdbId)} film={film} key={film.id} onSelect={setSelectedFilmId} />
                ))}
              </tbody>
            </table>
          </div>}
          <Pagination onPageChange={changePage} page={result} />
        </>
      )}
      {selectedFilmId !== null && (
        <OscarFilmDetailsDialog filmId={selectedFilmId} onClose={() => setSelectedFilmId(null)} />
      )}
    </section>
  )
}
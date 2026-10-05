import { useEffect, useState, type FormEvent } from 'react'
import { getOscarFilms } from '../../api/client'
import type {
  OscarCatalogQuery,
  OscarEnrichmentStatus,
  OscarFilm,
  PageResponse,
} from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { Poster } from '../../components/Poster'
import { MovieAwardsSummary, MovieImdbLink, MoviePosterCard } from '../../components/MoviePresentation'
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
  category: string
  result: string
  enrichmentStatus: string
}

const emptyFilters: OscarFilters = {
  search: '',
  yearFrom: '',
  yearTo: '',
  category: '',
  result: '',
  enrichmentStatus: '',
}

function buildQuery(page: number, filters: OscarFilters): OscarCatalogQuery {
  return {
    page,
    pageSize: 20,
    ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
    ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}),
    ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}),
    ...(filters.category ? { category: filters.category } : {}),
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
        <div className="title-cell">
          <Poster className="poster-small" label="OSC" src={film.posterUrl} title={film.title} />
          <span className="oscar-row-details">
            <button
              aria-label={`View ${film.title} Oscar details`}
              className="title-link"
              onClick={() => onSelect(film.id)}
              type="button"
            >
              {film.title}
            </button>
            <span className="subtle-line">
              {film.filmYear}
            </span>
            <span className="subtle-line">Director: {film.director ?? 'Not listed'}</span>
            <span className="subtle-line">{film.genres.join(' · ') || 'Genres unavailable'}</span>
            {film.plot && <span className="oscar-row-plot">{film.plot}</span>}
          </span>
        </div>
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
  const [result, setResult] = useState<PageResponse<OscarFilm> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedFilmId, setSelectedFilmId] = useState<number | null>(null)
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((film) => film.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)
  const activeFilterCount = Object.entries(draftFilters).filter(([key, value]) => key !== 'search' && value !== '').length

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

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoading(true)
    setError(null)
    setPage(1)
    setAppliedFilters({ ...draftFilters })
  }

  function updateFilter<K extends keyof OscarFilters>(key: K, value: OscarFilters[K]) {
    setDraftFilters((current) => ({ ...current, [key]: value }))
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
        <details className="advanced-filters">
          <summary>More filters{activeFilterCount > 0 ? ` (${activeFilterCount} active)` : ''}</summary>
          <div className="advanced-fields">
            <div className="field">
              <label htmlFor="oscar-year-from">Year from</label>
              <input id="oscar-year-from" max="2200" min="1800" onChange={(event) => updateFilter('yearFrom', event.target.value)} type="number" value={draftFilters.yearFrom} />
            </div>
            <div className="field">
              <label htmlFor="oscar-year-to">Year to</label>
              <input id="oscar-year-to" max="2200" min="1800" onChange={(event) => updateFilter('yearTo', event.target.value)} type="number" value={draftFilters.yearTo} />
            </div>
            <div className="field">
              <label htmlFor="oscar-category">Category</label>
              <select id="oscar-category" onChange={(event) => updateFilter('category', event.target.value)} value={draftFilters.category}>
                <option value="">All categories</option>
                <option value="BEST PICTURE">Best picture</option>
                <option value="DIRECTING">Directing</option>
                <option value="WRITING (Original Screenplay)">Original screenplay</option>
                <option value="WRITING (Adapted Screenplay)">Adapted screenplay</option>
                <option value="CINEMATOGRAPHY">Cinematography</option>
              </select>
            </div>
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
          <button className="button" type="submit">Apply filters</button>
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
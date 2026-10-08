import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { getGoldenGlobeCategories, getGoldenGlobeFilms } from '../../api/client'
import type { GoldenGlobeCatalogQuery, GoldenGlobeEnrichmentStatus, GoldenGlobeFilm, PageResponse } from '../../api/types'
import { CategoryMultiSelect } from '../../components/CategoryMultiSelect'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { YearRangeFilter } from '../../components/YearRangeFilter'
import { MovieAwardsSummary, MovieImdbLink, MoviePosterCard, MovieTableTitle } from '../../components/MoviePresentation'
import { Pagination } from '../../components/Pagination'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { formatWords } from '../../shared/format'
import { combineMovieAwards, getGoldenGlobeRecognitions, movieAwardsRequestKey } from '../../shared/movieAwards'
import { useMovieAwards } from '../../shared/useMovieAwards'
import { GoldenGlobeFilmDetailsDialog } from './GoldenGlobeFilmDetailsDialog'

interface Filters { search: string; yearFrom: string; yearTo: string; categories: string[] | null; result: string; enrichmentStatus: string }
const emptyFilters: Filters = { search: '', yearFrom: '', yearTo: '', categories: null, result: '', enrichmentStatus: '' }
const searchDebounceMs = 350

function queryFor(page: number, filters: Filters): GoldenGlobeCatalogQuery {
  return { page, pageSize: 20, ...(filters.search.trim() ? { search: filters.search.trim() } : {}), ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}), ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}), ...(filters.categories !== null ? { categories: filters.categories, categoryFilter: true } : {}), ...(filters.result ? { result: filters.result as GoldenGlobeCatalogQuery['result'] } : {}), ...(filters.enrichmentStatus ? { enrichmentStatus: filters.enrichmentStatus as GoldenGlobeEnrichmentStatus } : {}) }
}

function describeEnrichmentError(error: string, ceremonyYear: number) {
  const mismatch = /^year_mismatch:(\d{4})$/.exec(error)
  if (mismatch) return `OMDb matched a film from ${mismatch[1]}; this ceremony accepts films from ${ceremonyYear - 1} or ${ceremonyYear}.`

  const descriptions: Record<string, string> = {
    not_found: 'OMDb did not find a matching title.',
    no_confident_match: 'OMDb Search returned candidates, but none could be verified safely. Check the title, type, and year, or set the IMDb ID manually.',
    no_golden_globe_match: 'The closest title did not list a Golden Globe in OMDb Awards. Verify the match or set the IMDb ID manually.',
    ambiguous_match: 'Several close titles passed the Golden Globe check, so no IMDb ID was selected. Set the correct IMDb ID manually.',
    candidate_mismatch: 'The detailed IMDb lookup did not match the Search result. Set the correct IMDb ID manually.',
    missing_imdb_id: 'OMDb matched a title but returned no IMDb ID.',
    missing_year: 'OMDb matched a film but returned no release year.',
    type_mismatch: 'OMDb returned a title with a different media type.',
    invalid_imdb_id: 'OMDb returned an invalid IMDb ID.',
    timeout: 'The OMDb request timed out.',
    transport_error: 'MediaDock could not connect to OMDb.',
    http_error: 'OMDb returned an HTTP error.',
    authentication_failed: 'OMDb rejected the configured API key.',
    quota_exceeded: 'The OMDb provider quota has been exceeded.',
    invalid_response: 'OMDb returned an invalid response.',
    response_too_large: 'The OMDb response exceeded the configured size limit.',
    invalid_metadata: 'OMDb returned incomplete title metadata.',
    imdb_id_mismatch: 'The returned IMDb ID did not match the requested title.',
  }
  return descriptions[error] ?? formatWords(error).replace(':', ': ')
}

function EnrichmentStatus({ film }: { film: GoldenGlobeFilm }) {
  return <>
    <span className={`state-pill is-${film.enrichmentStatus.replaceAll('_', '-')}`}>{formatWords(film.enrichmentStatus)}</span>
    {film.enrichmentError && <div className="golden-globe-enrichment-error">
      <span><strong>Enrichment issue:</strong> {describeEnrichmentError(film.enrichmentError, film.year)}</span>
      <code>{film.enrichmentError}</code>
    </div>}
  </>
}

export function GoldenGlobeCatalogView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [page, setPage] = useState(1)
  const [draft, setDraft] = useState<Filters>(emptyFilters)
  const [applied, setApplied] = useState<Filters>(emptyFilters)
  const [categoryOptions, setCategoryOptions] = useState<string[]>([])
  const [categoryOptionsLoading, setCategoryOptionsLoading] = useState(true)
  const [categoryOptionsError, setCategoryOptionsError] = useState<string | null>(null)
  const [result, setResult] = useState<PageResponse<GoldenGlobeFilm> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedFilm, setSelectedFilm] = useState<GoldenGlobeFilm | null>(null)
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((film) => film.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)

  useEffect(() => {
    let current = true
    getGoldenGlobeCategories()
      .then((categories) => { if (current) setCategoryOptions(categories) })
      .catch((requestError: unknown) => {
        if (current) setCategoryOptionsError(requestError instanceof Error ? requestError.message : 'The Golden Globes categories request failed.')
      })
      .finally(() => { if (current) setCategoryOptionsLoading(false) })
    return () => { current = false }
  }, [])

  useEffect(() => {
    let current = true
    getGoldenGlobeFilms(queryFor(page, applied)).then((response) => { if (current) setResult(response) }).catch((requestError: unknown) => {
      if (current) setError(requestError instanceof Error ? requestError.message : 'The Golden Globes catalog request failed.')
    }).finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [page, applied, attempt])

  useEffect(() => {
    if (draft.search === applied.search) return
    const timeoutId = window.setTimeout(() => {
      setLoading(true)
      setError(null)
      setPage(1)
      setApplied((current) => ({ ...current, search: draft.search }))
    }, searchDebounceMs)
    return () => window.clearTimeout(timeoutId)
  }, [draft.search, applied.search])

  function apply(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setLoading(true); setError(null); setPage(1); setApplied({ ...draft }) }
  function clear() { setLoading(true); setError(null); setDraft(emptyFilters); setApplied(emptyFilters); setPage(1) }
  function applyCategories(categories: string[] | null) {
    const nextFilters = { ...draft, categories }
    setDraft(nextFilters)
    commitFilterChanges({ categories })
  }
  function refresh() { setLoading(true); setError(null); setAttempt((value) => value + 1) }
  const refreshFilm = useCallback(async (filmId: string) => {
    const response = await getGoldenGlobeFilms(queryFor(page, applied))
    setResult(response)
    return response.items.find((item) => item.filmId === filmId)
  }, [applied, page])
  function update(key: keyof Filters, value: string) {
    setDraft((current) => ({ ...current, [key]: value }))
    if (key !== 'search') commitFilterChanges({ [key]: value })
  }
  function commitFilterChanges(filters: Partial<Filters>) {
    setLoading(true)
    setError(null)
    setPage(1)
    setApplied((current) => ({ ...current, ...filters }))
  }
  function updateYearRange(from: number, to: number, applyRange: boolean) {
    const bounds = result?.yearBounds
    if (!bounds) return
    const years = {
      yearFrom: from === bounds.minYear ? '' : String(from),
      yearTo: to === bounds.maxYear ? '' : String(to),
    }
    setDraft((current) => ({ ...current, ...years }))
    if (applyRange) commitFilterChanges(years)
  }

  return <section aria-label="Golden Globes catalog" className="media-view">
    <form className="filter-form oscar-filter-form browse-filters" onSubmit={apply}>
      <div className="field filter-search"><label htmlFor="golden-globe-search">Search films</label><input id="golden-globe-search" onChange={(event) => update('search', event.target.value)} placeholder="Film title" type="search" value={draft.search} /></div>
      {result?.yearBounds && <YearRangeFilter
        bounds={result.yearBounds}
        id="golden-globe-year-range"
        label="Ceremony year"
        onCommit={(from, to) => updateYearRange(from, to, true)}
        onPreview={(from, to) => updateYearRange(from, to, false)}
        valueFrom={draft.yearFrom ? Number(draft.yearFrom) : result.yearBounds.minYear}
        valueTo={draft.yearTo ? Number(draft.yearTo) : result.yearBounds.maxYear}
      />}
      <details className="advanced-filters"><summary>More filters</summary><div className="advanced-fields">
        <CategoryMultiSelect error={categoryOptionsError} label="Categories" loading={categoryOptionsLoading} onChange={applyCategories} options={categoryOptions} selectedValues={draft.categories} />
        <div className="field"><label htmlFor="golden-globe-result">Award result</label><select id="golden-globe-result" onChange={(event) => update('result', event.target.value)} value={draft.result}><option value="">All results</option><option value="winner">Winners</option><option value="nominee">Nominees</option></select></div>
        <div className="field"><label htmlFor="golden-globe-enrichment-status">OMDb status</label><select id="golden-globe-enrichment-status" onChange={(event) => update('enrichmentStatus', event.target.value)} value={draft.enrichmentStatus}><option value="">All statuses</option><option value="pending">Pending</option><option value="enriched">Enriched</option><option value="problem">Problem</option><option value="not_found">Not found</option><option value="temporary_error">Temporary error</option></select></div>
      </div></details>
      <div className="filter-actions"><button className="button button-secondary" onClick={clear} type="button">Clear</button></div>
    </form>
    <div className="section-toolbar"><span className="result-count">{result ? `${result.totalCount.toLocaleString()} Golden Globes films` : 'Golden Globes records'}</span><div className="toolbar-actions"><ViewModeControl onChange={setViewMode} value={viewMode} /><button className="text-button" onClick={refresh} type="button">Refresh results</button></div></div>
    {loading && <LoadingState label="Loading Golden Globes catalog" />}
    {!loading && error && <ErrorState message={error} onRetry={refresh} />}
    {!loading && !error && result && result.items.length === 0 && <EmptyState title="No Golden Globes films found" message="Try changing the search or filters, or import the Golden Globes dataset." />}
    {!loading && !error && result && result.items.length > 0 && <>
      {awardsError && <p className="movie-awards-warning" role="status">Combined awards are temporarily unavailable.</p>}
      {viewMode === 'posters' ? <div className="poster-grid">{result.items.map((film) => <MoviePosterCard
        awards={combineMovieAwards(getGoldenGlobeRecognitions(film), pageAwards, film.imdbId)}
        imdbId={film.imdbId}
        imdbRating={film.imdbRating}
        key={film.filmId}
        mediaType={null}
        onOpen={() => setSelectedFilm(film)}
        posterLabel="GLO"
        posterUrl={film.posterUrl}
        title={film.title}
        year={film.year}
        yearLabel="Ceremony year"
      >
        <span className="state-pill is-muted">{formatWords(film.nomineeType)}</span>
        <EnrichmentStatus film={film} />
      </MoviePosterCard>)}</div> : <div className="table-wrap"><table className="data-table"><thead><tr><th>FILM</th><th>IMDB</th><th>AWARDS</th><th>RESULT</th><th>OMDb status</th></tr></thead><tbody>{result.items.map((film) => { const wins = film.nominations.filter((nomination) => nomination.isWinner).length; const awards = combineMovieAwards(getGoldenGlobeRecognitions(film), pageAwards, film.imdbId); return <tr key={film.filmId}><td><MovieTableTitle onOpen={() => setSelectedFilm(film)} openLabel={`View ${film.title} Golden Globes details`} posterLabel="GLO" posterUrl={film.posterUrl} subtitle={`Ceremony year ${film.year} - ${formatWords(film.nomineeType)}`} title={film.title} /></td><td><MovieImdbLink imdbId={film.imdbId} rating={film.imdbRating} title={film.title} /></td><td><MovieAwardsSummary awards={awards} compact /></td><td><span className={`state-pill ${wins > 0 ? 'is-winner' : 'is-muted'}`}>{wins > 0 ? 'Winner' : 'Nominee'}</span></td><td><EnrichmentStatus film={film} /></td></tr> })}</tbody></table></div>}
      <Pagination onPageChange={(nextPage) => { setLoading(true); setError(null); setPage(nextPage) }} page={result} />
    </>}
    {selectedFilm && <GoldenGlobeFilmDetailsDialog
      awards={combineMovieAwards(getGoldenGlobeRecognitions(selectedFilm), pageAwards, selectedFilm.imdbId)}
      film={selectedFilm}
      onClose={() => setSelectedFilm(null)}
      onUpdated={refreshFilm}
    />}
  </section>
}

import { useEffect, useState, type FormEvent } from 'react'
import { getGoldenGlobeFilms } from '../../api/client'
import type { GoldenGlobeCatalogQuery, GoldenGlobeEnrichmentStatus, GoldenGlobeFilm, PageResponse } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { Poster } from '../../components/Poster'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { formatWords } from '../../shared/format'

interface Filters { search: string; yearFrom: string; yearTo: string; award: string; result: string; enrichmentStatus: string }
const emptyFilters: Filters = { search: '', yearFrom: '', yearTo: '', award: '', result: '', enrichmentStatus: '' }

function queryFor(page: number, filters: Filters): GoldenGlobeCatalogQuery {
  return { page, pageSize: 20, ...(filters.search.trim() ? { search: filters.search.trim() } : {}), ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}), ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}), ...(filters.award ? { award: filters.award } : {}), ...(filters.result ? { result: filters.result as GoldenGlobeCatalogQuery['result'] } : {}), ...(filters.enrichmentStatus ? { enrichmentStatus: filters.enrichmentStatus as GoldenGlobeEnrichmentStatus } : {}) }
}

function AwardSummary({ film }: { film: GoldenGlobeFilm }) {
  const wins = film.nominations.filter((nomination) => nomination.isWinner).length
  const awards = [...new Set(film.nominations.map((nomination) => nomination.award))]
  return <>
    <span className={`winner-badge${wins === 0 ? ' nominee-badge' : ''}`}>
      {wins > 0 ? `Winner · ${wins} ${wins === 1 ? 'win' : 'wins'}` : `Nominee · ${film.nominations.length} ${film.nominations.length === 1 ? 'nomination' : 'nominations'}`}
    </span>
    <div className="tile-genres">{awards.slice(0, 2).join(' · ')}{awards.length > 2 ? ` · +${awards.length - 2}` : ''}</div>
    <EnrichmentStatus film={film} />
  </>
}

function describeEnrichmentError(error: string, ceremonyYear: number) {
  const mismatch = /^year_mismatch:(\d{4})$/.exec(error)
  if (mismatch) return `OMDb matched a film from ${mismatch[1]}; this ceremony accepts films from ${ceremonyYear - 1} or ${ceremonyYear}.`

  const descriptions: Record<string, string> = {
    not_found: 'OMDb did not find a matching title.',
    missing_imdb_id: 'OMDb matched a title but returned no IMDb ID.',
    missing_year: 'OMDb matched a film but returned no release year.',
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
    {film.enrichmentError && <span className="golden-globe-enrichment-error">{describeEnrichmentError(film.enrichmentError, film.year)}</span>}
  </>
}

export function GoldenGlobeCatalogView() {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [page, setPage] = useState(1)
  const [draft, setDraft] = useState<Filters>(emptyFilters)
  const [applied, setApplied] = useState<Filters>(emptyFilters)
  const [result, setResult] = useState<PageResponse<GoldenGlobeFilm> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let current = true
    getGoldenGlobeFilms(queryFor(page, applied)).then((response) => { if (current) setResult(response) }).catch((requestError: unknown) => {
      if (current) setError(requestError instanceof Error ? requestError.message : 'The Golden Globes catalog request failed.')
    }).finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [page, applied, attempt])

  function apply(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setLoading(true); setError(null); setPage(1); setApplied({ ...draft }) }
  function clear() { setLoading(true); setError(null); setDraft(emptyFilters); setApplied(emptyFilters); setPage(1) }
  function refresh() { setLoading(true); setError(null); setAttempt((value) => value + 1) }
  function update(key: keyof Filters, value: string) { setDraft((current) => ({ ...current, [key]: value })) }

  return <section aria-label="Golden Globes catalog" className="media-view">
    <form className="filter-form oscar-filter-form browse-filters" onSubmit={apply}>
      <div className="field filter-search"><label htmlFor="golden-globe-search">Search films</label><input id="golden-globe-search" onChange={(event) => update('search', event.target.value)} placeholder="Film title" type="search" value={draft.search} /></div>
      <details className="advanced-filters"><summary>More filters</summary><div className="advanced-fields">
        <div className="field"><label htmlFor="golden-globe-year-from">Year from</label><input id="golden-globe-year-from" max="2200" min="1800" onChange={(event) => update('yearFrom', event.target.value)} type="number" value={draft.yearFrom} /></div>
        <div className="field"><label htmlFor="golden-globe-year-to">Year to</label><input id="golden-globe-year-to" max="2200" min="1800" onChange={(event) => update('yearTo', event.target.value)} type="number" value={draft.yearTo} /></div>
        <div className="field"><label htmlFor="golden-globe-award">Award</label><input id="golden-globe-award" onChange={(event) => update('award', event.target.value)} placeholder="Award name" value={draft.award} /></div>
        <div className="field"><label htmlFor="golden-globe-result">Award result</label><select id="golden-globe-result" onChange={(event) => update('result', event.target.value)} value={draft.result}><option value="">All results</option><option value="winner">Winners</option><option value="nominee">Nominees</option></select></div>
        <div className="field"><label htmlFor="golden-globe-enrichment-status">OMDb status</label><select id="golden-globe-enrichment-status" onChange={(event) => update('enrichmentStatus', event.target.value)} value={draft.enrichmentStatus}><option value="">All statuses</option><option value="pending">Pending</option><option value="enriched">Enriched</option><option value="problem">Problem</option><option value="not_found">Not found</option><option value="temporary_error">Temporary error</option></select></div>
      </div></details>
      <div className="filter-actions"><button className="button" type="submit">Apply filters</button><button className="button button-secondary" onClick={clear} type="button">Clear</button></div>
    </form>
    <div className="section-toolbar"><span className="result-count">{result ? `${result.totalCount.toLocaleString()} Golden Globes films` : 'Golden Globes records'}</span><div className="toolbar-actions"><ViewModeControl onChange={setViewMode} value={viewMode} /><button className="text-button" onClick={refresh} type="button">Refresh results</button></div></div>
    {loading && <LoadingState label="Loading Golden Globes catalog" />}
    {!loading && error && <ErrorState message={error} onRetry={refresh} />}
    {!loading && !error && result && result.items.length === 0 && <EmptyState title="No Golden Globes films found" message="Try changing the search or filters, or import the Golden Globes dataset." />}
    {!loading && !error && result && result.items.length > 0 && <>
      {viewMode === 'posters' ? <div className="poster-grid">{result.items.map((film) => <article className="movie-tile" key={film.id}><Poster label="GLO" src={film.posterUrl} title={film.title} /><div className="movie-tile-info"><button className="tile-title" type="button">{film.title}</button><div className="tile-meta"><span>{film.year}</span><span aria-hidden="true">·</span><span>{film.nominations.length} {film.nominations.length === 1 ? 'nomination' : 'nominations'}</span></div><AwardSummary film={film} /></div></article>)}</div> : <div className="table-wrap"><table className="data-table"><thead><tr><th>FILM</th><th>AWARDS</th><th>RESULT</th><th>OMDb status</th></tr></thead><tbody>{result.items.map((film) => { const wins = film.nominations.filter((nomination) => nomination.isWinner).length; return <tr key={film.id}><td><div className="title-cell"><Poster className="poster-small" label="GLO" src={film.posterUrl} title={film.title} /><span><span className="title-link">{film.title}</span><span className="subtle-line">{film.year}</span></span></div></td><td>{[...new Set(film.nominations.map((nomination) => nomination.award))].join(', ')}</td><td><span className={`state-pill ${wins > 0 ? 'is-winner' : 'is-muted'}`}>{wins > 0 ? 'Winner' : 'Nominee'}</span></td><td><EnrichmentStatus film={film} /></td></tr> })}</tbody></table></div>}
      <Pagination onPageChange={(nextPage) => { setLoading(true); setError(null); setPage(nextPage) }} page={result} />
    </>}
  </section>
}

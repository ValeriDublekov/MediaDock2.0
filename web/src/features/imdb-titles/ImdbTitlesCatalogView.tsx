import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Pencil, RotateCcw, Trash2 } from 'lucide-react'
import { addPersonalRating, getImdbTitles } from '../../api/client'
import type { ImdbTitleCatalogItem, ImdbTitlesPage, ImdbTitlesQuery } from '../../api/types'
import { CategoryMultiSelect } from '../../components/CategoryMultiSelect'
import { DualRangeFilter } from '../../components/DualRangeFilter'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { ImdbRatingRangeFilter } from '../../components/ImdbRatingRangeFilter'
import { MovieImdbLink, MoviePosterCard, MovieTableTitle } from '../../components/MoviePresentation'
import { Pagination } from '../../components/Pagination'
import { ViewModeControl, type ViewMode } from '../../components/ViewModeControl'
import { YearRangeFilter } from '../../components/YearRangeFilter'
import { formatWords } from '../../shared/format'
import { combineMovieAwards, movieAwardsRequestKey } from '../../shared/movieAwards'
import { titlePageSize } from '../../shared/titlePagination'
import { useMovieAwards } from '../../shared/useMovieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'
import { ImdbTitleDetailsDialog } from './ImdbTitleDetailsDialog'

interface Filters {
  search: string
  mediaType: string
  genres: string[] | null
  countries: string[]
  countryFilter: boolean
  yearFrom: string
  yearTo: string
  imdbRatingFrom: string
  imdbRatingTo: string
  metadataStatus: string
  sort: 'year' | 'rating' | 'votes'
  showIgnored: boolean
  showWatched: boolean
}

const emptyFilters: Filters = {
  search: '',
  mediaType: '',
  genres: null,
  countries: [],
  countryFilter: false,
  yearFrom: '',
  yearTo: '',
  imdbRatingFrom: '',
  imdbRatingTo: '',
  metadataStatus: '',
  sort: 'year',
  showIgnored: true,
  showWatched: true,
}
const untypedMediaType = '__untagged__'
const countryOptions = ['USA']
const compactVoteCount = new Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 0 })
const searchDebounceMs = 350

function queryFor(page: number, filters: Filters): ImdbTitlesQuery {
  return {
    page,
    pageSize: titlePageSize,
    ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
    ...(filters.mediaType === untypedMediaType
      ? { mediaTypeIsNull: true }
      : filters.mediaType ? { mediaType: filters.mediaType } : {}),
    ...(filters.genres !== null ? { genres: filters.genres } : {}),
    genreFilter: filters.genres !== null,
    ...(filters.countryFilter ? { countries: filters.countries } : {}),
    countryFilter: filters.countryFilter,
    ...(filters.yearFrom ? { yearFrom: Number(filters.yearFrom) } : {}),
    ...(filters.yearTo ? { yearTo: Number(filters.yearTo) } : {}),
    ...(filters.imdbRatingFrom ? { imdbRatingFrom: Number(filters.imdbRatingFrom) } : {}),
    ...(filters.imdbRatingTo ? { imdbRatingTo: Number(filters.imdbRatingTo) } : {}),
    ...(filters.metadataStatus ? { metadataStatus: filters.metadataStatus } : {}),
    sort: filters.sort,
    showIgnored: filters.showIgnored,
    showWatched: filters.showWatched,
  }
}

function PersonalStateBadges({ item, hasActiveAccount }: { item: ImdbTitleCatalogItem; hasActiveAccount: boolean }) {
  if (!hasActiveAccount) return null
  const states: Array<{ label: string; className?: string }> = []
  if (item.isFavorite) states.push({ label: 'Favorite' })
  if (item.toWatch) states.push({ label: 'To watch', className: 'is-muted' })
  if (item.toDownload) states.push({ label: 'To download', className: 'is-muted' })
  if (item.isIgnored) states.push({ label: 'Ignored', className: 'is-muted' })
  if (item.personalRating !== null) states.push({ label: `Your rating ${item.personalRating}/10` })
  if (states.length === 0) return null
  return <div aria-label={`Personal state for ${item.title}`} className="imdb-title-personal-states">
    {states.map((state) => <span className={`state-pill ${state.className ?? ''}`.trim()} key={state.label}>{state.label}</span>)}
  </div>
}

function MetadataStatus({ status }: { status: string }) {
  return <span className={`state-pill is-${status.replaceAll('_', '-')}`}>{formatWords(status)}</span>
}

function ImdbTitleAwardsSummary({ awards }: { awards: ReturnType<typeof combineMovieAwards> }) {
  const summary = (source: 'oscars' | 'golden_globes', label: string) => {
    const records = awards.filter((award) => award.source === source)
    if (records.length === 0) return null
    const wins = records.filter((award) => award.isWinner).length
    return `${label} ${wins} ${wins === 1 ? 'win' : 'wins'} / ${records.length} ${records.length === 1 ? 'nomination' : 'nominations'}`
  }
  const lines = [summary('oscars', 'Oscars'), summary('golden_globes', 'Golden Globes')].filter(Boolean)
  if (lines.length === 0) return null
  return <span
    aria-label="Awards and nominations"
    className="imdb-title-award-summary"
    title={lines.join(' · ')}
  >{lines.join(' · ')}</span>
}

function RateTitleAction({ item, hasActiveAccount, onRate, placement = 'inline' }: {
  item: ImdbTitleCatalogItem
  hasActiveAccount: boolean
  onRate: () => void
  placement?: 'inline' | 'poster'
}) {
  if (!hasActiveAccount || item.metadataStatus !== 'enriched' || item.personalRating !== null) return null
  return placement === 'poster'
    ? <button
      aria-label={`Rate ${item.title}`}
      className="tile-icon-action"
      data-tooltip="Add your rating"
      onClick={onRate}
      type="button"
    ><Pencil aria-hidden="true" size={16} /></button>
    : <button className="text-button" onClick={onRate} type="button">Rate title</button>
}

function PersonalRatingDialog({ item, onClose, onSaved }: {
  item: ImdbTitleCatalogItem
  onClose: () => void
  onSaved: () => void
}) {
  const [rating, setRating] = useState('8')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function saveRating(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const value = Number(rating)
    if (!Number.isInteger(value) || value < 1 || value > 10) {
      setError('Enter a whole-number rating from 1 to 10.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      await addPersonalRating(item.imdbId, value)
      onSaved()
      onClose()
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : 'Could not save your rating.')
    } finally {
      setSaving(false)
    }
  }

  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget && !saving) onClose()
  }}>
    <section aria-labelledby="imdb-title-rating-heading" aria-modal="true" className="detail-dialog recommendation-watch-dialog" role="dialog">
      <header className="detail-header">
        <div><p className="eyebrow">PERSONAL RATING</p><h2 id="imdb-title-rating-heading">Rate {item.title}</h2></div>
        <button aria-label="Close rating dialog" className="button button-secondary" disabled={saving} onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        <form className="recommendation-watch-form" onSubmit={(event) => { void saveRating(event) }}>
          <label className="field" htmlFor="imdb-title-personal-rating">
            <span>Your rating (1-10)</span>
            <input id="imdb-title-personal-rating" max="10" min="1" onChange={(event) => setRating(event.target.value)} step="1" type="number" value={rating} />
          </label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="filter-actions">
            <button className="button" disabled={saving} type="submit">{saving ? 'Saving...' : 'Save rating'}</button>
          </div>
        </form>
      </div>
    </section>
  </div>
}

export function ImdbTitlesCatalogView({ hasActiveAccount }: { hasActiveAccount: boolean }) {
  const [viewMode, setViewMode] = useState<ViewMode>('posters')
  const [page, setPage] = useState(1)
  const [draft, setDraft] = useState<Filters>(emptyFilters)
  const [applied, setApplied] = useState<Filters>(emptyFilters)
  const [result, setResult] = useState<ImdbTitlesPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedTitle, setSelectedTitle] = useState<ImdbTitleCatalogItem | null>(null)
  const [ratingTitle, setRatingTitle] = useState<ImdbTitleCatalogItem | null>(null)
  const awardsRequestKey = movieAwardsRequestKey(result?.items.map((item) => item.imdbId) ?? [])
  const { awards: pageAwards, error: awardsError } = useMovieAwards(awardsRequestKey)

  const reload = useCallback(() => {
    setLoading(true)
    setError(null)
    setAttempt((current) => current + 1)
  }, [])

  useEffect(() => {
    let active = true
    setLoading(true)
    setError(null)
    getImdbTitles(queryFor(page, applied))
      .then((response) => {
        if (!active) return
        setResult(response)
        setSelectedTitle((current) => current
          ? response.items.find((item) => item.imdbId === current.imdbId) ?? current
          : null)
      })
      .catch((requestError: unknown) => {
        if (active) setError(requestError instanceof Error ? requestError.message : 'The IMDb titles request failed.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [applied, page, attempt])

  useEffect(() => {
    if (draft.search === applied.search) return
    const timeout = window.setTimeout(() => {
      setPage(1)
      setApplied((current) => ({ ...current, search: draft.search }))
    }, searchDebounceMs)
    return () => window.clearTimeout(timeout)
  }, [applied.search, draft.search])

  function commitFilterChanges(changes: Partial<Filters>) {
    setLoading(true)
    setError(null)
    setPage(1)
    setApplied((current) => ({ ...current, ...changes }))
  }

  function updateFilter<K extends keyof Filters>(key: K, value: Filters[K]) {
    setDraft((current) => ({ ...current, [key]: value }))
    if (key !== 'search') commitFilterChanges({ [key]: value } as Partial<Filters>)
  }

  function updateGenres(genres: string[] | null) {
    setDraft((current) => ({ ...current, genres }))
    commitFilterChanges({ genres })
  }

  function updateCountries(countries: string[] | null) {
    const selectedCountries = countries ?? countryOptions
    setDraft((current) => ({ ...current, countries: selectedCountries, countryFilter: true }))
    commitFilterChanges({ countries: selectedCountries, countryFilter: true })
  }

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoading(true)
    setError(null)
    setPage(1)
    setApplied({ ...draft })
  }

  function clearFilters() {
    setDraft({ ...emptyFilters })
    setApplied({ ...emptyFilters })
    setPage(1)
    setLoading(true)
    setError(null)
  }

  function updateYearRange(from: number, to: number, applyRange: boolean) {
    const bounds = result?.yearBounds
    if (bounds?.minYear === null || bounds?.minYear === undefined || bounds.maxYear === null) return
    const range = {
      yearFrom: from === bounds.minYear ? '' : String(from),
      yearTo: to === bounds.maxYear ? '' : String(to),
    }
    setDraft((current) => ({ ...current, ...range }))
    if (applyRange) commitFilterChanges(range)
  }

  function updateRatingRange(from: number, to: number, applyRange: boolean) {
    const bounds = result?.imdbRatingBounds
    if (bounds?.minRating === null || bounds?.minRating === undefined || bounds.maxRating === null) return
    const range = {
      imdbRatingFrom: from === bounds.minRating ? '' : String(from),
      imdbRatingTo: to === bounds.maxRating ? '' : String(to),
    }
    setDraft((current) => ({ ...current, ...range }))
    if (applyRange) commitFilterChanges(range)
  }

  const yearBounds = result?.yearBounds.minYear !== null && result?.yearBounds.minYear !== undefined
    && result.yearBounds.maxYear !== null
    ? { minYear: result.yearBounds.minYear, maxYear: result.yearBounds.maxYear }
    : null
  const ratingBounds = result?.imdbRatingBounds.minRating !== null && result?.imdbRatingBounds.minRating !== undefined
    && result.imdbRatingBounds.maxRating !== null
    ? { minRating: result.imdbRatingBounds.minRating, maxRating: result.imdbRatingBounds.maxRating }
    : null

  function renderPosterActions(item: ImdbTitleCatalogItem) {
    return <>
      {hasActiveAccount && <>
        <FavoriteControls
          from="imdb-titles"
          imdbId={item.imdbId}
          metadataStatus={item.metadataStatus}
          mediaType={item.mediaType}
          occurrenceCount={0}
          onAdded={reload}
          onRemoved={reload}
          onUpdated={reload}
          placement="poster"
        />
        <RateTitleAction hasActiveAccount={hasActiveAccount} item={item} onRate={() => setRatingTitle(item)} placement="poster" />
        <IgnoredMovieControl imdbId={item.imdbId} mediaType={item.mediaType} onChanged={reload} placement="poster" />
      </>}
      <button
        aria-label={`Delete ${item.title}`}
        className="tile-icon-action delete-icon"
        data-tooltip={`Delete ${item.title}`}
        onClick={() => { window.confirm(`Delete ${item.title}? This action is not connected yet.`) }}
        type="button"
      ><Trash2 aria-hidden="true" size={16} /></button>
    </>
  }

  function renderFooter(item: ImdbTitleCatalogItem) {
    return <FavoriteControls
      allowAddFromHere={false}
      from="imdb-titles"
      imdbId={item.imdbId}
      metadataStatus={item.metadataStatus}
      mediaType={item.mediaType}
      occurrenceCount={0}
      onAdded={reload}
      onRemoved={reload}
      onUpdated={reload}
      placement="status"
    />
  }

  return <section aria-label="IMDb titles catalog" className="media-view imdb-titles-view">
    <div className="page-heading">
      <div className="page-heading-copy">
        <h1>IMDb titles</h1>
        <span className="result-count page-heading-count">{result ? `${result.totalCount.toLocaleString()} titles` : 'Catalog records'}</span>
      </div>
    </div>
    <div className="section-toolbar catalog-section-toolbar">
      <div className="toolbar-actions">
        <ViewModeControl onChange={setViewMode} value={viewMode} />
        <button className="button button-secondary" onClick={clearFilters} type="button"><RotateCcw aria-hidden="true" size={15} />Clear filters</button>
        <button className="button button-secondary" disabled type="button">Add</button>
      </div>
    </div>

    <form className="filter-form imdb-title-filter-form" onSubmit={applyFilters}>
      <div className="field filter-search imdb-title-search">
        <label htmlFor="imdb-titles-search">Search titles</label>
        <input id="imdb-titles-search" onChange={(event) => updateFilter('search', event.target.value)} placeholder="Title, IMDb ID, or year" type="search" value={draft.search} />
      </div>
      <CategoryMultiSelect
        label="Genres"
        loading={loading && result === null}
        onChange={updateGenres}
        options={result?.genres ?? []}
        optionsLabel="genres"
        selectedValues={draft.genres}
      />
      <div className="imdb-title-country-filter">
        <CategoryMultiSelect
          label="Countries"
          onChange={updateCountries}
          options={countryOptions}
          optionsLabel="countries"
          selectedValues={draft.countries}
        />
      </div>
      <div className="field imdb-title-media-filter">
        <label htmlFor="imdb-titles-media-type">Media type</label>
        <select id="imdb-titles-media-type" onChange={(event) => updateFilter('mediaType', event.target.value)} value={draft.mediaType}>
          <option value="">All types</option>
          {result?.mediaTypes.map((mediaType) => <option key={mediaType} value={mediaType}>{formatWords(mediaType)}</option>)}
          {result?.hasUntypedTitles && <option value={untypedMediaType}>Type unknown</option>}
        </select>
      </div>
      <div className="field imdb-title-status-filter">
        <label htmlFor="imdb-titles-metadata-status">Metadata status</label>
        <select id="imdb-titles-metadata-status" onChange={(event) => updateFilter('metadataStatus', event.target.value)} value={draft.metadataStatus}>
          <option value="">All statuses</option>
          {(result?.metadataStatuses ?? ['pending', 'enriched', 'not_found', 'temporary_error']).map((status) => <option key={status} value={status}>{formatWords(status)}</option>)}
        </select>
      </div>
      <div className="field imdb-title-sort-filter">
        <label htmlFor="imdb-titles-sort">Sort results</label>
        <select id="imdb-titles-sort" onChange={(event) => updateFilter('sort', event.target.value as Filters['sort'])} value={draft.sort}>
          <option value="year">Year, newest first</option>
          <option value="rating">IMDb rating</option>
          <option value="votes">IMDb vote count</option>
        </select>
      </div>
      <div className="browse-range-filters imdb-title-range-filters">
        {yearBounds && <YearRangeFilter
          bounds={yearBounds}
          id="imdb-titles-year-range"
          label="Release year"
          onCommit={(from, to) => updateYearRange(from, to, true)}
          onPreview={(from, to) => updateYearRange(from, to, false)}
          valueFrom={draft.yearFrom ? Number(draft.yearFrom) : yearBounds.minYear}
          valueTo={draft.yearTo ? Number(draft.yearTo) : yearBounds.maxYear}
        />}
        {ratingBounds && <ImdbRatingRangeFilter
          bounds={ratingBounds}
          id="imdb-titles-rating-range"
          onCommit={(from, to) => updateRatingRange(from, to, true)}
          onPreview={(from, to) => updateRatingRange(from, to, false)}
          valueFrom={draft.imdbRatingFrom ? Number(draft.imdbRatingFrom) : ratingBounds.minRating}
          valueTo={draft.imdbRatingTo ? Number(draft.imdbRatingTo) : ratingBounds.maxRating}
        />}
        <DualRangeFilter
          className="year-range-filter imdb-votes-range-filter"
          id="imdb-titles-votes-range"
          label="Number of votes"
          max={1_000_000}
          min={0}
          onCommit={() => undefined}
          onPreview={() => undefined}
          outputLabel="Number of votes selected"
          valueFrom={0}
          valueTo={1_000_000}
          formatValue={(value) => compactVoteCount.format(value)}
        />
        <div className="imdb-title-visibility-filters">
          <label className="checkbox-field"><input checked={draft.showIgnored} onChange={(event) => updateFilter('showIgnored', event.target.checked)} type="checkbox" />Show ignored</label>
          <label className="checkbox-field"><input checked={draft.showWatched} onChange={(event) => updateFilter('showWatched', event.target.checked)} type="checkbox" />Show watched / rated</label>
        </div>
      </div>
    </form>

    {loading && <LoadingState label="Loading IMDb titles" />}
    {!loading && error && <ErrorState message={error} onRetry={reload} />}
    {!loading && !error && result && result.items.length === 0 && <EmptyState title="No IMDb titles found" message="Try changing the search or filters." />}
    {!loading && !error && result && result.items.length > 0 && <>
      {awardsError && <p className="movie-awards-warning" role="status">Linked awards are temporarily unavailable.</p>}
      {viewMode === 'posters' ? (
        <div className="poster-grid">
          {result.items.map((item) => <MoviePosterCard
            awards={combineMovieAwards([], pageAwards, item.imdbId)}
            footer={renderFooter(item)}
            genres={item.genres}
            imdbId={item.imdbId}
            imdbRating={item.imdbRating}
            imdbVotes={item.imdbVotes}
            key={item.imdbId}
            mediaType={item.mediaType}
            metadata={{ director: item.director, plot: item.plot }}
            onOpen={() => setSelectedTitle(item)}
            openLabel={`View ${item.title} details`}
            posterActions={renderPosterActions(item)}
            posterLabel={item.mediaType?.slice(0, 3).toUpperCase() ?? 'IMDb'}
            posterUrl={item.posterUrl}
            showAwards={false}
            title={item.title}
            year={item.year}
            yearLabel="Release year"
          >
            <ImdbTitleAwardsSummary awards={combineMovieAwards([], pageAwards, item.imdbId)} />
            <PersonalStateBadges hasActiveAccount={hasActiveAccount} item={item} />
            {item.metadataStatus !== 'enriched' && <MetadataStatus status={item.metadataStatus} />}
          </MoviePosterCard>)}
        </div>
      ) : <div className="table-wrap">
        <table className="data-table imdb-titles-table">
          <thead><tr><th>TITLE</th><th>TYPE</th><th>YEAR</th><th>IMDb RATING</th><th>AWARDS</th><th>GENRES</th><th>METADATA</th><th>PERSONAL STATE</th><th>ACTIONS</th></tr></thead>
          <tbody>{result.items.map((item) => <tr key={item.imdbId}>
            <td><MovieTableTitle
              metadata={{ director: item.director, plot: item.plot }}
              onOpen={() => setSelectedTitle(item)}
              openLabel={`View ${item.title} details`}
              posterLabel={item.mediaType?.slice(0, 3).toUpperCase() ?? 'IMDb'}
              posterUrl={item.posterUrl}
              subtitle={item.imdbId}
              title={item.title}
            /></td>
            <td><span className="type-label">{item.mediaType ? formatWords(item.mediaType) : 'Unknown'}</span></td>
            <td>{item.year ?? 'Year unknown'}</td>
            <td><MovieImdbLink imdbId={item.imdbId} rating={item.imdbRating} title={item.title} votes={item.imdbVotes} /></td>
            <td><ImdbTitleAwardsSummary awards={combineMovieAwards([], pageAwards, item.imdbId)} /></td>
            <td>{item.genres.slice(0, 2).join(', ') || 'Not tagged'}</td>
            <td><MetadataStatus status={item.metadataStatus} /></td>
            <td><PersonalStateBadges hasActiveAccount={hasActiveAccount} item={item} /></td>
            <td className="imdb-title-row-actions">
              <FavoriteControls from="imdb-titles" imdbId={item.imdbId} metadataStatus={item.metadataStatus} mediaType={item.mediaType} occurrenceCount={0} onAdded={reload} onRemoved={reload} onUpdated={reload} />
              <IgnoredMovieControl imdbId={item.imdbId} mediaType={item.mediaType} onChanged={reload} />
              <RateTitleAction hasActiveAccount={hasActiveAccount} item={item} onRate={() => setRatingTitle(item)} />
            </td>
          </tr>)}</tbody>
        </table>
      </div>}
      <Pagination page={result} onPageChange={(nextPage) => { setLoading(true); setError(null); setPage(nextPage) }} />
    </>}

    {selectedTitle && <ImdbTitleDetailsDialog
      awards={combineMovieAwards([], pageAwards, selectedTitle.imdbId)}
      hasActiveAccount={hasActiveAccount}
      item={selectedTitle}
      onClose={() => setSelectedTitle(null)}
      onRate={() => setRatingTitle(selectedTitle)}
      onUpdated={reload}
    />}
    {ratingTitle && <PersonalRatingDialog item={ratingTitle} onClose={() => setRatingTitle(null)} onSaved={reload} />}
  </section>
}
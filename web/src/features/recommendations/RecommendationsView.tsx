import { useEffect, useState } from 'react'
import { getRecommendationGenres, getRecommendations } from '../../api/client'
import type { MovieRecommendation, PageResponse, RecommendationCoefficients, RecommendationQuery } from '../../api/types'
import { CategoryMultiSelect } from '../../components/CategoryMultiSelect'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { Poster } from '../../components/Poster'
import { YearRangeFilter } from '../../components/YearRangeFilter'
import { formatWords } from '../../shared/format'

type CoefficientKey = keyof RecommendationCoefficients
type SliderPercentages = Record<CoefficientKey, number>

const recommendationsPageSize = 25
const coefficientControls: Array<{ key: CoefficientKey; id: string; label: string }> = [
  { key: 'ratingWeight', id: 'recommendation-rating-weight', label: 'IMDb rating' },
  { key: 'votesWeight', id: 'recommendation-votes-weight', label: 'Vote popularity' },
  { key: 'awardsWeight', id: 'recommendation-awards-weight', label: 'Award points' },
]

const initialSliderPercentages: SliderPercentages = {
  ratingWeight: 50,
  votesWeight: 25,
  awardsWeight: 25,
}
const emptyYearFilter = { from: '', to: '' }

function toCoefficients(percentages: SliderPercentages): RecommendationCoefficients {
  return {
    ratingWeight: percentages.ratingWeight / 100,
    votesWeight: percentages.votesWeight / 100,
    awardsWeight: percentages.awardsWeight / 100,
  }
}

function changeSliderPercentages(
  current: SliderPercentages,
  changedKey: CoefficientKey,
  rawValue: string,
): SliderPercentages {
  const changedValue = Math.min(100, Math.max(0, Math.round(Number(rawValue))))
  const otherKeys = (Object.keys(current) as CoefficientKey[]).filter((key) => key !== changedKey)
  const [firstKey, secondKey] = otherKeys
  const remaining = 100 - changedValue
  const previousOtherTotal = current[firstKey] + current[secondKey]
  const firstValue = previousOtherTotal === 0
    ? Math.floor(remaining / 2)
    : Math.round(remaining * current[firstKey] / previousOtherTotal)

  return {
    ...current,
    [changedKey]: changedValue,
    [firstKey]: firstValue,
    [secondKey]: remaining - firstValue,
  }
}

function coefficientLabel(value: number): string {
  return `${Math.round(value * 100)}%`
}

function scoreValue(value: number): string {
  return value.toFixed(2)
}

function sourceLabel(source: MovieRecommendation['awardEvents'][number]['awardSource']): string {
  return source === 'oscar' ? 'Oscar' : 'Golden Globes'
}

function eventOccasion(event: MovieRecommendation['awardEvents'][number]): string | null {
  if (event.awardSource === 'oscar' && event.ceremony !== null) return `Ceremony ${event.ceremony}`
  if (event.awardSource === 'golden_globes' && event.ceremonyYear !== null) return String(event.ceremonyYear)
  return null
}

export function RecommendationsView() {
  const [sliderPercentages, setSliderPercentages] = useState(initialSliderPercentages)
  const [pageNumber, setPageNumber] = useState(1)
  const [draftYearFilter, setDraftYearFilter] = useState(emptyYearFilter)
  const [appliedYearFilter, setAppliedYearFilter] = useState(emptyYearFilter)
  const [selectedGenres, setSelectedGenres] = useState<string[] | null>(null)
  const [genreOptions, setGenreOptions] = useState<string[]>([])
  const [genresLoading, setGenresLoading] = useState(true)
  const [genresError, setGenresError] = useState<string | null>(null)
  const [result, setResult] = useState<PageResponse<MovieRecommendation> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    getRecommendationGenres()
      .then((options) => { if (active) setGenreOptions(options) })
      .catch((requestError: unknown) => {
        if (active) setGenresError(requestError instanceof Error ? requestError.message : 'Could not load genres.')
      })
      .finally(() => { if (active) setGenresLoading(false) })

    return () => { active = false }
  }, [])

  useEffect(() => {
    let active = true
    const query: RecommendationQuery = {
      page: pageNumber,
      pageSize: recommendationsPageSize,
      ...toCoefficients(sliderPercentages),
      ...(appliedYearFilter.from ? { yearFrom: Number(appliedYearFilter.from) } : {}),
      ...(appliedYearFilter.to ? { yearTo: Number(appliedYearFilter.to) } : {}),
      ...(selectedGenres?.length ? { genres: selectedGenres } : {}),
    }
    getRecommendations(query)
      .then((response) => { if (active) setResult(response) })
      .catch((requestError: unknown) => {
        if (active) setError(requestError instanceof Error ? requestError.message : 'Could not load recommendations.')
      })
      .finally(() => { if (active) setLoading(false) })

    return () => { active = false }
  }, [appliedYearFilter, attempt, pageNumber, selectedGenres, sliderPercentages])

  function updateSlider(key: CoefficientKey, value: string) {
    setSliderPercentages((current) => changeSliderPercentages(current, key, value))
    setPageNumber(1)
    setLoading(true)
    setError(null)
  }

  function changePage(page: number) {
    setPageNumber(page)
    setLoading(true)
    setError(null)
  }

  function updateYearRange(from: number, to: number, apply: boolean) {
    const bounds = result?.yearBounds
    if (!bounds) return
    const years = {
      from: from === bounds.minYear ? '' : String(from),
      to: to === bounds.maxYear ? '' : String(to),
    }
    setDraftYearFilter(years)
    if (!apply) return
    setAppliedYearFilter(years)
    setPageNumber(1)
    setLoading(true)
    setError(null)
  }

  function updateGenres(values: string[] | null) {
    setSelectedGenres(values)
    setPageNumber(1)
    setLoading(true)
    setError(null)
  }

  function clearFilters() {
    setDraftYearFilter({ from: '', to: '' })
    setAppliedYearFilter({ from: '', to: '' })
    setSelectedGenres(null)
    setPageNumber(1)
    setLoading(true)
    setError(null)
  }

  function retry() {
    setLoading(true)
    setError(null)
    setAttempt((value) => value + 1)
  }

  return <section aria-labelledby="recommendations-heading" className="management-section recommendations-view">
    <h2 id="recommendations-heading">Recommendations</h2>
    <p className="section-caption">A personal ranking of eligible award-nominated movies, based on your ratings and the shared metadata catalog.</p>

    <div className="recommendation-filter-controls">
      {result?.yearBounds && <YearRangeFilter
        bounds={result.yearBounds}
        id="recommendation-year-range"
        label="Movie year"
        onCommit={(from, to) => updateYearRange(from, to, true)}
        onPreview={(from, to) => updateYearRange(from, to, false)}
        valueFrom={draftYearFilter.from ? Number(draftYearFilter.from) : result.yearBounds.minYear}
        valueTo={draftYearFilter.to ? Number(draftYearFilter.to) : result.yearBounds.maxYear}
      />}
      <CategoryMultiSelect
        error={genresError}
        label="Genres"
        loading={genresLoading}
        onChange={updateGenres}
        options={genreOptions}
        optionsLabel="genres"
        selectedValues={selectedGenres}
      />
      <button className="button button-secondary" disabled={!appliedYearFilter.from && !appliedYearFilter.to && (selectedGenres === null || selectedGenres.length === 0)} onClick={clearFilters} type="button">Clear filters</button>
    </div>

    <fieldset aria-label="Recommendation score weights" className="recommendation-controls">
      <legend>Score balance</legend>
      <div className="recommendation-sliders">
        {coefficientControls.map(({ key, id, label }) => <label className="recommendation-slider" htmlFor={id} key={key}>
          <span className="recommendation-slider-label">{label}<output htmlFor={id}>{sliderPercentages[key]}%</output></span>
          <input
            id={id}
            max="100"
            min="0"
            onChange={(event) => updateSlider(key, event.currentTarget.value)}
            step="1"
            type="range"
            value={sliderPercentages[key]}
          />
        </label>)}
      </div>
      <p aria-live="polite" className="recommendation-weight-total">Total weight: {Object.values(sliderPercentages).reduce((total, value) => total + value, 0)}%</p>
    </fieldset>

    <details className="recommendation-score-guide">
      <summary>How recommendation scores are calculated</summary>
      <p>The API combines a Bayesian IMDb rating, capped time-normalized vote popularity, and capped points from distinct award events. Award points use the configured win or nomination value multiplied by your winner-derived category taste. The sliders set the three request coefficients; candidate eligibility and all score calculations remain server-side.</p>
    </details>

    {loading && <LoadingState label="Loading recommendations" />}
    {!loading && error && <ErrorState message={error} onRetry={retry} />}
    {!loading && !error && result?.items.length === 0 && <EmptyState title="No recommendations available" message="No eligible award-nominated movies are available for this account yet." />}
    {!loading && !error && result && result.items.length > 0 && <>
      <div className="table-wrap">
        <table className="data-table recommendations-table">
          <thead><tr><th scope="col">MOVIE</th><th scope="col">FINAL SCORE</th><th scope="col">RATING</th><th scope="col">POPULARITY</th><th scope="col">AWARD POINTS</th><th scope="col">AWARD EVENTS</th></tr></thead>
          <tbody>{result.items.map((item) => <tr key={item.imdbId}>
            <td>
              <div className="recommendation-title-cell">
                <Poster className="poster-small recommendation-poster" src={item.posterUrl} title={item.title} />
                <div>
                  <a className="title-link" href={`https://www.imdb.com/title/${encodeURIComponent(item.imdbId)}/`} rel="noreferrer" target="_blank">{item.title}</a>
                  <span className="subtle-line">{item.year ?? 'Year unknown'} <span aria-hidden="true">·</span> IMDb {item.imdbRating.toFixed(1)} <span aria-hidden="true">·</span> {item.imdbVotes?.toLocaleString() ?? 'Votes unavailable'} votes</span>
                  {item.genres.length > 0 && <span className="subtle-line">{item.genres.join(', ')}</span>}
                </div>
              </div>
            </td>
            <td><strong className="recommendation-final-score">{scoreValue(item.finalScore)} / 10</strong></td>
            <td><span className="recommendation-component-score">{scoreValue(item.ratingComponent)}</span><span className="subtle-line">× {coefficientLabel(item.ratingWeight)}</span></td>
            <td><span className="recommendation-component-score">{scoreValue(item.popularityComponent)}</span><span className="subtle-line">× {coefficientLabel(item.votesWeight)}</span></td>
            <td><span className="recommendation-component-score">{scoreValue(item.awardComponent)}</span><span className="subtle-line">× {coefficientLabel(item.awardsWeight)}</span></td>
            <td>
              <details className="recommendation-award-details">
                <summary>{item.awardEvents.length} {item.awardEvents.length === 1 ? 'event' : 'events'}</summary>
                {item.awardEvents.length === 0 ? <p>No award points were applied.</p> : <ul className="recommendation-award-events">
                  {item.awardEvents.map((event, index) => <li key={`${event.awardSource}/${event.categoryKey}/${event.ceremony ?? event.ceremonyYear ?? index}`}>
                    <strong>{event.categoryName}</strong>
                    <span>{sourceLabel(event.awardSource)} · {formatWords(event.outcome)}{eventOccasion(event) ? ` · ${eventOccasion(event)}` : ''}</span>
                    <span>{scoreValue(event.basePoints)} base × {scoreValue(event.tasteMultiplier)} taste = {scoreValue(event.adjustedPoints)}</span>
                  </li>)}
                </ul>}
              </details>
            </td>
          </tr>)}</tbody>
        </table>
      </div>
      <Pagination page={result} onPageChange={changePage} />
    </>}
  </section>
}
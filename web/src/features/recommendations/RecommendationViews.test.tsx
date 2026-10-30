import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  addFavorite,
  getFavorites,
  getIgnoredMovies,
  getMovieAwards,
  getRecommendationGenres,
  getRecommendations,
  getRecommendationSettings,
  markRecommendationWatched,
  recalculateRecommendationVoteWeights,
  updateRecommendationSettings,
} from '../../api/client'
import type { FavoriteTitle, Recommendation, PageResponse, RecommendationSettings, VoteWeightRecalculationResponse } from '../../api/types'
import { titlePageSize } from '../../shared/titlePagination'
import { FavoriteProvider } from '../favorites/FavoriteContext'
import { IgnoredMoviesProvider } from '../ignored-movies/IgnoredMoviesContext'
import { RecommendationsView } from './RecommendationsView'
import { RecommendationSettingsView } from './RecommendationSettingsView'

vi.mock('../../api/client', () => ({
  addFavorite: vi.fn(),
  addIgnoredMovie: vi.fn(),
  getFavorites: vi.fn(),
  getIgnoredMovies: vi.fn(),
  getMovieAwards: vi.fn().mockResolvedValue([]),
  getRecommendationGenres: vi.fn(),
  getRecommendations: vi.fn(),
  getRecommendationSettings: vi.fn(),
  markRecommendationWatched: vi.fn(),
  removeIgnoredMovie: vi.fn(),
  removeFavorite: vi.fn(),
  recalculateRecommendationVoteWeights: vi.fn(),
  updateRecommendationSettings: vi.fn(),
  updateFavorite: vi.fn(),
}))

const dirtyWarning = 'New metadata detected. Recommended scores may be inaccurate. Recalculate vote weights.'
const voteRecalculation: VoteWeightRecalculationResponse = {
  calculatedAt: '2026-10-22T10:00:00Z',
  durationMilliseconds: 850,
  medianPeriodYears: 5,
  movieCount: 120,
  moviesWithVotesCount: 112,
  periodCount: 8,
  fallbackMedianCount: 2,
  missingVoteCount: 8,
}

function recommendation(overrides: Partial<Recommendation> = {}): Recommendation {
  return {
    imdbId: 'tt12345678',
    title: 'Quiet River',
    year: 2004,
    mediaType: 'movie',
    isWatched: false,
    imdbRating: 8.1,
    imdbVotes: 2500,
    posterUrl: null,
    genres: ['Drama'],
    metascore: 75,
    countries: ['USA'],
    director: 'A Director',
    plot: 'A quiet story by the water.',
    runtime: '100 min',
    awards: '1 win and 3 nominations.',
    boxOffice: '$10 million',
    ratingComponent: 8.0,
    popularityComponent: 7.2,
    awardComponent: 3.5,
    ratingWeight: 0.5,
    votesWeight: 0.25,
    awardsWeight: 0.25,
    finalScore: 6.675,
    awardEvents: [{
      awardSource: 'oscar',
      categoryKey: 'best_picture',
      categoryName: 'Best Picture',
      ceremony: 97,
      ceremonyYear: null,
      outcome: 'win',
      basePoints: 3,
      tasteMultiplier: 1.2,
      adjustedPoints: 3.6,
    }],
    ...overrides,
  }
}

function recommendationPage(
  items: Recommendation[],
  page = 1,
  totalCount = items.length,
  totalPages = totalCount === 0 ? 0 : 1,
  yearBounds?: PageResponse<Recommendation>['yearBounds'],
): PageResponse<Recommendation> {
  return { items, page, pageSize: titlePageSize, totalCount, totalPages, ...(yearBounds ? { yearBounds } : {}) }
}

function favoritePage(items: FavoriteTitle[] = []): PageResponse<FavoriteTitle> {
  return { items, page: 1, pageSize: titlePageSize, totalCount: items.length, totalPages: items.length === 0 ? 0 : 1 }
}

function recommendationSettings(overrides: Partial<RecommendationSettings> = {}): RecommendationSettings {
  return {
    medianPeriodYears: 5,
    bayesianDefaultC: 5.5,
    bayesianMRatio: 0.25,
    oscarWinPoints: 3,
    oscarNomPoints: 1,
    ggWinPoints: 2,
    ggNomPoints: 0.5,
    requiresVoteRecalculation: false,
    lastVoteRecalculationAt: null,
    lastVoteRecalculationPeriodYears: null,
    awardWeights: [
      { awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 3, nominationPoints: 1, isOverride: false },
      { awardSource: 'golden_globes', categoryKey: 'award_other', winPoints: 2, nominationPoints: 0.5, isOverride: false },
    ],
    ...overrides,
  }
}

describe('RecommendationSettingsView', () => {
  beforeEach(() => {
    vi.mocked(getRecommendationSettings).mockReset()
    vi.mocked(updateRecommendationSettings).mockReset()
    vi.mocked(recalculateRecommendationVoteWeights).mockReset()
    vi.mocked(getRecommendations).mockReset()
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
  })

  it('loads and saves global settings, then clears the dirty warning after recalculation', async () => {
    const dirtySettings = recommendationSettings({ requiresVoteRecalculation: true })
    const savedSettings = recommendationSettings({
      medianPeriodYears: 7,
      bayesianDefaultC: 6.25,
      bayesianMRatio: 0.5,
      oscarWinPoints: 5,
      oscarNomPoints: 2,
      requiresVoteRecalculation: true,
      awardWeights: [
        { awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 4.5, nominationPoints: 1.25, isOverride: true },
        { awardSource: 'golden_globes', categoryKey: 'award_other', winPoints: 2, nominationPoints: 0.5, isOverride: false },
      ],
    })
    const cleanSettings = recommendationSettings({
      medianPeriodYears: 7,
      lastVoteRecalculationAt: voteRecalculation.calculatedAt,
      lastVoteRecalculationPeriodYears: 7,
      awardWeights: savedSettings.awardWeights,
    })
    vi.mocked(getRecommendationSettings)
      .mockResolvedValueOnce(dirtySettings)
      .mockResolvedValueOnce(cleanSettings)
    vi.mocked(updateRecommendationSettings).mockResolvedValue(savedSettings)
    vi.mocked(recalculateRecommendationVoteWeights).mockResolvedValue(voteRecalculation)

    render(<RecommendationSettingsView />)

    expect(await screen.findByRole('heading', { name: 'Recommendation settings' })).toBeTruthy()
    expect(screen.getByRole('alert').textContent).toContain(dirtyWarning)
    fireEvent.change(screen.getByLabelText('Vote median period (years)'), { target: { value: '7' } })
    fireEvent.change(screen.getByLabelText('Bayesian default C'), { target: { value: '6.25' } })
    fireEvent.change(screen.getByLabelText('Bayesian m ratio'), { target: { value: '0.5' } })
    fireEvent.change(screen.getByLabelText('Oscar win points'), { target: { value: '5' } })
    fireEvent.change(screen.getByLabelText('Oscar nomination points'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /oscar best picture use category override/i }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Oscar best picture win points' }), { target: { value: '4.5' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Oscar best picture nomination points' }), { target: { value: '1.25' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save recommendation settings' }))

    expect(await screen.findByText('Recommendation settings saved.')).toBeTruthy()
    expect(updateRecommendationSettings).toHaveBeenCalledWith({
      medianPeriodYears: 7,
      bayesianDefaultC: 6.25,
      bayesianMRatio: 0.5,
      oscarWinPoints: 5,
      oscarNomPoints: 2,
      ggWinPoints: 2,
      ggNomPoints: 0.5,
      awardWeights: [
        { awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 4.5, nominationPoints: 1.25, isOverride: true },
        { awardSource: 'golden_globes', categoryKey: 'award_other', winPoints: 2, nominationPoints: 0.5, isOverride: false },
      ],
    })

    fireEvent.click(screen.getByRole('button', { name: 'Recalculate vote weights' }))

    expect(await screen.findByText('Vote weights are up to date. 120 movies processed in 850 ms.')).toBeTruthy()
    expect(screen.queryByText(dirtyWarning)).toBeNull()
    expect(recalculateRecommendationVoteWeights).toHaveBeenCalledOnce()
  })

  it('keeps the dirty warning visible after a recalculation timeout', async () => {
    vi.mocked(getRecommendationSettings).mockResolvedValue(recommendationSettings({ requiresVoteRecalculation: true }))
    vi.mocked(recalculateRecommendationVoteWeights).mockRejectedValue(Object.assign(new Error('Request timed out'), { status: 504 }))

    render(<RecommendationSettingsView />)
    await screen.findByRole('heading', { name: 'Recommendation settings' })
    fireEvent.click(screen.getByRole('button', { name: 'Recalculate vote weights' }))

    expect(await screen.findByText(/Vote recalculation timed out after 10 seconds/)).toBeTruthy()
    expect(screen.getByText(dirtyWarning)).toBeTruthy()
  })

  it('polls settings after a 202 response until vote weights are clean', async () => {
    vi.useFakeTimers()
    vi.mocked(getRecommendationSettings)
      .mockResolvedValueOnce(recommendationSettings({ requiresVoteRecalculation: true }))
      .mockResolvedValueOnce(recommendationSettings({ requiresVoteRecalculation: true }))
      .mockResolvedValueOnce(recommendationSettings())
    vi.mocked(recalculateRecommendationVoteWeights).mockResolvedValue({
      id: 17,
      status: 'queued',
      statusUrl: '/api/background-jobs/17',
    })

    render(<RecommendationSettingsView />)
    await act(async () => { await Promise.resolve(); await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Recalculate vote weights' }))
    await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() })

    expect(screen.getByText('Vote-weight recalculation was accepted and is running.')).toBeTruthy()
    expect(screen.getByText(dirtyWarning)).toBeTruthy()
    await act(async () => { await vi.advanceTimersByTimeAsync(2_000) })

    expect(screen.queryByText(dirtyWarning)).toBeNull()
    expect(screen.getByText('Vote weights are up to date.')).toBeTruthy()
    expect(getRecommendationSettings).toHaveBeenCalledTimes(3)
  })

  it('shows settings load failures and retries the request', async () => {
    vi.mocked(getRecommendationSettings)
      .mockRejectedValueOnce(new Error('Settings offline'))
      .mockResolvedValueOnce(recommendationSettings())

    render(<RecommendationSettingsView />)

    expect((await screen.findByRole('alert')).textContent).toContain('Settings offline')
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('heading', { name: 'Recommendation settings' })).toBeTruthy()
  })
})

describe('RecommendationsView', () => {
  beforeEach(() => {
    vi.mocked(addFavorite).mockReset()
    vi.mocked(getFavorites).mockReset()
    vi.mocked(getRecommendationGenres).mockReset()
    vi.mocked(getRecommendationGenres).mockResolvedValue(['Comedy', 'Drama'])
    vi.mocked(getRecommendations).mockReset()
    vi.mocked(getMovieAwards).mockReset().mockResolvedValue([])
    vi.mocked(getIgnoredMovies).mockReset()
    vi.mocked(markRecommendationWatched).mockReset().mockResolvedValue(undefined)
    vi.mocked(getRecommendationSettings).mockReset()
    vi.mocked(updateRecommendationSettings).mockReset()
    vi.mocked(recalculateRecommendationVoteWeights).mockReset()
  })

  afterEach(cleanup)

  it('defaults to poster cards and keeps the table as an alternate view', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()]))
    const onResultCountChange = vi.fn()

    const view = render(<RecommendationsView mediaType="movie" onResultCountChange={onResultCountChange} />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    expect(screen.queryByText('Ranking and results')).toBeNull()
    expect(screen.queryByText(/A personal ranking of eligible award-nominated/)).toBeNull()
    expect(view.container.querySelector('.catalog-section-toolbar')).toBeNull()
    const filterControls = view.container.querySelector('.recommendation-filter-controls') as HTMLElement
    const toolbarActions = filterControls.lastElementChild as HTMLElement
    expect(toolbarActions.classList.contains('recommendation-toolbar-actions')).toBe(true)
    expect(toolbarActions.querySelector('.view-mode-control')).toBeTruthy()
    expect(toolbarActions.lastElementChild).toBe(screen.getByRole('button', { name: 'How recommendation scores are calculated' }))
    await waitFor(() => expect(onResultCountChange).toHaveBeenLastCalledWith({ mediaType: 'movie', totalCount: 1 }))
    expect(screen.getByRole('button', { name: 'Posters' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.queryByRole('columnheader', { name: 'MOVIE' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByRole('columnheader', { name: 'MOVIE' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Posters' }))
    expect(screen.getByRole('button', { name: 'Posters' }).getAttribute('aria-pressed')).toBe('true')
  })

  it.each(['movie', 'series'] as const)('opens the score explanation for %s recommendations', async (mediaType) => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([]))

    render(<RecommendationsView mediaType={mediaType} />)

    await screen.findByText('No recommendations available')
    const guideButton = screen.getByRole('button', { name: 'How recommendation scores are calculated' })
    expect(guideButton.getAttribute('title')).toBe('View how recommendation scores are calculated')
    expect(screen.getByRole('button', { name: 'Posters' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Table' })).toBeTruthy()
    const filterControls = screen.getByRole('button', { name: 'Table' }).closest('.recommendation-filter-controls') as HTMLElement
    const toolbarActions = filterControls.lastElementChild as HTMLElement
    expect(toolbarActions.classList.contains('recommendation-toolbar-actions')).toBe(true)
    expect(toolbarActions.lastElementChild).toBe(guideButton)

    fireEvent.click(guideButton)

    const dialog = screen.getByRole('dialog', { name: 'How recommendation scores are calculated' })
    expect(within(dialog).getByText(/Bayesian IMDb rating, capped time-normalized vote popularity/)).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close recommendation scoring explanation' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('shows catalog-style metadata and opens full recommendation details', async () => {
    vi.mocked(getMovieAwards).mockResolvedValue([{
      id: 1,
      imdbId: 'tt12345678',
      source: 'oscars',
      filmYear: 2004,
      ceremonyYear: null,
      ceremony: 77,
      award: 'Best Picture',
      name: 'Producer',
      nominees: 'Producer',
      detail: '',
      isWinner: true,
    }])
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()]))

    render(<RecommendationsView mediaType="movie" />)

    const detailsButton = await screen.findByRole('button', { name: 'View Quiet River details' })
    const card = detailsButton.closest('article') as HTMLElement
    expect(within(card).getByText('Director: A Director')).toBeTruthy()
    expect(within(card).getByText('Drama')).toBeTruthy()
    expect(await within(card).findByText('1 win · 1 nomination')).toBeTruthy()
    expect(within(card).queryByText('3.00 base × 1.20 taste = 3.60')).toBeNull()

    fireEvent.click(detailsButton)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('A quiet story by the water.')).toBeTruthy()
    expect(screen.getByText('Director')).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Best Picture' })).toBeTruthy()
    expect(within(dialog).getByRole('region', { name: 'Recommendation score details' })).toBeTruthy()
    expect(screen.getByText('3.00 base × 1.20 taste = 3.60')).toBeTruthy()
  })

  it('renders all three recommendation actions as buttons on poster cards', async () => {
    vi.mocked(getFavorites).mockResolvedValue(favoritePage())
    vi.mocked(getIgnoredMovies).mockResolvedValue({ items: [], page: 1, pageSize: titlePageSize, totalCount: 0, totalPages: 0 })
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()]))

    render(<FavoriteProvider accountKey="test-account"><IgnoredMoviesProvider accountKey="test-account"><RecommendationsView mediaType="movie" /></IgnoredMoviesProvider></FavoriteProvider>)

    expect(await screen.findByRole('button', { name: 'Mark Quiet River as watched' })).toBeTruthy()
    expect(await screen.findByRole('button', { name: 'Add Quiet River to watch list' })).toBeTruthy()
    expect(await screen.findByRole('button', { name: 'Ignore movie' })).toBeTruthy()
  })

  it('renders only server-returned candidates and keeps all three coefficients at 100%', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()], 1, 26, 2, { minYear: 2000, maxYear: 2025 }))

    render(<RecommendationsView mediaType="movie" />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    expect(screen.queryByText('Unrated feature')).toBeNull()
    expect(screen.queryByText('Series candidate')).toBeNull()
    const scoreWeights = within(screen.getByRole('group', { name: 'Recommendation score weights' }))
    const sliders = scoreWeights.getAllByRole('slider') as HTMLInputElement[]
    expect(sliders.map((slider) => slider.value)).toEqual(['50', '25', '25'])
    expect(screen.getByText('Total weight: 100%')).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })
    expect(screen.getByLabelText('Movie year selected years').textContent).toBe('2000 – 2025')
    expect(screen.queryByText('Best Picture')).toBeNull()
    expect(screen.queryByText('3.00 base × 1.20 taste = 3.60')).toBeNull()

    fireEvent.change(sliders[0]!, { target: { value: '80' } })
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.8,
      votesWeight: 0.1,
      awardsWeight: 0.1,
    }))
    expect((scoreWeights.getAllByRole('slider') as HTMLInputElement[]).map((slider) => slider.value)).toEqual(['80', '10', '10'])

    const yearMinimum = screen.getByRole('slider', { name: 'Movie year minimum' })
    fireEvent.change(yearMinimum, { target: { value: '2010' } })
    fireEvent.pointerUp(yearMinimum)
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.8,
      votesWeight: 0.1,
      awardsWeight: 0.1,
      yearFrom: 2010,
    }))

    fireEvent.click(await screen.findByText('All genres (2)'))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Drama' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.8,
      votesWeight: 0.1,
      awardsWeight: 0.1,
      yearFrom: 2010,
      genres: ['Comedy'],
    }))

    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 2,
      pageSize: titlePageSize,
      ratingWeight: 0.8,
      votesWeight: 0.1,
      awardsWeight: 0.1,
      yearFrom: 2010,
      genres: ['Comedy'],
    }))
  })

  it('selects or clears all genres and closes the menu on outside pointer', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()]))

    render(<RecommendationsView mediaType="movie" />)

    const trigger = await screen.findByText('All genres (2)')
    const menu = trigger.closest('details')
    fireEvent.click(trigger)
    expect(menu?.open).toBe(true)

    const checkboxes = within(menu as HTMLElement).getAllByRole('checkbox') as HTMLInputElement[]
    expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([true, true])
    let requestCount = vi.mocked(getRecommendations).mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'Select none' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenCalledTimes(requestCount + 1))
    requestCount += 1
    expect(screen.getByText('0 of 2 selected')).toBeTruthy()
    expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([false, false])
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })

    fireEvent.click(screen.getByRole('checkbox', { name: 'Comedy' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenCalledTimes(requestCount + 1))
    requestCount += 1
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      genres: ['Comedy'],
    })

    fireEvent.click(screen.getByRole('button', { name: 'Select all' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenCalledTimes(requestCount + 1))
    expect(screen.getByText('All genres (2)')).toBeTruthy()
    expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([true, true])

    fireEvent.pointerDown(document.body)
    expect(menu?.open).toBe(false)
  })

  it('includes already watched movies without actions and can clear that filter', async () => {
    vi.mocked(getRecommendations)
      .mockResolvedValueOnce(recommendationPage([recommendation()]))
      .mockResolvedValueOnce(recommendationPage([recommendation({ isWatched: true })]))
      .mockResolvedValue(recommendationPage([recommendation()]))

    render(<RecommendationsView mediaType="movie" />)

    const watchedCheckbox = await screen.findByRole('checkbox', { name: 'Exclude already watched movies' }) as HTMLInputElement
    expect(watchedCheckbox.checked).toBe(true)
    fireEvent.click(watchedCheckbox)
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      excludeWatched: false,
    }))
    expect(watchedCheckbox.checked).toBe(false)
    const watchedMovieButton = await screen.findByRole('button', { name: 'View Quiet River details' })
    const watchedMovieCard = watchedMovieButton.closest('article')
    expect(watchedMovieCard).not.toBeNull()
    expect(within(watchedMovieCard as HTMLElement).queryByRole('button', { name: 'Mark Quiet River as watched' })).toBeNull()
    expect(within(watchedMovieCard as HTMLElement).queryByRole('button', { name: 'Ignore movie' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    }))
    expect(watchedCheckbox.checked).toBe(true)
  })

  it('requires a rating before marking a recommendation watched and refreshes the results', async () => {
    vi.mocked(getRecommendations)
      .mockResolvedValueOnce(recommendationPage([recommendation()]))
      .mockResolvedValueOnce(recommendationPage([]))

    render(<RecommendationsView mediaType="movie" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Mark Quiet River as watched' }))
    expect(screen.getByRole('dialog').getAttribute('aria-modal')).toBe('true')
    const submit = screen.getByRole('button', { name: 'Save rating and mark watched' }) as HTMLButtonElement
    expect(submit.disabled).toBe(true)

    fireEvent.change(screen.getByRole('spinbutton', { name: 'Your rating (1-10)' }), { target: { value: '9' } })
    expect(submit.disabled).toBe(false)
    fireEvent.click(submit)

    await waitFor(() => expect(markRecommendationWatched).toHaveBeenCalledWith('tt12345678', 9, 'movie'))
    expect(await screen.findByText('No recommendations available')).toBeTruthy()
    expect(getRecommendations).toHaveBeenCalledTimes(1)
  })

  it('adds only unwatched movie recommendations to the watch list and removes the item locally', async () => {
    const savedFavorite: FavoriteTitle = {
      imdbId: 'tt12345678', torrentMovieId: null, title: 'Quiet River', year: 2004, mediaType: 'movie', metadataStatus: 'enriched',
      imdbRating: 8.1, posterUrl: null, toWatch: true, toDownload: false,
      addedFromOscar: false, addedFromCatalog: false, addedFromRecommendations: true,
      createdAt: '2026-10-26T00:00:00Z', updatedAt: '2026-10-26T00:00:00Z',
      oscarFilmCount: 0, nominationCount: 0, winCount: 0, occurrenceCount: 0, lastSeenAt: null,
    }
    vi.mocked(getFavorites).mockResolvedValue(favoritePage())
    vi.mocked(addFavorite).mockResolvedValue(savedFavorite)
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([
      recommendation(),
      recommendation({ imdbId: 'tt87654321', title: 'Already Watched', isWatched: true }),
      recommendation({ imdbId: 'tt87654322', title: 'Another Candidate' }),
    ]))

    render(<FavoriteProvider accountKey="test-account"><RecommendationsView mediaType="movie" /></FavoriteProvider>)

    const addButton = await screen.findByRole('button', { name: 'Add Quiet River to watch list' }) as HTMLButtonElement
    const watchedCard = (await screen.findAllByRole('button', { name: 'View Already Watched details' }))[0]?.closest('article')
    expect(watchedCard).not.toBeNull()
    expect(within(watchedCard as HTMLElement).queryByRole('button', { name: 'Add Already Watched to watch list' })).toBeNull()
    await waitFor(() => expect(addButton.disabled).toBe(false))
    fireEvent.click(addButton)

    await waitFor(() => expect(addFavorite).toHaveBeenCalledWith('tt12345678', 'recommendation'))
    expect(screen.queryByRole('button', { name: 'View Quiet River details' })).toBeNull()
    expect(await screen.findAllByRole('button', { name: 'View Already Watched details' })).not.toHaveLength(0)
    expect(await screen.findAllByRole('button', { name: 'View Another Candidate details' })).not.toHaveLength(0)
    expect(getRecommendations).toHaveBeenCalledTimes(1)
  })

  it('keeps the current page and remaining candidates when marking watched', async () => {
    vi.mocked(getRecommendations)
      .mockResolvedValueOnce(recommendationPage([recommendation()], 1, 51, 3))
      .mockResolvedValueOnce(recommendationPage([
        recommendation({ title: 'Second page title' }),
        recommendation({ imdbId: 'tt87654322', title: 'Another second page title' }),
      ], 2, 51, 3))

    render(<RecommendationsView mediaType="movie" />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(await screen.findByRole('button', { name: 'View Second page title details' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Mark Second page title as watched' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Your rating (1-10)' }), { target: { value: '9' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save rating and mark watched' }))

    await waitFor(() => expect(markRecommendationWatched).toHaveBeenCalledWith('tt12345678', 9, 'movie'))
    expect(screen.queryByRole('button', { name: 'View Second page title details' })).toBeNull()
    expect(await screen.findAllByRole('button', { name: 'View Another second page title details' })).not.toHaveLength(0)
    expect(getRecommendations).toHaveBeenCalledTimes(2)
  })

  it('shows an empty state when the server returns no candidates', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([]))

    render(<RecommendationsView mediaType="movie" />)

    expect(await screen.findByText('No recommendations available')).toBeTruthy()
    expect(screen.getByText('No eligible award-nominated movies are available for this account yet.')).toBeTruthy()
  })

  it('shows recommendation errors and retries with the current coefficient values', async () => {
    vi.mocked(getRecommendations)
      .mockRejectedValueOnce(new Error('Recommendations offline'))
      .mockResolvedValueOnce(recommendationPage([recommendation()]))

    render(<RecommendationsView mediaType="movie" />)

    expect((await screen.findByRole('alert')).textContent).toContain('Recommendations offline')
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })
  })

  it('uses the same watch-list and watched actions for series', async () => {
    const savedSeries: FavoriteTitle = {
      imdbId: 'tt12345678', torrentMovieId: null, title: 'Quiet River', year: 2004, mediaType: 'series', metadataStatus: 'enriched',
      imdbRating: 8.1, posterUrl: null, toWatch: true, toDownload: false,
      addedFromOscar: false, addedFromCatalog: false, addedFromRecommendations: true,
      createdAt: '2026-10-26T00:00:00Z', updatedAt: '2026-10-26T00:00:00Z',
      oscarFilmCount: 0, nominationCount: 0, winCount: 0, occurrenceCount: 0, lastSeenAt: null,
    }
    vi.mocked(addFavorite).mockResolvedValue(savedSeries)
    const seriesRecommendation = recommendation({
      mediaType: 'series',
      awardEvents: [{
        awardSource: 'golden_globes',
        categoryKey: 'best_television_series_drama',
        categoryName: 'Best Television Series - Drama',
        ceremony: null,
        ceremonyYear: 2025,
        outcome: 'nomination',
        basePoints: 0.5,
        tasteMultiplier: 1,
        adjustedPoints: 0.5,
      }],
    })
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([
      seriesRecommendation,
      { ...seriesRecommendation, imdbId: 'tt87654321', title: 'Second Series' },
    ], 1, 2, 1, { minYear: 2000, maxYear: 2025 }))
    vi.mocked(getFavorites).mockResolvedValue(favoritePage())

    render(<FavoriteProvider accountKey="test-account"><RecommendationsView mediaType="series" /></FavoriteProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(await screen.findByRole('columnheader', { name: 'SERIES' })).toBeTruthy()
    expect(getRecommendationGenres).toHaveBeenCalledWith('series')
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'series',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })

    const yearMinimum = screen.getByRole('slider', { name: 'Series year minimum' })
    fireEvent.change(yearMinimum, { target: { value: '2010' } })
    fireEvent.pointerUp(yearMinimum)
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'series',
      page: 1,
      pageSize: titlePageSize,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      yearFrom: 2010,
    }))

    expect(await screen.findByRole('checkbox', { name: 'Exclude already watched series' })).toBeTruthy()
    const addButton = await screen.findByRole('button', { name: 'Add Quiet River to watch list' }) as HTMLButtonElement
    await waitFor(() => expect(addButton.disabled).toBe(false))
    fireEvent.click(addButton)
    await waitFor(() => expect(addFavorite).toHaveBeenCalledWith('tt12345678', 'recommendation'))
    expect(screen.queryByRole('link', { name: 'Quiet River' })).toBeNull()
    expect(await screen.findByRole('link', { name: 'Second Series' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Mark Second Series as watched' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Your rating (1-10)' }), { target: { value: '9' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save rating and mark watched' }))
    await waitFor(() => expect(markRecommendationWatched).toHaveBeenCalledWith('tt87654321', 9, 'series'))
  })
})
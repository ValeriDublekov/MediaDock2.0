import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getRecommendationGenres,
  getRecommendations,
  getRecommendationSettings,
  markRecommendationWatched,
  recalculateRecommendationVoteWeights,
  updateRecommendationSettings,
} from '../../api/client'
import type { Recommendation, PageResponse, RecommendationSettings, VoteWeightRecalculationResponse } from '../../api/types'
import { RecommendationsView } from './RecommendationsView'
import { RecommendationSettingsView } from './RecommendationSettingsView'

vi.mock('../../api/client', () => ({
  getRecommendationGenres: vi.fn(),
  getRecommendations: vi.fn(),
  getRecommendationSettings: vi.fn(),
  markRecommendationWatched: vi.fn(),
  recalculateRecommendationVoteWeights: vi.fn(),
  updateRecommendationSettings: vi.fn(),
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
  return { items, page, pageSize: 25, totalCount, totalPages, ...(yearBounds ? { yearBounds } : {}) }
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
    vi.mocked(getRecommendationGenres).mockReset()
    vi.mocked(getRecommendationGenres).mockResolvedValue(['Comedy', 'Drama'])
    vi.mocked(getRecommendations).mockReset()
    vi.mocked(markRecommendationWatched).mockReset().mockResolvedValue(undefined)
    vi.mocked(getRecommendationSettings).mockReset()
    vi.mocked(updateRecommendationSettings).mockReset()
    vi.mocked(recalculateRecommendationVoteWeights).mockReset()
  })

  afterEach(cleanup)

  it('renders only server-returned candidates and keeps all three coefficients at 100%', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()], 1, 26, 2, { minYear: 2000, maxYear: 2025 }))

    render(<RecommendationsView mediaType="movie" />)

    expect(await screen.findByRole('link', { name: 'Quiet River' })).toBeTruthy()
    expect(screen.queryByText('Unrated feature')).toBeNull()
    expect(screen.queryByText('Series candidate')).toBeNull()
    const scoreWeights = within(screen.getByRole('group', { name: 'Recommendation score weights' }))
    const sliders = scoreWeights.getAllByRole('slider') as HTMLInputElement[]
    expect(sliders.map((slider) => slider.value)).toEqual(['50', '25', '25'])
    expect(screen.getByText('Total weight: 100%')).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })
    expect(screen.getByLabelText('Movie year selected years').textContent).toBe('2000 – 2025')
    expect(screen.getByText('Best Picture')).toBeTruthy()
    expect(screen.getByText('3.00 base × 1.20 taste = 3.60')).toBeTruthy()

    fireEvent.change(sliders[0]!, { target: { value: '80' } })
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: 25,
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
      pageSize: 25,
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
      pageSize: 25,
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
      pageSize: 25,
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
      pageSize: 25,
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
      pageSize: 25,
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
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      excludeWatched: false,
    }))
    expect(watchedCheckbox.checked).toBe(false)
    const watchedMovieLink = await screen.findByRole('link', { name: 'Quiet River' })
    const watchedMovieRow = watchedMovieLink.closest('tr')
    expect(watchedMovieRow).not.toBeNull()
    expect(within(watchedMovieRow as HTMLElement).queryByRole('button', { name: 'Mark Quiet River as watched' })).toBeNull()
    expect(within(watchedMovieRow as HTMLElement).queryByRole('button', { name: 'Ignore movie' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: 25,
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
  })

  it('keeps the current page when refreshing after marking a recommendation watched', async () => {
    vi.mocked(getRecommendations)
      .mockResolvedValueOnce(recommendationPage([recommendation()], 1, 51, 3))
      .mockResolvedValueOnce(recommendationPage([recommendation({ title: 'Second page title' })], 2, 51, 3))
      .mockResolvedValueOnce(recommendationPage([recommendation({ title: 'Updated second page title' })], 2, 50, 2))

    render(<RecommendationsView mediaType="movie" />)

    expect(await screen.findByRole('link', { name: 'Quiet River' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(await screen.findByRole('link', { name: 'Second page title' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Mark Second page title as watched' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Your rating (1-10)' }), { target: { value: '9' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save rating and mark watched' }))

    expect(await screen.findByRole('link', { name: 'Updated second page title' })).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 2,
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })
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
    expect(await screen.findByRole('link', { name: 'Quiet River' })).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'movie',
      page: 1,
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })
  })

  it('uses the shared controls and watch action for series', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([
      recommendation({
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
      }),
    ], 1, 1, 1, { minYear: 2000, maxYear: 2025 }))

    render(<RecommendationsView mediaType="series" />)

    expect(await screen.findByRole('columnheader', { name: 'SERIES' })).toBeTruthy()
    expect(getRecommendationGenres).toHaveBeenCalledWith('series')
    expect(getRecommendations).toHaveBeenLastCalledWith({
      mediaType: 'series',
      page: 1,
      pageSize: 25,
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
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      yearFrom: 2010,
    }))

    expect(await screen.findByRole('checkbox', { name: 'Exclude already watched series' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Mark Quiet River as watched' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Your rating (1-10)' }), { target: { value: '9' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save rating and mark watched' }))
    await waitFor(() => expect(markRecommendationWatched).toHaveBeenCalledWith('tt12345678', 9, 'series'))
  })
})
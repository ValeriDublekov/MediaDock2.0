import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getRecommendationGenres,
  getRecommendations,
  getRecommendationSettings,
  recalculateRecommendationVoteWeights,
  updateRecommendationSettings,
} from '../../api/client'
import type { MovieRecommendation, PageResponse, RecommendationSettings, VoteWeightRecalculationResponse } from '../../api/types'
import { RecommendationsView } from './RecommendationsView'
import { RecommendationSettingsView } from './RecommendationSettingsView'

vi.mock('../../api/client', () => ({
  getRecommendationGenres: vi.fn(),
  getRecommendations: vi.fn(),
  getRecommendationSettings: vi.fn(),
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
  globalFallbackCount: 2,
  missingVoteCount: 8,
}

function recommendation(overrides: Partial<MovieRecommendation> = {}): MovieRecommendation {
  return {
    imdbId: 'tt12345678',
    title: 'Quiet River',
    year: 2004,
    mediaType: 'movie',
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
  items: MovieRecommendation[],
  page = 1,
  totalCount = items.length,
  totalPages = totalCount === 0 ? 0 : 1,
  yearBounds?: PageResponse<MovieRecommendation>['yearBounds'],
): PageResponse<MovieRecommendation> {
  return { items, page, pageSize: 25, totalCount, totalPages, ...(yearBounds ? { yearBounds } : {}) }
}

function recommendationSettings(overrides: Partial<RecommendationSettings> = {}): RecommendationSettings {
  return {
    medianPeriodYears: 5,
    requiresVoteRecalculation: false,
    lastVoteRecalculationAt: null,
    lastVoteRecalculationPeriodYears: null,
    awardWeights: [
      { awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 3, nominationPoints: 1 },
      { awardSource: 'golden_globes', categoryKey: 'award_other', winPoints: 0, nominationPoints: 0 },
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
      requiresVoteRecalculation: true,
      awardWeights: [
        { awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 4.5, nominationPoints: 1.25 },
        { awardSource: 'golden_globes', categoryKey: 'award_other', winPoints: 0, nominationPoints: 0 },
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
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Oscar best picture win points' }), { target: { value: '4.5' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Oscar best picture nomination points' }), { target: { value: '1.25' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save recommendation settings' }))

    expect(await screen.findByText('Recommendation settings saved.')).toBeTruthy()
    expect(updateRecommendationSettings).toHaveBeenCalledWith({
      medianPeriodYears: 7,
      awardWeights: [
        { awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 4.5, nominationPoints: 1.25 },
        { awardSource: 'golden_globes', categoryKey: 'award_other', winPoints: 0, nominationPoints: 0 },
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
    vi.mocked(getRecommendationSettings).mockReset()
    vi.mocked(updateRecommendationSettings).mockReset()
    vi.mocked(recalculateRecommendationVoteWeights).mockReset()
  })

  afterEach(cleanup)

  it('renders only server-returned candidates and keeps all three coefficients at 100%', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([recommendation()], 1, 26, 2, { minYear: 2000, maxYear: 2025 }))

    render(<RecommendationsView />)

    expect(await screen.findByRole('link', { name: 'Quiet River' })).toBeTruthy()
    expect(screen.queryByText('Unrated feature')).toBeNull()
    expect(screen.queryByText('Series candidate')).toBeNull()
    const scoreWeights = within(screen.getByRole('group', { name: 'Recommendation score weights' }))
    const sliders = scoreWeights.getAllByRole('slider') as HTMLInputElement[]
    expect(sliders.map((slider) => slider.value)).toEqual(['50', '25', '25'])
    expect(screen.getByText('Total weight: 100%')).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
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
      page: 2,
      pageSize: 25,
      ratingWeight: 0.8,
      votesWeight: 0.1,
      awardsWeight: 0.1,
      yearFrom: 2010,
      genres: ['Comedy'],
    }))
  })

  it('shows an empty state when the server returns no candidates', async () => {
    vi.mocked(getRecommendations).mockResolvedValue(recommendationPage([]))

    render(<RecommendationsView />)

    expect(await screen.findByText('No recommendations available')).toBeTruthy()
    expect(screen.getByText('No eligible award-nominated movies are available for this account yet.')).toBeTruthy()
  })

  it('shows recommendation errors and retries with the current coefficient values', async () => {
    vi.mocked(getRecommendations)
      .mockRejectedValueOnce(new Error('Recommendations offline'))
      .mockResolvedValueOnce(recommendationPage([recommendation()]))

    render(<RecommendationsView />)

    expect((await screen.findByRole('alert')).textContent).toContain('Recommendations offline')
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('link', { name: 'Quiet River' })).toBeTruthy()
    expect(getRecommendations).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
    })
  })
})
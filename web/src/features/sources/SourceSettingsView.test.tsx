import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  addSourceUrl,
  enqueuePersonalRatingsEnrichment,
  enqueueManualScan,
  enqueueOscarImport,
  getActiveBackgroundJob,
  getBackgroundJob,
  getBackgroundJobEvents,
  getOmdbDailyUsage,
  getProviderSettings,
  getPersonalRatings,
  getPersonalRatingsEnrichmentJob,
  getSettings,
  getSources,
  getVersion,
  importPersonalRatings,
  removePersonalRating,
  removeSourceUrl,
  replaceSourceUrl,
  updateProviderSettings,
  updateSettings,
} from '../../api/client'
import type { BackgroundJob, OmdbDailyUsage, PersonalRating, ProviderSettings, Settings, SourceProfile, SystemVersion } from '../../api/types'
import { titlePageSize } from '../../shared/titlePagination'
import { OmdbProviderView, PersonalRatingsView, SystemSettingsView } from './ConfigurationSettingsViews'
import { SourceSettingsView } from './SourceSettingsView'

vi.mock('../../api/client', () => ({
  addSourceUrl: vi.fn(),
  enqueuePersonalRatingsEnrichment: vi.fn(),
  enqueueManualScan: vi.fn(),
  enqueueOscarImport: vi.fn(),
  getActiveBackgroundJob: vi.fn(),
  getBackgroundJob: vi.fn(),
  getBackgroundJobEvents: vi.fn(),
  getPersonalRatingsEnrichmentJob: vi.fn(),
  getOmdbDailyUsage: vi.fn(),
  getPersonalRatings: vi.fn(),
  getProviderSettings: vi.fn(),
  getSettings: vi.fn(),
  getSources: vi.fn(),
  getVersion: vi.fn(),
  importPersonalRatings: vi.fn(),
  removePersonalRating: vi.fn(),
  removeSourceUrl: vi.fn(),
  replaceSourceUrl: vi.fn(),
  updateProviderSettings: vi.fn(),
  updateSettings: vi.fn(),
}))

const providerSettings: ProviderSettings = {
  omdbApiKeyConfigured: true,
  omdbDailyRequestLimit: 25,
  updatedAt: null,
}

const systemVersion: SystemVersion = {
  version: '2026.10.01+abc1234',
  commitSha: '0123456789abcdef0123456789abcdef01234567',
  commitDateUtc: '2026-10-01T12:30:00+00:00',
}

function personalRating(imdbId: string, title: string, rating = 8, metadataStatus = 'enriched', isManuallyAdded = false): PersonalRating {
  return {
    imdbId, title, rating, isManuallyAdded, year: 2024, mediaType: 'movie', imdbRating: 7.5, imdbVotes: 1000,
    metascore: null, genres: [], countries: [], director: null, plot: null, posterUrl: null,
    runtime: null, awards: null, boxOffice: null, metadataStatus,
  }
}

function personalRatingsPage(items: PersonalRating[], page = 1, totalPages = 1) {
  return { items, page, pageSize: titlePageSize, totalCount: totalPages * titlePageSize, totalPages }
}

const personalRatingsJob: BackgroundJob = {
  id: 12,
  jobType: 'imdb_title_enrichment',
  trigger: 'manual',
  status: 'queued',
  enqueuedAt: '2026-10-19T10:00:00Z',
  startedAt: null,
  finishedAt: null,
  currentStage: 'queued',
  currentSource: null,
  progressUpdatedAt: null,
  errorCode: null,
  resultSummary: null,
  scanRunId: null,
  inputFileName: null,
}

describe('SourceSettingsView', () => {
  beforeEach(() => {
    vi.mocked(addSourceUrl).mockReset()
    vi.mocked(enqueueManualScan).mockReset()
    vi.mocked(enqueueOscarImport).mockReset()
    vi.mocked(getActiveBackgroundJob).mockReset().mockResolvedValue(null)
    vi.mocked(getBackgroundJob).mockReset()
    vi.mocked(getBackgroundJobEvents).mockReset().mockResolvedValue({ items: [], nextAfterId: 0 })
    vi.mocked(getOmdbDailyUsage).mockReset().mockResolvedValue([])
    vi.mocked(getPersonalRatings).mockReset().mockResolvedValue({
      items: [], page: 1, pageSize: titlePageSize, totalCount: 0, totalPages: 0,
    })
    vi.mocked(getPersonalRatingsEnrichmentJob).mockReset().mockResolvedValue(personalRatingsJob)
    vi.mocked(enqueuePersonalRatingsEnrichment).mockReset().mockResolvedValue(undefined)
    vi.mocked(getProviderSettings).mockReset().mockResolvedValue(providerSettings)
    vi.mocked(getSettings).mockReset().mockResolvedValue({
      excludedGenres: [],
      excludedCountries: [],
      minMovieRating: 0,
      minSeriesRating: 0,
      minImdbVotes: 0,
      updatedAt: null,
    } satisfies Settings)
    vi.mocked(getSources).mockReset().mockResolvedValue([
      { id: 'movie', name: 'Movies', urls: [] },
      { id: 'series_complete', name: 'Complete seasons', urls: [] },
      { id: 'series_ongoing', name: 'Ongoing episodes', urls: [] },
    ] satisfies SourceProfile[])
    vi.mocked(getVersion).mockReset().mockResolvedValue(systemVersion)
    vi.mocked(importPersonalRatings).mockReset()
    vi.mocked(removePersonalRating).mockReset()
    vi.mocked(updateProviderSettings).mockReset().mockResolvedValue(providerSettings)
    vi.mocked(updateSettings).mockReset()
    vi.mocked(removeSourceUrl).mockReset()
    vi.mocked(replaceSourceUrl).mockReset()
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
  })

  it('shows the deployed system version and source commit', async () => {
    render(<SystemSettingsView />)

    expect(await screen.findByText('Version 2026.10.01+abc1234')).toBeTruthy()
    expect(screen.getByText('Commit 0123456789ab')).toBeTruthy()
    expect(screen.getByText('Commit date (UTC) 2026-10-01 12:30:00')).toBeTruthy()
  })

  it('shows per-day OMDb counts and why that day was blocked', async () => {
    vi.mocked(getOmdbDailyUsage).mockResolvedValue([{
      utcDate: '2026-10-01',
      totalRequests: 12,
      rssRequests: 9,
      oscarRequests: 3,
      dailyRequestLimitReached: true,
      providerQuotaExceeded: true,
      lastErrorCode: 'quota_exceeded',
    } satisfies OmdbDailyUsage])

    render(<OmdbProviderView />)

    expect(await screen.findByText('2026-10-01')).toBeTruthy()
    expect(screen.getByText('OMDb quota exceeded; blocked for day')).toBeTruthy()
    expect(screen.getByText('Last 30 UTC days. Counts are reserved HTTP attempts and may include a request interrupted before sending.')).toBeTruthy()
  })

  it('keeps the saved key write-only and clears a replacement after saving', async () => {
    render(<OmdbProviderView />)

    const keyInput = await screen.findByLabelText('OMDb API key') as HTMLInputElement
    expect(keyInput.type).toBe('password')
    expect(keyInput.value).toBe('')
    expect(screen.getByText('Key configured')).toBeTruthy()

    fireEvent.change(keyInput, { target: { value: 'replacement-test-key' } })
    fireEvent.change(screen.getByLabelText('Shared daily HTTP request limit'), { target: { value: '25' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save provider settings' }))

    await waitFor(() => expect(updateProviderSettings).toHaveBeenCalledWith({
      omdbApiKey: 'replacement-test-key',
      clearOmdbApiKey: false,
      omdbDailyRequestLimit: 25,
    }))
    expect(await screen.findByText('Provider settings saved.')).toBeTruthy()
    expect(keyInput.value).toBe('')
  })

  it('uploads IMDb ratings and reports imported and skipped entries', async () => {
    vi.mocked(importPersonalRatings).mockResolvedValue({
      ratingsInFile: 3,
      added: 1,
      updated: 1,
      unchanged: 1,
      totalRatings: 12,
      importedAt: '2026-10-02T10:00:00Z',
      errors: [{ id: 'tt1234567', message: 'Missing rating.' }],
    })

    render(<PersonalRatingsView />)

    const file = new File(['[{"id":"tt14452776","rating":8}]'], 'ratings.json', { type: 'application/json' })
    const fileInput = await screen.findByLabelText('IMDb ratings JSON')
    Object.defineProperty(fileInput, 'files', { configurable: true, value: [file] })
    fireEvent.submit(fileInput.closest('form')!)

    await waitFor(() => expect(importPersonalRatings).toHaveBeenCalledWith(file))
    const importStatus = await screen.findByText((_, element) =>
      element?.getAttribute('role') === 'status' && (element.textContent ?? '').includes('Imported'))
    expect(importStatus.textContent).toContain('1 added, 1 updated, 1 unchanged. 12 ratings stored.')
    expect((await screen.findByText(/Skipped entries with missing ratings:/)).textContent).toContain('tt1234567 (Missing rating.)')
  })

  it('lists personal ratings, paginates, and resets to page one when sorting changes', async () => {
    vi.mocked(getPersonalRatings).mockImplementation(async ({ page, sort }) => personalRatingsPage([
      personalRating(`tt000000${page}`, `Rating page ${page}`, sort === 'asc' ? 2 : 9),
    ], page, 2))

    render(<PersonalRatingsView />)

    expect(await screen.findByRole('link', { name: 'Rating page 1' })).toBeTruthy()
    expect(vi.mocked(getPersonalRatings).mock.calls.at(-1)?.[0]).toEqual({ page: 1, pageSize: titlePageSize, sort: 'desc' })
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(await screen.findByRole('link', { name: 'Rating page 2' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Lowest first' }))
    expect(await screen.findByRole('link', { name: 'Rating page 1' })).toBeTruthy()
    await waitFor(() => expect(getPersonalRatings).toHaveBeenLastCalledWith({ page: 1, pageSize: titlePageSize, sort: 'asc' }))

    const callsBeforeSelectedSort = vi.mocked(getPersonalRatings).mock.calls.length
    fireEvent.click(screen.getByRole('button', { name: 'Lowest first' }))
    expect(vi.mocked(getPersonalRatings).mock.calls).toHaveLength(callsBeforeSelectedSort)
    expect(screen.getByRole('button', { name: 'Lowest first' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('deletes manually added ratings and keeps imported ratings read-only', async () => {
    const manualRating = personalRating('tt1234567', 'Manual title', 8, 'enriched', true)
    const importedRating = personalRating('tt7654321', 'Imported title')
    vi.mocked(getPersonalRatings)
      .mockResolvedValueOnce(personalRatingsPage([manualRating, importedRating]))
      .mockResolvedValueOnce(personalRatingsPage([importedRating]))
    vi.mocked(removePersonalRating).mockResolvedValue(undefined)
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)

    try {
      render(<PersonalRatingsView />)

      const deleteButton = await screen.findByRole('button', { name: 'Delete Manual title personal rating' })
      expect(screen.queryByRole('button', { name: 'Delete Imported title personal rating' })).toBeNull()
      fireEvent.click(deleteButton)

      await waitFor(() => expect(removePersonalRating).toHaveBeenCalledWith('tt1234567'))
      expect(await screen.findByRole('link', { name: 'Imported title' })).toBeTruthy()
      expect(screen.queryByRole('link', { name: 'Manual title' })).toBeNull()
      expect(getPersonalRatings).toHaveBeenCalledTimes(2)
      expect(confirm).toHaveBeenCalledWith('Delete your manually added rating for Manual title? This cannot be undone.')
    } finally {
      confirm.mockRestore()
    }
  })

  it('shows enrichment job status and refreshes ratings when the job completes', async () => {
    vi.useFakeTimers()
    vi.mocked(enqueuePersonalRatingsEnrichment).mockResolvedValue({ jobId: 12, status: 'queued' })
    vi.mocked(getPersonalRatings)
      .mockResolvedValueOnce(personalRatingsPage([personalRating('tt1234567', 'Pending title', 8, 'pending')]))
      .mockResolvedValueOnce(personalRatingsPage([personalRating('tt1234567', 'Resolved title', 8, 'enriched')]))
    vi.mocked(getPersonalRatingsEnrichmentJob)
      .mockResolvedValueOnce(personalRatingsJob)
      .mockResolvedValueOnce({ ...personalRatingsJob, status: 'succeeded', currentStage: 'completed', finishedAt: '2026-10-19T10:00:03Z' })

    render(<PersonalRatingsView />)
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()
    })
    expect(enqueuePersonalRatingsEnrichment).toHaveBeenCalledOnce()
    expect(screen.getByRole('status', { name: 'Personal ratings enrichment status' }).textContent).toContain('queued')
    expect(screen.queryByText('Loading personal IMDb ratings')).toBeNull()

    await act(async () => { await vi.advanceTimersByTimeAsync(2000) })
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()
    })
    expect(screen.getByRole('link', { name: 'Resolved title' })).toBeTruthy()
    expect(screen.getByRole('status', { name: 'Personal ratings enrichment status' }).textContent).toContain('succeeded')
    expect(getPersonalRatings).toHaveBeenCalledTimes(2)
    expect(getPersonalRatingsEnrichmentJob).toHaveBeenCalledTimes(2)
    expect(enqueuePersonalRatingsEnrichment).toHaveBeenCalledOnce()
  })

  it.each([
    [401, 'The sign-in session has expired.'],
    [403, 'This account is not allowed to import IMDb ratings.'],
  ])('shows API %s errors on the personal ratings form', async (status, message) => {
    vi.mocked(importPersonalRatings).mockRejectedValue(Object.assign(new Error(message), { status }))

    render(<PersonalRatingsView />)

    const file = new File(['[]'], 'ratings.json', { type: 'application/json' })
    const fileInput = await screen.findByLabelText('IMDb ratings JSON')
    Object.defineProperty(fileInput, 'files', { configurable: true, value: [file] })
    fireEvent.submit(fileInput.closest('form')!)

    expect((await screen.findByRole('alert')).textContent).toContain(message)
  })

  it('loads only RSS and matching data on torrent settings', async () => {
    render(<SourceSettingsView />)

    expect(await screen.findByRole('heading', { name: 'RSS profiles' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Preview RSS' })).toBeNull()
    expect(screen.getAllByRole('button', { name: 'Add URL' })).toHaveLength(3)
    expect(getSources).toHaveBeenCalledOnce()
    expect(getSettings).toHaveBeenCalledOnce()
    expect(getProviderSettings).not.toHaveBeenCalled()
    expect(getOmdbDailyUsage).not.toHaveBeenCalled()
    expect(getVersion).not.toHaveBeenCalled()
    expect(importPersonalRatings).not.toHaveBeenCalled()
  })
})
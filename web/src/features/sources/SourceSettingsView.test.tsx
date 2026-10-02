import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  addSourceUrl,
  enqueueManualScan,
  enqueueOscarImport,
  getActiveBackgroundJob,
  getBackgroundJob,
  getBackgroundJobEvents,
  getOmdbDailyUsage,
  getProviderSettings,
  getSettings,
  getSources,
  getVersion,
  removeSourceUrl,
  replaceSourceUrl,
  updateProviderSettings,
  updateSettings,
} from '../../api/client'
import type { OmdbDailyUsage, ProviderSettings, Settings, SourceProfile, SystemVersion } from '../../api/types'
import { SourceSettingsView } from './SourceSettingsView'

vi.mock('../../api/client', () => ({
  addSourceUrl: vi.fn(),
  enqueueManualScan: vi.fn(),
  enqueueOscarImport: vi.fn(),
  getActiveBackgroundJob: vi.fn(),
  getBackgroundJob: vi.fn(),
  getBackgroundJobEvents: vi.fn(),
  getOmdbDailyUsage: vi.fn(),
  getProviderSettings: vi.fn(),
  getSettings: vi.fn(),
  getSources: vi.fn(),
  getVersion: vi.fn(),
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

describe('SourceSettingsView', () => {
  beforeEach(() => {
    vi.mocked(addSourceUrl).mockReset()
    vi.mocked(enqueueManualScan).mockReset()
    vi.mocked(enqueueOscarImport).mockReset()
    vi.mocked(getActiveBackgroundJob).mockReset().mockResolvedValue(null)
    vi.mocked(getBackgroundJob).mockReset()
    vi.mocked(getBackgroundJobEvents).mockReset().mockResolvedValue({ items: [], nextAfterId: 0 })
    vi.mocked(getOmdbDailyUsage).mockReset().mockResolvedValue([])
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
    vi.mocked(updateProviderSettings).mockReset().mockResolvedValue(providerSettings)
    vi.mocked(updateSettings).mockReset()
    vi.mocked(removeSourceUrl).mockReset()
    vi.mocked(replaceSourceUrl).mockReset()
  })

  afterEach(() => cleanup())

  it('shows the deployed system version and source commit', async () => {
    render(<SourceSettingsView />)

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

    render(<SourceSettingsView />)

    expect(await screen.findByText('2026-10-01')).toBeTruthy()
    expect(screen.getByText('OMDb quota exceeded; blocked for day')).toBeTruthy()
    expect(screen.getByText('Last 30 UTC days. Counts are reserved HTTP attempts and may include a request interrupted before sending.')).toBeTruthy()
  })

  it('keeps the saved key write-only and clears a replacement after saving', async () => {
    render(<SourceSettingsView />)

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
})
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createSource,
  getProviderSettings,
  getSettings,
  getSources,
  getVersion,
  updateProviderSettings,
  updateSettings,
  updateSource,
} from '../../api/client'
import type { ProviderSettings, Settings, SystemVersion } from '../../api/types'
import { SourceSettingsView } from './SourceSettingsView'

vi.mock('../../api/client', () => ({
  createSource: vi.fn(),
  getProviderSettings: vi.fn(),
  getSettings: vi.fn(),
  getSources: vi.fn(),
  getVersion: vi.fn(),
  updateProviderSettings: vi.fn(),
  updateSettings: vi.fn(),
  updateSource: vi.fn(),
}))

const providerSettings: ProviderSettings = {
  omdbApiKeyConfigured: true,
  omdbDailyRequestLimit: 25,
  oscarEnrichmentMaxFilmsPerRun: 0,
  oscarEnrichmentMaxRequestsPerDay: 0,
  updatedAt: null,
}

const systemVersion: SystemVersion = {
  version: '2026.10.01+abc1234',
  commitSha: '0123456789abcdef0123456789abcdef01234567',
  commitDateUtc: '2026-10-01T12:30:00+00:00',
}

describe('SourceSettingsView', () => {
  beforeEach(() => {
    vi.mocked(createSource).mockReset()
    vi.mocked(getProviderSettings).mockReset().mockResolvedValue(providerSettings)
    vi.mocked(getSettings).mockReset().mockResolvedValue({
      excludedGenres: [],
      excludedCountries: [],
      minMovieRating: 0,
      minSeriesRating: 0,
      minImdbVotes: 0,
      updatedAt: null,
    } satisfies Settings)
    vi.mocked(getSources).mockReset().mockResolvedValue([])
    vi.mocked(getVersion).mockReset().mockResolvedValue(systemVersion)
    vi.mocked(updateProviderSettings).mockReset().mockResolvedValue(providerSettings)
    vi.mocked(updateSettings).mockReset()
    vi.mocked(updateSource).mockReset()
  })

  afterEach(() => cleanup())

  it('shows the deployed system version and source commit', async () => {
    render(<SourceSettingsView />)

    expect(await screen.findByText('Version 2026.10.01+abc1234')).toBeTruthy()
    expect(screen.getByText('Commit 0123456789ab')).toBeTruthy()
    expect(screen.getByText('Commit date (UTC) 2026-10-01')).toBeTruthy()
  })

  it('keeps the saved key write-only and clears a replacement after saving', async () => {
    render(<SourceSettingsView />)

    const keyInput = await screen.findByLabelText('OMDb API key') as HTMLInputElement
    expect(keyInput.type).toBe('password')
    expect(keyInput.value).toBe('')
    expect(screen.getByText('Key configured')).toBeTruthy()

    fireEvent.change(keyInput, { target: { value: 'replacement-test-key' } })
    fireEvent.change(screen.getByLabelText('Shared daily HTTP request limit'), { target: { value: '25' } })
    fireEvent.change(screen.getByLabelText('Oscar films per run'), { target: { value: '10' } })
    fireEvent.change(screen.getByLabelText('Oscar daily HTTP limit'), { target: { value: '8' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save provider settings' }))

    await waitFor(() => expect(updateProviderSettings).toHaveBeenCalledWith({
      omdbApiKey: 'replacement-test-key',
      clearOmdbApiKey: false,
      omdbDailyRequestLimit: 25,
      oscarEnrichmentMaxFilmsPerRun: 10,
      oscarEnrichmentMaxRequestsPerDay: 8,
    }))
    expect(await screen.findByText('Provider settings saved.')).toBeTruthy()
    expect(keyInput.value).toBe('')
  })
})
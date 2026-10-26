import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getMovieTablesDashboard } from '../../api/client'
import type { MovieTablesDashboard } from '../../api/types'
import { MovieTablesDashboardView } from './MovieTablesDashboardView'

vi.mock('../../api/client', () => ({
  getMovieTablesDashboard: vi.fn(),
}))

const sampleDashboard: MovieTablesDashboard = {
  imdb: {
    totalCount: 1540,
    byMediaType: { movie: 1200, series: 340 },
    byMetadataStatus: { enriched: 1450, pending: 80, temporary_error: 10 },
    enrichedCount: 1450,
    pendingCount: 80,
  },
  goldenGlobes: {
    totalCount: 920,
    byNomineeType: { movie: 750, series: 170 },
    byEnrichmentStatus: { enriched: 850, pending: 60, problem: 10 },
    withImdbLinkCount: 860,
    withoutImdbLinkCount: 60,
    totalNominationsCount: 1350,
  },
  oscar: {
    totalCount: 640,
    byEnrichmentStatus: { enriched: 610, pending: 25, not_found: 5 },
    withImdbLinkCount: 620,
    withoutImdbLinkCount: 20,
    totalNominationsCount: 2800,
  },
  generatedAtUtc: '2026-10-24T12:00:00Z',
}

describe('MovieTablesDashboardView', () => {
  beforeEach(() => {
    vi.mocked(getMovieTablesDashboard).mockReset().mockResolvedValue(sampleDashboard)
  })

  afterEach(() => {
    cleanup()
  })

  it('renders table totals and status breakdowns', async () => {
    render(<MovieTablesDashboardView />)

    expect(await screen.findByRole('heading', { name: 'System movie tables overview' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'IMDb titles (imdb_titles)' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Golden Globes titles (golden_globe_titles)' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Oscar movies (oscar_movies)' })).toBeTruthy()
    expect(screen.getAllByText('1,540').length).toBeGreaterThan(0)
    expect(screen.getAllByText('1,350').length).toBeGreaterThan(0)
    expect(screen.getAllByText('2,800').length).toBeGreaterThan(0)
  })

  it('refreshes dashboard statistics on button click', async () => {
    render(<MovieTablesDashboardView />)

    expect(await screen.findByRole('heading', { name: 'System movie tables overview' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Refresh statistics' }))

    await waitFor(() => expect(getMovieTablesDashboard).toHaveBeenCalledTimes(2))
  })

  it('renders an error state and retries the request', async () => {
    vi.mocked(getMovieTablesDashboard).mockRejectedValueOnce(new Error('Server unavailable'))

    render(<MovieTablesDashboardView />)

    expect(await screen.findByText('Server unavailable')).toBeTruthy()
    vi.mocked(getMovieTablesDashboard).mockResolvedValueOnce(sampleDashboard)
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('heading', { name: 'System movie tables overview' })).toBeTruthy()
  })
})
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ApiError,
  enqueueFailedEntryRecheck,
  enqueueGoldenGlobeEnrichment,
  enqueueGoldenGlobeImport,
  enqueueImdbTitleImport,
  enqueueImdbRatingRefresh,
  enqueueManualScan,
  enqueueOscarImport,
  getActiveBackgroundJob,
  getBackgroundJob,
  getBackgroundJobEvents,
  getLatestImdbRatingRefresh,
} from '../../api/client'
import type { BackgroundJob } from '../../api/types'
import { BackgroundIngestionPanel } from './BackgroundIngestionPanel'

vi.mock('../../api/client', () => ({
  ApiError: class extends Error {
    readonly status: number

    constructor(message: string, status: number) {
      super(message)
      this.status = status
    }
  },
  enqueueFailedEntryRecheck: vi.fn(),
  enqueueGoldenGlobeEnrichment: vi.fn(),
  enqueueGoldenGlobeImport: vi.fn(),
  enqueueImdbTitleImport: vi.fn(),
  enqueueImdbRatingRefresh: vi.fn(),
  enqueueManualScan: vi.fn(),
  enqueueOscarImport: vi.fn(),
  getActiveBackgroundJob: vi.fn(),
  getBackgroundJob: vi.fn(),
  getBackgroundJobEvents: vi.fn(),
  getLatestImdbRatingRefresh: vi.fn(),
}))

const queuedJob: BackgroundJob = {
  id: 7,
  jobType: 'rss_scan',
  trigger: 'manual',
  status: 'queued',
  enqueuedAt: '2026-10-01T12:00:00Z',
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

describe('BackgroundIngestionPanel', () => {
  beforeEach(() => {
    vi.mocked(enqueueFailedEntryRecheck).mockReset().mockResolvedValue({ id: 11, status: 'queued', statusUrl: '/api/background-jobs/11' })
    vi.mocked(enqueueGoldenGlobeEnrichment).mockReset().mockResolvedValue({ id: 9, status: 'queued', statusUrl: '/api/background-jobs/9' })
    vi.mocked(enqueueGoldenGlobeImport).mockReset().mockResolvedValue({ id: 10, status: 'queued', statusUrl: '/api/background-jobs/10' })
    vi.mocked(enqueueImdbTitleImport).mockReset().mockResolvedValue({ id: 12, status: 'queued', statusUrl: '/api/background-jobs/12' })
    vi.mocked(enqueueImdbRatingRefresh).mockReset().mockResolvedValue({ id: 13, status: 'queued', statusUrl: '/api/background-jobs/13' })
    vi.mocked(enqueueManualScan).mockReset().mockResolvedValue({ id: 7, status: 'queued', statusUrl: '/api/background-jobs/7' })
    vi.mocked(enqueueOscarImport).mockReset().mockResolvedValue({ id: 8, status: 'queued', statusUrl: '/api/background-jobs/8' })
    vi.mocked(getActiveBackgroundJob).mockReset().mockResolvedValue(null)
    vi.mocked(getBackgroundJob).mockReset().mockResolvedValue(queuedJob)
    vi.mocked(getBackgroundJobEvents).mockReset().mockResolvedValue({ items: [], nextAfterId: 0 })
    vi.mocked(getLatestImdbRatingRefresh).mockReset().mockResolvedValue(null)
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('queues a scan after confirmation and opens its job details', async () => {
    vi.stubGlobal('confirm', vi.fn(() => true))
    render(<BackgroundIngestionPanel mode="torrent" onOpenHistory={vi.fn()} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Start scan' }))

    await screen.findByRole('dialog')
    expect(enqueueManualScan).toHaveBeenCalledOnce()
    expect(screen.getByText(/Job #/)).toBeTruthy()
  })

  it('queues a failed-entry recheck after confirmation and opens its job details', async () => {
    vi.stubGlobal('confirm', vi.fn(() => true))
    vi.mocked(getBackgroundJob).mockResolvedValue({
      ...queuedJob,
      status: 'partial',
      currentStage: 'recheck_completed',
      resultSummary: {
        rss: {},
        recheck: { retryableEntriesSelected: 2, entriesUnavailable: 1 },
      },
    })
    render(<BackgroundIngestionPanel mode="torrent" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Recheck failed' }))

    await screen.findByRole('dialog')
    expect(enqueueFailedEntryRecheck).toHaveBeenCalledOnce()
    expect(screen.getByRole('heading', { name: 'Failed torrent recheck' })).toBeTruthy()
    expect(screen.getByText('RECHECK ITEMS').nextElementSibling?.textContent).toBe('2')
    expect(screen.getByText('UNAVAILABLE').nextElementSibling?.textContent).toBe('1')
  })

  it('restores an existing active job and stops polling when details close', async () => {
    vi.mocked(getActiveBackgroundJob).mockResolvedValue({ ...queuedJob, status: 'running' })
    const clearInterval = vi.spyOn(window, 'clearInterval')
    render(<BackgroundIngestionPanel mode="awards" />)

    fireEvent.click(await screen.findByRole('button', { name: 'View job' }))
    await screen.findByRole('dialog')
    await waitFor(() => expect(window.setInterval).toBeDefined())
    fireEvent.click(screen.getByRole('button', { name: 'Close job details' }))

    await waitFor(() => expect(clearInterval).toHaveBeenCalled())
  })

  it('recovers the active job after a scan conflict without sending another scan', async () => {
    vi.stubGlobal('confirm', vi.fn(() => true))
    vi.mocked(enqueueManualScan).mockRejectedValue(new ApiError('An RSS scan is already queued or running.', 409))
    vi.mocked(getActiveBackgroundJob)
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce({ ...queuedJob, status: 'running' })
    render(<BackgroundIngestionPanel mode="torrent" onOpenHistory={vi.fn()} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Start scan' }))

    await screen.findByRole('dialog')
    expect(enqueueManualScan).toHaveBeenCalledOnce()
    expect(getActiveBackgroundJob).toHaveBeenCalledTimes(2)
  })

  it('rejects an oversized upload before submitting it', async () => {
    render(<BackgroundIngestionPanel mode="awards" />)
    await screen.findByText('No active ingestion job.')
    const file = new File(['x'], 'large.csv', { type: 'text/csv' })
    Object.defineProperty(file, 'size', { value: 10 * 1024 * 1024 + 1 })
    fireEvent.change(screen.getByLabelText('Oscar dataset'), { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Queue import' }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(enqueueOscarImport).not.toHaveBeenCalled()
  })

  it('queues Golden Globes enrichment after confirmation', async () => {
    vi.stubGlobal('confirm', vi.fn(() => true))
    render(<BackgroundIngestionPanel mode="awards" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Enrich Golden Globes films' }))

    await screen.findByRole('dialog')
    expect(enqueueGoldenGlobeEnrichment).toHaveBeenCalledOnce()
  })

  it('queues an IMDb TSV and tracks its import and catalog enrichment jobs', async () => {
    const importJob: BackgroundJob = {
      ...queuedJob,
      id: 12,
      jobType: 'imdb_title_import',
      status: 'succeeded',
      resultSummary: {
        import: { rowsRead: 2, rowsSkippedByType: 0, rowsInserted: 1, rowsUpdated: 1, rowsIgnored: 0 },
        enrichmentJobId: 18,
      },
    }
    const enrichmentJob: BackgroundJob = {
      ...queuedJob,
      id: 18,
      jobType: 'imdb_catalog_enrichment',
      status: 'running',
      currentStage: 'imdb_catalog_enrichment',
    }
    vi.mocked(getBackgroundJob).mockImplementation(async (id) => id === 18 ? enrichmentJob : importJob)
    render(<BackgroundIngestionPanel mode="awards" />)
    await screen.findByText('No active ingestion job.')
    const file = new File(['tconst\taverageRating\tnumVotes'], 'title.ratings.filtered.movies-series.tsv', { type: 'text/tab-separated-values' })

    fireEvent.change(screen.getByLabelText('IMDb title ratings TSV'), { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Queue IMDb import' }))

    await screen.findByRole('dialog')
    expect(enqueueImdbTitleImport).toHaveBeenCalledWith(file)
    expect(screen.getByRole('heading', { name: 'IMDb title dataset import' })).toBeTruthy()
    expect(screen.getByText('ROWS UPDATED').nextElementSibling?.textContent).toBe('1')
    const enrichmentStatus = await screen.findByRole('region', { name: 'IMDb catalog enrichment job' })
    expect(within(enrichmentStatus).getByText('running')).toBeTruthy()
    expect(getBackgroundJob).toHaveBeenCalledWith(18)
  })

  it('queues an IMDb ratings refresh and displays its summary', async () => {
    vi.mocked(getBackgroundJob).mockResolvedValue({
      ...queuedJob, id: 13, jobType: 'imdb_rating_refresh', status: 'succeeded',
      resultSummary: { UpdatedRows: 4, ConcurrentConflicts: 2, ProcessingMilliseconds: 1200, DatabaseApplyMilliseconds: 35, FinalizationMilliseconds: 5, JobTotalMilliseconds: 1240, Message: '4 titles updated.' },
    })
    render(<BackgroundIngestionPanel mode="awards" />)
    fireEvent.click(await screen.findByRole('button', { name: 'Refresh IMDb ratings' }))
    await screen.findByRole('dialog')
    expect(enqueueImdbRatingRefresh).toHaveBeenCalledOnce()
    expect(screen.getByText('UPDATED').nextElementSibling?.textContent).toBe('4')
    expect(screen.getByText('CONFLICTS').nextElementSibling?.textContent).toBe('2')
    expect(screen.getByText('DATASET PROCESSING').nextElementSibling?.textContent).toBe('1200 ms')
    expect(screen.getByText('4 titles updated.')).toBeTruthy()
  })

  it('opens the latest completed IMDb refresh', async () => {
    const completed: BackgroundJob = { ...queuedJob, id: 13, jobType: 'imdb_rating_refresh', status: 'succeeded' }
    vi.mocked(getLatestImdbRatingRefresh).mockResolvedValue(completed)
    vi.mocked(getBackgroundJob).mockResolvedValue(completed)
    render(<BackgroundIngestionPanel mode="awards" />)
    fireEvent.click(await screen.findByRole('button', { name: 'View latest IMDb refresh' }))
    expect(await screen.findByRole('heading', { name: 'IMDb ratings refresh' })).toBeTruthy()
    expect(getBackgroundJob).toHaveBeenCalledWith(13)
  })

  it('rejects non-TSV, empty, and oversized IMDb files before submitting', async () => {
    render(<BackgroundIngestionPanel mode="awards" />)
    await screen.findByText('No active ingestion job.')
    const input = screen.getByLabelText('IMDb title ratings TSV')
    const oversizedFile = new File(['x'], 'large.tsv', { type: 'text/tab-separated-values' })
    Object.defineProperty(oversizedFile, 'size', { value: 10 * 1024 * 1024 + 1 })
    const cases = [
      [new File(['data'], 'ratings.csv', { type: 'text/csv' }), 'Choose a file with the .tsv extension.'],
      [new File([], 'empty.tsv', { type: 'text/tab-separated-values' }), 'The TSV file must be non-empty.'],
      [oversizedFile, 'The TSV file must be no larger than 10 MiB.'],
    ] as const

    for (const [file, message] of cases) {
      fireEvent.change(input, { target: { files: [file] } })
      fireEvent.click(screen.getByRole('button', { name: 'Queue IMDb import' }))
      await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(message))
    }
    expect(enqueueImdbTitleImport).not.toHaveBeenCalled()
  })

  it('shows server upload errors for IMDb files', async () => {
    vi.mocked(enqueueImdbTitleImport).mockRejectedValue(new ApiError('The upload exceeds the configured limit.', 413))
    render(<BackgroundIngestionPanel mode="awards" />)
    await screen.findByText('No active ingestion job.')
    const file = new File(['dataset'], 'ratings.tsv', { type: 'text/tab-separated-values' })

    fireEvent.change(screen.getByLabelText('IMDb title ratings TSV'), { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Queue IMDb import' }))

    expect((await screen.findByRole('alert')).textContent).toContain('The upload exceeds the configured limit.')
  })

  it('queues a selected Golden Globes dataset for import', async () => {
    render(<BackgroundIngestionPanel mode="awards" />)
    await screen.findByText('No active ingestion job.')
    const file = new File(['nominee_type,year,winner,award,title'], 'globes.csv', { type: 'text/csv' })

    fireEvent.change(screen.getByLabelText('Golden Globes dataset'), { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Queue Golden Globes import' }))

    await screen.findByRole('dialog')
    expect(enqueueGoldenGlobeImport).toHaveBeenCalledWith(file, 1980)
  })

  it('keeps RSS scan controls out of awards ingestion', async () => {
    render(<BackgroundIngestionPanel mode="awards" />)

    expect(await screen.findByText('No active ingestion job.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Start scan' })).toBeNull()
    expect(screen.getByLabelText('Oscar dataset')).toBeTruthy()
  })

  it('keeps award imports and enrichment out of torrent settings', async () => {
    render(<BackgroundIngestionPanel mode="torrent" onOpenHistory={vi.fn()} />)

    expect(await screen.findByText('No active ingestion job.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Start scan' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Recheck failed' })).toBeTruthy()
    expect(screen.queryByLabelText('Oscar dataset')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Enrich Golden Globes films' })).toBeNull()
  })
})
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ApiError,
  enqueueManualScan,
  enqueueOscarImport,
  getActiveBackgroundJob,
  getBackgroundJob,
  getBackgroundJobEvents,
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
  enqueueManualScan: vi.fn(),
  enqueueOscarImport: vi.fn(),
  getActiveBackgroundJob: vi.fn(),
  getBackgroundJob: vi.fn(),
  getBackgroundJobEvents: vi.fn(),
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
    vi.mocked(enqueueManualScan).mockReset().mockResolvedValue({ id: 7, status: 'queued', statusUrl: '/api/background-jobs/7' })
    vi.mocked(enqueueOscarImport).mockReset().mockResolvedValue({ id: 8, status: 'queued', statusUrl: '/api/background-jobs/8' })
    vi.mocked(getActiveBackgroundJob).mockReset().mockResolvedValue(null)
    vi.mocked(getBackgroundJob).mockReset().mockResolvedValue(queuedJob)
    vi.mocked(getBackgroundJobEvents).mockReset().mockResolvedValue({ items: [], nextAfterId: 0 })
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('queues a scan after confirmation and opens its job details', async () => {
    vi.stubGlobal('confirm', vi.fn(() => true))
    render(<BackgroundIngestionPanel onOpenHistory={vi.fn()} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Start scan' }))

    await screen.findByRole('dialog')
    expect(enqueueManualScan).toHaveBeenCalledOnce()
    expect(screen.getByText(/Job #/)).toBeTruthy()
  })

  it('restores an existing active job and stops polling when details close', async () => {
    vi.mocked(getActiveBackgroundJob).mockResolvedValue({ ...queuedJob, status: 'running' })
    const clearInterval = vi.spyOn(window, 'clearInterval')
    render(<BackgroundIngestionPanel onOpenHistory={vi.fn()} />)

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
    render(<BackgroundIngestionPanel onOpenHistory={vi.fn()} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Start scan' }))

    await screen.findByRole('dialog')
    expect(enqueueManualScan).toHaveBeenCalledOnce()
    expect(getActiveBackgroundJob).toHaveBeenCalledTimes(2)
  })

  it('rejects an oversized upload before submitting it', async () => {
    render(<BackgroundIngestionPanel onOpenHistory={vi.fn()} />)
    await screen.findByText('No active ingestion job.')
    const file = new File(['x'], 'large.csv', { type: 'text/csv' })
    Object.defineProperty(file, 'size', { value: 10 * 1024 * 1024 + 1 })
    fireEvent.change(screen.getByLabelText('Oscar dataset'), { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Queue import' }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(enqueueOscarImport).not.toHaveBeenCalled()
  })
})
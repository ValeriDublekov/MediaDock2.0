import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getParseLogs, getScanRuns } from '../../api/client'
import type { PageResponse, ParseLog, ScanRun } from '../../api/types'
import { HistoryView } from './HistoryView'

vi.mock('../../api/client', () => ({
  getParseLogs: vi.fn(),
  getScanRuns: vi.fn(),
}))

function createLog(overrides: Partial<ParseLog>): ParseLog {
  return {
    id: 1,
    sourceId: 1,
    sourceName: 'Movies',
    sourceItemKey: 'item-1',
    rawTitle: 'Example Film (2024)',
    feedName: 'Movies',
    parsedSuccessfully: true,
    parsedTitle: 'Example Film',
    parsedYear: 2024,
    lookupTitles: [],
    omdbStatus: 'found',
    ignored: false,
    ignoreReason: null,
    errorMessage: null,
    decision: null,
    processedAt: '2026-10-07T12:00:00Z',
    retryState: 'resolved',
    attemptCount: 1,
    lastAttemptAt: '2026-10-07T12:00:00Z',
    feedType: 'movie',
    sourcePublishedAt: null,
    observedAt: '2026-10-07T12:00:00Z',
    eventKind: 'ingestion',
    ...overrides,
  }
}

function createPage<T>(items: T[]): PageResponse<T> {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length > 0 ? 1 : 0 }
}

describe('HistoryView parse diagnosis', () => {
  afterEach(() => {
    cleanup()
    vi.resetAllMocks()
  })

  it('separates parse failures from OMDb misses and shows every lookup name', async () => {
    vi.mocked(getParseLogs).mockResolvedValue(createPage([
      createLog({
        id: 1,
        parsedSuccessfully: false,
        parsedTitle: null,
        lookupTitles: [],
        omdbStatus: 'not_requested',
        ignored: true,
        ignoreReason: 'empty_title',
        decision: null,
        attemptCount: 0,
        lastAttemptAt: null,
        observedAt: null,
        retryState: 'retryable',
      }),
      createLog({
        id: 2,
        rawTitle: 'Локално име / English Title (2024)',
        parsedTitle: 'English Title',
        lookupTitles: ['English Title', 'Локално име'],
        omdbStatus: 'confirmed_not_found',
        ignored: true,
        ignoreReason: 'metadata_not_found',
        decision: 'fallback_not_found|y_bracket',
      }),
    ]))
    vi.mocked(getScanRuns).mockResolvedValue(createPage<ScanRun>([]))

    render(<HistoryView scanRunId={42} />)

    expect(await screen.findByText('Parsing failed')).toBeTruthy()
    expect(screen.getByText('OMDb found no result')).toBeTruthy()
    expect(screen.getByText('Lookup names: English Title → Локално име')).toBeTruthy()
  })
})
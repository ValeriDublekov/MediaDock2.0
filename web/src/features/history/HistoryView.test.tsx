import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { applyParseLogImdbCandidate, getParseLogs, getScanRuns, searchParseLogImdbCandidates } from '../../api/client'
import type { ImdbCandidateSearch, PageResponse, ParseLog, ScanRun } from '../../api/types'
import { HistoryView } from './HistoryView'

vi.mock('../../api/client', () => ({
  getParseLogs: vi.fn(),
  getScanRuns: vi.fn(),
  searchParseLogImdbCandidates: vi.fn(),
  lookupParseLogImdbCandidate: vi.fn(),
  applyParseLogImdbCandidate: vi.fn(),
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
    canRetryMatch: false,
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

  it('lets an administrator apply a searched IMDb candidate from an unresolved parse log', async () => {
    const entry = createLog({ id: 42, retryState: 'retryable', omdbStatus: 'not_requested', canRetryMatch: true })
    const candidates: ImdbCandidateSearch = {
      status: 'found',
      candidates: [{ title: 'Example Film', year: 2025, imdbId: 'tt87654321', sourceType: 'movie' }],
      totalResults: 12,
      errorCode: null,
    }
    vi.mocked(getParseLogs).mockResolvedValue(createPage([entry]))
    vi.mocked(getScanRuns).mockResolvedValue(createPage<ScanRun>([]))
    vi.mocked(searchParseLogImdbCandidates).mockResolvedValue(candidates)
    vi.mocked(applyParseLogImdbCandidate).mockResolvedValue({ torrentTitleId: 17, imdbId: 'tt87654321' })

    render(<HistoryView scanRunId={99} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Retry IMDb match for Example Film' }))

    expect(screen.getAllByText('Example Film (2024)').length).toBeGreaterThan(0)
    expect(searchParseLogImdbCandidates).not.toHaveBeenCalled()
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Search' }))
    expect(await screen.findByRole('link', { name: 'IMDb' })).toBeTruthy()
    expect(screen.getByText('Page 1 of 2')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Next candidate page' }))
    await waitFor(() => expect(searchParseLogImdbCandidates).toHaveBeenLastCalledWith(42, 'Example Film', 2))
    expect(await screen.findByText('Page 2 of 2')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Use match' }))

    await waitFor(() => expect(applyParseLogImdbCandidate).toHaveBeenCalledWith(42, 'tt87654321'))
    await waitFor(() => expect(getParseLogs).toHaveBeenCalledTimes(2))
  })

  it('does not offer retry matching for feed-level errors without a torrent URL', async () => {
    vi.mocked(getParseLogs).mockResolvedValue(createPage([createLog({
      id: 43,
      rawTitle: '',
      parsedSuccessfully: false,
      parsedTitle: null,
      retryState: 'retryable',
      ignoreReason: 'feed_error',
      eventKind: 'feed',
      canRetryMatch: false,
    })]))
    vi.mocked(getScanRuns).mockResolvedValue(createPage<ScanRun>([]))

    render(<HistoryView scanRunId={100} />)

    expect(await screen.findByText('RSS feed error')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Retry IMDb match/ })).toBeNull()
  })
})
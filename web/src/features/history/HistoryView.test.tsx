import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { applyParseLogImdbCandidate, deleteOldOperationalHistory, getParseLogs, getScanRuns, searchParseLogImdbCandidates } from '../../api/client'
import type { ImdbCandidateSearch, PageResponse, ParseLog, ScanRun } from '../../api/types'
import { HistoryView } from './HistoryView'

vi.mock('../../api/client', () => ({
  getParseLogs: vi.fn(),
  getScanRuns: vi.fn(),
  deleteOldOperationalHistory: vi.fn(),
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

  it('opens on failed parse entries by default and offers success and all outcomes', async () => {
    vi.mocked(getParseLogs).mockResolvedValue(createPage([createLog({
      retryState: 'terminal',
      feedType: 'movie',
    })]))

    render(<HistoryView />)

    await waitFor(() => expect(getParseLogs).toHaveBeenCalledWith({
      page: 1,
      pageSize: 20,
      ignored: true,
    }))
    const outcomeFilter = screen.getByLabelText('Outcome') as HTMLSelectElement
    expect(outcomeFilter.value).toBe('failed')
    expect(within(outcomeFilter).getAllByRole('option').map((option) => option.textContent)).toEqual(['Failed', 'Success', 'All'])
    expect(screen.getByRole('columnheader', { name: 'TYPE' })).toBeTruthy()
    expect(getScanRuns).not.toHaveBeenCalled()

    fireEvent.change(outcomeFilter, { target: { value: 'success' } })
    await waitFor(() => expect(getParseLogs).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      ignored: false,
    }))
    fireEvent.change(outcomeFilter, { target: { value: 'all' } })
    await waitFor(() => expect(getParseLogs).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
    }))
  })

  it('shows known torrent URLs as skipped instead of parsing failures', async () => {
    vi.mocked(getParseLogs).mockResolvedValue(createPage([createLog({
      parsedSuccessfully: false,
      parsedTitle: null,
      omdbStatus: 'not_requested',
      decision: 'known_torrent_url',
    })]))
    vi.mocked(getScanRuns).mockResolvedValue(createPage<ScanRun>([]))

    render(<HistoryView scanRunId={42} />)

    expect(await screen.findByText('Skipped')).toBeTruthy()
    expect(screen.getByText('Already in catalog')).toBeTruthy()
    expect(screen.getByText('Decision: Known torrent URL')).toBeTruthy()
    expect(screen.queryByText('Parsing failed')).toBeNull()
  })

  it('shows the raw torrent title, parsed title, and year in separate columns', async () => {
    const entry = createLog({ rawTitle: 'Example Film (2024) [1080p]', parsedTitle: 'Example Film', parsedYear: 2024 })
    vi.mocked(getParseLogs).mockResolvedValue(createPage([entry]))
    vi.mocked(getScanRuns).mockResolvedValue(createPage<ScanRun>([]))

    render(<HistoryView scanRunId={42} />)

    const row = await screen.findByRole('row', { name: /Example Film \(2024\) \[1080p\]/ })
    const cells = within(row).getAllByRole('cell')
    expect(cells[0].textContent).toBe(entry.rawTitle)
    expect(cells[1].textContent).toBe(entry.parsedTitle)
    expect(cells[2].textContent).toBe('2024')
  })

  it('confirms deletion of history older than the selected days and refreshes scans', async () => {
    vi.mocked(getParseLogs).mockResolvedValue(createPage<ParseLog>([]))
    vi.mocked(getScanRuns).mockResolvedValue(createPage<ScanRun>([]))
    vi.mocked(deleteOldOperationalHistory).mockResolvedValue({
      olderThanDays: 45,
      cutoff: '2026-09-06T12:00:00Z',
      deletedScanRuns: 2,
      deletedParseLogs: 14,
    })
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)

    render(<HistoryView />)
    fireEvent.click(screen.getByRole('button', { name: 'Scan runs' }))
    fireEvent.change(await screen.findByLabelText('Delete older than (days)'), { target: { value: '45' } })
    fireEvent.click(screen.getByRole('button', { name: 'Delete old logs' }))

    await waitFor(() => expect(deleteOldOperationalHistory).toHaveBeenCalledWith(45))
    expect(confirm).toHaveBeenCalledWith('Delete scan and parse history older than 45 days? This cannot be undone.')
    expect(await screen.findByText('Deleted 2 scan runs and 14 parse logs older than 45 days.')).toBeTruthy()
    await waitFor(() => expect(getScanRuns).toHaveBeenCalledTimes(2))
    confirm.mockRestore()
  })

  it('lets an administrator apply a searched IMDb candidate from an unresolved parse log', async () => {
    const entry = createLog({ id: 42, retryState: 'retryable', omdbStatus: 'not_requested', canRetryMatch: true })
    const candidates: ImdbCandidateSearch = {
      status: 'found',
      candidates: [{ title: 'Example Film', year: 2025, imdbId: 'tt87654321', sourceType: 'movie' }],
      totalResults: 12,
      errorCode: null,
    }
    vi.mocked(getParseLogs)
      .mockResolvedValueOnce(createPage([entry]))
      .mockResolvedValueOnce(createPage([createLog({
        ...entry,
        omdbStatus: 'found',
        ignored: false,
        decision: 'manual_imdb_match',
        retryState: 'resolved',
        canRetryMatch: false,
      })]))
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
    const resolvedRow = await screen.findByRole('row', { name: /Example Film/ })
    expect(within(resolvedRow).getByText('Resolved')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Retry IMDb match/ })).toBeNull()
  })

  it('shows feed-level errors in the all view without offering retry matching', async () => {
    vi.mocked(getParseLogs)
      .mockResolvedValueOnce(createPage<ParseLog>([]))
      .mockResolvedValueOnce(createPage([createLog({
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

    expect(await screen.findByText('No parse entries found')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Outcome'), { target: { value: 'all' } })
    expect(await screen.findByText('RSS feed error')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Retry IMDb match/ })).toBeNull()
  })
})
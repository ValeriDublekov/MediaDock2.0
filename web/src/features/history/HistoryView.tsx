import { useEffect, useState, type FormEvent } from 'react'
import { deleteOldOperationalHistory, getParseLogs, getScanRuns } from '../../api/client'
import type { PageResponse, ParseLog, ScanRun } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { ImdbCandidatePicker, type ImdbCandidateTarget } from '../../components/ImdbCandidatePicker'
import { formatDate, formatWords } from '../../shared/format'

type HistoryTab = 'scans' | 'parses'
type ParseLogFilter = '' | 'retryable' | 'terminal' | 'resolved' | 'unresolvedMovies'

export function HistoryView({ scanRunId = null, onClearScanRun, showCleanupControls = true }: {
  scanRunId?: number | null
  onClearScanRun?: () => void
  showCleanupControls?: boolean
} = {}) {
  const [tab, setTab] = useState<HistoryTab>('parses')
  const [page, setPage] = useState(1)
  const [scanStatus, setScanStatus] = useState('')
  const [scanTrigger, setScanTrigger] = useState('')
  const [retryState, setRetryState] = useState<ParseLogFilter>(() => scanRunId === null ? 'unresolvedMovies' : '')
  const [searchDraft, setSearchDraft] = useState('')
  const [search, setSearch] = useState('')
  const [scanResult, setScanResult] = useState<PageResponse<ScanRun> | null>(null)
  const [parseResult, setParseResult] = useState<PageResponse<ParseLog> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [imdbMatchTarget, setImdbMatchTarget] = useState<ImdbCandidateTarget | null>(null)
  const [retentionDays, setRetentionDays] = useState('90')
  const [deletingHistory, setDeletingHistory] = useState(false)
  const [retentionMessage, setRetentionMessage] = useState<string | null>(null)
  const [retentionError, setRetentionError] = useState<string | null>(null)

  useEffect(() => {
    let current = true
    setLoading(true)
    setError(null)
    const request = tab === 'scans'
      ? getScanRuns({
        page,
        pageSize: 20,
        ...(scanStatus ? { status: scanStatus as ScanRun['status'] } : {}),
        ...(scanTrigger ? { trigger: scanTrigger as ScanRun['trigger'] } : {}),
      })
      : getParseLogs({
        page,
        pageSize: 20,
        ...(retryState === 'unresolvedMovies'
          ? { unresolvedMoviesOnly: true }
          : retryState ? { retryState } : {}),
        ...(search ? { search } : {}),
        ...(scanRunId === null ? {} : { scanRunId }),
      })

    request
      .then((result) => {
        if (!current) return
        if (tab === 'scans') setScanResult(result as PageResponse<ScanRun>)
        else setParseResult(result as PageResponse<ParseLog>)
      })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'Could not load operation history.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [tab, page, scanStatus, scanTrigger, retryState, search, scanRunId, attempt])

  function changeTab(nextTab: HistoryTab) {
    setTab(nextTab)
    setPage(1)
  }

  function applySearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPage(1)
    setSearch(searchDraft.trim())
  }

  async function deleteOldHistory() {
    const days = Number(retentionDays)
    if (!Number.isInteger(days) || days < 1 || days > 36_500) return
    if (!window.confirm(`Delete scan and parse history older than ${days} days? This cannot be undone.`)) return

    setDeletingHistory(true)
    setRetentionMessage(null)
    setRetentionError(null)
    try {
      const result = await deleteOldOperationalHistory(days)
      setRetentionMessage(`Deleted ${result.deletedScanRuns} scan runs and ${result.deletedParseLogs} parse logs older than ${days} days.`)
      setPage(1)
      setAttempt((current) => current + 1)
    } catch (requestError: unknown) {
      setRetentionError(requestError instanceof Error ? requestError.message : 'Could not delete old operation history.')
    } finally {
      setDeletingHistory(false)
    }
  }

  const activeResult = tab === 'scans' ? scanResult : parseResult
  const validRetentionDays = Number.isInteger(Number(retentionDays)) && Number(retentionDays) >= 1 && Number(retentionDays) <= 36_500

  return (
    <section aria-label="Operation history">
      <div className="history-toolbar">
        <div aria-label="History type" className="segment-control" role="group">
          <button aria-pressed={tab === 'scans'} onClick={() => changeTab('scans')} type="button">Scan runs</button>
          <button aria-pressed={tab === 'parses'} onClick={() => changeTab('parses')} type="button">Parse log</button>
        </div>
        {tab === 'scans' ? (
          <div className="history-filters">
            <div className="field"><label htmlFor="scan-status-filter">Status</label><select id="scan-status-filter" onChange={(event) => { setPage(1); setScanStatus(event.target.value) }} value={scanStatus}><option value="">All statuses</option><option value="running">Running</option><option value="succeeded">Succeeded</option><option value="partial">Partial</option><option value="failed">Failed</option></select></div>
            <div className="field"><label htmlFor="scan-trigger-filter">Trigger</label><select id="scan-trigger-filter" onChange={(event) => { setPage(1); setScanTrigger(event.target.value) }} value={scanTrigger}><option value="">All triggers</option><option value="schedule">Schedule</option><option value="manual">Manual</option><option value="local">Local</option></select></div>
            {showCleanupControls && <>
              <div className="field"><label htmlFor="history-retention-days">Delete older than (days)</label><input id="history-retention-days" max="36500" min="1" onChange={(event) => setRetentionDays(event.target.value)} type="number" value={retentionDays} /></div>
              <button className="button button-secondary" disabled={deletingHistory || !validRetentionDays} onClick={() => void deleteOldHistory()} type="button">{deletingHistory ? 'Deleting…' : 'Delete old logs'}</button>
            </>}
          </div>
        ) : (
          <form className="history-filters" onSubmit={applySearch}>
            <div className="field"><label htmlFor="parse-retry-filter">Retry state</label><select id="parse-retry-filter" onChange={(event) => { setPage(1); setRetryState(event.target.value as ParseLogFilter) }} value={retryState}><option value="unresolvedMovies">Unresolved movies</option><option value="">All states</option><option value="retryable">Retryable</option><option value="terminal">Terminal</option><option value="resolved">Resolved</option></select></div>
            <div className="field field-search"><label htmlFor="parse-search">Search entries</label><input id="parse-search" onChange={(event) => setSearchDraft(event.target.value)} placeholder="Title or feed" value={searchDraft} /></div>
            <button className="button" type="submit">Search</button>
          </form>
        )}
      </div>

      {retentionError && <p className="history-retention-feedback" role="alert">{retentionError}</p>}
      {retentionMessage && <p className="history-retention-feedback" role="status">{retentionMessage}</p>}

      {scanRunId !== null && <p className="history-scan-scope">Parse entries for scan #{scanRunId}<button className="text-button" onClick={onClearScanRun} type="button">Show all parse entries</button></p>}

      {loading && <LoadingState label={tab === 'scans' ? 'Loading scan history' : 'Loading parse history'} />}
      {!loading && error && <ErrorState message={error} onRetry={() => setAttempt((current) => current + 1)} />}
      {!loading && !error && activeResult && activeResult.items.length === 0 && (
        <EmptyState title={tab === 'scans' ? 'No scan runs found' : 'No parse entries found'} message="History will appear here after the worker processes configured feeds." />
      )}
      {!loading && !error && activeResult && activeResult.items.length > 0 && (
        <>
          {tab === 'scans' && <ScanRunTable items={scanResult?.items ?? []} />}
          {tab === 'parses' && <ParseLogTable
            items={parseResult?.items ?? []}
            onRetry={(entry) => setImdbMatchTarget({
              source: 'parse-log',
              id: entry.id,
              title: entry.parsedTitle ?? entry.rawTitle,
              rawTitle: entry.rawTitle,
            })}
          />}
          <Pagination onPageChange={setPage} page={activeResult} />
        </>
      )}
      {imdbMatchTarget && <ImdbCandidatePicker
        onApplied={() => {
          setImdbMatchTarget(null)
          setAttempt((current) => current + 1)
        }}
        onClose={() => setImdbMatchTarget(null)}
        target={imdbMatchTarget}
      />}
    </section>
  )
}

function ScanRunTable({ items }: { items: ScanRun[] }) {
  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead><tr><th>STARTED</th><th>STATUS</th><th>TRIGGER</th><th>FEEDS</th><th>ENTRIES</th><th>KNOWN SKIPPED</th><th>ADDED</th><th>ISSUES</th></tr></thead>
        <tbody>{items.map((run) => (
          <tr key={run.id}>
            <td>{formatDate(run.startedAt)}<span className="subtle-line">Finished {formatDate(run.finishedAt)}</span></td>
            <td><span className={`state-pill${run.status === 'failed' || run.status === 'partial' ? ' is-muted' : ''}`}>{formatWords(run.status)}</span></td>
            <td>{formatWords(run.trigger)}</td><td>{run.feedsProcessed}</td><td>{run.entriesSeen}</td><td>{run.knownEntriesSkipped}</td>
            <td>{run.titlesCreated} titles<span className="subtle-line">{run.occurrencesCreated} observations</span></td>
            <td>{run.errorCount ? run.errorSummary.join('; ') || `${run.errorCount} issues` : 'None'}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

function ParseLogTable({ items, onRetry }: { items: ParseLog[]; onRetry: (entry: ParseLog) => void }) {
  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead><tr><th>RAW TITLE</th><th>PARSED TITLE</th><th>YEAR</th><th>FEED</th><th>RESULT</th><th>RETRY</th><th>PROCESSED</th><th>ACTION</th></tr></thead>
        <tbody>{items.map((entry) => (
          <tr key={entry.id}>
            <td>{entry.rawTitle || '—'}</td>
            <td>{entry.parsedTitle ?? 'No normalized title'}</td>
            <td>{entry.parsedYear ?? '—'}</td>
            <td>{entry.sourceName ?? entry.feedName}<span className="subtle-line">{entry.feedType ?? 'Unknown feed type'}</span></td>
            <td>
              <span className="state-pill">{getParseLogDiagnosis(entry)}</span>
              <span className={`subtle-line${entry.parsedSuccessfully ? '' : ' is-muted'}`}>{getParseLogStatus(entry)}</span>
              {entry.lookupTitles.length > 0 && <span className="subtle-line">Lookup names: {entry.lookupTitles.join(' → ')}</span>}
              {entry.ignoreReason && <span className="subtle-line">Reason: {formatWords(entry.ignoreReason)}</span>}
              {entry.errorMessage && <span className="subtle-line">Error: {entry.errorMessage}</span>}
              {entry.decision && <span className="subtle-line">Decision: {entry.decision === 'known_torrent_url' ? 'Known torrent URL' : formatWords(entry.decision)}</span>}
            </td>
            <td>{formatWords(entry.retryState)}<span className="subtle-line">{entry.attemptCount} attempts</span></td>
            <td>{formatDate(entry.processedAt)}</td>
            <td>{entry.canRetryMatch && <button
              aria-label={`Retry IMDb match for ${entry.parsedTitle ?? entry.rawTitle}`}
              className="button button-secondary"
              onClick={() => onRetry(entry)}
              type="button"
            >Retry match</button>}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

function getParseLogStatus(entry: Pick<ParseLog, 'parsedSuccessfully' | 'decision'>) {
  if (entry.decision === 'known_torrent_url') return 'Skipped'
  return entry.parsedSuccessfully ? 'Parsed' : 'Rejected'
}

function getParseLogDiagnosis(entry: Pick<ParseLog, 'parsedSuccessfully' | 'omdbStatus' | 'ignored' | 'ignoreReason' | 'decision'>) {
  if (entry.ignoreReason === 'feed_error') return 'RSS feed error'
  if (entry.ignoreReason === 'malformed_entry') return 'Invalid RSS entry'
  if (entry.decision === 'known_torrent_url') return 'Already in catalog'
  if (!entry.parsedSuccessfully) return 'Parsing failed'
  if (entry.omdbStatus === 'confirmed_not_found') return 'OMDb found no result'
  if (entry.omdbStatus === 'found') return entry.ignored ? 'OMDb match rejected' : 'Resolved'
  if (entry.omdbStatus === 'not_requested') return 'Lookup not attempted'
  return 'OMDb request failed'
}
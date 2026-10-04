import { useEffect, useRef, useState } from 'react'
import {
  ApiError,
  enqueueManualScan,
  enqueueOscarEnrichment,
  enqueueGoldenGlobeEnrichment,
  enqueueOscarImport,
  enqueueGoldenGlobeImport,
  getActiveBackgroundJob,
  getBackgroundJob,
  getBackgroundJobEvents,
} from '../../api/client'
import type { BackgroundJob, BackgroundJobEvent } from '../../api/types'
import { formatDate, formatWords } from '../../shared/format'

const maximumUploadBytes = 10 * 1024 * 1024

interface BackgroundIngestionPanelProps {
  onOpenHistory: (scanRunId: number | null) => void
}

function isActive(job: BackgroundJob | null): boolean {
  return job?.status === 'queued' || job?.status === 'running'
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function progressSummary(job: BackgroundJob): Record<string, unknown> {
  const rss = job.resultSummary?.rss
  if (isRecord(rss)) return rss
  const enrichment = job.resultSummary?.summary
  return isRecord(enrichment) ? enrichment : job.resultSummary ?? {}
}

function jobLabel(job: BackgroundJob): string {
  if (job.jobType === 'rss_scan') return 'RSS scan'
  if (job.jobType === 'oscar_enrichment') return 'Oscar film metadata'
  if (job.jobType === 'golden_globe_import') return 'Golden Globes dataset import'
  if (job.jobType === 'golden_globe_enrichment') return 'Golden Globes film metadata'
  return 'Oscar dataset import'
}

function counter(summary: Record<string, unknown>, camelName: string, pascalName: string): string {
  const value = summary[camelName] ?? summary[pascalName]
  return typeof value === 'number' ? String(value) : '0'
}

export function BackgroundIngestionPanel({ onOpenHistory }: BackgroundIngestionPanelProps) {
  const [job, setJob] = useState<BackgroundJob | null>(null)
  const [events, setEvents] = useState<BackgroundJobEvent[]>([])
  const [loading, setLoading] = useState(true)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const [yearAfter, setYearAfter] = useState('1980')
  const [uploadError, setUploadError] = useState<string | null>(null)
  const eventsCursor = useRef(0)
  const fileInput = useRef<HTMLInputElement>(null)
  const goldenGlobeFileInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    let current = true
    getActiveBackgroundJob()
      .then(async (activeJob) => {
        if (!current) return
        setJob(activeJob)
        setEvents([])
        eventsCursor.current = 0
        setError(null)
        if (activeJob) {
          const page = await getBackgroundJobEvents(activeJob.id, { afterId: 0, pageSize: 50 })
          if (!current) return
          setEvents(page.items)
          eventsCursor.current = page.nextAfterId
        }
      })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'Could not load ingestion status.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [retry])

  const activeJobId = job?.id
  const activeJob = isActive(job)

  useEffect(() => {
    if (!dialogOpen || activeJobId === undefined || !activeJob) return
    let current = true
    const poll = async () => {
      try {
        const [latest, page] = await Promise.all([
          getBackgroundJob(activeJobId),
          getBackgroundJobEvents(activeJobId, { afterId: eventsCursor.current, pageSize: 50 }),
        ])
        if (!current) return
        setJob(latest)
        setEvents((existing) => [...existing, ...page.items])
        eventsCursor.current = page.nextAfterId
        setError(null)
      } catch (requestError: unknown) {
        if (current) setError(requestError instanceof Error ? requestError.message : 'Could not refresh ingestion status.')
      }
    }
    const interval = window.setInterval(() => { void poll() }, 2000)
    return () => {
      current = false
      window.clearInterval(interval)
    }
  }, [dialogOpen, activeJobId, activeJob])

  async function showJob(jobId: number) {
    try {
      const latest = await getBackgroundJob(jobId)
      const page = await getBackgroundJobEvents(jobId, { afterId: 0, pageSize: 50 })
      setJob(latest)
      setEvents(page.items)
      eventsCursor.current = page.nextAfterId
      setDialogOpen(true)
      setError(null)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : 'Could not load ingestion status.')
    }
  }

  function refreshActiveJob() {
    setLoading(true)
    setRetry((current) => current + 1)
  }

  async function startScan() {
    if (!window.confirm('Start a scan? It reads configured feeds and may send OMDb requests within the saved limits.')) return
    setBusy(true)
    setError(null)
    try {
      const accepted = await enqueueManualScan()
      await showJob(accepted.id)
    } catch (requestError: unknown) {
      if (requestError instanceof ApiError && requestError.status === 409) {
        const activeJob = await getActiveBackgroundJob().catch(() => null)
        if (activeJob) await showJob(activeJob.id)
      }
      setError(requestError instanceof Error ? requestError.message : 'Could not queue the scan.')
    } finally {
      setBusy(false)
    }
  }

  async function startImport() {
    const file = fileInput.current?.files?.[0]
    setUploadError(null)
    if (!file) {
      setUploadError('Choose a CSV or TSV file.')
      return
    }
    if (file.size === 0 || file.size > maximumUploadBytes) {
      setUploadError('The file must be non-empty and no larger than 10 MiB.')
      return
    }
    const parsedYearAfter = Number(yearAfter)
    if (!Number.isInteger(parsedYearAfter) || parsedYearAfter < 0 || parsedYearAfter >= 9999) {
      setUploadError('Enter a year between 0 and 9998.')
      return
    }

    setBusy(true)
    try {
      const accepted = await enqueueOscarImport(file, parsedYearAfter)
      if (fileInput.current) fileInput.current.value = ''
      await showJob(accepted.id)
    } catch (requestError: unknown) {
      setUploadError(requestError instanceof Error ? requestError.message : 'Could not queue the Oscar import.')
    } finally {
      setBusy(false)
    }
  }

  async function startOscarEnrichment() {
    if (!window.confirm('Start Oscar film metadata extraction? It may send OMDb requests within the saved limits.')) return
    setBusy(true)
    setError(null)
    try {
      const accepted = await enqueueOscarEnrichment()
      await showJob(accepted.id)
    } catch (requestError: unknown) {
      if (requestError instanceof ApiError && requestError.status === 409) {
        const activeJob = await getActiveBackgroundJob().catch(() => null)
        if (activeJob?.jobType === 'oscar_enrichment') await showJob(activeJob.id)
      }
      setError(requestError instanceof Error ? requestError.message : 'Could not queue Oscar enrichment.')
    } finally {
      setBusy(false)
    }
  }

  async function startGoldenGlobeEnrichment() {
    if (!window.confirm('Start Golden Globes film metadata extraction? It may send OMDb requests within the saved limits.')) return
    setBusy(true); setError(null)
    try { const accepted = await enqueueGoldenGlobeEnrichment(); await showJob(accepted.id) }
    catch (requestError: unknown) { setError(requestError instanceof Error ? requestError.message : 'Could not queue Golden Globes enrichment.') }
    finally { setBusy(false) }
  }

  async function startGoldenGlobeImport() {
    const file = goldenGlobeFileInput.current?.files?.[0]
    setUploadError(null)
    if (!file) { setUploadError('Choose a Golden Globes CSV or TSV file.'); return }
    const parsedYearAfter = Number(yearAfter)
    if (!Number.isInteger(parsedYearAfter) || parsedYearAfter < 0 || parsedYearAfter >= 9999) { setUploadError('Enter a year between 0 and 9998.'); return }
    setBusy(true)
    try {
      const accepted = await enqueueGoldenGlobeImport(file, parsedYearAfter)
      if (goldenGlobeFileInput.current) goldenGlobeFileInput.current.value = ''
      await showJob(accepted.id)
    } catch (requestError: unknown) {
      setUploadError(requestError instanceof Error ? requestError.message : 'Could not queue the Golden Globes import.')
    } finally { setBusy(false) }
  }

  const summary = job ? progressSummary(job) : {}
  const active = isActive(job)

  return (
    <section aria-labelledby="ingestion-heading" className="management-section ingestion-section">
      <div className="section-title-row">
        <div><h2 id="ingestion-heading">Ingestion</h2><p>RSS parsing and Oscar film metadata</p></div>
        <div className="form-actions">
          <button className="button" disabled={busy || (job?.jobType === 'rss_scan' && active)} onClick={() => { void startScan() }} type="button">
            {busy ? 'Working...' : 'Start scan'}
          </button>
          <button className="button button-secondary" disabled={busy || (job?.jobType === 'oscar_enrichment' && active)} onClick={() => { void startOscarEnrichment() }} type="button">
            Enrich Oscar films
          </button>
          <button className="button button-secondary" disabled={busy || (job?.jobType === 'golden_globe_enrichment' && active)} onClick={() => { void startGoldenGlobeEnrichment() }} type="button">
            Enrich Golden Globes films
          </button>
          {job && <button className="button button-secondary" onClick={() => { void showJob(job.id) }} type="button">View job</button>}
        </div>
      </div>

      {loading && <p className="section-caption" role="status">Loading ingestion status...</p>}
      {!loading && !job && !error && <p className="section-caption">No active ingestion job.</p>}
      {job && (
        <div className="ingestion-current">
          <span className={`state-pill is-${job.status}`}>{formatWords(job.status)}</span>
          <span>{job.jobType === 'oscar_import' ? job.inputFileName ?? 'Oscar import' : jobLabel(job)}</span>
          <span className="section-caption">{job.currentStage ? formatWords(job.currentStage) : formatWords(job.trigger)}</span>
          {job.currentSource && <span className="section-caption">{job.currentSource}</span>}
          {job.errorCode && <span className="form-error">{formatWords(job.errorCode)}</span>}
        </div>
      )}
      {error && <div className="ingestion-error" role="alert"><span>{error}</span><button className="text-button" onClick={() => { void refreshActiveJob() }} type="button">Retry</button></div>}

      <form className="ingestion-upload" onSubmit={(event) => { event.preventDefault(); void startImport() }}>
        <div className="field">
          <label htmlFor="oscar-dataset-file">Oscar dataset</label>
          <input accept=".csv,.tsv,text/csv,text/tab-separated-values" id="oscar-dataset-file" ref={fileInput} type="file" />
        </div>
        <div className="field ingestion-year-field">
          <label htmlFor="oscar-year-after">Film year after</label>
          <input id="oscar-year-after" max="9998" min="0" onChange={(event) => setYearAfter(event.target.value)} type="number" value={yearAfter} />
        </div>
        <button className="button button-secondary" disabled={busy} type="submit">Queue import</button>
        {uploadError && <p className="form-error" role="alert">{uploadError}</p>}
      </form>
      <form className="ingestion-upload" onSubmit={(event) => { event.preventDefault(); void startGoldenGlobeImport() }}>
        <div className="field"><label htmlFor="golden-globe-dataset-file">Golden Globes dataset</label><input accept=".csv,.tsv,text/csv,text/tab-separated-values" id="golden-globe-dataset-file" ref={goldenGlobeFileInput} type="file" /></div>
        <button className="button button-secondary" disabled={busy} type="submit">Queue Golden Globes import</button>
      </form>

      {dialogOpen && job && (
        <div className="detail-backdrop" onClick={(event) => { if (event.target === event.currentTarget) setDialogOpen(false) }}>
          <section aria-labelledby="background-job-title" aria-modal="true" className="detail-dialog background-job-dialog" onKeyDown={(event) => { if (event.key === 'Escape') setDialogOpen(false) }} role="dialog" tabIndex={-1}>
            <header className="detail-header">
              <div>
                <h2 id="background-job-title">{jobLabel(job)}</h2>
                <p className="detail-meta">Job #{job.id} · {formatWords(job.trigger)}</p>
              </div>
              <button aria-label="Close job details" className="button button-secondary" onClick={() => setDialogOpen(false)} type="button">Close</button>
            </header>
            <div className="detail-body">
              <div className="background-job-state">
                <span className={`state-pill is-${job.status}`}>{formatWords(job.status)}</span>
                <span>{job.currentStage ? formatWords(job.currentStage) : 'Waiting for execution'}</span>
                {job.currentSource && <strong>{job.currentSource}</strong>}
              </div>
              <dl className="detail-facts">
                <div><dt>QUEUED</dt><dd>{formatDate(job.enqueuedAt)}</dd></div>
                <div><dt>STARTED</dt><dd>{formatDate(job.startedAt)}</dd></div>
                <div><dt>FINISHED</dt><dd>{formatDate(job.finishedAt)}</dd></div>
                {job.jobType === 'rss_scan' && <>
                  <div><dt>FEEDS</dt><dd>{counter(summary, 'feedsProcessed', 'FeedsProcessed')}</dd></div>
                  <div><dt>ENTRIES</dt><dd>{counter(summary, 'entriesSeen', 'EntriesSeen')}</dd></div>
                  <div><dt>KNOWN SKIPPED</dt><dd>{counter(summary, 'knownEntriesSkipped', 'KnownEntriesSkipped')}</dd></div>
                  <div><dt>OMDb REQUESTS</dt><dd>{counter(summary, 'omdbRequests', 'OmdbRequests')}</dd></div>
                  <div><dt>CACHE HITS</dt><dd>{counter(summary, 'cacheHits', 'CacheHits')}</dd></div>
                  <div><dt>ADDED TITLES</dt><dd>{counter(summary, 'titlesCreated', 'TitlesCreated')}</dd></div>
                  <div><dt>OBSERVATIONS</dt><dd>{counter(summary, 'occurrencesCreated', 'OccurrencesCreated')}</dd></div>
                </>}
                {job.jobType === 'oscar_enrichment' && <>
                  <div><dt>ELIGIBLE FILMS</dt><dd>{counter(summary, 'eligibleFilms', 'EligibleFilms')}</dd></div>
                  <div><dt>ATTEMPTED FILMS</dt><dd>{counter(summary, 'attemptedFilms', 'AttemptedFilms')}</dd></div>
                  <div><dt>ENRICHED</dt><dd>{counter(summary, 'enrichedFilms', 'EnrichedFilms')}</dd></div>
                  <div><dt>OMDb REQUESTS</dt><dd>{counter(summary, 'httpAttempts', 'HttpAttempts')}</dd></div>
                  <div><dt>CACHE HITS</dt><dd>{counter(summary, 'cacheHits', 'CacheHits')}</dd></div>
                </>}
                {job.errorCode && <div><dt>ERROR</dt><dd>{formatWords(job.errorCode)}</dd></div>}
              </dl>
              {job.resultSummary && <pre className="background-job-summary">{JSON.stringify(job.resultSummary, null, 2)}</pre>}
              <div>
                <h3 className="background-job-events-title">Recent events</h3>
                {events.length === 0 ? <p className="section-caption">No events recorded.</p> : (
                  <ol className="background-job-events">
                    {events.slice(-30).map((item) => (
                      <li key={item.id}>
                        <time dateTime={item.occurredAt}>{formatDate(item.occurredAt)}</time>
                        <span>{item.message}</span>
                        {item.data && <small>{Object.entries(item.data).filter(([, value]) => value !== null).map(([key, value]) => `${formatWords(key)}: ${String(value)}`).join(' · ')}</small>}
                      </li>
                    ))}
                  </ol>
                )}
              </div>
              <div className="form-actions">
                {job.scanRunId && <button className="text-button" onClick={() => { setDialogOpen(false); onOpenHistory(job.scanRunId) }} type="button">Open scan and parse history</button>}
                {!job.scanRunId && <button className="text-button" onClick={() => { setDialogOpen(false); onOpenHistory(null) }} type="button">Open scan history</button>}
                {error && <button className="text-button" onClick={() => { void refreshActiveJob() }} type="button">Retry status</button>}
              </div>
            </div>
          </section>
        </div>
      )}
    </section>
  )
}

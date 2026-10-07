import { useEffect, useState, type FormEvent } from 'react'
import { getBackgroundJob, setGoldenGlobeImdbId } from '../../api/client'
import type { BackgroundJobStatus, GoldenGlobeFilm, MovieAwardRecognition } from '../../api/types'
import { MovieAwardsList, MovieDetailsIntro } from '../../components/MoviePresentation'
import { formatWords } from '../../shared/format'

interface GoldenGlobeFilmDetailsDialogProps {
  film: GoldenGlobeFilm
  awards: MovieAwardRecognition[]
  onClose: () => void
  onUpdated: (filmId: string) => Promise<GoldenGlobeFilm | undefined>
}

function refreshMessage(status: BackgroundJobStatus, outcome?: unknown, errorCode?: unknown) {
  if (outcome === 'superseded') return 'A newer IMDb selection replaced this refresh; the older result was discarded.'
  if (status === 'queued') return 'Metadata refresh queued.'
  if (status === 'running') return 'Metadata refresh running.'
  if (status === 'succeeded') return 'Metadata refresh complete.'
  if (errorCode === 'not_found') return 'OMDb could not find this IMDb ID. The manual link is retained.'
  if (errorCode === 'imdb_id_mismatch') return 'OMDb returned a different IMDb ID. The manual link is retained.'
  if (errorCode === 'type_mismatch') return 'The IMDb ID is not compatible with this nominee type. The manual link is retained.'
  if (errorCode === 'quota_exceeded' || errorCode === 'daily_budget_exhausted') return 'OMDb request limits stopped the refresh. The manual link is retained for retry.'
  if (status === 'failed') return 'Metadata refresh failed. The manual link is retained.'
  return 'Metadata refresh needs attention. The manual link is retained for retry.'
}

export function GoldenGlobeFilmDetailsDialog({ film, awards, onClose, onUpdated }: GoldenGlobeFilmDetailsDialogProps) {
  const [currentFilm, setCurrentFilm] = useState(film)
  const [imdbId, setImdbId] = useState(film.imdbId ?? '')
  const [refreshJobId, setRefreshJobId] = useState<number | null>(null)
  const [refreshJobStatus, setRefreshJobStatus] = useState<BackgroundJobStatus | null>(null)
  const [refreshStatus, setRefreshStatus] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (refreshJobId === null || refreshJobStatus === null || !['queued', 'running'].includes(refreshJobStatus)) return
    let current = true
    const timer = window.setTimeout(() => {
      void getBackgroundJob(refreshJobId).then(async (job) => {
        if (!current) return
        setRefreshJobStatus(job.status)
        setRefreshStatus(refreshMessage(job.status, job.resultSummary?.outcome, job.resultSummary?.errorCode ?? job.errorCode))
        if (['succeeded', 'partial', 'failed'].includes(job.status)) {
          const updatedFilm = await onUpdated(film.filmId)
          if (current && updatedFilm) {
            setCurrentFilm(updatedFilm)
            setImdbId(updatedFilm.imdbId ?? '')
          }
        }
      }).catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'Could not check metadata refresh status.')
      })
    }, 1000)
    return () => {
      current = false
      window.clearTimeout(timer)
    }
  }, [film.filmId, onUpdated, refreshJobId, refreshJobStatus])

  async function saveImdbId(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const normalizedId = imdbId.trim().toLowerCase()
    if (!normalizedId && !currentFilm.imdbId) return
    if (currentFilm.imdbId && normalizedId !== currentFilm.imdbId.toLowerCase()) {
      const message = normalizedId
        ? 'Replace the linked IMDb ID? Metadata linked to the current ID will be replaced.'
        : 'Clear the linked IMDb ID and its associated metadata?'
      if (!window.confirm(message)) return
    }

    setSaving(true)
    setError(null)
    setRefreshStatus(null)
    try {
      const result = await setGoldenGlobeImdbId(currentFilm.filmId, normalizedId || null)
      setCurrentFilm({
        ...currentFilm,
        imdbId: result.imdbId,
        isImdbIdManual: result.imdbId !== null,
        imdbRating: null,
        posterUrl: null,
        enrichmentStatus: 'pending',
        enrichmentError: null,
      })
      setImdbId(result.imdbId ?? '')
      if (result.refreshJob) {
        setRefreshJobId(result.refreshJob.id)
        setRefreshJobStatus(result.refreshJob.status)
        setRefreshStatus(refreshMessage(result.refreshJob.status))
      } else {
        setRefreshJobId(null)
        setRefreshJobStatus(null)
        setRefreshStatus('IMDb ID cleared. Automatic matching is available again.')
        const updatedFilm = await onUpdated(currentFilm.filmId)
        if (updatedFilm) setCurrentFilm(updatedFilm)
      }
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : 'Could not update the IMDb ID.')
    } finally {
      setSaving(false)
    }
  }

  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget) onClose()
  }}>
    <section aria-labelledby="golden-globe-film-details-heading" aria-modal="true" className="detail-dialog" role="dialog">
      <header className="detail-header">
        <div>
          <p className="eyebrow">GOLDEN GLOBES</p>
          <h2 id="golden-globe-film-details-heading">{currentFilm.title}</h2>
          <div className="detail-meta">Ceremony year {currentFilm.year} | {formatWords(currentFilm.enrichmentStatus)}</div>
        </div>
        <button aria-label="Close Golden Globes details" className="button button-secondary" onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        <MovieDetailsIntro imdbId={currentFilm.imdbId} imdbRating={currentFilm.imdbRating} posterLabel="GLO" posterUrl={currentFilm.posterUrl} title={currentFilm.title}>
          {currentFilm.enrichmentError && <p className="golden-globe-enrichment-error">{formatWords(currentFilm.enrichmentError.replaceAll('_', ' '))}</p>}
        </MovieDetailsIntro>
        <dl className="detail-facts">
          <div><dt>IMDb ID</dt><dd>{currentFilm.imdbId ?? 'Not listed'}{currentFilm.isImdbIdManual ? ' (manual)' : ''}</dd></div>
          <div><dt>IMDb rating</dt><dd>{currentFilm.imdbRating?.toFixed(1) ?? 'Not rated'}</dd></div>
          <div><dt>OMDb status</dt><dd>{formatWords(currentFilm.enrichmentStatus)}</dd></div>
        </dl>
        <form className="golden-globe-imdb-link" onSubmit={(event) => { void saveImdbId(event) }}>
          <div className="field">
            <label htmlFor="golden-globe-imdb-id">IMDb ID</label>
            <input autoComplete="off" id="golden-globe-imdb-id" maxLength={12} onChange={(event) => setImdbId(event.target.value)} pattern="[Tt][Tt][0-9]{7,10}" placeholder="tt1234567" value={imdbId} />
          </div>
          <div className="filter-actions">
            <button className="button" disabled={saving || (!imdbId.trim() && !currentFilm.imdbId)} type="submit">{saving ? 'Saving…' : imdbId.trim() ? 'Save and refresh' : 'Clear IMDb ID'}</button>
          </div>
          {refreshStatus && <p role="status">{refreshStatus}</p>}
          {error && <p className="golden-globe-enrichment-error" role="alert">{error}</p>}
        </form>
        <MovieAwardsList awards={awards} />
      </div>
    </section>
  </div>
}
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { enqueuePersonalRatingsEnrichment, getOmdbDailyUsage, getPersonalRatings, getProviderSettings, getVersion, importPersonalRatings, testOmdbApi, updateProviderSettings } from '../../api/client'
import type { OmdbDailyUsage, OmdbDiagnostic, PageResponse, PersonalRating, PersonalRatingsImportResult, PersonalRatingsSort, ProviderSettings, ProviderSettingsInput, SystemVersion } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { EmptyState } from '../../components/Feedback'
import { Pagination } from '../../components/Pagination'
import { formatDate } from '../../shared/format'
import { BackgroundIngestionPanel } from './BackgroundIngestionPanel'
import { DeploymentControlPanel } from './DeploymentControlPanel'

interface ProviderSettingsDraft {
  omdbApiKey: string
  clearOmdbApiKey: boolean
  omdbDailyRequestLimit: string
}

const emptyProviderSettings: ProviderSettingsDraft = {
  omdbApiKey: '',
  clearOmdbApiKey: false,
  omdbDailyRequestLimit: '0',
}

const personalRatingsPageSize = 25
const maximumMetadataRefreshes = 10
const metadataRefreshDelayMs = 1500

function providerSettingsToDraft(settings: ProviderSettings): ProviderSettingsDraft {
  return {
    ...emptyProviderSettings,
    omdbDailyRequestLimit: String(settings.omdbDailyRequestLimit),
  }
}

export function IngestionSettingsView() {
  return <BackgroundIngestionPanel mode="awards" />
}

export function PersonalRatingsView() {
  const [importingRatings, setImportingRatings] = useState(false)
  const [ratingsImportError, setRatingsImportError] = useState<string | null>(null)
  const [ratingsImportResult, setRatingsImportResult] = useState<PersonalRatingsImportResult | null>(null)
  const [ratings, setRatings] = useState<PageResponse<PersonalRating> | null>(null)
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState<PersonalRatingsSort>('desc')
  const [loadingRatings, setLoadingRatings] = useState(true)
  const [ratingsError, setRatingsError] = useState<string | null>(null)
  const [ratingsAttempt, setRatingsAttempt] = useState(0)
  const [enrichmentError, setEnrichmentError] = useState<string | null>(null)
  const requestedEnrichment = useRef(new Set<string>())
  const metadataRefreshes = useRef(new Map<string, number>())

  useEffect(() => {
    let active = true

    getPersonalRatings({ page, pageSize: personalRatingsPageSize, sort })
      .then((response) => { if (active) setRatings(response) })
      .catch((requestError: unknown) => {
        if (active) setRatingsError(requestError instanceof Error ? requestError.message : 'Could not load personal IMDb ratings.')
      })
      .finally(() => { if (active) setLoadingRatings(false) })

    return () => { active = false }
  }, [page, sort, ratingsAttempt])

  useEffect(() => {
    const queryKey = `${page}:${sort}`
    if (loadingRatings || ratingsError || !ratings || ratings.page !== page || !ratings.items.some((rating) => rating.metadataStatus === 'pending')) return

    let active = true
    let timeout: number | undefined
    const scheduleRefresh = () => {
      const refreshCount = metadataRefreshes.current.get(queryKey) ?? 0
      if (refreshCount >= maximumMetadataRefreshes) return
      metadataRefreshes.current.set(queryKey, refreshCount + 1)
      timeout = window.setTimeout(() => {
        if (!active) return
        setLoadingRatings(true)
        setRatingsAttempt((current) => current + 1)
      }, metadataRefreshDelayMs)
    }

    if (requestedEnrichment.current.has(queryKey)) {
      scheduleRefresh()
    } else {
      requestedEnrichment.current.add(queryKey)
      enqueuePersonalRatingsEnrichment()
        .then(() => {
          if (!active) return
          setEnrichmentError(null)
          scheduleRefresh()
        })
        .catch((requestError: unknown) => {
          requestedEnrichment.current.delete(queryKey)
          if (active) setEnrichmentError(requestError instanceof Error ? requestError.message : 'Could not queue metadata enrichment.')
        })
    }

    return () => {
      active = false
      if (timeout !== undefined) window.clearTimeout(timeout)
    }
  }, [loadingRatings, page, ratings, ratingsError, sort])

  async function importRatings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const fileInput = form.elements.namedItem('personal-ratings-file') as HTMLInputElement | null
    const file = fileInput?.files?.[0]
    if (!file) {
      setRatingsImportError('Select a JSON file to import.')
      return
    }

    setImportingRatings(true)
    setRatingsImportError(null)
    setRatingsImportResult(null)
    try {
      const result = await importPersonalRatings(file)
      setRatingsImportResult(result)
      setRatingsError(null)
      setPage(1)
      setLoadingRatings(true)
      setRatingsAttempt((current) => current + 1)
      form.reset()
    } catch (requestError) {
      setRatingsImportError(requestError instanceof Error ? requestError.message : 'Could not import IMDb ratings.')
    } finally {
      setImportingRatings(false)
    }
  }

  function changeRatingsSort(nextSort: PersonalRatingsSort) {
    if (sort === nextSort) return
    setSort(nextSort)
    setPage(1)
    setLoadingRatings(true)
    setRatingsError(null)
  }

  return (
    <section aria-labelledby="personal-ratings-heading" className="management-section">
      <h2 id="personal-ratings-heading">Personal IMDb ratings</h2>
      <p className="section-caption">Upload one JSON export at a time. Re-imports add or update ratings by IMDb ID and keep ratings not included in the file.</p>
      <form className="settings-form" onSubmit={importRatings}>
        <div className="field">
          <label htmlFor="personal-ratings-file">IMDb ratings JSON</label>
          <input accept=".json,application/json" id="personal-ratings-file" name="personal-ratings-file" required type="file" />
        </div>
        {ratingsImportError && <p className="form-error" role="alert">{ratingsImportError}</p>}
        {ratingsImportResult && <>
          <p className="form-message" role="status">
            Imported {ratingsImportResult.ratingsInFile}: {ratingsImportResult.added} added, {ratingsImportResult.updated} updated, {ratingsImportResult.unchanged} unchanged. {ratingsImportResult.totalRatings} ratings stored.
          </p>
          {ratingsImportResult.errors.length > 0 && <p className="form-error" role="alert">
            Skipped entries with missing ratings: {ratingsImportResult.errors.map(error => `${error.id} (${error.message})`).join(', ')}.
          </p>}
        </>}
        <div className="form-actions">
          <button className="button" disabled={importingRatings} type="submit">{importingRatings ? 'Importing...' : 'Import ratings'}</button>
        </div>
      </form>
      <div className="ratings-toolbar">
        <h3>Your ratings</h3>
        <div aria-label="Sort personal ratings by your score" className="favorite-filters" role="group">
          <button aria-pressed={sort === 'asc'} className={`button ${sort === 'asc' ? '' : 'button-secondary'}`} onClick={() => changeRatingsSort('asc')} type="button">Lowest first</button>
          <button aria-pressed={sort === 'desc'} className={`button ${sort === 'desc' ? '' : 'button-secondary'}`} onClick={() => changeRatingsSort('desc')} type="button">Highest first</button>
        </div>
      </div>
      {enrichmentError && <p className="form-error" role="alert">Metadata enrichment could not be queued: {enrichmentError}</p>}
      {loadingRatings && <LoadingState label="Loading personal IMDb ratings" />}
      {!loadingRatings && ratingsError && <ErrorState message={ratingsError} onRetry={() => {
        setRatingsError(null)
        setLoadingRatings(true)
        setRatingsAttempt((current) => current + 1)
      }} />}
      {!loadingRatings && !ratingsError && ratings?.items.length === 0 && <EmptyState title="No personal ratings yet" message="Import an IMDb ratings export to see your titles here." />}
      {!loadingRatings && !ratingsError && ratings && ratings.items.length > 0 && <>
        <div className="table-wrap">
          <table className="data-table personal-ratings-table">
            <thead><tr><th>TITLE</th><th>YOUR RATING</th><th>IMDb RATING</th><th>METADATA</th></tr></thead>
            <tbody>{ratings.items.map((rating) => <tr key={rating.imdbId}>
              <td>
                <a className="title-link" href={`https://www.imdb.com/title/${encodeURIComponent(rating.imdbId)}/`} rel="noreferrer" target="_blank">{rating.title}</a>
                <span className="subtle-line">{rating.imdbId} <span aria-hidden="true">·</span> {rating.year ?? 'Year unknown'}</span>
              </td>
              <td><span className="rating-value">{rating.rating} / 10</span></td>
              <td>{rating.imdbRating === null ? 'Not rated' : rating.imdbRating.toFixed(1)}</td>
              <td><span className="type-label">{rating.metadataStatus.replaceAll('_', ' ')}</span></td>
            </tr>)}</tbody>
          </table>
        </div>
        <Pagination page={ratings} onPageChange={(next) => {
          setPage(next)
          setLoadingRatings(true)
          setRatingsError(null)
        }} />
      </>}
    </section>
  )
}

export function OmdbProviderView() {
  const [providerSettings, setProviderSettings] = useState<ProviderSettings | null>(null)
  const [providerDraft, setProviderDraft] = useState<ProviderSettingsDraft>(emptyProviderSettings)
  const [omdbUsage, setOmdbUsage] = useState<OmdbDailyUsage[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [providerError, setProviderError] = useState<string | null>(null)
  const [savingProvider, setSavingProvider] = useState(false)
  const [providerSaved, setProviderSaved] = useState(false)
  const [testingProvider, setTestingProvider] = useState(false)
  const [diagnosticResult, setDiagnosticResult] = useState<OmdbDiagnostic | null>(null)
  const [diagnosticError, setDiagnosticError] = useState<string | null>(null)

  useEffect(() => {
    let current = true
    Promise.all([getProviderSettings(), getOmdbDailyUsage()])
      .then(([settings, usage]) => {
        if (!current) return
        setProviderSettings(settings)
        setProviderDraft(providerSettingsToDraft(settings))
        setOmdbUsage(usage)
      })
      .catch((requestError: unknown) => {
        if (current) setLoadError(requestError instanceof Error ? requestError.message : 'Could not load OMDb settings.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [loadAttempt])

  async function saveProviderSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSavingProvider(true)
    setProviderError(null)
    setProviderSaved(false)
    const payload: ProviderSettingsInput = {
      omdbApiKey: providerDraft.omdbApiKey.trim() || null,
      clearOmdbApiKey: providerDraft.clearOmdbApiKey,
      omdbDailyRequestLimit: Number(providerDraft.omdbDailyRequestLimit),
    }
    try {
      const saved = await updateProviderSettings(payload)
      setProviderSettings(saved)
      setProviderDraft(providerSettingsToDraft(saved))
      setProviderSaved(true)
    } catch (requestError) {
      setProviderError(requestError instanceof Error ? requestError.message : 'Could not save provider settings.')
    } finally {
      setSavingProvider(false)
    }
  }

  async function testProvider() {
    setTestingProvider(true)
    setDiagnosticResult(null)
    setDiagnosticError(null)
    try {
      const result = await testOmdbApi()
      setDiagnosticResult(result)
      setOmdbUsage(await getOmdbDailyUsage())
    } catch (requestError) {
      setDiagnosticError(requestError instanceof Error ? requestError.message : 'Could not run the OMDb diagnostic request.')
    } finally {
      setTestingProvider(false)
    }
  }

  if (loading) return <LoadingState label="Loading OMDb provider settings" />
  if (loadError) return <ErrorState message={loadError} onRetry={() => {
    setLoadError(null)
    setLoading(true)
    setLoadAttempt((current) => current + 1)
  }} />

  return (
    <section aria-labelledby="provider-settings-heading" className="management-section">
      <h2 id="provider-settings-heading">OMDb provider</h2>
      <p className="section-caption">Credentials and request limits used by API-hosted ingestion jobs.</p>
      <form className="settings-form" onSubmit={saveProviderSettings}>
        <div className="field">
          <label htmlFor="omdb-api-key">OMDb API key</label>
          <input
            autoComplete="new-password"
            id="omdb-api-key"
            maxLength={512}
            onChange={(event) => setProviderDraft((current) => ({
              ...current,
              omdbApiKey: event.target.value,
              clearOmdbApiKey: false,
            }))}
            placeholder={providerSettings?.omdbApiKeyConfigured ? 'Leave blank to keep saved key' : 'Enter API key'}
            type="password"
            value={providerDraft.omdbApiKey}
          />
          {providerSettings?.omdbApiKeyConfigured && <span className="state-pill is-enriched">Key configured</span>}
        </div>
        <label className="checkbox-field">
          <input
            checked={providerDraft.clearOmdbApiKey}
            disabled={!providerSettings?.omdbApiKeyConfigured}
            onChange={(event) => setProviderDraft((current) => ({
              ...current,
              omdbApiKey: '',
              clearOmdbApiKey: event.target.checked,
            }))}
            type="checkbox"
          />
          Clear saved key
        </label>
        <div className="field">
          <label htmlFor="omdb-daily-limit">Shared daily HTTP request limit</label>
          <input id="omdb-daily-limit" min="0" onChange={(event) => setProviderDraft((current) => ({ ...current, omdbDailyRequestLimit: event.target.value }))} required type="number" value={providerDraft.omdbDailyRequestLimit} />
        </div>
        <p className="section-caption">Set the shared daily limit to the OMDb key's confirmed quota. RSS and Oscar use the same limit; requests stop when it is reached or OMDb reports that its quota is exhausted.</p>
        {providerSettings?.updatedAt && <p className="section-caption">Last updated {formatDate(providerSettings.updatedAt)}</p>}
        {providerError && <p className="form-error" role="alert">{providerError}</p>}
        {providerSaved && <p className="form-message" role="status">Provider settings saved.</p>}
        <div className="form-actions">
          <button className="button" disabled={savingProvider} type="submit">{savingProvider ? 'Saving...' : 'Save provider settings'}</button>
          <button className="button button-secondary" disabled={savingProvider || testingProvider || !providerSettings?.omdbApiKeyConfigured} onClick={testProvider} type="button">
            {testingProvider ? 'Testing...' : 'Test OMDb with tt16311594'}
          </button>
        </div>
        {diagnosticResult && (
          <div className={diagnosticResult.isSuccessful ? 'form-message' : 'form-error'} role={diagnosticResult.isSuccessful ? 'status' : 'alert'}>
            <strong>{diagnosticResult.isSuccessful ? 'OMDb responded' : 'OMDb test failed'}:</strong> {diagnosticResult.message}
            {diagnosticResult.title && ` ${diagnosticResult.title}${diagnosticResult.year ? ` (${diagnosticResult.year})` : ''}.`}
          </div>
        )}
        {diagnosticError && <p className="form-error" role="alert">{diagnosticError}</p>}
      </form>
      <div aria-label="OMDb daily request history" className="usage-history">
        <h3>Daily request history</h3>
        <p className="section-caption">Last 30 UTC days. Counts are reserved HTTP attempts and may include a request interrupted before sending.</p>
        {omdbUsage.length === 0 ? (
          <p className="section-caption">No requests recorded in the last 30 days.</p>
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th scope="col">Date (UTC)</th><th scope="col">Total</th><th scope="col">RSS</th><th scope="col">Oscar</th><th scope="col">Status</th></tr></thead>
              <tbody>
                {omdbUsage.map((usage) => {
                  const status = usage.providerQuotaExceeded
                    ? 'OMDb quota exceeded; blocked for day'
                    : usage.dailyRequestLimitReached
                      ? 'Daily limit reached; blocked for day'
                      : usage.lastErrorCode
                        ? `Last error: ${usage.lastErrorCode.replaceAll('_', ' ')}`
                        : 'No error'
                  return (
                    <tr key={usage.utcDate}>
                      <td>{usage.utcDate}</td>
                      <td>{usage.totalRequests}</td>
                      <td>{usage.rssRequests}</td>
                      <td>{usage.oscarRequests}</td>
                      <td>{status}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  )
}

export function SystemSettingsView() {
  const [systemVersion, setSystemVersion] = useState<SystemVersion | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let current = true
    getVersion().then((version) => {
      if (current) setSystemVersion(version)
    }).catch(() => {
      if (current) setSystemVersion(null)
    }).finally(() => {
      if (current) setLoading(false)
    })
    return () => { current = false }
  }, [])

  return (
    <>
      <section aria-labelledby="system-version-heading" className="management-section">
        <h2 id="system-version-heading">System version</h2>
        {loading ? <p className="section-caption">Loading version...</p> : systemVersion ? (
          <>
            <p className="section-caption">Version {systemVersion.version}</p>
            <p className="section-caption">Commit {systemVersion.commitSha.slice(0, 12)}</p>
            {systemVersion.commitDateUtc && <p className="section-caption">Commit date (UTC) {systemVersion.commitDateUtc.slice(0, 19).replace('T', ' ')}</p>}
          </>
        ) : <p className="section-caption">Version unavailable</p>}
      </section>
      <DeploymentControlPanel />
      <p className="trust-note">The API key is stored in the local database and never returned by the API. Settings writes are unauthenticated and use the local HTTP connection, so keep MediaDock on a trusted LAN and protect database backups.</p>
    </>
  )
}

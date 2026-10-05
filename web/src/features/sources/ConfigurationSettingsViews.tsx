import { useEffect, useState, type FormEvent } from 'react'
import { getOmdbDailyUsage, getProviderSettings, getVersion, importPersonalRatings, updateProviderSettings } from '../../api/client'
import type { OmdbDailyUsage, PersonalRatingsImportResult, ProviderSettings, ProviderSettingsInput, SystemVersion } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
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
      form.reset()
    } catch (requestError) {
      setRatingsImportError(requestError instanceof Error ? requestError.message : 'Could not import IMDb ratings.')
    } finally {
      setImportingRatings(false)
    }
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
        <div className="form-actions"><button className="button" disabled={savingProvider} type="submit">{savingProvider ? 'Saving...' : 'Save provider settings'}</button></div>
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

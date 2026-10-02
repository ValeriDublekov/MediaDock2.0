import { useEffect, useState, type FormEvent } from 'react'
import { addSourceUrl, getOmdbDailyUsage, getProviderSettings, getSettings, getSources, getVersion, removeSourceUrl, replaceSourceUrl, updateProviderSettings, updateSettings } from '../../api/client'
import type { FeedType, OmdbDailyUsage, ProviderSettings, ProviderSettingsInput, Settings, SettingsInput, SourceProfile, SourceUrl, SystemVersion } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { formatDate } from '../../shared/format'
import { BackgroundIngestionPanel } from './BackgroundIngestionPanel'

interface SettingsDraft {
  excludedGenres: string
  excludedCountries: string
  minMovieRating: string
  minSeriesRating: string
  minImdbVotes: string
}

interface ProviderSettingsDraft {
  omdbApiKey: string
  clearOmdbApiKey: boolean
  omdbDailyRequestLimit: string
  oscarEnrichmentMaxFilmsPerRun: string
  oscarEnrichmentMaxRequestsPerDay: string
}

const emptySettings: SettingsDraft = { excludedGenres: '', excludedCountries: '', minMovieRating: '0', minSeriesRating: '0', minImdbVotes: '0' }
const emptyProviderSettings: ProviderSettingsDraft = {
  omdbApiKey: '',
  clearOmdbApiKey: false,
  omdbDailyRequestLimit: '0',
  oscarEnrichmentMaxFilmsPerRun: '0',
  oscarEnrichmentMaxRequestsPerDay: '0',
}

function settingsToDraft(settings: Settings): SettingsDraft {
  return {
    excludedGenres: settings.excludedGenres.join(', '),
    excludedCountries: settings.excludedCountries.join(', '),
    minMovieRating: String(settings.minMovieRating),
    minSeriesRating: String(settings.minSeriesRating),
    minImdbVotes: String(settings.minImdbVotes),
  }
}

function providerSettingsToDraft(settings: ProviderSettings): ProviderSettingsDraft {
  return {
    ...emptyProviderSettings,
    omdbDailyRequestLimit: String(settings.omdbDailyRequestLimit),
    oscarEnrichmentMaxFilmsPerRun: String(settings.oscarEnrichmentMaxFilmsPerRun),
    oscarEnrichmentMaxRequestsPerDay: String(settings.oscarEnrichmentMaxRequestsPerDay),
  }
}

function splitValues(value: string): string[] {
  return value.split(',').map((part) => part.trim()).filter(Boolean)
}

export function SourceSettingsView({ onOpenHistory = () => {} }: {
  onOpenHistory?: (scanRunId: number | null) => void
} = {}) {
  const [sourceProfiles, setSourceProfiles] = useState<SourceProfile[]>([])
  const [settings, setSettings] = useState<Settings | null>(null)
  const [settingsDraft, setSettingsDraft] = useState<SettingsDraft>(emptySettings)
  const [providerSettings, setProviderSettings] = useState<ProviderSettings | null>(null)
  const [omdbUsage, setOmdbUsage] = useState<OmdbDailyUsage[]>([])
  const [systemVersion, setSystemVersion] = useState<SystemVersion | null>(null)
  const [providerDraft, setProviderDraft] = useState<ProviderSettingsDraft>(emptyProviderSettings)
  const [sourceDraft, setSourceDraft] = useState('')
  const [editingProfileId, setEditingProfileId] = useState<FeedType>('movie')
  const [editingId, setEditingId] = useState<number | null>(null)
  const [removingId, setRemovingId] = useState<number | null>(null)
  const [showSourceForm, setShowSourceForm] = useState(false)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [sourceError, setSourceError] = useState<string | null>(null)
  const [settingsError, setSettingsError] = useState<string | null>(null)
  const [providerError, setProviderError] = useState<string | null>(null)
  const [savingSource, setSavingSource] = useState(false)
  const [savingSettings, setSavingSettings] = useState(false)
  const [savingProvider, setSavingProvider] = useState(false)
  const [sourceSaved, setSourceSaved] = useState(false)
  const [settingsSaved, setSettingsSaved] = useState(false)
  const [providerSaved, setProviderSaved] = useState(false)

  useEffect(() => {
    let current = true
    setLoading(true)
    setLoadError(null)
    Promise.all([getSources(), getSettings(), getProviderSettings(), getOmdbDailyUsage(), getVersion().catch(() => null)])
      .then(([sourceList, applicationSettings, omdbSettings, usage, applicationVersion]) => {
        if (!current) return
        setSourceProfiles(sourceList)
        setSettings(applicationSettings)
        setSettingsDraft(settingsToDraft(applicationSettings))
        setProviderSettings(omdbSettings)
        setProviderDraft(providerSettingsToDraft(omdbSettings))
        setOmdbUsage(usage)
        setSystemVersion(applicationVersion)
      })
      .catch((requestError: unknown) => {
        if (current) setLoadError(requestError instanceof Error ? requestError.message : 'Could not load source settings.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [loadAttempt])

  function startNewSource(profileId: FeedType) {
    setEditingId(null)
    setEditingProfileId(profileId)
    setSourceDraft('')
    setSourceError(null)
    setSourceSaved(false)
    setShowSourceForm(true)
  }

  function editSource(profileId: FeedType, source: SourceUrl) {
    setEditingId(source.id)
    setEditingProfileId(profileId)
    setSourceDraft(source.url)
    setSourceError(null)
    setSourceSaved(false)
    setShowSourceForm(true)
  }

  async function saveSource(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSavingSource(true)
    setSourceError(null)
    setSourceSaved(false)
    try {
      const saved = editingId === null
        ? await addSourceUrl(editingProfileId, sourceDraft)
        : await replaceSourceUrl(editingProfileId, editingId, sourceDraft)
      setSourceProfiles((current) => current.map((profile) => profile.id !== editingProfileId ? profile : {
        ...profile,
        urls: editingId === null
          ? [...profile.urls, saved]
          : profile.urls.map((source) => source.id === saved.id ? saved : source),
      }))
      setShowSourceForm(false)
      setSourceDraft('')
      setEditingId(null)
      setSourceSaved(true)
    } catch (requestError) {
      setSourceError(requestError instanceof Error ? requestError.message : 'Could not save this source.')
    } finally {
      setSavingSource(false)
    }
  }

  async function removeSource(profileId: FeedType, source: SourceUrl) {
    setRemovingId(source.id)
    setSourceError(null)
    setSourceSaved(false)
    try {
      await removeSourceUrl(profileId, source.id)
      setSourceProfiles((current) => current.map((profile) => profile.id === profileId
        ? { ...profile, urls: profile.urls.filter((item) => item.id !== source.id) }
        : profile))
      setSourceSaved(true)
    } catch (requestError) {
      setSourceError(requestError instanceof Error ? requestError.message : 'Could not remove this URL.')
    } finally {
      setRemovingId(null)
    }
  }

  async function saveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSavingSettings(true)
    setSettingsError(null)
    setSettingsSaved(false)
    const payload: SettingsInput = {
      excludedGenres: splitValues(settingsDraft.excludedGenres),
      excludedCountries: splitValues(settingsDraft.excludedCountries),
      minMovieRating: Number(settingsDraft.minMovieRating),
      minSeriesRating: Number(settingsDraft.minSeriesRating),
      minImdbVotes: Number(settingsDraft.minImdbVotes),
    }
    try {
      const saved = await updateSettings(payload)
      setSettings(saved)
      setSettingsDraft(settingsToDraft(saved))
      setSettingsSaved(true)
    } catch (requestError) {
      setSettingsError(requestError instanceof Error ? requestError.message : 'Could not save matching settings.')
    } finally {
      setSavingSettings(false)
    }
  }

  async function saveProviderSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSavingProvider(true)
    setProviderError(null)
    setProviderSaved(false)
    const payload: ProviderSettingsInput = {
      omdbApiKey: providerDraft.omdbApiKey.trim() || null,
      clearOmdbApiKey: providerDraft.clearOmdbApiKey,
      omdbDailyRequestLimit: Number(providerDraft.omdbDailyRequestLimit),
      oscarEnrichmentMaxFilmsPerRun: Number(providerDraft.oscarEnrichmentMaxFilmsPerRun),
      oscarEnrichmentMaxRequestsPerDay: Number(providerDraft.oscarEnrichmentMaxRequestsPerDay),
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

  function updateSettingsField<K extends keyof SettingsDraft>(key: K, value: SettingsDraft[K]) {
    setSettingsDraft((current) => ({ ...current, [key]: value }))
  }

  if (loading) return <LoadingState label="Loading source configuration" />
  if (loadError) return <ErrorState message={loadError} onRetry={() => setLoadAttempt((current) => current + 1)} />

  return (
    <section aria-label="Sources and settings">
      <div className="management-section">
        <div className="section-title-row">
          <div><h2>RSS profiles</h2><p>Feed URLs are fixed to their system profile.</p></div>
        </div>
        {sourceSaved && <p className="form-message" role="status">Feed URL saved.</p>}
        {sourceError && <p className="form-error" role="alert">{sourceError}</p>}
        {sourceProfiles.map((profile) => (
          <section aria-label={`${profile.name} profile`} className="source-profile" key={profile.id}>
            <div className="source-profile-heading">
              <h3>{profile.name}</h3>
              <button className="button button-secondary" onClick={() => startNewSource(profile.id)} type="button">Add URL</button>
            </div>
            {profile.urls.length === 0 ? (
              <p className="section-caption">No URLs configured.</p>
            ) : (
              <div className="source-list">
                {profile.urls.map((source) => (
                  <div className="source-row" key={source.id}>
                    <span className="source-url" title={source.url}>{source.url}</span>
                    <div className="source-actions">
                      <button aria-label={`Replace ${source.url}`} className="text-button" onClick={() => editSource(profile.id, source)} type="button">Replace</button>
                      <button aria-label={`Remove ${source.url}`} className="text-button" disabled={removingId === source.id} onClick={() => removeSource(profile.id, source)} type="button">Remove</button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>
        ))}

        {showSourceForm && (
          <form className="source-form" onSubmit={saveSource}>
            <div className="section-title-row"><div><h3>{editingId === null ? 'Add RSS URL' : 'Replace RSS URL'}</h3><p>{sourceProfiles.find(profile => profile.id === editingProfileId)?.name}</p></div></div>
            <div className="form-grid">
              <div className="field"><label htmlFor="source-url">Feed URL</label><input id="source-url" maxLength={2048} onChange={(event) => setSourceDraft(event.target.value)} required type="url" value={sourceDraft} /></div>
            </div>
            <div className="form-actions">
              <button className="button" disabled={savingSource} type="submit">{savingSource ? 'Saving...' : 'Save URL'}</button>
              <button className="button button-secondary" onClick={() => setShowSourceForm(false)} type="button">Cancel</button>
            </div>
          </form>
        )}
      </div>

      <BackgroundIngestionPanel onOpenHistory={onOpenHistory} />

      <div className="management-grid">
        <section aria-labelledby="matching-settings-heading">
          <h2 id="matching-settings-heading">Matching settings</h2>
          <p className="section-caption">Thresholds and exclusions applied to feed matches.</p>
          <form className="settings-form" onSubmit={saveSettings}>
            <div className="field"><label htmlFor="excluded-genres">Excluded genres</label><textarea id="excluded-genres" onChange={(event) => updateSettingsField('excludedGenres', event.target.value)} placeholder="Comma-separated values" value={settingsDraft.excludedGenres} /></div>
            <div className="field"><label htmlFor="excluded-countries">Excluded countries</label><textarea id="excluded-countries" onChange={(event) => updateSettingsField('excludedCountries', event.target.value)} placeholder="Comma-separated values" value={settingsDraft.excludedCountries} /></div>
            <div className="form-grid">
              <div className="field"><label htmlFor="min-movie-rating">Minimum movie rating</label><input id="min-movie-rating" max="10" min="0" onChange={(event) => updateSettingsField('minMovieRating', event.target.value)} required step="0.1" type="number" value={settingsDraft.minMovieRating} /></div>
              <div className="field"><label htmlFor="min-series-rating">Minimum series rating</label><input id="min-series-rating" max="10" min="0" onChange={(event) => updateSettingsField('minSeriesRating', event.target.value)} required step="0.1" type="number" value={settingsDraft.minSeriesRating} /></div>
            </div>
            <div className="field"><label htmlFor="min-imdb-votes">Minimum IMDb votes</label><input id="min-imdb-votes" max="1000000000" min="0" onChange={(event) => updateSettingsField('minImdbVotes', event.target.value)} required type="number" value={settingsDraft.minImdbVotes} /></div>
            {settings?.updatedAt && <p className="section-caption">Last updated {formatDate(settings.updatedAt)}</p>}
            {settingsError && <p className="form-error" role="alert">{settingsError}</p>}
            {settingsSaved && <p className="form-message" role="status">Matching settings saved.</p>}
            <div className="form-actions"><button className="button" disabled={savingSettings} type="submit">{savingSettings ? 'Saving...' : 'Save settings'}</button></div>
          </form>
        </section>

        <section aria-labelledby="provider-settings-heading">
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
            <div className="form-grid">
              <div className="field">
                <label htmlFor="oscar-max-films">Oscar films per run</label>
                <input id="oscar-max-films" max="100000" min="0" onChange={(event) => setProviderDraft((current) => ({ ...current, oscarEnrichmentMaxFilmsPerRun: event.target.value }))} required type="number" value={providerDraft.oscarEnrichmentMaxFilmsPerRun} />
              </div>
              <div className="field">
                <label htmlFor="oscar-daily-limit">Oscar daily HTTP limit</label>
                <input id="oscar-daily-limit" min="0" onChange={(event) => setProviderDraft((current) => ({ ...current, oscarEnrichmentMaxRequestsPerDay: event.target.value }))} required type="number" value={providerDraft.oscarEnrichmentMaxRequestsPerDay} />
              </div>
            </div>
            <p className="section-caption">Set a positive shared limit matching the OMDb key's confirmed quota. RSS and Oscar share a 50-request safety reserve and stop at 50 below this limit. Oscar limits are additional caps, not reserved capacity. Set Oscar films per run to 0 to disable enrichment.</p>
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
      </div>
      <section aria-labelledby="system-version-heading" className="management-section">
        <h2 id="system-version-heading">System version</h2>
        {systemVersion ? (
          <>
            <p className="section-caption">Version {systemVersion.version}</p>
            <p className="section-caption">Commit {systemVersion.commitSha.slice(0, 12)}</p>
            {systemVersion.commitDateUtc && <p className="section-caption">Commit date (UTC) {systemVersion.commitDateUtc.slice(0, 19).replace('T', ' ')}</p>}
          </>
        ) : <p className="section-caption">Version unavailable</p>}
      </section>
      <p className="trust-note">The API key is stored in the local database and never returned by the API. Settings writes are unauthenticated and use the local HTTP connection, so keep MediaDock on a trusted LAN and protect database backups.</p>
    </section>
  )
}
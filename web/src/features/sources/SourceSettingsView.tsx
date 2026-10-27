import { useEffect, useState, type FormEvent } from 'react'
import { addSourceUrl, getSettings, getSources, removeSourceUrl, replaceSourceUrl, updateSettings } from '../../api/client'
import type { FeedType, Settings, SettingsInput, SourceProfile, SourceUrl } from '../../api/types'
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

const emptySettings: SettingsDraft = { excludedGenres: '', excludedCountries: '', minMovieRating: '0', minSeriesRating: '0', minImdbVotes: '0' }

function settingsToDraft(settings: Settings): SettingsDraft {
  return {
    excludedGenres: settings.excludedGenres.join(', '),
    excludedCountries: settings.excludedCountries.join(', '),
    minMovieRating: String(settings.minMovieRating),
    minSeriesRating: String(settings.minSeriesRating),
    minImdbVotes: String(settings.minImdbVotes),
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
  const [savingSource, setSavingSource] = useState(false)
  const [savingSettings, setSavingSettings] = useState(false)
  const [sourceSaved, setSourceSaved] = useState(false)
  const [settingsSaved, setSettingsSaved] = useState(false)

  useEffect(() => {
    let current = true
    Promise.all([getSources(), getSettings()])
      .then(([sourceList, applicationSettings]) => {
        if (!current) return
        setSourceProfiles(sourceList)
        setSettings(applicationSettings)
        setSettingsDraft(settingsToDraft(applicationSettings))
      })
      .catch((requestError: unknown) => {
        if (current) setLoadError(requestError instanceof Error ? requestError.message : 'Could not load torrent settings.')
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

  function updateSettingsField<K extends keyof SettingsDraft>(key: K, value: SettingsDraft[K]) {
    setSettingsDraft((current) => ({ ...current, [key]: value }))
  }

  if (loading) return <LoadingState label="Loading torrent settings" />
  if (loadError) return <ErrorState message={loadError} onRetry={() => {
    setLoadError(null)
    setLoading(true)
    setLoadAttempt((current) => current + 1)
  }} />

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

      <BackgroundIngestionPanel mode="torrent" onOpenHistory={onOpenHistory} />

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
      </div>
    </section>
  )
}
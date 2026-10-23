import { useEffect, useState, type FormEvent } from 'react'
import {
  getRecommendationSettings,
  recalculateRecommendationVoteWeights,
  updateRecommendationSettings,
} from '../../api/client'
import type {
  RecommendationAwardWeight,
  RecommendationSettings,
  RecommendationSettingsInput,
  VoteWeightRecalculationResponse,
} from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { formatDate, formatWords } from '../../shared/format'

interface RecommendationAwardWeightDraft {
  awardSource: RecommendationAwardWeight['awardSource']
  categoryKey: string
  winPoints: string
  nominationPoints: string
  isOverride: boolean
}

interface RecommendationSettingsDraft {
  medianPeriodYears: string
  bayesianDefaultC: string
  bayesianMRatio: string
  oscarWinPoints: string
  oscarNomPoints: string
  ggWinPoints: string
  ggNomPoints: string
  awardWeights: RecommendationAwardWeightDraft[]
}

type RecommendationGlobalSetting = Exclude<keyof RecommendationSettingsDraft, 'awardWeights'>

const voteWeightPollIntervalMs = 2_000
const dirtyWarning = 'New metadata detected. Recommended scores may be inaccurate. Recalculate vote weights.'

function settingsToDraft(settings: RecommendationSettings): RecommendationSettingsDraft {
  return {
    medianPeriodYears: String(settings.medianPeriodYears),
    bayesianDefaultC: String(settings.bayesianDefaultC),
    bayesianMRatio: String(settings.bayesianMRatio),
    oscarWinPoints: String(settings.oscarWinPoints),
    oscarNomPoints: String(settings.oscarNomPoints),
    ggWinPoints: String(settings.ggWinPoints),
    ggNomPoints: String(settings.ggNomPoints),
    awardWeights: settings.awardWeights.map((weight) => ({
      awardSource: weight.awardSource,
      categoryKey: weight.categoryKey,
      winPoints: String(weight.winPoints),
      nominationPoints: String(weight.nominationPoints),
      isOverride: weight.isOverride,
    })),
  }
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback
}

function recalculationErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'status' in error && error.status === 504) {
    return 'Vote recalculation timed out after 10 seconds. The dirty flag remains set; retry when ready.'
  }
  return errorMessage(error, 'Could not recalculate vote weights.')
}

function awardSourceLabel(source: RecommendationAwardWeight['awardSource']): string {
  return source === 'oscar' ? 'Oscar' : 'Golden Globes'
}

export function RecommendationSettingsView() {
  const [settings, setSettings] = useState<RecommendationSettings | null>(null)
  const [draft, setDraft] = useState<RecommendationSettingsDraft | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [settingsSaved, setSettingsSaved] = useState(false)
  const [recalculating, setRecalculating] = useState(false)
  const [polling, setPolling] = useState(false)
  const [recalculationError, setRecalculationError] = useState<string | null>(null)
  const [recalculationMessage, setRecalculationMessage] = useState<string | null>(null)
  const [recalculationResult, setRecalculationResult] = useState<VoteWeightRecalculationResponse | null>(null)

  useEffect(() => {
    let active = true
    getRecommendationSettings()
      .then((response) => {
        if (!active) return
        setSettings(response)
        setDraft(settingsToDraft(response))
      })
      .catch((error: unknown) => {
        if (active) setLoadError(errorMessage(error, 'Could not load recommendation settings.'))
      })
      .finally(() => { if (active) setLoading(false) })

    return () => { active = false }
  }, [loadAttempt])

  useEffect(() => {
    if (!polling) return

    let active = true
    let timeoutId: number | undefined
    const poll = async () => {
      try {
        const response = await getRecommendationSettings()
        if (!active) return
        setSettings(response)
        setDraft(settingsToDraft(response))
        setRecalculationError(null)
        if (!response.requiresVoteRecalculation) {
          setPolling(false)
          setRecalculationMessage('Vote weights are up to date.')
          return
        }
      } catch (error: unknown) {
        if (!active) return
        setRecalculationError(errorMessage(error, 'Could not refresh vote-weight recalculation status.'))
      }
      timeoutId = window.setTimeout(() => { void poll() }, voteWeightPollIntervalMs)
    }

    void poll()
    return () => {
      active = false
      if (timeoutId !== undefined) window.clearTimeout(timeoutId)
    }
  }, [polling])

  function updateAwardWeight(index: number, field: 'winPoints' | 'nominationPoints', value: string) {
    setDraft((current) => current && ({
      ...current,
      awardWeights: current.awardWeights.map((weight, weightIndex) =>
        weightIndex === index ? { ...weight, [field]: value, isOverride: true } : weight),
    }))
    setSettingsSaved(false)
  }

  function updateAwardOverride(index: number, isOverride: boolean) {
    setDraft((current) => current && ({
      ...current,
      awardWeights: current.awardWeights.map((weight, weightIndex) => {
        if (weightIndex !== index) return weight
        if (!isOverride) return { ...weight, isOverride: false }
        return {
          ...weight,
          isOverride: true,
          winPoints: weight.awardSource === 'oscar' ? current.oscarWinPoints : current.ggWinPoints,
          nominationPoints: weight.awardSource === 'oscar' ? current.oscarNomPoints : current.ggNomPoints,
        }
      }),
    }))
    setSettingsSaved(false)
  }

  function updateGlobalSetting(field: RecommendationGlobalSetting, value: string) {
    setDraft((current) => current && ({ ...current, [field]: value }))
    setSettingsSaved(false)
  }

  async function saveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!draft) return

    const medianPeriodYears = Number(draft.medianPeriodYears)
    const bayesianDefaultC = Number(draft.bayesianDefaultC)
    const bayesianMRatio = Number(draft.bayesianMRatio)
    const oscarWinPoints = Number(draft.oscarWinPoints)
    const oscarNomPoints = Number(draft.oscarNomPoints)
    const ggWinPoints = Number(draft.ggWinPoints)
    const ggNomPoints = Number(draft.ggNomPoints)
    const awardWeights = draft.awardWeights.map((weight) => ({
      awardSource: weight.awardSource,
      categoryKey: weight.categoryKey,
      winPoints: Number(weight.winPoints),
      nominationPoints: Number(weight.nominationPoints),
      isOverride: weight.isOverride,
    }))
    if (!Number.isInteger(medianPeriodYears) || medianPeriodYears < 1
      || !Number.isFinite(bayesianDefaultC) || bayesianDefaultC < 0 || bayesianDefaultC > 10
      || !Number.isFinite(bayesianMRatio) || bayesianMRatio <= 0 || bayesianMRatio > 1
      || [oscarWinPoints, oscarNomPoints, ggWinPoints, ggNomPoints].some((points) => !Number.isFinite(points) || points < 0)
      || awardWeights.some((weight) => !Number.isFinite(weight.winPoints)
        || weight.winPoints < 0
        || !Number.isFinite(weight.nominationPoints)
        || weight.nominationPoints < 0)) {
      setSaveError('Enter valid Bayesian settings and nonnegative award points.')
      return
    }

    const input: RecommendationSettingsInput = {
      medianPeriodYears,
      bayesianDefaultC,
      bayesianMRatio,
      oscarWinPoints,
      oscarNomPoints,
      ggWinPoints,
      ggNomPoints,
      awardWeights,
    }
    setSaving(true)
    setSaveError(null)
    setSettingsSaved(false)
    setRecalculationMessage(null)
    try {
      const response = await updateRecommendationSettings(input)
      setSettings(response)
      setDraft(settingsToDraft(response))
      setSettingsSaved(true)
    } catch (error: unknown) {
      setSaveError(errorMessage(error, 'Could not save recommendation settings.'))
    } finally {
      setSaving(false)
    }
  }

  async function recalculateVoteWeights() {
    setRecalculating(true)
    setRecalculationError(null)
    setRecalculationMessage(null)
    setRecalculationResult(null)
    try {
      const response = await recalculateRecommendationVoteWeights()
      if (!('calculatedAt' in response)) {
        setRecalculationMessage('Vote-weight recalculation was accepted and is running.')
        setPolling(true)
        return
      }

      setRecalculationResult(response)
      try {
        const latestSettings = await getRecommendationSettings()
        setSettings(latestSettings)
        setDraft(settingsToDraft(latestSettings))
        setRecalculationMessage(latestSettings.requiresVoteRecalculation
          ? 'Recalculation completed, but newer metadata still needs vote weights.'
          : 'Vote weights are up to date.')
      } catch (error: unknown) {
        setRecalculationError(`Vote weights were recalculated, but settings could not be refreshed: ${errorMessage(error, 'request failed.')}`)
      }
    } catch (error: unknown) {
      setRecalculationError(recalculationErrorMessage(error))
    } finally {
      setRecalculating(false)
    }
  }

  if (loading) return <LoadingState label="Loading recommendation settings" />
  if (loadError) return <ErrorState message={loadError} onRetry={() => {
    setLoadError(null)
    setLoading(true)
    setLoadAttempt((attempt) => attempt + 1)
  }} />
  if (!settings || !draft) {
    return <EmptyState title="Recommendation settings unavailable" message="The settings response did not contain a usable configuration." />
  }

  const controlsDisabled = saving || recalculating || polling

  return <section aria-labelledby="recommendation-settings-heading" className="management-section">
    <h2 id="recommendation-settings-heading">Recommendation settings</h2>
    <p className="section-caption">Global vote-normalization and award-point settings. Personal score coefficients are selected on the recommendations page and are not saved here.</p>

    {settings.requiresVoteRecalculation && <aside className="recommendation-dirty-warning" role="alert">
      <p>{dirtyWarning}</p>
    </aside>}

    <div className="recommendation-freshness">
      <div>
        <strong>Vote-weight freshness</strong>
        <p className="section-caption">
          {settings.lastVoteRecalculationAt
            ? `Last recalculated ${formatDate(settings.lastVoteRecalculationAt)}${settings.lastVoteRecalculationPeriodYears ? ` using a ${settings.lastVoteRecalculationPeriodYears}-year period` : ''}.`
            : 'No successful vote-weight recalculation is recorded.'}
        </p>
      </div>
      <button className="button button-secondary" disabled={controlsDisabled} onClick={() => void recalculateVoteWeights()} type="button">
        {polling ? 'Waiting for recalculation...' : recalculating ? 'Recalculating...' : 'Recalculate vote weights'}
      </button>
    </div>

    {recalculationMessage && <p className="form-message" role="status">{recalculationMessage}
      {recalculationResult && ` ${recalculationResult.movieCount.toLocaleString()} movies processed in ${recalculationResult.durationMilliseconds.toLocaleString()} ms.`}
    </p>}
    {recalculationError && <p className="form-error" role="alert">{recalculationError}</p>}

    <form className="settings-form" onSubmit={(event) => void saveSettings(event)}>
      <div className="field">
        <label htmlFor="recommendation-median-period">Vote median period (years)</label>
        <input
          disabled={controlsDisabled}
          id="recommendation-median-period"
          min="1"
          onChange={(event) => updateGlobalSetting('medianPeriodYears', event.currentTarget.value)}
          required
          step="1"
          type="number"
          value={draft.medianPeriodYears}
        />
      </div>

      <div>
        <h3 className="recommendation-settings-subheading">Scoring defaults</h3>
        <p className="section-caption">Bayesian confidence uses each movie period's vote median. Source-level award points apply unless a category override is enabled.</p>
        <div className="recommendation-scoring-fields">
          <div className="field">
            <label htmlFor="recommendation-bayesian-default-c">Bayesian default C</label>
            <input disabled={controlsDisabled} id="recommendation-bayesian-default-c" max="10" min="0" onChange={(event) => updateGlobalSetting('bayesianDefaultC', event.currentTarget.value)} required step="any" type="number" value={draft.bayesianDefaultC} />
          </div>
          <div className="field">
            <label htmlFor="recommendation-bayesian-m-ratio">Bayesian m ratio</label>
            <input disabled={controlsDisabled} id="recommendation-bayesian-m-ratio" max="1" min="0.0001" onChange={(event) => updateGlobalSetting('bayesianMRatio', event.currentTarget.value)} required step="any" type="number" value={draft.bayesianMRatio} />
          </div>
          <div className="field">
            <label htmlFor="recommendation-oscar-win-points">Oscar win points</label>
            <input disabled={controlsDisabled} id="recommendation-oscar-win-points" min="0" onChange={(event) => updateGlobalSetting('oscarWinPoints', event.currentTarget.value)} required step="any" type="number" value={draft.oscarWinPoints} />
          </div>
          <div className="field">
            <label htmlFor="recommendation-oscar-nom-points">Oscar nomination points</label>
            <input disabled={controlsDisabled} id="recommendation-oscar-nom-points" min="0" onChange={(event) => updateGlobalSetting('oscarNomPoints', event.currentTarget.value)} required step="any" type="number" value={draft.oscarNomPoints} />
          </div>
          <div className="field">
            <label htmlFor="recommendation-gg-win-points">Golden Globes win points</label>
            <input disabled={controlsDisabled} id="recommendation-gg-win-points" min="0" onChange={(event) => updateGlobalSetting('ggWinPoints', event.currentTarget.value)} required step="any" type="number" value={draft.ggWinPoints} />
          </div>
          <div className="field">
            <label htmlFor="recommendation-gg-nom-points">Golden Globes nomination points</label>
            <input disabled={controlsDisabled} id="recommendation-gg-nom-points" min="0" onChange={(event) => updateGlobalSetting('ggNomPoints', event.currentTarget.value)} required step="any" type="number" value={draft.ggNomPoints} />
          </div>
        </div>
      </div>

      <div>
        <h3 className="recommendation-settings-subheading">Category overrides</h3>
        <p className="section-caption">Enable an override to use custom points for one source and category.</p>
        {draft.awardWeights.length === 0 ? <EmptyState title="No award weights configured" message="The server has not returned any source-qualified award categories." /> : (
          <div className="table-wrap">
            <table className="data-table recommendation-settings-table">
              <thead><tr><th scope="col">SOURCE</th><th scope="col">CATEGORY</th><th scope="col">OVERRIDE</th><th scope="col">WIN POINTS</th><th scope="col">NOMINATION POINTS</th></tr></thead>
              <tbody>{draft.awardWeights.map((weight, index) => {
                const source = awardSourceLabel(weight.awardSource)
                const category = formatWords(weight.categoryKey)
                return <tr key={`${weight.awardSource}/${weight.categoryKey}`}>
                  <td>{source}</td>
                  <td>{category}</td>
                  <td><input
                    aria-label={`${source} ${category} use category override`}
                    checked={weight.isOverride}
                    disabled={controlsDisabled}
                    onChange={(event) => updateAwardOverride(index, event.currentTarget.checked)}
                    type="checkbox"
                  /></td>
                  <td><input
                    aria-label={`${source} ${category} win points`}
                    disabled={controlsDisabled || !weight.isOverride}
                    min="0"
                    onChange={(event) => updateAwardWeight(index, 'winPoints', event.currentTarget.value)}
                    required
                    step="any"
                    type="number"
                    value={weight.winPoints}
                  /></td>
                  <td><input
                    aria-label={`${source} ${category} nomination points`}
                    disabled={controlsDisabled || !weight.isOverride}
                    min="0"
                    onChange={(event) => updateAwardWeight(index, 'nominationPoints', event.currentTarget.value)}
                    required
                    step="any"
                    type="number"
                    value={weight.nominationPoints}
                  /></td>
                </tr>
              })}</tbody>
            </table>
          </div>
        )}
      </div>

      {saveError && <p className="form-error" role="alert">{saveError}</p>}
      {settingsSaved && <p className="form-message" role="status">Recommendation settings saved.</p>}
      <div className="form-actions">
        <button className="button" disabled={controlsDisabled} type="submit">{saving ? 'Saving...' : 'Save recommendation settings'}</button>
      </div>
    </form>
  </section>
}
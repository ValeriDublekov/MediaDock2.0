import { useEffect, useState } from 'react'
import { ApiError, getDeploymentStatus, runDeploymentAction } from '../../api/client'
import type { DeploymentStatus } from '../../api/types'

function attemptSummary(status: DeploymentStatus): string {
  if (status.isRunning) return 'Deployment running'
  if (!status.startedAt) return 'No deployment attempt recorded'
  if (status.result === 'success' && status.exitCode === '0') return 'Last deployment check succeeded'
  if (status.result === 'failed' || status.exitCode !== '0') return 'Last deployment check failed'
  return `Last deployment check: ${status.result}`
}

export function DeploymentControlPanel() {
  const [status, setStatus] = useState<DeploymentStatus | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [refreshToken, setRefreshToken] = useState(0)

  useEffect(() => {
    let current = true
    let timer: number | undefined
    const poll = async () => {
      try {
        const latest = await getDeploymentStatus()
        if (!current) return
        setStatus(latest)
        setStatusError(null)
        timer = window.setTimeout(() => { void poll() }, latest.isRunning ? 3000 : 12000)
      } catch (requestError: unknown) {
        if (!current) return
        setStatusError(requestError instanceof Error ? requestError.message : 'Could not load deployment status.')
        timer = window.setTimeout(() => { void poll() }, 12000)
      }
    }
    void poll()
    return () => {
      current = false
      if (timer !== undefined) window.clearTimeout(timer)
    }
  }, [refreshToken])

  async function runAction(action: 'check' | 'retry_failed_gate') {
    const message = action === 'check'
      ? 'Run the deployment gate and, if main has advanced, back up, migrate, and restart MediaDock now?'
      : `Retry the failed staging gate for ${status?.gateFailedSha?.slice(0, 12)}? This clears only the gate-failed marker.`
    if (!window.confirm(message)) return

    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      const result = await runDeploymentAction(action)
      setNotice(result.message)
      setRefreshToken((current) => current + 1)
    } catch (requestError: unknown) {
      setActionError(requestError instanceof ApiError && requestError.status === 409
        ? requestError.message
        : requestError instanceof Error ? requestError.message : 'Could not start the deployment check.')
      setRefreshToken((current) => current + 1)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-labelledby="deployment-control-heading" className="management-section deployment-control">
      <div className="section-title-row">
        <div>
          <h2 id="deployment-control-heading">Deployment</h2>
          <p>GitHub main · five-minute automatic checks</p>
        </div>
        <div className="form-actions">
          {status?.gateFailedSha && !status.recoveryRequired && (
            <button className="button button-secondary" disabled={busy || status.isRunning} onClick={() => { void runAction('retry_failed_gate') }} type="button">
              Retry failed gate
            </button>
          )}
          <button className="button" disabled={busy || status?.isRunning || status?.recoveryRequired} onClick={() => { void runAction('check') }} type="button">
            {busy ? 'Starting...' : 'Check and deploy now'}
          </button>
        </div>
      </div>

      {statusError && <p className="form-error" role="alert">Deployment control unavailable: {statusError}</p>}
      {notice && <p className="form-message" role="status">{notice}</p>}
      {actionError && <p className="form-error" role="alert">{actionError}</p>}
      {status && (
        <>
          <div className="deployment-status-grid">
            <div><span className={`state-pill is-${status.isRunning ? 'running' : status.result === 'failed' ? 'failed' : 'muted'}`}>{attemptSummary(status)}</span></div>
            <p className="section-caption">Live commit: <code>{status.deployedSha?.slice(0, 12) ?? 'Unknown'}</code></p>
            {status.startedAt && <p className="section-caption">Last started: {status.startedAt}</p>}
            {status.finishedAt && <p className="section-caption">Last finished: {status.finishedAt}</p>}
          </div>
          {status.gateFailedSha && <p className="form-error">Staging gate failed for <code>{status.gateFailedSha.slice(0, 12)}</code>.</p>}
          {status.recoveryRequired && (
            <p className="form-error" role="alert">
              Recovery required{status.failureTargetSha ? ` for ${status.failureTargetSha.slice(0, 12)}` : ''}. Automatic and manual deployment are blocked; inspect the host recovery procedure.
            </p>
          )}
          {status.recentOutput.length > 0 && (
            <details className="deployment-output">
              <summary>Recent deployment output</summary>
              <pre>{status.recentOutput.join('\n')}</pre>
            </details>
          )}
        </>
      )}
    </section>
  )
}
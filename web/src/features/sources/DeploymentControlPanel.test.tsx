import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getDeploymentStatus, runDeploymentAction } from '../../api/client'
import type { DeploymentStatus } from '../../api/types'
import { DeploymentControlPanel } from './DeploymentControlPanel'

vi.mock('../../api/client', () => ({
  ApiError: class extends Error {
    readonly status: number

    constructor(message: string, status: number) {
      super(message)
      this.status = status
    }
  },
  getDeploymentStatus: vi.fn(),
  runDeploymentAction: vi.fn(),
}))

const status: DeploymentStatus = {
  isRunning: false,
  activeState: 'inactive',
  subState: 'dead',
  result: 'success',
  exitCode: '0',
  startedAt: 'Thu 2026-10-01 12:00:00 UTC',
  finishedAt: 'Thu 2026-10-01 12:02:00 UTC',
  deployedSha: 'a'.repeat(40),
  gateFailedSha: null,
  recoveryRequired: false,
  failureTargetSha: null,
  recentOutput: ['Deployment succeeded.'],
}

describe('DeploymentControlPanel', () => {
  beforeEach(() => {
    vi.mocked(getDeploymentStatus).mockReset().mockResolvedValue(status)
    vi.mocked(runDeploymentAction).mockReset().mockResolvedValue({ message: 'Deployment check queued.' })
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows deployment state and starts an immediate check after confirmation', async () => {
    render(<DeploymentControlPanel />)

    await screen.findByText('Last deployment check succeeded')
    expect(screen.getByText('aaaaaaaaaaaa')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Check and deploy now' }))

    expect((await screen.findByRole('status')).textContent).toContain('Deployment check queued.')
    expect(runDeploymentAction).toHaveBeenCalledWith('check')
  })

  it('offers retry only for a failed staging gate and blocks it while recovery is required', async () => {
    vi.mocked(getDeploymentStatus).mockResolvedValueOnce({
      ...status,
      result: 'failed',
      exitCode: '1',
      gateFailedSha: 'b'.repeat(40),
    }).mockResolvedValueOnce({
      ...status,
      recoveryRequired: true,
      failureTargetSha: 'c'.repeat(40),
    })
    render(<DeploymentControlPanel />)

    fireEvent.click(await screen.findByRole('button', { name: 'Retry failed gate' }))
    expect(runDeploymentAction).toHaveBeenCalledWith('retry_failed_gate')

    expect(await screen.findByText(/Recovery required for cccccccccccc/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Check and deploy now' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
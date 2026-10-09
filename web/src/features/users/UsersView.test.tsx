import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, decideRegistrationRequest, getAdminUsers, setAdminUserStatus } from '../../api/client'
import type { AdminUsersPage } from '../../api/types'
import { UsersView } from './UsersView'

vi.mock('../../api/client', () => ({
  ApiError: class ApiError extends Error {
    status: number
    constructor(message: string, status: number) { super(message); this.status = status }
  },
  decideRegistrationRequest: vi.fn(),
  getAdminUsers: vi.fn(),
  setAdminUserStatus: vi.fn(),
}))

const requestedUsers: AdminUsersPage = {
  items: [{
    id: 4,
    email: 'new@example.com',
    givenName: 'New',
    familyName: 'User',
    status: 'pending',
    role: null,
    createdAt: '2026-10-09T10:00:00Z',
    registrationRequest: { id: 8, status: 'pending', requestedAt: '2026-10-09T10:00:00Z', decidedAt: null },
  }],
  page: 1,
  pageSize: 50,
  totalCount: 1,
  totalPages: 1,
}

describe('UsersView', () => {
  beforeEach(() => {
    vi.mocked(getAdminUsers).mockReset().mockResolvedValue(requestedUsers)
    vi.mocked(decideRegistrationRequest).mockReset().mockResolvedValue(undefined)
    vi.mocked(setAdminUserStatus).mockReset().mockResolvedValue(undefined)
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows and manages requested users without requiring sign-in', async () => {
    render(<UsersView />)

    expect(await screen.findByText('new@example.com')).toBeTruthy()
    expect(screen.getByText(/open to clients within the configured network boundary/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))

    await waitFor(() => expect(decideRegistrationRequest).toHaveBeenCalledWith(8, 'approve'))
    expect(window.confirm).toHaveBeenCalledWith('Approve the registration request for New User?')
    await waitFor(() => expect(getAdminUsers).toHaveBeenCalledTimes(2))
  })

  it('shows a mutation error without hiding the open users list', async () => {
    vi.mocked(decideRegistrationRequest).mockRejectedValueOnce(new ApiError('Forbidden', 403))

    render(<UsersView />)

    expect(await screen.findByText('new@example.com')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))
    expect(await screen.findByText('An active administrator account is required to manage users.')).toBeTruthy()
  })

  it('shows API errors if the open users list cannot be loaded', async () => {
    vi.mocked(getAdminUsers).mockRejectedValueOnce(new ApiError('Service unavailable', 503))

    render(<UsersView />)

    expect(await screen.findByText('Service unavailable')).toBeTruthy()
  })
})
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { confirmGoogleAccountLink, getCurrentSession, requestRegistration, signOut } from '../../api/client'
import type { CurrentSession } from '../../api/types'
import { AuthStatus } from './AuthStatus'

vi.mock('../../api/client', () => ({
  confirmGoogleAccountLink: vi.fn(),
  getCurrentSession: vi.fn(),
  requestRegistration: vi.fn(),
  signOut: vi.fn(),
}))

const anonymousSession: CurrentSession = {
  authenticated: false,
  identity: null,
  user: null,
  accountState: 'anonymous',
  signInEnabled: true,
  registrationRequest: null,
}

const unmatchedSession: CurrentSession = {
  authenticated: true,
  identity: {
    issuer: 'https://accounts.google.com',
    subject: 'validated-subject',
    email: 'person@example.com',
    givenName: 'Google',
    familyName: 'Profile',
    profileComplete: true,
  },
  user: null,
  accountState: 'unmatched',
  signInEnabled: true,
  registrationRequest: null,
}

function linkedSession(registrationRequest: CurrentSession['registrationRequest'] = null): CurrentSession {
  return {
    ...unmatchedSession,
    user: { email: 'person@example.com', givenName: 'Stored', familyName: 'Account', status: 'pending' },
    accountState: 'linked',
    registrationRequest,
  }
}

describe('AuthStatus', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => cleanup())

  it('keeps anonymous use available and offers Google sign-in only when enabled', async () => {
    vi.mocked(getCurrentSession).mockResolvedValue(anonymousSession)

    render(<AuthStatus />)

    expect(await screen.findByText('Anonymous access')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Sign in with Google' }).getAttribute('href')).toBe('/api/auth/google/login')
  })

  it('shows linked account and request state without offering a duplicate request', async () => {
    vi.mocked(getCurrentSession).mockResolvedValue(linkedSession({
      status: 'pending',
      requestedAt: '2026-10-06T10:00:00Z',
      decidedAt: null,
    }))

    render(<AuthStatus />)

    expect(await screen.findByText('Stored Account')).toBeTruthy()
    expect(screen.getByRole('status').textContent).toContain('pending review')
    expect(screen.queryByRole('button', { name: 'Request registration' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy()
  })

  it('submits registration and reloads the resulting pending status', async () => {
    vi.mocked(getCurrentSession)
      .mockResolvedValueOnce(unmatchedSession)
      .mockResolvedValueOnce(linkedSession({
        status: 'pending',
        requestedAt: '2026-10-06T10:00:00Z',
        decidedAt: null,
      }))
    vi.mocked(requestRegistration).mockResolvedValue({
      status: 'pending',
      requestedAt: '2026-10-06T10:00:00Z',
      decidedAt: null,
    })

    render(<AuthStatus />)
    fireEvent.click(await screen.findByRole('button', { name: 'Request registration' }))

    expect(await screen.findByText('Registration request pending review')).toBeTruthy()
    expect(requestRegistration).toHaveBeenCalledOnce()
    expect(getCurrentSession).toHaveBeenCalledTimes(2)
  })

  it('requires explicit confirmation before linking a matching account', async () => {
    const linkConfirmation: CurrentSession = { ...linkedSession(), accountState: 'link_confirmation_required' }
    vi.mocked(getCurrentSession)
      .mockResolvedValueOnce(linkConfirmation)
      .mockResolvedValueOnce(linkedSession())
    vi.mocked(confirmGoogleAccountLink).mockResolvedValue(undefined)

    render(<AuthStatus />)
    fireEvent.click(await screen.findByRole('button', { name: 'Link this account' }))

    expect(await screen.findByText('Stored Account')).toBeTruthy()
    expect(confirmGoogleAccountLink).toHaveBeenCalledOnce()
  })

  it('explains missing Google names and does not offer registration', async () => {
    vi.mocked(getCurrentSession).mockResolvedValue({
      ...unmatchedSession,
      identity: { ...unmatchedSession.identity!, givenName: null, profileComplete: false },
    })

    render(<AuthStatus />)

    expect(await screen.findByText(/needs both given and family names/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Request registration' })).toBeNull()
    expect(requestRegistration).not.toHaveBeenCalled()
  })

  it('signs out and returns to anonymous status', async () => {
    vi.mocked(getCurrentSession)
      .mockResolvedValueOnce(linkedSession())
      .mockResolvedValueOnce(anonymousSession)
    vi.mocked(signOut).mockResolvedValue(undefined)

    render(<AuthStatus />)
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Anonymous access')).toBeTruthy()
    expect(signOut).toHaveBeenCalledOnce()
  })
})
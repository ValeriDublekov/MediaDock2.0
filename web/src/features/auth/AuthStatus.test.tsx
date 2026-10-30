import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { confirmGoogleAccountLink, getCurrentSession, signOut } from '../../api/client'
import type { CurrentSession } from '../../api/types'
import { AuthStatus } from './AuthStatus'

vi.mock('../../api/client', () => ({
  confirmGoogleAccountLink: vi.fn(),
  getCurrentSession: vi.fn(),
  signOut: vi.fn(),
}))

const anonymousSession: CurrentSession = {
  authenticated: false,
  identity: null,
  user: null,
  accountState: 'anonymous',
  signInEnabled: true,
  authorizationEnforced: false,
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
  authorizationEnforced: false,
  registrationRequest: null,
}

function linkedSession(registrationRequest: CurrentSession['registrationRequest'] = null): CurrentSession {
  return {
    ...unmatchedSession,
    user: { email: 'person@example.com', givenName: 'Stored', familyName: 'Account', status: 'pending', role: null },
    accountState: 'linked',
    registrationRequest,
  }
}

function authError(message: string, status: number, endpoint: string, traceId: string) {
  return Object.assign(new Error(message), { status, endpoint, traceId })
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

  it('clears an invalid saved session before offering a fresh sign-in', async () => {
    vi.mocked(getCurrentSession)
      .mockRejectedValueOnce(authError('The saved sign-in session is invalid.', 401, '/api/auth/session', 'trace-invalid-session'))
      .mockResolvedValueOnce(anonymousSession)
    vi.mocked(signOut).mockResolvedValue(undefined)
    const onSessionChange = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    render(<AuthStatus onSessionChange={onSessionChange} />)

    expect(await screen.findAllByText('The saved sign-in session is invalid.')).toHaveLength(2)
    expect(onSessionChange).toHaveBeenCalledWith(null, expect.objectContaining({
      operation: 'Load sign-in status',
      endpoint: '/api/auth/session',
      status: 401,
      traceId: 'trace-invalid-session',
    }))
    expect(consoleError).toHaveBeenCalledWith('[MediaDock auth diagnostic]', expect.objectContaining({ traceId: 'trace-invalid-session' }))
    fireEvent.click(screen.getByRole('button', { name: 'Reset sign-in' }))

    expect(await screen.findByText('Anonymous access')).toBeTruthy()
    expect(signOut).toHaveBeenCalledOnce()
    consoleError.mockRestore()
  })

  it('shows linked account and request state without offering a duplicate request', async () => {
    vi.mocked(getCurrentSession).mockResolvedValue(linkedSession({
      status: 'pending',
      requestedAt: '2026-10-06T10:00:00Z',
      decidedAt: null,
    }))

    render(<AuthStatus />)

    const profileButton = await screen.findByRole('button', { name: 'Account for Stored Account' })
    expect(profileButton.textContent).toBe('SA')
    expect(screen.queryByRole('button', { name: 'Sign out' })).toBeNull()
    fireEvent.click(profileButton)

    expect(await screen.findByText('Stored Account')).toBeTruthy()
    expect(screen.getByRole('status').textContent).toContain('pending review')
    expect(screen.queryByRole('button', { name: 'Request registration' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy()
  })

  it('closes the profile disclosure on Escape and outside pointer input', async () => {
    vi.mocked(getCurrentSession).mockResolvedValue(linkedSession())
    render(<AuthStatus />)

    const profileButton = await screen.findByRole('button', { name: 'Account for Stored Account' })
    fireEvent.click(profileButton)
    expect(profileButton.getAttribute('aria-expanded')).toBe('true')

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(profileButton.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(profileButton)

    fireEvent.click(profileButton)
    fireEvent.pointerDown(document.body)
    expect(profileButton.getAttribute('aria-expanded')).toBe('false')
  })

  it('shows the Google name for an unmatched account', async () => {
    vi.mocked(getCurrentSession).mockResolvedValue(unmatchedSession)
    render(<AuthStatus />)

    const profileButton = await screen.findByRole('button', { name: 'Account for Google Profile' })
    fireEvent.click(profileButton)
    expect(await screen.findByText('Google Profile')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Request registration' })).toBeNull()
  })

  it('keeps reset available and reports details when sign-out fails', async () => {
    vi.mocked(getCurrentSession).mockRejectedValueOnce(authError('Invalid session.', 401, '/api/auth/session', 'trace-load'))
    vi.mocked(signOut).mockRejectedValueOnce(authError('A valid anti-forgery token is required.', 400, '/api/auth/logout', 'trace-logout'))
    const onSessionChange = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    render(<AuthStatus onSessionChange={onSessionChange} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Reset sign-in' }))

    expect((await screen.findByRole('alert')).textContent).toContain('A valid anti-forgery token is required.')
    expect(screen.getByRole('button', { name: 'Reset sign-in' })).toBeTruthy()
    expect(onSessionChange).toHaveBeenLastCalledWith(null, expect.objectContaining({
      operation: 'Reset sign-in',
      endpoint: '/api/auth/logout',
      status: 400,
      traceId: 'trace-logout',
    }))
    consoleError.mockRestore()
  })

  it('requires explicit confirmation before linking a matching account', async () => {
    const linkConfirmation: CurrentSession = { ...linkedSession(), accountState: 'link_confirmation_required' }
    vi.mocked(getCurrentSession)
      .mockResolvedValueOnce(linkConfirmation)
      .mockResolvedValueOnce(linkedSession())
    vi.mocked(confirmGoogleAccountLink).mockResolvedValue(undefined)

    render(<AuthStatus />)
    fireEvent.click(await screen.findByRole('button', { name: 'Account for Stored Account' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Link this account' }))

    expect(await screen.findByText('Stored Account')).toBeTruthy()
    expect(confirmGoogleAccountLink).toHaveBeenCalledOnce()
  })

  it('signs out and returns to anonymous status', async () => {
    vi.mocked(getCurrentSession)
      .mockResolvedValueOnce(linkedSession())
      .mockResolvedValueOnce(anonymousSession)
    vi.mocked(signOut).mockResolvedValue(undefined)

    render(<AuthStatus />)
    fireEvent.click(await screen.findByRole('button', { name: 'Account for Stored Account' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Anonymous access')).toBeTruthy()
    expect(signOut).toHaveBeenCalledOnce()
  })

  it('clears the active session when logout succeeds but status refresh fails', async () => {
    vi.mocked(getCurrentSession)
      .mockResolvedValueOnce(linkedSession())
      .mockRejectedValueOnce(authError('Session status is unavailable.', 500, '/api/auth/session', 'trace-refresh'))
    vi.mocked(signOut).mockResolvedValue(undefined)
    const onSessionChange = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    render(<AuthStatus onSessionChange={onSessionChange} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Account for Stored Account' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Sign-in status unavailable')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Account for Stored Account' })).toBeNull()
    expect(onSessionChange).toHaveBeenLastCalledWith(null, expect.objectContaining({
      operation: 'Sign out',
      endpoint: '/api/auth/session',
      status: 500,
      traceId: 'trace-refresh',
    }))
    consoleError.mockRestore()
  })
})
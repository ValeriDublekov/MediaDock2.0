import { useEffect, useRef, useState } from 'react'
import { confirmGoogleAccountLink, getCurrentSession, signOut } from '../../api/client'
import type { AuthDiagnostic, CurrentSession } from '../../api/types'

function requestStateLabel(status: string) {
  if (status === 'pending') return 'Registration request pending review'
  if (status === 'approved') return 'Registration request approved'
  return 'Registration request rejected'
}

function isUnauthorizedError(error: unknown) {
  return error instanceof Error && 'status' in error && error.status === 401
}

function createDiagnostic(operation: string, fallbackEndpoint: string, error: unknown): AuthDiagnostic {
  const details = error && typeof error === 'object' ? error as Record<string, unknown> : null
  const status = typeof details?.status === 'number' ? details.status : null
  return {
    operation,
    endpoint: typeof details?.endpoint === 'string' ? details.endpoint : fallbackEndpoint,
    status,
    message: error instanceof Error ? error.message : 'An unexpected sign-in error occurred.',
    traceId: typeof details?.traceId === 'string' ? details.traceId : null,
    occurredAt: new Date().toISOString(),
  }
}

export function AuthStatus({ onSessionChange }: {
  onSessionChange?: (session: CurrentSession | null, diagnostic?: AuthDiagnostic | null) => void
}) {
  const [session, setSession] = useState<CurrentSession | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [invalidSession, setInvalidSession] = useState(false)
  const [profileOpen, setProfileOpen] = useState(false)
  const profileButtonRef = useRef<HTMLButtonElement>(null)
  const profilePanelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let active = true
    async function loadSession() {
      try {
        const currentSession = await getCurrentSession()
        if (!active) return

        setSession(currentSession)
        onSessionChange?.(currentSession, null)
        setInvalidSession(false)
      } catch (loadError: unknown) {
        if (active) {
          const diagnostic = createDiagnostic('Load sign-in status', '/api/auth/session', loadError)
          console.error('[MediaDock auth diagnostic]', diagnostic)
          onSessionChange?.(null, diagnostic)
          setError(diagnostic.message)
          setInvalidSession(isUnauthorizedError(loadError))
        }
      } finally {
        if (active) setLoading(false)
      }
    }

    void loadSession()
    return () => { active = false }
  }, [onSessionChange])

  useEffect(() => {
    if (!profileOpen) return

    function handlePointerDown(event: PointerEvent) {
      if (!(event.target instanceof Node)) return
      if (profilePanelRef.current?.contains(event.target) || profileButtonRef.current?.contains(event.target)) return
      setProfileOpen(false)
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return
      setProfileOpen(false)
      profileButtonRef.current?.focus()
    }

    document.addEventListener('pointerdown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [profileOpen])

  async function runAction(
    operation: string,
    endpoint: string,
    action: () => Promise<unknown>,
    clearSessionAfterAction = false,
  ) {
    setBusy(true)
    setError(null)
    let actionCompleted = false
    try {
      await action()
      actionCompleted = true
      if (clearSessionAfterAction) {
        setSession(null)
        onSessionChange?.(null, null)
        setInvalidSession(false)
      }
      const updatedSession = await getCurrentSession()
      setSession(updatedSession)
      onSessionChange?.(updatedSession, null)
      setInvalidSession(false)
    } catch (actionError) {
      const diagnostic = createDiagnostic(operation, endpoint, actionError)
      console.error('[MediaDock auth diagnostic]', diagnostic)
      const signedOut = clearSessionAfterAction && actionCompleted
      const nextSession = signedOut ? null : session
      setSession(nextSession)
      onSessionChange?.(nextSession, diagnostic)
      setError(diagnostic.message)
      setInvalidSession(signedOut ? false : nextSession === null || isUnauthorizedError(actionError))
    } finally {
      setBusy(false)
    }
  }

  const identity = session?.identity
  const user = session?.user
  const displayName = user
    ? `${user.givenName} ${user.familyName}`
    : identity?.givenName && identity.familyName
      ? `${identity.givenName} ${identity.familyName}`
      : identity?.givenName ?? identity?.familyName ?? identity?.email
  const profileLabel = displayName?.trim() || identity?.email || 'Signed-in account'
  const profileInitials = profileLabel.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part.charAt(0)).join('').toUpperCase()

  return (
    <section aria-label="Sign-in status" className={`auth-strip${session?.authenticated ? ' auth-profile' : ''}`}>
      {loading ? (
        <span className="auth-summary" role="status">Checking sign-in status</span>
      ) : session === null ? (
        <>
          <span className="auth-message" role="status">{invalidSession ? 'The saved sign-in session is invalid.' : 'Sign-in status unavailable'}</span>
          {error && <button className="text-button" disabled={busy} onClick={() => void runAction('Reset sign-in', '/api/auth/logout', signOut, true)} type="button">Reset sign-in</button>}
          <button className="text-button" disabled={busy} onClick={() => void runAction('Retry session check', '/api/auth/session', getCurrentSession)} type="button">Retry</button>
        </>
      ) : !session.authenticated ? (
        <>
          <div className="auth-summary" aria-live="polite">
            <strong>Anonymous access</strong>
            <span>Sign-in is optional</span>
          </div>

          <div className="auth-controls">
            {session.signInEnabled
              ? <a className="button button-secondary" href="/api/auth/google/login">Sign in with Google</a>
              : <span className="auth-unavailable">Google sign-in is disabled</span>}
          </div>
        </>
      ) : (
        <>
          <button
            aria-controls="auth-profile-panel"
            aria-expanded={profileOpen}
            aria-label={`Account for ${profileLabel}`}
            className="auth-profile-trigger"
            onClick={() => setProfileOpen((open) => !open)}
            ref={profileButtonRef}
            type="button"
          >
            {profileInitials}
          </button>
          <div className="auth-profile-panel" hidden={!profileOpen} id="auth-profile-panel" ref={profilePanelRef}>
            <div className="auth-summary" aria-live="polite">
              <strong>{displayName}</strong>
              <span className="auth-email">{identity?.email}</span>
              {session.registrationRequest && (
                <span className="auth-request-state" role="status">
                  {requestStateLabel(session.registrationRequest.status)}
                </span>
              )}
              {session.accountState === 'link_confirmation_required' && (
                <span>Confirm the matching account before linking.</span>
              )}
              {session.accountState === 'account_conflict' && (
                <span className="auth-message">This email is already linked to another Google identity.</span>
              )}
            </div>

            <div className="auth-controls">
              {session.accountState === 'link_confirmation_required' && (
                <button className="button button-secondary" disabled={busy} onClick={() => void runAction('Link Google account', '/api/auth/google/link', confirmGoogleAccountLink)} type="button">
                  {busy ? 'Linking…' : 'Link this account'}
                </button>
              )}
              <button className="text-button" disabled={busy} onClick={() => void runAction('Sign out', '/api/auth/logout', signOut, true)} type="button">
                {busy ? 'Please wait' : 'Sign out'}
              </button>
            </div>
            {error && <span className="auth-message" role="alert">{error}</span>}
          </div>
        </>
      )}
      {!session?.authenticated && error && <span className="auth-message" role="alert">{error}</span>}
    </section>
  )
}
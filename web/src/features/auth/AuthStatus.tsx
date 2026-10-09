import { useEffect, useState } from 'react'
import { confirmGoogleAccountLink, getCurrentSession, signOut } from '../../api/client'
import type { CurrentSession } from '../../api/types'

function requestStateLabel(status: string) {
  if (status === 'pending') return 'Registration request pending review'
  if (status === 'approved') return 'Registration request approved'
  return 'Registration request rejected'
}

function isUnauthorizedError(error: unknown) {
  return error instanceof Error && 'status' in error && error.status === 401
}
export function AuthStatus({ onSessionChange }: { onSessionChange?: (session: CurrentSession | null) => void }) {
  const [session, setSession] = useState<CurrentSession | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [invalidSession, setInvalidSession] = useState(false)

  useEffect(() => {
    let active = true
    async function loadSession() {
      try {
        const currentSession = await getCurrentSession()
        if (!active) return

        setSession(currentSession)
        onSessionChange?.(currentSession)
        setInvalidSession(false)
      } catch (loadError: unknown) {
        if (active) {
          onSessionChange?.(null)
          setError(loadError instanceof Error ? loadError.message : 'Could not load sign-in status.')
          setInvalidSession(isUnauthorizedError(loadError))
        }
      } finally {
        if (active) setLoading(false)
      }
    }

    void loadSession()
    return () => { active = false }
  }, [onSessionChange])

  async function runAction(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      const updatedSession = await getCurrentSession()
      setSession(updatedSession)
      onSessionChange?.(updatedSession)
      setInvalidSession(false)
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : 'The sign-in action failed.')
      setInvalidSession(isUnauthorizedError(actionError))
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

  return (
    <section aria-label="Sign-in status" className="auth-strip">
      {loading ? (
        <span className="auth-summary" role="status">Checking sign-in status</span>
      ) : session === null ? (
        <>
          <span className="auth-message" role="status">{invalidSession ? 'The saved sign-in session is invalid.' : 'Sign-in status unavailable'}</span>
          {invalidSession && <button className="text-button" disabled={busy} onClick={() => void runAction(signOut)} type="button">Reset sign-in</button>}
          <button className="text-button" disabled={busy} onClick={() => void runAction(getCurrentSession)} type="button">Retry</button>
        </>
      ) : (
        <>
          <div className="auth-summary" aria-live="polite">
            {!session.authenticated ? (
              <>
                <strong>Anonymous access</strong>
                <span>Sign-in is optional</span>
              </>
            ) : (
              <>
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
              </>
            )}
          </div>

          <div className="auth-controls">
            {!session.authenticated ? (
              session.signInEnabled
                ? <a className="button button-secondary" href="/api/auth/google/login">Sign in with Google</a>
                : <span className="auth-unavailable">Google sign-in is disabled</span>
            ) : (
              <>
                {session.accountState === 'link_confirmation_required' && (
                  <button className="button button-secondary" disabled={busy} onClick={() => void runAction(confirmGoogleAccountLink)} type="button">
                    {busy ? 'Linking…' : 'Link this account'}
                  </button>
                )}
                <button className="text-button" disabled={busy} onClick={() => void runAction(signOut)} type="button">
                  {busy ? 'Please wait' : 'Sign out'}
                </button>
              </>
            )}
          </div>
        </>
      )}
      {error && <span className="auth-message" role="alert">{error}</span>}
    </section>
  )
}
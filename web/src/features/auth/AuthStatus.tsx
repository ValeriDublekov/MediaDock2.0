import { useEffect, useState } from 'react'
import { confirmGoogleAccountLink, getCurrentSession, requestRegistration, signOut } from '../../api/client'
import type { CurrentSession } from '../../api/types'

function requestStateLabel(status: string) {
  if (status === 'pending') return 'Registration request pending review'
  if (status === 'approved') return 'Registration request approved'
  return 'Registration request rejected'
}

export function AuthStatus() {
  const [session, setSession] = useState<CurrentSession | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    void getCurrentSession()
      .then((currentSession) => {
        if (active) setSession(currentSession)
      })
      .catch((loadError: unknown) => {
        if (active) setError(loadError instanceof Error ? loadError.message : 'Could not load sign-in status.')
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => { active = false }
  }, [])

  async function runAction(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      setSession(await getCurrentSession())
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : 'The sign-in action failed.')
    } finally {
      setBusy(false)
    }
  }

  const identity = session?.identity
  const user = session?.user

  return (
    <section aria-label="Sign-in status" className="auth-strip">
      {loading ? (
        <span className="auth-summary" role="status">Checking sign-in status</span>
      ) : session === null ? (
        <>
          <span className="auth-message" role="status">Sign-in status unavailable</span>
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
                <strong>{user ? `${user.givenName} ${user.familyName}` : identity?.email}</strong>
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
                {session.accountState === 'unmatched' && identity && !identity.profileComplete && (
                  <span className="auth-message">Your Google profile needs both given and family names before registration.</span>
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
                {session.accountState === 'unmatched' && identity?.profileComplete && !session.registrationRequest && (
                  <button className="button button-secondary" disabled={busy} onClick={() => void runAction(requestRegistration)} type="button">
                    {busy ? 'Submitting…' : 'Request registration'}
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
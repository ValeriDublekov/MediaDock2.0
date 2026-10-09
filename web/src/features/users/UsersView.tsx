import { useEffect, useState, type FormEvent } from 'react'
import { ApiError, decideRegistrationRequest, getAdminUsers, setAdminUserStatus } from '../../api/client'
import type { AdminUser, AdminUserState, AdminUsersPage } from '../../api/types'
import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'

const emptyUsers: AdminUsersPage = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 }

function loadErrorMessage(error: unknown) {
  if (error instanceof ApiError && error.status === 401) return 'Sign in with a linked Google account to continue.'
  if (error instanceof ApiError && error.status === 403) return 'An active administrator account is required to manage users.'
  return error instanceof Error ? error.message : 'Could not load users.'
}

export function UsersView() {
  const [state, setState] = useState<AdminUserState>('requested')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [pageNumber, setPageNumber] = useState(1)
  const [page, setPage] = useState<AdminUsersPage>(emptyUsers)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [busyUserId, setBusyUserId] = useState<number | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let current = true
    void getAdminUsers({ state, search: search || undefined, page: pageNumber, pageSize: 50 })
      .then((result) => { if (current) setPage(result) })
      .catch((loadError: unknown) => { if (current) setError(loadErrorMessage(loadError)) })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [state, search, pageNumber, reload])

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoading(true)
    setError(null)
    setSearch(searchInput.trim())
    setPageNumber(1)
  }

  async function runAction(user: AdminUser, action: 'approve' | 'reject' | 'activate' | 'deactivate') {
    const person = `${user.givenName} ${user.familyName}`
    if (action === 'approve' && !window.confirm(`Approve the registration request for ${person}?`)) return
    if (action === 'reject' && !window.confirm(`Reject the registration request for ${person}?`)) return
    if (action === 'deactivate' && !window.confirm(`Deactivate ${person}'s account?`)) return

    setBusyUserId(user.id)
    setActionError(null)
    try {
      if (action === 'approve' || action === 'reject') {
        if (user.registrationRequest) await decideRegistrationRequest(user.registrationRequest.id, action)
      } else {
        await setAdminUserStatus(user.id, action === 'activate' ? 'active' : 'deactivated')
      }
      setLoading(true)
      setReload((current) => current + 1)
    } catch (requestError) {
      setActionError(loadErrorMessage(requestError))
    } finally {
      setBusyUserId(null)
    }
  }

  function retryLoad() {
    setLoading(true)
    setError(null)
    setReload((current) => current + 1)
  }

  if (loading) return <LoadingState label="Loading users" />
  if (error) return <ErrorState message={error} onRetry={retryLoad} />

  return (
    <section aria-labelledby="users-heading" className="management-section">
      <h2 id="users-heading">Users</h2>
      <p className="section-caption">Review registration requests and manage accounts. These actions are open to clients within the configured network boundary.</p>
      <div className="users-toolbar">
        <label className="field users-state-filter">
          <span className="field-label">Show</span>
          <select onChange={(event) => {
            setLoading(true)
            setError(null)
            setState(event.target.value as AdminUserState)
            setPageNumber(1)
          }} value={state}>
            <option value="requested">Requested</option>
            <option value="all">All users</option>
            <option value="active">Active</option>
            <option value="deactivated">Deactivated</option>
          </select>
        </label>
        <form className="users-search" onSubmit={submitSearch}>
          <label className="field">
            <span className="field-label">Search</span>
            <input onChange={(event) => setSearchInput(event.target.value)} placeholder="Name or email" value={searchInput} />
          </label>
          <button className="button button-secondary" type="submit">Search</button>
        </form>
        <span className="result-count">{page.totalCount} {page.totalCount === 1 ? 'user' : 'users'}</span>
      </div>
      {actionError && <p className="form-error" role="alert">{actionError}</p>}
      {page.items.length === 0 ? (
        <EmptyState title="No users found" message={state === 'requested' ? 'There are no pending registration requests.' : 'No accounts match these filters.'} />
      ) : (
        <div className="table-wrap">
          <table className="data-table users-table">
            <thead><tr><th scope="col">Account</th><th scope="col">Status</th><th scope="col">Role</th><th scope="col">Request</th><th scope="col">Actions</th></tr></thead>
            <tbody>
              {page.items.map((user) => (
                <tr key={user.id}>
                  <td><strong>{user.givenName} {user.familyName}</strong><span className="subtle-line">{user.email}</span></td>
                  <td><span className={`state-pill ${user.status === 'active' ? 'is-enriched' : ''}`}>{user.status}</span></td>
                  <td>{user.role ?? 'Not assigned'}</td>
                  <td>{user.registrationRequest ? user.registrationRequest.status : 'No request'}</td>
                  <td>
                    <div className="users-actions">
                      {user.registrationRequest?.status === 'pending' && <>
                        <button className="button" disabled={busyUserId === user.id} onClick={() => void runAction(user, 'approve')} type="button">Approve</button>
                        <button className="button button-danger" disabled={busyUserId === user.id} onClick={() => void runAction(user, 'reject')} type="button">Reject</button>
                      </>}
                      {user.status === 'active' && <button className="button button-secondary" disabled={busyUserId === user.id} onClick={() => void runAction(user, 'deactivate')} type="button">Deactivate</button>}
                      {user.status === 'deactivated' && <button className="button button-secondary" disabled={busyUserId === user.id} onClick={() => void runAction(user, 'activate')} type="button">Activate</button>}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {page.totalPages > 1 && <div aria-label="User pagination" className="pagination">
        <span>Page {page.page} of {page.totalPages}</span>
        <div className="pagination-actions">
          <button className="button button-secondary" disabled={pageNumber <= 1} onClick={() => {
            setLoading(true)
            setPageNumber((current) => current - 1)
          }} type="button">Previous</button>
          <button className="button button-secondary" disabled={pageNumber >= page.totalPages} onClick={() => {
            setLoading(true)
            setPageNumber((current) => current + 1)
          }} type="button">Next</button>
        </div>
      </div>}
    </section>
  )
}
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useState, type ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { getCurrentSession, requestRegistration } from './api/client'
import type { AuthDiagnostic, CurrentSession } from './api/types'
import App from './App'

const authMock = vi.hoisted(() => ({
  session: null as CurrentSession | null,
  diagnostic: null as AuthDiagnostic | null,
  nextSession: null as CurrentSession | null,
}))

vi.mock('./api/client', () => ({
  getCurrentSession: vi.fn(),
  requestRegistration: vi.fn(),
}))
vi.mock('./features/auth/AuthStatus', async () => {
  const { useEffect, useRef } = await import('react')
  return {
    AuthStatus: ({ onSessionChange }: { onSessionChange: (session: CurrentSession | null, diagnostic?: AuthDiagnostic | null) => void }) => {
      const reported = useRef(false)
      useEffect(() => {
        if (!reported.current) {
          reported.current = true
          onSessionChange(authMock.session, authMock.diagnostic)
        }
      }, [onSessionChange])
      return <div>
        Authentication status
        {authMock.nextSession && <button onClick={() => {
          const nextSession = authMock.nextSession
          authMock.nextSession = null
          if (nextSession) {
            authMock.session = nextSession
            onSessionChange(nextSession)
          }
        }} type="button">Switch account</button>}
      </div>
    },
  }
})
vi.mock('./features/catalog/CatalogView', () => ({
  CatalogView: ({ canDeleteTitles, canViewScanHistory }: { canDeleteTitles: boolean; canViewScanHistory: boolean }) => <div data-can-delete-titles={canDeleteTitles} data-can-view-scan-history={canViewScanHistory}>Catalog route content</div>,
}))
vi.mock('./features/oscar/OscarCatalogView', () => ({ OscarCatalogView: () => <div>Oscar route content</div> }))
vi.mock('./features/golden-globes/GoldenGlobeCatalogView', () => ({ GoldenGlobeCatalogView: () => <div>Golden Globes route content</div> }))
vi.mock('./features/favorites/FavoritesView', () => ({ FavoritesView: () => <div>Favorites route content</div> }))
vi.mock('./features/ignored-movies/IgnoredMoviesView', () => ({ IgnoredMoviesView: () => <div>Ignored movies route content</div> }))
vi.mock('./features/recommendations/RecommendationSettingsView', () => ({
  RecommendationSettingsView: () => <div>Recommendation settings route content</div>,
}))
vi.mock('./features/recommendations/RecommendationsView', () => ({
  RecommendationsView: ({ mediaType }: { mediaType: string }) => (
    <div data-media-type={mediaType} data-testid="recommendations-route-content">Recommendations route content</div>
  ),
}))
vi.mock('./features/favorites/FavoriteContext', () => ({
  FavoriteProvider: ({ children }: { children: ReactNode }) => children,
}))
vi.mock('./features/ignored-movies/IgnoredMoviesContext', () => ({
  IgnoredMoviesProvider: ({ children }: { children: ReactNode }) => children,
}))
vi.mock('./features/sources/SourceSettingsView', () => ({
  SourceSettingsView: ({ onOpenHistory }: { onOpenHistory: (scanRunId: number) => void }) => (
    <button onClick={() => onOpenHistory(42)} type="button">Open scan 42</button>
  ),
}))
vi.mock('./features/sources/ConfigurationSettingsViews', () => ({
  IngestionSettingsView: () => <div>Ingestion route content</div>,
  OmdbProviderView: () => <div>OMDb route content</div>,
  PersonalRatingsView: () => {
    const [hasImportResult, setHasImportResult] = useState(false)
    return <div>
      <div>Ratings route content</div>
      <button onClick={() => setHasImportResult(true)} type="button">Mark import result</button>
      {hasImportResult && <div>Previous account import result</div>}
    </div>
  },
  SystemSettingsView: () => <div>System route content</div>,
}))
vi.mock('./features/history/HistoryView', () => ({
  HistoryView: ({ scanRunId, onClearScanRun }: { scanRunId: number | null; onClearScanRun: () => void }) => (
    <div>History scan: {scanRunId ?? 'all'}<button onClick={onClearScanRun} type="button">Clear scan</button></div>
  ),
}))
vi.mock('./features/users/UsersView', () => ({ UsersView: () => <div>Users route content</div> }))
vi.mock('./features/dashboard/MovieTablesDashboardView', () => ({ MovieTablesDashboardView: () => <div>Dashboard route content</div> }))

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}{location.search}</output>
}

function renderAt(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><App /><LocationProbe /></MemoryRouter>)
}

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

const activeUserSession: CurrentSession = {
  ...unmatchedSession,
  user: { email: 'person@example.com', givenName: 'Stored', familyName: 'Account', status: 'active', role: 'user' },
  accountState: 'linked',
}

const activeAdminSession: CurrentSession = {
  ...activeUserSession,
  user: { ...activeUserSession.user!, role: 'admin' },
}

describe('App routes', () => {
  beforeEach(() => {
    authMock.session = anonymousSession
    authMock.diagnostic = null
    authMock.nextSession = null
    vi.mocked(getCurrentSession).mockReset()
    vi.mocked(requestRegistration).mockReset()
  })
  afterEach(() => cleanup())

  it.each([
    ['/catalog', 'Torents catalog'],
    ['/oscar', 'Oscar catalog'],
    ['/golden-globes', 'Golden Globes catalog'],
  ])('opens %s directly', async (path, heading) => {
    renderAt(path)

    expect(await screen.findByRole('heading', { name: heading })).toBeTruthy()
    expect(screen.getByRole('note').textContent).toContain('Access control is disabled')
    expect(screen.getByTestId('location').textContent).toBe(path)
  })

  it('blocks anonymous visitors from opening favorites directly', async () => {
    renderAt('/favorites')

    expect(await screen.findByRole('heading', { name: 'Favorites unavailable' })).toBeTruthy()
    expect(screen.queryByText('Favorites route content')).toBeNull()
  })

  it('blocks anonymous visitors from opening the ignore list directly', async () => {
    renderAt('/account/ignored-movies')

    expect(await screen.findByRole('heading', { name: 'Ignore list unavailable' })).toBeTruthy()
    expect(screen.queryByText('Ignored movies route content')).toBeNull()
  })

  it('blocks anonymous visitors from opening recommendations directly', async () => {
    renderAt('/account/recommendations')

    expect(await screen.findByRole('heading', { name: 'Recommendations unavailable' })).toBeTruthy()
    expect(screen.queryByText('Recommendations route content')).toBeNull()
  })

  it('shows shared library navigation without personal or admin links to anonymous visitors', async () => {
    renderAt('/catalog')

    expect(await screen.findByRole('navigation', { name: 'Library navigation' })).toBeTruthy()
    expect(screen.getByRole('link', { name: /Torents catalog/ })).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Favorites/ })).toBeNull()
    expect(screen.queryByRole('navigation', { name: 'Personal navigation' })).toBeNull()
    expect(screen.queryByRole('navigation', { name: 'Administration navigation' })).toBeNull()
  })

  it('shows personal ratings to active users but keeps administration hidden', async () => {
    authMock.session = activeUserSession

    renderAt('/account/personal-ratings')

    expect(await screen.findByText('Ratings route content')).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Personal IMDb ratings')
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Favorites')
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Ignored titles')
    expect(screen.queryByRole('navigation', { name: 'Administration navigation' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Users' })).toBeNull()
  })

  it('shows personal recommendations to active users but keeps administration hidden', async () => {
    authMock.session = activeUserSession

    renderAt('/account/recommendations')

    expect(await screen.findByText('Recommendations route content')).toBeTruthy()
    expect(screen.getByTestId('recommendations-route-content').getAttribute('data-media-type')).toBe('movie')
    expect(screen.getByRole('link', { name: 'Movie recommendations' }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByRole('link', { name: 'Series recommendations' })).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Personal IMDb ratings')
    expect(screen.queryByRole('navigation', { name: 'Administration navigation' })).toBeNull()
  })

  it('opens series recommendations from their own personal navigation item', async () => {
    authMock.session = activeUserSession

    renderAt('/account/recommendations/series')

    expect(await screen.findByText('Recommendations route content')).toBeTruthy()
    expect(screen.getByTestId('recommendations-route-content').getAttribute('data-media-type')).toBe('series')
    expect(screen.getByRole('link', { name: 'Series recommendations' }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByRole('link', { name: 'Movie recommendations' }).getAttribute('aria-current')).toBeNull()
  })

  it('shows the ignore list to active users', async () => {
    authMock.session = activeUserSession

    renderAt('/account/ignored-movies')

    expect(await screen.findByText('Ignored movies route content')).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Ignored titles')
  })

  it('shows catalog delete and scan history permissions only to active administrators', async () => {
    authMock.session = activeUserSession
    const userView = renderAt('/catalog')
    const userCatalog = await screen.findByText('Catalog route content')
    expect(userCatalog.getAttribute('data-can-delete-titles')).toBe('false')
    expect(userCatalog.getAttribute('data-can-view-scan-history')).toBe('false')
    userView.unmount()

    authMock.session = activeAdminSession
    renderAt('/catalog')
    const adminCatalog = await screen.findByText('Catalog route content')
    expect(adminCatalog.getAttribute('data-can-delete-titles')).toBe('true')
    expect(adminCatalog.getAttribute('data-can-view-scan-history')).toBe('true')
  })

  it('shows admin controls separately from account and library navigation', async () => {
    authMock.session = activeAdminSession

    renderAt('/configuration/users')

    expect(await screen.findByText('Users route content')).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Library navigation' })).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Personal IMDb ratings')
    const administrationNavigation = screen.getByRole('navigation', { name: 'Administration navigation' })
    expect(administrationNavigation.textContent).toContain('Users')
    expect(administrationNavigation.textContent).toContain('Recommendation settings')
    expect(administrationNavigation.textContent).not.toContain('Personal IMDb ratings')
    expect(screen.getByRole('navigation', { name: 'Administration section navigation' }).textContent).toContain('Administration')
  })

  it('opens recommendation settings for administrators', async () => {
    authMock.session = activeAdminSession

    renderAt('/configuration/recommendations')

    expect(await screen.findByText('Recommendation settings route content')).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Administration navigation' }).textContent).toContain('Recommendation settings')
  })

  it('opens movie tables dashboard for administrators', async () => {
    authMock.session = activeAdminSession

    renderAt('/configuration/dashboard')

    expect(await screen.findByText('Dashboard route content')).toBeTruthy()
    expect(screen.getByRole('navigation', { name: 'Administration navigation' }).textContent).toContain('Dashboard')
  })

  it.each([
    '/configuration/dashboard',
    '/configuration/torrent',
    '/configuration/ingestion',
    '/configuration/recommendations',
    '/configuration/omdb',
    '/configuration/system',
    '/configuration/users',
    '/history',
  ])('blocks an ordinary user from the admin URL %s', async (path) => {
    authMock.session = activeUserSession

    renderAt(path)

    expect(await screen.findByRole('heading', { name: 'Administrator access required' })).toBeTruthy()
    expect(screen.queryByText(/route content/)).toBeNull()
  })

  it('requires an active linked account for personal ratings and redirects the old route', async () => {
    renderAt('/configuration/personal-ratings')

    expect(await screen.findByRole('heading', { name: 'Personal ratings unavailable' })).toBeTruthy()
    expect(screen.getByTestId('location').textContent).toBe('/account/personal-ratings')
    expect(screen.queryByText('Ratings route content')).toBeNull()
  })

  it('clears the previous account ratings view and refreshes role navigation on account change', async () => {
    const switchedAdminSession: CurrentSession = {
      ...activeAdminSession,
      identity: { ...activeAdminSession.identity!, subject: 'second-account' },
    }
    authMock.session = activeUserSession
    authMock.nextSession = switchedAdminSession

    renderAt('/account/personal-ratings')
    fireEvent.click(await screen.findByRole('button', { name: 'Mark import result' }))
    expect(screen.getByText('Previous account import result')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Switch account' }))

    expect(await screen.findByRole('navigation', { name: 'Administration section navigation' })).toBeTruthy()
    expect(screen.queryByText('Previous account import result')).toBeNull()
    expect(screen.getByRole('navigation', { name: 'Personal navigation' }).textContent).toContain('Personal IMDb ratings')
  })

  it('hides the open-mode warning only when the server confirms enforcement', async () => {
    authMock.session = { ...anonymousSession, authorizationEnforced: true }

    renderAt('/catalog')

    expect(await screen.findByRole('heading', { name: 'Torents catalog' })).toBeTruthy()
    expect(screen.queryByRole('note')).toBeNull()
  })

  it('redirects the configuration root to system settings', async () => {
    authMock.session = activeAdminSession
    renderAt('/configuration')

    expect(await screen.findByRole('heading', { name: 'System' })).toBeTruthy()
    expect(screen.getByTestId('location').textContent).toBe('/configuration/system')
  })

  it('shows configuration subnavigation and opens its pages', () => {
    authMock.session = activeAdminSession
    renderAt('/catalog')

    fireEvent.click(screen.getByRole('link', { name: /Administration/ }))
    expect(screen.getByTestId('location').textContent).toBe('/configuration/system')
    expect(screen.getByText('System route content')).toBeTruthy()
    const administrationLink = screen.getByRole('link', { name: /Administration/ })
    const administrationNavigation = screen.getByRole('navigation', { name: 'Administration navigation' })
    const historyLink = screen.getByRole('link', { name: 'Scan history' })
    expect(administrationLink.compareDocumentPosition(administrationNavigation) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(administrationNavigation.compareDocumentPosition(historyLink) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(administrationNavigation.querySelector('a')?.textContent).toBe('System')

    fireEvent.click(screen.getByRole('link', { name: 'OMDb provider' }))
    expect(screen.getByTestId('location').textContent).toBe('/configuration/omdb')
    expect(screen.getByRole('heading', { name: 'OMDb provider' })).toBeTruthy()
  })

  it('updates the URL through navigation and preserves a selected scan in history', () => {
    authMock.session = activeAdminSession
    renderAt('/catalog')

    fireEvent.click(screen.getByRole('link', { name: /Golden Globes catalog/ }))
    expect(screen.getByTestId('location').textContent).toBe('/golden-globes')

    fireEvent.click(screen.getByRole('link', { name: /Administration/ }))
    fireEvent.click(screen.getByRole('link', { name: 'Torrent settings' }))
    fireEvent.click(screen.getByRole('button', { name: 'Open scan 42' }))
    expect(screen.getByTestId('location').textContent).toBe('/history?scanRunId=42')
    expect(screen.getByText('History scan: 42')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Clear scan' }))
    expect(screen.getByTestId('location').textContent).toBe('/history')
    expect(screen.getByText('History scan: all')).toBeTruthy()
  })

  it('gates an unmatched Google user until they explicitly request registration', async () => {
    authMock.session = unmatchedSession
    vi.mocked(requestRegistration).mockResolvedValue({ status: 'pending', requestedAt: '2026-10-09T10:00:00Z', decidedAt: null })
    vi.mocked(getCurrentSession).mockResolvedValue({
      ...unmatchedSession,
      user: { email: 'person@example.com', givenName: 'Google', familyName: 'Profile', status: 'pending', role: null },
      accountState: 'linked',
      registrationRequest: { status: 'pending', requestedAt: '2026-10-09T10:00:00Z', decidedAt: null },
    })

    renderAt('/catalog')

    expect(await screen.findByRole('heading', { name: 'Hello Google' })).toBeTruthy()
    expect(screen.queryByText('Catalog route content')).toBeNull()
    expect(requestRegistration).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Request registration' }))

    expect(await screen.findByText('Catalog route content')).toBeTruthy()
    expect(requestRegistration).toHaveBeenCalledOnce()
  })

  it('keeps the registration screen open when the request fails', async () => {
    authMock.session = unmatchedSession
    vi.mocked(requestRegistration).mockRejectedValue(new Error('Request failed'))

    renderAt('/catalog')
    expect(await screen.findByRole('heading', { name: 'Hello Google' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Request registration' }))

    expect((await screen.findByRole('alert')).textContent).toContain('Request failed')
    expect(screen.getByRole('heading', { name: 'Hello Google' })).toBeTruthy()
  })

  it('does not offer registration when Google name claims are incomplete', async () => {
    authMock.session = {
      ...unmatchedSession,
      identity: { ...unmatchedSession.identity!, givenName: null, familyName: null, profileComplete: false },
    }

    renderAt('/catalog')

    expect(await screen.findByRole('heading', { name: 'Hello' })).toBeTruthy()
    expect(screen.getByText(/needs both given and family names/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Request registration' })).toBeNull()
    expect(requestRegistration).not.toHaveBeenCalled()
  })

  it('shows a full-page diagnostic when the saved session cannot be loaded', async () => {
    authMock.session = null
    authMock.diagnostic = {
      operation: 'Load sign-in status',
      endpoint: '/api/auth/session',
      status: 401,
      message: 'The saved sign-in session is invalid.',
      traceId: 'trace-session-123',
      occurredAt: '2026-10-09T10:00:00.000Z',
    }

    renderAt('/catalog')

    expect(await screen.findByRole('heading', { name: 'Sign-in needs attention' })).toBeTruthy()
    expect(screen.getByText('The saved sign-in session is invalid.')).toBeTruthy()
    expect(screen.getByText('HTTP 401')).toBeTruthy()
    expect(screen.getByText('trace-session-123')).toBeTruthy()
    expect(screen.queryByText('Catalog route content')).toBeNull()
  })
})
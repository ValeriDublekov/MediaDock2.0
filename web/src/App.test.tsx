import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { getCurrentSession, requestRegistration } from './api/client'
import type { AuthDiagnostic, CurrentSession } from './api/types'
import App from './App'

const authMock = vi.hoisted(() => ({ session: null as CurrentSession | null, diagnostic: null as AuthDiagnostic | null }))

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
      return <div>Authentication status</div>
    },
  }
})
vi.mock('./features/catalog/CatalogView', () => ({ CatalogView: () => <div>Catalog route content</div> }))
vi.mock('./features/oscar/OscarCatalogView', () => ({ OscarCatalogView: () => <div>Oscar route content</div> }))
vi.mock('./features/golden-globes/GoldenGlobeCatalogView', () => ({ GoldenGlobeCatalogView: () => <div>Golden Globes route content</div> }))
vi.mock('./features/favorites/FavoritesView', () => ({ FavoritesView: () => <div>Favorites route content</div> }))
vi.mock('./features/favorites/FavoriteContext', () => ({
  FavoriteProvider: ({ children }: { children: ReactNode }) => children,
}))
vi.mock('./features/sources/SourceSettingsView', () => ({
  SourceSettingsView: ({ onOpenHistory }: { onOpenHistory: (scanRunId: number) => void }) => (
    <button onClick={() => onOpenHistory(42)} type="button">Open scan 42</button>
  ),
}))
vi.mock('./features/sources/ConfigurationSettingsViews', () => ({
  IngestionSettingsView: () => <div>Ingestion route content</div>,
  OmdbProviderView: () => <div>OMDb route content</div>,
  PersonalRatingsView: () => <div>Ratings route content</div>,
  SystemSettingsView: () => <div>System route content</div>,
}))
vi.mock('./features/history/HistoryView', () => ({
  HistoryView: ({ scanRunId, onClearScanRun }: { scanRunId: number | null; onClearScanRun: () => void }) => (
    <div>History scan: {scanRunId ?? 'all'}<button onClick={onClearScanRun} type="button">Clear scan</button></div>
  ),
}))
vi.mock('./features/users/UsersView', () => ({ UsersView: () => <div>Users route content</div> }))

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

describe('App routes', () => {
  beforeEach(() => {
    authMock.session = anonymousSession
    authMock.diagnostic = null
    vi.mocked(getCurrentSession).mockReset()
    vi.mocked(requestRegistration).mockReset()
  })
  afterEach(() => cleanup())

  it.each([
    ['/catalog', 'Catalog'],
    ['/oscar', 'Oscar catalog'],
    ['/golden-globes', 'Golden Globes catalog'],
    ['/favorites', 'Favorites'],
    ['/configuration/torrent', 'Torrent settings'],
    ['/configuration/ingestion', 'Ingestion'],
    ['/configuration/personal-ratings', 'Personal IMDb ratings'],
    ['/configuration/omdb', 'OMDb provider'],
    ['/configuration/system', 'System'],
    ['/configuration/users', 'Users'],
    ['/history', 'Scan history'],
  ])('opens %s directly', async (path, heading) => {
    renderAt(path)

    expect(await screen.findByRole('heading', { name: heading })).toBeTruthy()
    expect(screen.getByRole('note').textContent).toContain('Application permissions are not enforced')
    expect(screen.getByTestId('location').textContent).toBe(path)
  })

  it('redirects the configuration root to torrent settings', async () => {
    renderAt('/configuration')

    expect(await screen.findByRole('heading', { name: 'Torrent settings' })).toBeTruthy()
    expect(screen.getByTestId('location').textContent).toBe('/configuration/torrent')
  })

  it('shows configuration subnavigation and opens its pages', () => {
    renderAt('/catalog')

    fireEvent.click(screen.getByRole('link', { name: /Configuration/ }))
    expect(screen.getByTestId('location').textContent).toBe('/configuration/torrent')
    const configurationLink = screen.getByRole('link', { name: /Configuration/ })
    const configurationNavigation = screen.getByRole('navigation', { name: 'Configuration navigation' })
    const historyLink = screen.getByRole('link', { name: /Scan history/ })
    expect(configurationLink.compareDocumentPosition(configurationNavigation) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(configurationNavigation.compareDocumentPosition(historyLink) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(configurationNavigation.querySelector('a')?.textContent).toBe('System')

    fireEvent.click(screen.getByRole('link', { name: 'OMDb provider' }))
    expect(screen.getByTestId('location').textContent).toBe('/configuration/omdb')
    expect(screen.getByRole('heading', { name: 'OMDb provider' })).toBeTruthy()
  })

  it('updates the URL through navigation and preserves a selected scan in history', () => {
    renderAt('/catalog')

    fireEvent.click(screen.getByRole('link', { name: /Golden Globes catalog/ }))
    expect(screen.getByTestId('location').textContent).toBe('/golden-globes')

    fireEvent.click(screen.getByRole('link', { name: /Configuration/ }))
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
      user: { email: 'person@example.com', givenName: 'Google', familyName: 'Profile', status: 'pending' },
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
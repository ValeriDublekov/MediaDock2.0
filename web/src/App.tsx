import { useCallback, useState, type ReactNode } from 'react'
import { Navigate, NavLink, Route, Routes, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { getCurrentSession, requestRegistration } from './api/client'
import type { AuthDiagnostic, CurrentSession, RecommendationMediaType } from './api/types'
import { CatalogView } from './features/catalog/CatalogView'
import { HistoryView } from './features/history/HistoryView'
import { OscarCatalogView } from './features/oscar/OscarCatalogView'
import { GoldenGlobeCatalogView } from './features/golden-globes/GoldenGlobeCatalogView'
import { FavoriteProvider } from './features/favorites/FavoriteContext'
import { FavoritesView } from './features/favorites/FavoritesView'
import { IgnoredMoviesProvider } from './features/ignored-movies/IgnoredMoviesContext'
import { IgnoredMoviesView } from './features/ignored-movies/IgnoredMoviesView'
import { SourceSettingsView } from './features/sources/SourceSettingsView'
import { IngestionSettingsView, OmdbProviderView, PersonalRatingsView, SystemSettingsView } from './features/sources/ConfigurationSettingsViews'
import { AuthStatus } from './features/auth/AuthStatus'
import { UsersView } from './features/users/UsersView'
import { RecommendationSettingsView } from './features/recommendations/RecommendationSettingsView'
import { RecommendationsView } from './features/recommendations/RecommendationsView'
import { MovieTablesDashboardView } from './features/dashboard/MovieTablesDashboardView'

type Section = 'catalog' | 'oscar' | 'golden-globes' | 'favorites' | 'account' | 'administration'

const sections: { id: Section; path: string; number: string; label: string }[] = [
  { id: 'catalog', path: '/catalog', number: '01', label: 'Torents catalog' },
  { id: 'oscar', path: '/oscar', number: '02', label: 'Oscar catalog' },
  { id: 'golden-globes', path: '/golden-globes', number: '03', label: 'Golden Globes catalog' },
  { id: 'favorites', path: '/favorites', number: '04', label: 'Favorites' },
]

const administrationPages = [
  { path: '/configuration/system', label: 'System', description: 'Review deployment controls and the running version.' },
  { path: '/configuration/dashboard', label: 'Dashboard', description: 'Review the state and counts of system movie tables.' },
  { path: '/configuration/torrent', label: 'Torrent settings', description: 'Manage RSS profiles and matching rules.' },
  { path: '/configuration/ingestion', label: 'Ingestion', description: 'Import and enrich award datasets.' },
  { path: '/configuration/recommendations', label: 'Recommendation settings', description: 'Configure vote normalization and award points.' },
  { path: '/configuration/omdb', label: 'OMDb provider', description: 'Configure provider credentials and shared request limits.' },
  { path: '/configuration/users', label: 'Users', description: 'Review registration requests and manage account status.' },
  { path: '/history', label: 'Scan history', description: 'Review completed scans and individual parser decisions.' },
]

const recommendationPages: { path: string; label: string; mediaType: RecommendationMediaType }[] = [
  { path: '/account/recommendations/movies', label: 'Movie recommendations', mediaType: 'movie' },
  { path: '/account/recommendations/series', label: 'Series recommendations', mediaType: 'series' },
]

const sectionContent: Record<Section, { eyebrow: string; title: string; description: string }> = {
  catalog: {
    eyebrow: 'LIBRARY',
    title: 'Torents catalog',
    description: 'Browse titles collected from your configured feeds.',
  },
  oscar: {
    eyebrow: 'ACADEMY AWARDS',
    title: 'Oscar catalog',
    description: 'Browse nominated films, award outcomes, and available metadata.',
  },
  'golden-globes': {
    eyebrow: 'GOLDEN GLOBES',
    title: 'Golden Globes catalog',
    description: 'Browse Golden Globes nominees, winners, and award categories.',
  },
  favorites: {
    eyebrow: 'MY MOVIES',
    title: 'Favorites',
    description: 'Movies saved from recommendations, the catalog, and Oscar awards.',
  },
  account: {
    eyebrow: 'PERSONAL',
    title: 'Personal',
    description: 'Manage personal data for the signed-in account.',
  },
  administration: {
    eyebrow: 'ADMINISTRATION',
    title: 'Administration',
    description: 'Manage shared configuration, accounts, and operational history.',
  },
}

function RouteAccessRequired({ title, message }: { title: string; message: string }) {
  return (
    <section aria-labelledby="route-access-required-heading" className="management-section">
      <h2 id="route-access-required-heading">{title}</h2>
      <p className="section-caption">{message}</p>
    </section>
  )
}

function AdminArea({ isAdmin, children }: { isAdmin: boolean; children: ReactNode }) {
  return isAdmin
    ? children
    : <RouteAccessRequired message="This page requires an active administrator account." title="Administrator access required" />
}

function ActiveAccountArea({ isActiveAccount, children, title, message }: {
  isActiveAccount: boolean
  children: ReactNode
  title: string
  message: string
}) {
  return isActiveAccount
    ? children
    : <RouteAccessRequired message={message} title={title} />
}

function PersonalRecommendationsRoute({
  accountKey,
  isActiveAccount,
  mediaType,
}: {
  accountKey: string | null
  isActiveAccount: boolean
  mediaType: RecommendationMediaType
}) {
  return <ActiveAccountArea
    isActiveAccount={isActiveAccount}
    message="Sign in with an active linked account to view personal recommendations."
    title="Recommendations unavailable"
  >
    <RecommendationsView key={`${accountKey ?? 'no-active-account'}-${mediaType}`} mediaType={mediaType} />
  </ActiveAccountArea>
}

function RegistrationGate({
  session,
  onSessionChange,
}: {
  session: CurrentSession
  onSessionChange: (session: CurrentSession | null) => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const identity = session.identity
  const firstName = identity?.givenName?.trim()
  const profileComplete = Boolean(identity?.profileComplete && firstName && identity.familyName?.trim())

  async function submitRequest() {
    setBusy(true)
    setError(null)
    try {
      await requestRegistration()
      onSessionChange(await getCurrentSession())
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'The registration request failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-labelledby="registration-heading" className="registration-gate">
      <p className="registration-eyebrow">ACCOUNT ACCESS</p>
      <h1 id="registration-heading">Hello{firstName ? ` ${firstName}` : ''}</h1>
      {profileComplete ? (
        <>
          <p>Your Google account is not registered yet. Send a request to create your MediaDock account.</p>
          <button className="button" disabled={busy} onClick={() => void submitRequest()} type="button">
            {busy ? 'Submitting request...' : 'Request registration'}
          </button>
        </>
      ) : (
        <p className="registration-note">Your Google profile needs both given and family names before you can request registration.</p>
      )}
      {error && <p className="registration-error" role="alert">{error}</p>}
    </section>
  )
}

function AuthDiagnosticDetails({ diagnostic }: { diagnostic: AuthDiagnostic }) {
  return (
    <dl className="auth-diagnostic-details">
      <div><dt>Operation:</dt><dd>{diagnostic.operation}</dd></div>
      <div><dt>Endpoint:</dt><dd><code>{diagnostic.endpoint}</code></dd></div>
      <div><dt>Result:</dt><dd>{diagnostic.status === 0 ? 'Network error' : diagnostic.status ? `HTTP ${diagnostic.status}` : 'Unknown'}</dd></div>
      <div><dt>Time:</dt><dd>{diagnostic.occurredAt}</dd></div>
      {diagnostic.traceId && <div><dt>Server trace ID:</dt><dd><code>{diagnostic.traceId}</code></dd></div>}
    </dl>
  )
}

function AuthRecoveryScreen({ diagnostic }: { diagnostic: AuthDiagnostic }) {
  return (
    <section aria-labelledby="auth-recovery-heading" className="auth-recovery">
      <p className="registration-eyebrow">SIGN-IN DIAGNOSTICS</p>
      <h1 id="auth-recovery-heading">Sign-in needs attention</h1>
      <p className="auth-recovery-message">{diagnostic.message}</p>
      <AuthDiagnosticDetails diagnostic={diagnostic} />
      <p className="auth-recovery-help">Use <strong>Reset sign-in</strong> or <strong>Retry</strong> in the upper-right corner. If this continues, share the details above with the MediaDock operator.</p>
    </section>
  )
}

function AppContent() {
  const [authChecked, setAuthChecked] = useState(false)
  const [session, setSession] = useState<CurrentSession | null>(null)
  const [authDiagnostic, setAuthDiagnostic] = useState<AuthDiagnostic | null>(null)
  const onSessionChange = useCallback((currentSession: CurrentSession | null, diagnostic: AuthDiagnostic | null = null) => {
    setSession(currentSession)
    setAuthDiagnostic(diagnostic)
    setAuthChecked(true)
  }, [])
  const location = useLocation()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const isActiveAccount = session?.accountState === 'linked' && session.user?.status === 'active'
  const accountKey = isActiveAccount && session?.identity
    ? `${session.identity.issuer}:${session.identity.subject}`
    : null
  const isAdmin = isActiveAccount && session?.user?.role === 'admin'
  const isAdministrationPath = location.pathname.startsWith('/configuration') || location.pathname === '/history'
  const section: Section = isAdministrationPath
    ? 'administration'
    : location.pathname.startsWith('/account')
      ? 'account'
      : sections.find((item) => item.path === location.pathname)?.id ?? 'catalog'
  const administrationPage = isAdmin ? administrationPages.find((page) => page.path === location.pathname) : undefined
  const recommendationPage = recommendationPages.find((page) => page.path === location.pathname)
  const requestedScanRunId = Number(searchParams.get('scanRunId'))
  const historyScanRunId = Number.isSafeInteger(requestedScanRunId) && requestedScanRunId > 0
    ? requestedScanRunId
    : null
  const content = sectionContent[section]
  const pageContent = location.pathname === '/account/ignored-movies'
    ? { eyebrow: 'PERSONAL', title: 'Ignored titles', description: '' }
    : recommendationPage
      ? { eyebrow: 'PERSONAL', title: recommendationPage.label, description: '' }
      : content
  const title = administrationPage?.label ?? pageContent.title
  const description = administrationPage?.description ?? pageContent.description
  const registrationSession = session?.authenticated && session.accountState === 'unmatched' ? session : null
  const recoveryDiagnostic = session === null ? authDiagnostic : null
  const isAccessGated = !authChecked || registrationSession !== null || recoveryDiagnostic !== null

  return (
    <IgnoredMoviesProvider accountKey={accountKey} key={accountKey ?? 'no-active-owner'}>
    <FavoriteProvider accountKey={accountKey} key={accountKey ?? 'no-active-owner'}>
    <div className={`app-shell${isAccessGated ? ' is-gated' : ''}`}>
      {!isAccessGated && <aside className="sidebar">
        <NavLink className="brand" to="/catalog">
          <span className="brand-mark" aria-hidden="true">MD</span>
          <span className="brand-name">MediaDock</span>
        </NavLink>

        <div className="sidebar-label sidebar-label-first">LIBRARY</div>
        <nav className="primary-nav" aria-label="Library navigation">
          {sections.filter((item) => item.id !== 'favorites').map((item) => (
            <NavLink
              aria-current={section === item.id ? 'page' : undefined}
              className={({ isActive }) => `nav-item${isActive ? ' is-active' : ''}`}
              end
              key={item.id}
              to={item.path}
            >
              <span className="nav-number">{item.number}</span>
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>

        {isActiveAccount && (
          <div className="account-nav-group">
            <div className="sidebar-label">PERSONAL</div>
            <nav aria-label="Personal navigation" className="configuration-nav">
              <NavLink
                className={({ isActive }) => `configuration-nav-item${isActive ? ' is-active' : ''}`}
                end
                to="/favorites"
              >
                Favorites
              </NavLink>
              <NavLink
                className={({ isActive }) => `configuration-nav-item${isActive ? ' is-active' : ''}`}
                end
                to="/account/ignored-movies"
              >
                Ignored titles
              </NavLink>
              {recommendationPages.map((page) => <NavLink
                className={({ isActive }) => `configuration-nav-item${isActive ? ' is-active' : ''}`}
                end
                key={page.path}
                to={page.path}
              >
                {page.label}
              </NavLink>)}
              <NavLink
                className={({ isActive }) => `configuration-nav-item${isActive ? ' is-active' : ''}`}
                end
                to="/account/personal-ratings"
              >
                Personal IMDb ratings
              </NavLink>
            </nav>
          </div>
        )}

        {isAdmin && (
          <div className="configuration-nav-group">
            <div className="sidebar-label">ADMINISTRATION</div>
            <nav aria-label="Administration section navigation" className="admin-root-nav">
              <NavLink
                aria-current={section === 'administration' ? 'page' : undefined}
                className={`nav-item${section === 'administration' ? ' is-active' : ''}`}
                end
                to="/configuration/system"
              >
                <span className="nav-number">05</span>
                <span>Administration</span>
              </NavLink>
            </nav>
            {section === 'administration' && (
              <nav aria-label="Administration navigation" className="configuration-nav">
                {administrationPages.map((page) => (
                  <NavLink
                    className={({ isActive }) => `configuration-nav-item${isActive ? ' is-active' : ''}`}
                    end
                    key={page.path}
                    to={page.path}
                  >
                    {page.label}
                  </NavLink>
                ))}
              </nav>
            )}
          </div>
        )}

        <div className="sidebar-footer">
          <span className="status-mark" aria-hidden="true" />
          <span>Local instance</span>
          <span className="status-caption">API-backed</span>
        </div>
      </aside>}

      <main className="main-shell">
        <header className="topbar">
          <div className="breadcrumb">MEDIADOCK <span>/</span> {recoveryDiagnostic ? 'SIGN-IN DIAGNOSTICS' : registrationSession ? 'ACCOUNT ACCESS' : !authChecked ? 'SIGN-IN CHECK' : <>{content.eyebrow}{administrationPage && <> <span>/</span> {administrationPage.label.toUpperCase()}</>}</>}</div>
          <AuthStatus onSessionChange={onSessionChange} />
        </header>

        {(!authChecked || session?.authorizationEnforced !== true) && (
          <aside className="open-mode-warning" role="note">
            Access control is disabled: shared, system, and administration APIs are not authorization-protected. Keep the API within its configured network boundary. Favorites, ignored movies, and personal ratings still require an active linked account and remain owner-scoped.
          </aside>
        )}

        {!authChecked ? (
          <div className="session-check" role="status">Checking sign-in status</div>
        ) : recoveryDiagnostic ? (
          <AuthRecoveryScreen diagnostic={recoveryDiagnostic} />
        ) : registrationSession ? (
          <RegistrationGate onSessionChange={onSessionChange} session={registrationSession} />
        ) : <div className="page-content">
          {authDiagnostic && (
            <section aria-label="Sign-in diagnostic" className="auth-diagnostic-notice" role="alert">
              <strong>{authDiagnostic.message}</strong>
              <AuthDiagnosticDetails diagnostic={authDiagnostic} />
            </section>
          )}
          <div className="page-heading">
            <div>
              <p className="eyebrow">{content.eyebrow}</p>
              <h1>{title}</h1>
              {description && <p className="page-description">{description}</p>}
            </div>
          </div>

          <Routes>
            <Route path="/" element={<Navigate replace to="/catalog" />} />
            <Route path="/catalog" element={<CatalogView canDeleteTitles={isAdmin} canViewScanHistory={isAdmin} />} />
            <Route path="/oscar" element={<OscarCatalogView />} />
            <Route path="/golden-globes" element={<GoldenGlobeCatalogView />} />
            <Route path="/favorites" element={isActiveAccount
              ? <FavoritesView />
              : <RouteAccessRequired message="Sign in with an active linked account to view favorites." title="Favorites unavailable" />} />
            <Route path="/account/ignored-movies" element={(
              <ActiveAccountArea
                isActiveAccount={isActiveAccount}
                message="Sign in with an active linked account to manage ignored titles."
                title="Ignore list unavailable"
              >
                <IgnoredMoviesView />
              </ActiveAccountArea>
            )} />
            <Route path="/configuration/personal-ratings" element={<Navigate replace to="/account/personal-ratings" />} />
            <Route path="/account" element={<Navigate replace to="/account/personal-ratings" />} />
            <Route path="/account/recommendations" element={<Navigate replace to="/account/recommendations/movies" />} />
            {recommendationPages.map((page) => <Route
              element={<PersonalRecommendationsRoute
                accountKey={accountKey}
                isActiveAccount={Boolean(isActiveAccount)}
                mediaType={page.mediaType}
              />}
              key={page.path}
              path={page.path}
            />)}
            <Route path="/account/personal-ratings" element={(
              <ActiveAccountArea
                isActiveAccount={isActiveAccount}
                message="Sign in with an active linked account to manage personal IMDb ratings."
                title="Personal ratings unavailable"
              >
                <PersonalRatingsView key={accountKey ?? 'no-active-account'} />
              </ActiveAccountArea>
            )} />
            <Route path="/configuration" element={<AdminArea isAdmin={isAdmin}><Navigate replace to="/configuration/system" /></AdminArea>} />
            <Route path="/configuration/dashboard" element={<AdminArea isAdmin={isAdmin}><MovieTablesDashboardView /></AdminArea>} />
            <Route path="/configuration/torrent" element={(
              <AdminArea isAdmin={isAdmin}>
                <SourceSettingsView onOpenHistory={(scanRunId) => navigate(scanRunId ? `/history?scanRunId=${scanRunId}` : '/history')} />
              </AdminArea>
            )} />
            <Route path="/configuration/ingestion" element={<AdminArea isAdmin={isAdmin}><IngestionSettingsView /></AdminArea>} />
            <Route path="/configuration/recommendations" element={<AdminArea isAdmin={isAdmin}><RecommendationSettingsView /></AdminArea>} />
            <Route path="/configuration/omdb" element={<AdminArea isAdmin={isAdmin}><OmdbProviderView /></AdminArea>} />
            <Route path="/configuration/system" element={<AdminArea isAdmin={isAdmin}><SystemSettingsView /></AdminArea>} />
            <Route path="/configuration/users" element={<AdminArea isAdmin={isAdmin}><UsersView /></AdminArea>} />
            <Route path="/history" element={(
              <AdminArea isAdmin={isAdmin}>
                <HistoryView scanRunId={historyScanRunId} onClearScanRun={() => setSearchParams({})} />
              </AdminArea>
            )} />
            <Route path="*" element={<Navigate replace to="/catalog" />} />
          </Routes>
        </div>}
      </main>
    </div>
    </FavoriteProvider>
    </IgnoredMoviesProvider>
  )
}

function App() {
  return <AppContent />
}

export default App

import { Navigate, NavLink, Route, Routes, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { CatalogView } from './features/catalog/CatalogView'
import { HistoryView } from './features/history/HistoryView'
import { OscarCatalogView } from './features/oscar/OscarCatalogView'
import { GoldenGlobeCatalogView } from './features/golden-globes/GoldenGlobeCatalogView'
import { FavoriteProvider } from './features/favorites/FavoriteContext'
import { FavoritesView } from './features/favorites/FavoritesView'
import { SourceSettingsView } from './features/sources/SourceSettingsView'
import { IngestionSettingsView, OmdbProviderView, PersonalRatingsView, SystemSettingsView } from './features/sources/ConfigurationSettingsViews'
import { AuthStatus } from './features/auth/AuthStatus'

type Section = 'catalog' | 'oscar' | 'golden-globes' | 'favorites' | 'sources' | 'history'

const sections: { id: Section; path: string; number: string; label: string }[] = [
  { id: 'catalog', path: '/catalog', number: '01', label: 'Catalog' },
  { id: 'oscar', path: '/oscar', number: '02', label: 'Oscar catalog' },
  { id: 'golden-globes', path: '/golden-globes', number: '03', label: 'Golden Globes catalog' },
  { id: 'favorites', path: '/favorites', number: '04', label: 'Favorites' },
  { id: 'sources', path: '/configuration/torrent', number: '05', label: 'Configuration' },
  { id: 'history', path: '/history', number: '06', label: 'Scan history' },
]

const configurationPages = [
  { path: '/configuration/system', label: 'System', description: 'Review deployment controls and the running version.' },
  { path: '/configuration/torrent', label: 'Torrent settings', description: 'Manage RSS profiles and matching rules.' },
  { path: '/configuration/ingestion', label: 'Ingestion', description: 'Import and enrich award datasets.' },
  { path: '/configuration/personal-ratings', label: 'Personal IMDb ratings', description: 'Import and maintain your IMDb ratings.' },
  { path: '/configuration/omdb', label: 'OMDb provider', description: 'Configure provider credentials and shared request limits.' },
]

const sectionContent: Record<Section, { eyebrow: string; title: string; description: string }> = {
  catalog: {
    eyebrow: 'LIBRARY',
    title: 'Catalog',
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
    description: 'Movies saved from the catalog and Oscar awards.',
  },
  sources: {
    eyebrow: 'CONFIGURATION',
    title: 'Configuration',
    description: 'Configure feeds, metadata, and system operations.',
  },
  history: {
    eyebrow: 'OPERATIONS',
    title: 'Scan history',
    description: 'Review completed scans and individual parser decisions.',
  },
}

function AppContent() {
  const location = useLocation()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const section = sections.find((item) => item.id === 'sources'
    ? location.pathname.startsWith('/configuration')
    : item.path === location.pathname)?.id ?? 'catalog'
  const configurationPage = configurationPages.find((page) => page.path === location.pathname)
  const requestedScanRunId = Number(searchParams.get('scanRunId'))
  const historyScanRunId = Number.isSafeInteger(requestedScanRunId) && requestedScanRunId > 0
    ? requestedScanRunId
    : null
  const content = sectionContent[section]
  const title = configurationPage?.label ?? content.title
  const description = configurationPage?.description ?? content.description

  return (
    <div className="app-shell">
      <aside className={`sidebar${section === 'sources' ? ' is-configuration' : ''}`}>
        <NavLink className="brand" to="/catalog">
          <span className="brand-mark" aria-hidden="true">MD</span>
          <span className="brand-name">MediaDock</span>
        </NavLink>

        <div className="sidebar-label">LIBRARY</div>
        <nav className="primary-nav" aria-label="Main navigation">
          {sections.map((item) => item.id === 'sources' ? (
            <div className="configuration-nav-group" key={item.id}>
              <NavLink
                aria-current={section === item.id ? 'page' : undefined}
                className={({ isActive }) => `nav-item${isActive ? ' is-active' : ''}`}
                end
                to={item.path}
              >
                <span className="nav-number">{item.number}</span>
                <span>{item.label}</span>
              </NavLink>
              {section === 'sources' && (
                <nav aria-label="Configuration navigation" className="configuration-nav">
                  {configurationPages.map((page) => (
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
          ) : (
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

        <div className="sidebar-footer">
          <span className="status-mark" aria-hidden="true" />
          <span>Local instance</span>
          <span className="status-caption">API-backed</span>
        </div>
      </aside>

      <main className="main-shell">
        <header className="topbar">
          <div className="breadcrumb">MEDIADOCK <span>/</span> {content.eyebrow}{configurationPage && <> <span>/</span> {configurationPage.label.toUpperCase()}</>}</div>
          <AuthStatus />
        </header>

        <aside className="open-mode-warning" role="note">
          Sign-in is optional and verifies identity only. Application permissions are not enforced, and data remains accessible to clients within the configured network boundary.
        </aside>

        <div className="page-content">
          <div className="page-heading">
            <div>
              <p className="eyebrow">{content.eyebrow}</p>
              <h1>{title}</h1>
              <p className="page-description">{description}</p>
            </div>
          </div>

          <Routes>
            <Route path="/" element={<Navigate replace to="/catalog" />} />
            <Route path="/catalog" element={<CatalogView />} />
            <Route path="/oscar" element={<OscarCatalogView />} />
            <Route path="/golden-globes" element={<GoldenGlobeCatalogView />} />
            <Route path="/favorites" element={<FavoritesView />} />
            <Route path="/configuration" element={<Navigate replace to="/configuration/torrent" />} />
            <Route path="/configuration/torrent" element={<SourceSettingsView onOpenHistory={(scanRunId) => navigate(scanRunId ? `/history?scanRunId=${scanRunId}` : '/history')} />} />
            <Route path="/configuration/ingestion" element={<IngestionSettingsView />} />
            <Route path="/configuration/personal-ratings" element={<PersonalRatingsView />} />
            <Route path="/configuration/omdb" element={<OmdbProviderView />} />
            <Route path="/configuration/system" element={<SystemSettingsView />} />
            <Route path="/history" element={<HistoryView scanRunId={historyScanRunId} onClearScanRun={() => setSearchParams({})} />} />
            <Route path="*" element={<Navigate replace to="/catalog" />} />
          </Routes>
        </div>
      </main>
    </div>
  )
}

function App() {
  return <FavoriteProvider><AppContent /></FavoriteProvider>
}

export default App

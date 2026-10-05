import { Navigate, NavLink, Route, Routes, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { CatalogView } from './features/catalog/CatalogView'
import { HistoryView } from './features/history/HistoryView'
import { OscarCatalogView } from './features/oscar/OscarCatalogView'
import { GoldenGlobeCatalogView } from './features/golden-globes/GoldenGlobeCatalogView'
import { FavoriteProvider } from './features/favorites/FavoriteContext'
import { FavoritesView } from './features/favorites/FavoritesView'
import { SourceSettingsView } from './features/sources/SourceSettingsView'

type Section = 'catalog' | 'oscar' | 'golden-globes' | 'favorites' | 'sources' | 'history'

const sections: { id: Section; path: string; number: string; label: string }[] = [
  { id: 'catalog', path: '/catalog', number: '01', label: 'Catalog' },
  { id: 'oscar', path: '/oscar', number: '02', label: 'Oscar catalog' },
  { id: 'golden-globes', path: '/golden-globes', number: '03', label: 'Golden Globes catalog' },
  { id: 'favorites', path: '/favorites', number: '04', label: 'Favorites' },
  { id: 'sources', path: '/configuration', number: '05', label: 'Configuration' },
  { id: 'history', path: '/history', number: '06', label: 'Scan history' },
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
    description: 'Manage feed sources, matching rules, and OMDb provider settings.',
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
  const section = sections.find((item) => item.path === location.pathname)?.id ?? 'catalog'
  const requestedScanRunId = Number(searchParams.get('scanRunId'))
  const historyScanRunId = Number.isSafeInteger(requestedScanRunId) && requestedScanRunId > 0
    ? requestedScanRunId
    : null
  const content = sectionContent[section]

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <NavLink className="brand" to="/catalog">
          <span className="brand-mark" aria-hidden="true">MD</span>
          <span className="brand-name">MediaDock</span>
        </NavLink>

        <div className="sidebar-label">LIBRARY</div>
        <nav className="primary-nav" aria-label="Main navigation">
          {sections.map((item) => (
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
          <div className="breadcrumb">MEDIADOCK <span>/</span> {content.eyebrow}</div>
        </header>

        <div className="page-content">
          <div className="page-heading">
            <div>
              <p className="eyebrow">{content.eyebrow}</p>
              <h1>{content.title}</h1>
              <p className="page-description">{content.description}</p>
            </div>
          </div>

          <Routes>
            <Route path="/" element={<Navigate replace to="/catalog" />} />
            <Route path="/catalog" element={<CatalogView />} />
            <Route path="/oscar" element={<OscarCatalogView />} />
            <Route path="/golden-globes" element={<GoldenGlobeCatalogView />} />
            <Route path="/favorites" element={<FavoritesView />} />
            <Route path="/configuration" element={<SourceSettingsView onOpenHistory={(scanRunId) => navigate(`/history?scanRunId=${scanRunId}`)} />} />
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

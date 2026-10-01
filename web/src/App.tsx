import { useState } from 'react'
import { CatalogView } from './features/catalog/CatalogView'
import { HistoryView } from './features/history/HistoryView'
import { OscarCatalogView } from './features/oscar/OscarCatalogView'
import { FavoriteProvider } from './features/favorites/FavoriteContext'
import { FavoritesView } from './features/favorites/FavoritesView'
import { SourceSettingsView } from './features/sources/SourceSettingsView'

type Section = 'catalog' | 'oscar' | 'favorites' | 'sources' | 'history'

const sections: { id: Section; number: string; label: string }[] = [
  { id: 'catalog', number: '01', label: 'Catalog' },
  { id: 'oscar', number: '02', label: 'Oscar catalog' },
  { id: 'favorites', number: '03', label: 'Favorites' },
  { id: 'sources', number: '04', label: 'Configuration' },
  { id: 'history', number: '05', label: 'Scan history' },
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
  const [section, setSection] = useState<Section>('catalog')
  const [historyScanRunId, setHistoryScanRunId] = useState<number | null>(null)
  const content = sectionContent[section]

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="#catalog" onClick={() => setSection('catalog')}>
          <span className="brand-mark" aria-hidden="true">MD</span>
          <span className="brand-name">MediaDock</span>
        </a>

        <div className="sidebar-label">LIBRARY</div>
        <nav className="primary-nav" aria-label="Main navigation">
          {sections.map((item) => (
            <button
              aria-current={section === item.id ? 'page' : undefined}
              className={`nav-item${section === item.id ? ' is-active' : ''}`}
              key={item.id}
              onClick={() => { setHistoryScanRunId(null); setSection(item.id) }}
              type="button"
            >
              <span className="nav-number">{item.number}</span>
              <span>{item.label}</span>
            </button>
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

          {section === 'catalog' && <CatalogView />}
          {section === 'oscar' && <OscarCatalogView />}
          {section === 'favorites' && <FavoritesView />}
          {section === 'sources' && <SourceSettingsView onOpenHistory={(scanRunId) => { setHistoryScanRunId(scanRunId); setSection('history') }} />}
          {section === 'history' && <HistoryView scanRunId={historyScanRunId} onClearScanRun={() => setHistoryScanRunId(null)} />}
        </div>
      </main>
    </div>
  )
}

function App() {
  return <FavoriteProvider><AppContent /></FavoriteProvider>
}

export default App

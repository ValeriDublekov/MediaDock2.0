import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'
import App from './App'

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

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}{location.search}</output>
}

function renderAt(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><App /><LocationProbe /></MemoryRouter>)
}

describe('App routes', () => {
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
    ['/history', 'Scan history'],
  ])('opens %s directly', (path, heading) => {
    renderAt(path)

    expect(screen.getByRole('heading', { name: heading })).toBeTruthy()
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
})
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { addFavorite, getFavorites, updateFavorite } from '../../api/client'
import type { FavoriteTitle, PageResponse } from '../../api/types'
import { FavoriteControls } from './FavoriteControls'
import { FavoriteProvider } from './FavoriteContext'
import { FavoritesView } from './FavoritesView'

vi.mock('../../api/client', () => ({ getFavorites: vi.fn(), addFavorite: vi.fn(), updateFavorite: vi.fn(), removeFavorite: vi.fn() }))
afterEach(() => {
  cleanup()
  vi.mocked(getFavorites).mockReset()
  vi.mocked(addFavorite).mockReset()
  vi.mocked(updateFavorite).mockReset()
})

it('does not report zero movies when the API fails', async () => {
  vi.mocked(getFavorites).mockRejectedValue(new Error('Service unavailable'))
  render(<FavoriteProvider accountKey="test-account"><FavoritesView /></FavoriteProvider>)

  expect(await screen.findByText('Could not load this view')).toBeTruthy()
  expect(screen.queryByText('0 titles')).toBeNull()
})

it('filters one canonical movie independently by watch and download status', async () => {
  const movie: FavoriteTitle = {
    imdbId: 'tt12345678', torrentMovieId: 42, title: 'Shared Movie', year: 2025, mediaType: 'movie', metadataStatus: 'enriched',
    imdbRating: null, posterUrl: null, toWatch: true, toDownload: true,
    addedFromOscar: true, addedFromCatalog: true, addedFromRecommendations: false, createdAt: '2026-09-30T00:00:00Z',
    updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 1, nominationCount: 2,
    winCount: 1, occurrenceCount: 1, lastSeenAt: '2026-09-30T00:00:00Z',
  }
  const response = (pageSize: number): PageResponse<FavoriteTitle> => ({
    items: [movie], page: 1, pageSize, totalCount: 1, totalPages: 1,
  })
  vi.mocked(getFavorites).mockImplementation(async (query) => response(query.pageSize))
  render(<FavoriteProvider accountKey="test-account"><FavoritesView /></FavoriteProvider>)

  expect(await screen.findByRole('button', { name: 'Shared Movie' })).toBeTruthy()
  expect(screen.getAllByText('Oscar / Catalog')).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Table' }))
  expect(screen.getByRole('columnheader', { name: 'ORIGIN' })).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'Posters' }))
  fireEvent.click(screen.getByRole('button', { name: 'To watch' }))
  await waitFor(() => expect(getFavorites).toHaveBeenCalledWith({ status: 'to_watch', page: 1, pageSize: 20 }))
  fireEvent.click(screen.getByRole('button', { name: 'To download' }))
  await waitFor(() => expect(getFavorites).toHaveBeenCalledWith({ status: 'to_download', page: 1, pageSize: 20 }))
  expect(screen.getAllByRole('button', { name: 'Shared Movie' })).toHaveLength(1)
})

it('does not load favorites without an active account', async () => {
  render(<FavoriteProvider accountKey={null}><FavoritesView /></FavoriteProvider>)

  expect(await screen.findByText('Sign in to view favorites')).toBeTruthy()
  expect(getFavorites).not.toHaveBeenCalled()
})

it('reloads and clears favorites when the account changes', async () => {
  const firstMovie: FavoriteTitle = {
    imdbId: 'tt12345678', torrentMovieId: 1, title: 'First Account Film', year: 2025, mediaType: 'movie', metadataStatus: 'enriched',
    imdbRating: null, posterUrl: null, toWatch: true, toDownload: false,
    addedFromOscar: false, addedFromCatalog: true, addedFromRecommendations: false, createdAt: '2026-09-30T00:00:00Z',
    updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 0, nominationCount: 0,
    winCount: 0, occurrenceCount: 1, lastSeenAt: '2026-09-30T00:00:00Z',
  }
  const secondMovie: FavoriteTitle = { ...firstMovie, imdbId: 'tt87654321', torrentMovieId: 2, title: 'Second Account Film' }
  let currentMovie = firstMovie
  vi.mocked(getFavorites).mockImplementation(async (query) => ({
    items: [currentMovie], page: query.page, pageSize: query.pageSize, totalCount: 1, totalPages: 1,
  }))
  const renderForAccount = (accountKey: string) => (
    <FavoriteProvider accountKey={accountKey} key={accountKey}><FavoritesView /></FavoriteProvider>
  )
  const { rerender } = render(renderForAccount('first-account'))

  expect(await screen.findByRole('button', { name: 'First Account Film' })).toBeTruthy()
  currentMovie = secondMovie
  rerender(renderForAccount('second-account'))

  expect(await screen.findByRole('button', { name: 'Second Account Film' })).toBeTruthy()
  expect(screen.queryByRole('button', { name: 'First Account Film' })).toBeNull()
})

it('shares state by IMDb ID, blocks ineligible adds, and allows download before a torrent exists', async () => {
  const favorite: FavoriteTitle = {
    imdbId: 'tt12345678', torrentMovieId: null, title: 'Shared Film', year: 2025, mediaType: 'movie', metadataStatus: 'enriched',
    imdbRating: 8.2, posterUrl: null, toWatch: true, toDownload: false,
    addedFromOscar: true, addedFromCatalog: false, addedFromRecommendations: false, createdAt: '2026-09-30T00:00:00Z',
    updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 1, nominationCount: 0,
    winCount: 0, occurrenceCount: 0, lastSeenAt: null,
  }
  vi.mocked(getFavorites).mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 })
  vi.mocked(addFavorite).mockResolvedValue(favorite)
  vi.mocked(updateFavorite).mockResolvedValue({ ...favorite, toDownload: true })
  render(<FavoriteProvider accountKey="test-account">
    <>
      <FavoriteControls from="catalog" imdbId={null} metadataStatus={null} mediaType="movie" occurrenceCount={0} />
      <FavoriteControls from="catalog" imdbId="tt12345678" metadataStatus="pending" mediaType="movie" occurrenceCount={0} />
      <FavoriteControls from="oscar" imdbId="tt12345678" metadataStatus="enriched" mediaType="movie" occurrenceCount={0} />
      <FavoriteControls from="catalog" imdbId="tt12345678" metadataStatus="enriched" mediaType="movie" occurrenceCount={0} />
    </>
  </FavoriteProvider>)

  const addButtons = await screen.findAllByRole('button', { name: 'Add to favorites' })
  expect(addButtons).toHaveLength(2)
  const firstAddButton = addButtons[0] as HTMLButtonElement
  await waitFor(() => expect(firstAddButton.disabled).toBe(false))
  fireEvent.click(firstAddButton)
  await waitFor(() => expect(addFavorite).toHaveBeenCalledWith('tt12345678', 'oscar'))
  expect(await screen.findByRole('button', { name: 'Add from catalog' })).toBeTruthy()

  const download = screen.getAllByRole('checkbox', { name: 'To download' })[0] as HTMLInputElement
  expect(download.disabled).toBe(false)
  fireEvent.click(download)
  await waitFor(() => expect(updateFavorite).toHaveBeenCalledWith('tt12345678', { toDownload: true }))
})

it('shows series recommendations in the shared favorites list with watch-only controls', async () => {
  const series: FavoriteTitle = {
    imdbId: 'tt12345678', torrentMovieId: null, title: 'Shared Series', year: 2025, mediaType: 'series', metadataStatus: 'enriched',
    imdbRating: 8.4, posterUrl: null, toWatch: true, toDownload: false,
    addedFromOscar: false, addedFromCatalog: false, addedFromRecommendations: true,
    createdAt: '2026-10-26T00:00:00Z', updatedAt: '2026-10-26T00:00:00Z',
    oscarFilmCount: 0, nominationCount: 0, winCount: 0, occurrenceCount: 0, lastSeenAt: null,
  }
  vi.mocked(getFavorites).mockResolvedValue({ items: [series], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
  vi.mocked(updateFavorite).mockResolvedValue({ ...series, toWatch: false })
  render(<FavoriteProvider accountKey="test-account"><FavoritesView /></FavoriteProvider>)

  expect(await screen.findAllByText('Shared Series')).toHaveLength(2)
  expect(screen.getByText('Series watch list')).toBeTruthy()
  const watchToggle = screen.getByRole('checkbox', { name: 'To watch' }) as HTMLInputElement
  expect(watchToggle.checked).toBe(true)
  fireEvent.click(watchToggle)
  await waitFor(() => expect(updateFavorite).toHaveBeenCalledWith('tt12345678', { toWatch: false }))
})

it('uses the shared poster card and opens details for a favorite without a torrent record', async () => {
  const movie: FavoriteTitle = {
    imdbId: 'tt12345678', torrentMovieId: null, title: 'Award Film', year: 2025, mediaType: 'movie', metadataStatus: 'enriched',
    imdbRating: 8.2, posterUrl: null, toWatch: true, toDownload: false,
    addedFromOscar: true, addedFromCatalog: false, addedFromRecommendations: false, createdAt: '2026-09-30T00:00:00Z',
    updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 1, nominationCount: 2,
    winCount: 1, occurrenceCount: 0, lastSeenAt: null,
  }
  vi.mocked(getFavorites).mockResolvedValue({ items: [movie], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
  render(<FavoriteProvider accountKey="test-account"><FavoritesView /></FavoriteProvider>)

  expect(await screen.findByRole('button', { name: 'View Award Film details' })).toBeTruthy()
  expect(screen.getByText('1 Oscar win')).toBeTruthy()
  fireEvent.click(screen.getByText('1 Oscar win'))

  expect(await screen.findByRole('dialog')).toBeTruthy()
  expect(screen.getByRole('heading', { name: 'Award Film' })).toBeTruthy()
  expect(screen.getByText('2', { selector: 'dd' })).toBeTruthy()
})
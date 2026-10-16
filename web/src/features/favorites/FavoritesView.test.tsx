import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { getFavorites } from '../../api/client'
import type { FavoriteMovie, PageResponse } from '../../api/types'
import { FavoriteProvider } from './FavoriteContext'
import { FavoritesView } from './FavoritesView'

vi.mock('../../api/client', () => ({ getFavorites: vi.fn() }))
afterEach(() => { cleanup(); vi.mocked(getFavorites).mockReset() })

it('does not report zero movies when the API fails', async () => {
  vi.mocked(getFavorites).mockRejectedValue(new Error('Service unavailable'))
  render(<FavoriteProvider accountKey="test-account"><FavoritesView /></FavoriteProvider>)

  expect(await screen.findByText('Could not load this view')).toBeTruthy()
  expect(screen.queryByText('0 movies')).toBeNull()
})

it('filters one canonical movie independently by watch and download status', async () => {
  const movie: FavoriteMovie = {
    titleId: 42, title: 'Shared Movie', year: 2025, mediaType: 'movie',
    imdbRating: null, posterUrl: null, toWatch: true, toDownload: true,
    addedFromOscar: true, addedFromCatalog: true, createdAt: '2026-09-30T00:00:00Z',
    updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 1, nominationCount: 2,
    winCount: 1, occurrenceCount: 1, lastSeenAt: '2026-09-30T00:00:00Z',
  }
  const response = (pageSize: number): PageResponse<FavoriteMovie> => ({
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
  const firstMovie: FavoriteMovie = {
    titleId: 1, title: 'First Account Film', year: 2025, mediaType: 'movie',
    imdbRating: null, posterUrl: null, toWatch: true, toDownload: false,
    addedFromOscar: false, addedFromCatalog: true, createdAt: '2026-09-30T00:00:00Z',
    updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 0, nominationCount: 0,
    winCount: 0, occurrenceCount: 1, lastSeenAt: '2026-09-30T00:00:00Z',
  }
  const secondMovie: FavoriteMovie = { ...firstMovie, titleId: 2, title: 'Second Account Film' }
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
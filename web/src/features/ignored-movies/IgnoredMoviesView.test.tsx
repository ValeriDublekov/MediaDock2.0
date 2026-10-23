import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { addIgnoredMovie, getIgnoredMovies, removeIgnoredMovie } from '../../api/client'
import type { IgnoredMovie, PageResponse } from '../../api/types'
import { IgnoredMovieControl } from './IgnoredMovieControl'
import { IgnoredMoviesProvider } from './IgnoredMoviesContext'
import { IgnoredMoviesView } from './IgnoredMoviesView'

vi.mock('../../api/client', () => ({
  addIgnoredMovie: vi.fn(),
  getIgnoredMovies: vi.fn(),
  removeIgnoredMovie: vi.fn(),
}))

const movie: IgnoredMovie = {
  imdbId: 'tt12345678',
  title: 'Quiet River',
  year: 2004,
  mediaType: 'movie',
  imdbRating: 8.1,
  posterUrl: null,
  createdAt: '2026-10-23T12:00:00Z',
}

const emptyPage: PageResponse<IgnoredMovie> = {
  items: [],
  page: 1,
  pageSize: 100,
  totalCount: 0,
  totalPages: 0,
}

describe('IgnoredMoviesView', () => {
  beforeEach(() => {
    vi.mocked(getIgnoredMovies).mockReset().mockResolvedValue(emptyPage)
    vi.mocked(addIgnoredMovie).mockReset().mockResolvedValue(movie)
    vi.mocked(removeIgnoredMovie).mockReset().mockResolvedValue(undefined)
  })

  afterEach(cleanup)

  it('adds a movie from a list and removes it from the personal ignore page', async () => {
    render(<IgnoredMoviesProvider accountKey="issuer:subject">
      <IgnoredMovieControl imdbId={movie.imdbId} mediaType="movie" />
      <IgnoredMoviesView />
    </IgnoredMoviesProvider>)

    fireEvent.click(await screen.findByRole('button', { name: 'Ignore movie' }))
    expect(await screen.findByRole('button', { name: 'Remove from ignore list' })).toBeTruthy()
    expect(await screen.findByRole('row', { name: /Quiet River/ })).toBeTruthy()
    expect(addIgnoredMovie).toHaveBeenCalledWith(movie.imdbId)

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }))
    await waitFor(() => expect(removeIgnoredMovie).toHaveBeenCalledWith(movie.imdbId))
    expect(await screen.findByText('No ignored movies')).toBeTruthy()
    expect(await screen.findByRole('button', { name: 'Ignore movie' })).toBeTruthy()
  })

  it('does not offer ignore actions to anonymous users or for non-movies', async () => {
    const view = render(<IgnoredMoviesProvider accountKey={null}>
      <IgnoredMovieControl imdbId={movie.imdbId} mediaType="movie" />
      <IgnoredMovieControl imdbId={movie.imdbId} mediaType="series" />
    </IgnoredMoviesProvider>)

    expect(view.queryByRole('button', { name: 'Ignore movie' })).toBeNull()
    expect(getIgnoredMovies).not.toHaveBeenCalled()
  })
})
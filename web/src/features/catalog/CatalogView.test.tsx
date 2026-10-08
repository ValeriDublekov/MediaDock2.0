import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getCatalog, getMovieAwards, getTitle } from '../../api/client'
import type { CatalogTitle, MovieAwardRecognition, PageResponse } from '../../api/types'
import { CatalogView } from './CatalogView'

vi.mock('../../api/client', () => ({
  ApiError: class ApiError extends Error {},
  getCatalog: vi.fn(),
  getTitle: vi.fn(),
  getTitleOccurrences: vi.fn(),
  getTitleOscars: vi.fn().mockResolvedValue([]),
  getMovieAwards: vi.fn().mockResolvedValue([]),
}))

const catalogRequest = vi.mocked(getCatalog)

function page(
  items: CatalogTitle[],
  number = 1,
  totalPages = 1,
  yearBounds?: PageResponse<CatalogTitle>['yearBounds'],
  imdbRatingBounds?: PageResponse<CatalogTitle>['imdbRatingBounds'],
): PageResponse<CatalogTitle> {
  return { items, page: number, pageSize: 20, totalCount: items.length, totalPages, ...(yearBounds ? { yearBounds } : {}), ...(imdbRatingBounds ? { imdbRatingBounds } : {}) }
}

const title: CatalogTitle = {
  id: 7,
  title: 'Quiet River',
  year: 2004,
  mediaType: 'series',
  sourceType: 'series',
  contentKind: 'standard',
  imdbId: 'tt1234567',
  imdbRating: 8.1,
  posterUrl: null,
  genres: ['Drama'],
  countries: ['Canada'],
  lastSeenAt: '2026-09-28T12:00:00Z',
  occurrenceCount: 3,
}

describe('CatalogView', () => {
  beforeEach(() => { catalogRequest.mockReset() })
  afterEach(() => cleanup())

  it('switches between posters and table without refetching and replaces broken images', async () => {
    catalogRequest.mockResolvedValue(page([{ ...title, posterUrl: '/missing-poster.jpg' }]))
    render(<CatalogView />)

    expect(await screen.findByRole('img', { name: 'Poster for Quiet River' })).toBeTruthy()
    fireEvent.error(screen.getByRole('img', { name: 'Poster for Quiet River' }))
    expect(screen.queryByRole('img', { name: 'Poster for Quiet River' })).toBeNull()
    expect(screen.getByText('SER')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByRole('img', { name: 'Poster for Quiet River' })).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: 'LAST SEEN' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Posters' }))
    expect(screen.getByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    expect(catalogRequest).toHaveBeenCalledTimes(1)
  })

  it('associates the IMDb rating with its title and opens that title at its torrent sources', async () => {
    catalogRequest.mockResolvedValue(page([{ ...title, occurrenceCount: 3 }]))
    render(<CatalogView />)

    const imdbLink = await screen.findByRole('link', { name: 'Open Quiet River on IMDb (opens in new tab)' })
    expect(imdbLink.textContent).toContain('8.1')
    fireEvent.click(screen.getByRole('button', { name: 'View torrent sources for Quiet River (3)' }))

    expect(await screen.findByRole('dialog')).toBeTruthy()
    expect(getTitle).toHaveBeenCalledWith(title.id)
  })

  it('opens the title directly on IMDb from the catalog card', async () => {
    catalogRequest.mockResolvedValue(page([title]))
    render(<CatalogView />)

    const imdbLink = await screen.findByRole('link', { name: 'Open Quiet River on IMDb (opens in new tab)' })
    expect(imdbLink.getAttribute('href')).toBe('https://www.imdb.com/title/tt1234567/')
    expect(imdbLink.getAttribute('target')).toBe('_blank')
    expect(imdbLink.getAttribute('rel')).toBe('noopener noreferrer')
  })

  it('shows both award sources from the page-scoped IMDb lookup', async () => {
    vi.mocked(getMovieAwards).mockResolvedValue([
      {
        id: 21, imdbId: 'tt1234567', source: 'oscars', filmYear: 2004, ceremonyYear: null,
        ceremony: 77, award: 'Best Picture', name: null, nominees: 'Producer', detail: null, isWinner: true,
      },
      {
        id: 22, imdbId: 'tt1234567', source: 'golden_globes', filmYear: null, ceremonyYear: 2005,
        ceremony: null, award: 'Best Drama', name: null, nominees: null, detail: null, isWinner: false,
      },
    ] satisfies MovieAwardRecognition[])
    catalogRequest.mockResolvedValue(page([title]))
    render(<CatalogView />)

    const awards = await screen.findByLabelText('Awards and nominations')
    expect(await within(awards).findByText('Oscars')).toBeTruthy()
    expect(await within(awards).findByText('Golden Globes')).toBeTruthy()
    expect(getMovieAwards).toHaveBeenCalledWith(['tt1234567'])
  })

  it('requests the next page from the pagination controls', async () => {
    catalogRequest.mockResolvedValue(page([title], 1, 2))
    render(<CatalogView />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 2,
      pageSize: 20,
      feedTypes: ['movie', 'series_complete', 'series_ongoing'],
    }))
  })

  it('previews global year bounds and applies only the released range to applied filters', async () => {
    catalogRequest.mockResolvedValue(page([title], 1, 2, { minYear: 1980, maxYear: 2024 }))
    render(<CatalogView />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    const minimum = screen.getByRole('slider', { name: 'Title year minimum' }) as HTMLInputElement
    expect(minimum.min).toBe('1980')
    expect(minimum.max).toBe('2024')
    expect(screen.getByText('1980 – 2024')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Search titles'), { target: { value: 'Quiet River' } })
    expect(catalogRequest).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, feedTypes: ['movie', 'series_complete', 'series_ongoing'], search: 'Quiet River',
    }))
    fireEvent.click(await screen.findByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 2, pageSize: 20, feedTypes: ['movie', 'series_complete', 'series_ongoing'], search: 'Quiet River',
    }))

    fireEvent.change(screen.getByLabelText('Search titles'), { target: { value: 'Unsubmitted search' } })
    fireEvent.change(minimum, { target: { value: '2000' } })
    expect(screen.getByText('2000 – 2024')).toBeTruthy()
    expect(catalogRequest).toHaveBeenCalledTimes(3)
    fireEvent.pointerUp(minimum)

    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      feedTypes: ['movie', 'series_complete', 'series_ongoing'],
      search: 'Quiet River',
      yearFrom: 2000,
    }))
  })

  it('hides the year range when the catalog has no valid year bounds', async () => {
    catalogRequest.mockResolvedValue(page([]))
    render(<CatalogView />)

    expect(await screen.findByText('No titles found')).toBeTruthy()
    expect(screen.queryByRole('slider')).toBeNull()
  })

  it('applies an IMDb rating minimum using the database-provided bounds', async () => {
    catalogRequest.mockResolvedValue(page([title], 1, 1, undefined, { minRating: 6.4, maxRating: 9.2 }))
    render(<CatalogView />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    const minimum = screen.getByRole('slider', { name: 'IMDb rating minimum' }) as HTMLInputElement
    expect(minimum.min).toBe('6.4')
    expect(minimum.max).toBe('9.2')
    fireEvent.change(minimum, { target: { value: '8.1' } })
    expect(screen.getByLabelText('IMDb rating selected range').textContent).toBe('8.1 – 9.2')
    fireEvent.pointerUp(minimum)

    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, feedTypes: ['movie', 'series_complete', 'series_ongoing'], imdbRatingFrom: 8.1,
    }))
  })

  it('omits both year parameters for the full range so yearless titles remain included', async () => {
    const yearlessTitle = { ...title, id: 8, title: 'Year Unknown', year: null }
    catalogRequest.mockResolvedValue(page([yearlessTitle], 1, 1, { minYear: 1980, maxYear: 2024 }))
    render(<CatalogView />)

    expect(await screen.findByRole('button', { name: 'View Year Unknown details' })).toBeTruthy()
    fireEvent.pointerUp(screen.getByRole('slider', { name: 'Title year minimum' }))

    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, feedTypes: ['movie', 'series_complete', 'series_ongoing'],
    }))
    expect(screen.getByRole('button', { name: 'View Year Unknown details' })).toBeTruthy()
  })

  it('defaults to the three main categories and switches categories with one click', async () => {
    catalogRequest.mockResolvedValue(page([]))
    render(<CatalogView />)

    expect(await screen.findByText('No titles found')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Main categories' }).getAttribute('aria-pressed')).toBe('true')
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      feedTypes: ['movie', 'series_complete', 'series_ongoing'],
    }))

    fireEvent.click(screen.getByRole('button', { name: 'Movies' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, feedTypes: ['movie'] }))
    fireEvent.click(screen.getByRole('button', { name: 'Series' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, feedTypes: ['series_complete'] }))
    fireEvent.click(screen.getByRole('button', { name: 'Series in progress' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, feedTypes: ['series_ongoing'] }))
    fireEvent.click(screen.getByRole('button', { name: 'All' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20 }))
    expect(screen.getByRole('button', { name: 'All' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('applies search and filters and renders an empty result state', async () => {
    catalogRequest.mockResolvedValue(page([]))
    render(<CatalogView />)

    fireEvent.click(screen.getByText('More filters'))
    fireEvent.change(screen.getByLabelText('Search titles'), { target: { value: 'quiet river' } })
    expect(catalogRequest).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('button', { name: 'Apply filters' })).toBeNull()
    fireEvent.change(screen.getByLabelText('Media type'), { target: { value: 'series' } })
    expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, feedTypes: ['movie', 'series_complete', 'series_ongoing'], mediaType: 'series',
    })
    fireEvent.change(screen.getByLabelText('Genre'), { target: { value: 'drama' } })
    expect(screen.getByText('More filters (2 active)')).toBeTruthy()
    expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, feedTypes: ['movie', 'series_complete', 'series_ongoing'], mediaType: 'series', genre: 'drama',
    })

    expect(await screen.findByText('No titles found')).toBeTruthy()
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      feedTypes: ['movie', 'series_complete', 'series_ongoing'],
      search: 'quiet river',
      mediaType: 'series',
      genre: 'drama',
    }))
  })

  it('offers retry after a catalog request error', async () => {
    catalogRequest
      .mockRejectedValueOnce(new ApiError('Catalog service unavailable.', 503))
      .mockResolvedValueOnce(page([]))
    render(<CatalogView />)

    expect(await screen.findByRole('alert')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('No titles found')).toBeTruthy()
    expect(catalogRequest).toHaveBeenCalledTimes(2)
  })
})
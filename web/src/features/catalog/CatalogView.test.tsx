import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getCatalog } from '../../api/client'
import type { CatalogTitle, PageResponse } from '../../api/types'
import { CatalogView } from './CatalogView'

vi.mock('../../api/client', () => ({
  ApiError: class ApiError extends Error {},
  getCatalog: vi.fn(),
  getTitle: vi.fn(),
  getTitleOccurrences: vi.fn(),
}))

const catalogRequest = vi.mocked(getCatalog)

function page(items: CatalogTitle[], number = 1, totalPages = 1): PageResponse<CatalogTitle> {
  return { items, page: number, pageSize: 20, totalCount: items.length, totalPages }
}

const title: CatalogTitle = {
  id: 7,
  title: 'Quiet River',
  year: 2004,
  mediaType: 'series',
  sourceType: 'series',
  contentKind: 'standard',
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
    expect(screen.getByRole('columnheader', { name: 'LAST SEEN' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Posters' }))
    expect(screen.getByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    expect(catalogRequest).toHaveBeenCalledTimes(1)
  })

  it('requests the next page from the pagination controls', async () => {
    catalogRequest.mockResolvedValue(page([title], 1, 2))
    render(<CatalogView />)

    expect(await screen.findByRole('button', { name: 'View Quiet River details' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 }))
  })

  it('applies search and filters and renders an empty result state', async () => {
    catalogRequest.mockResolvedValue(page([]))
    render(<CatalogView />)

    fireEvent.change(screen.getByLabelText('Search titles'), { target: { value: 'quiet river' } })
    fireEvent.change(screen.getByLabelText('Media type'), { target: { value: 'series' } })
    fireEvent.change(screen.getByLabelText('Genre'), { target: { value: 'drama' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    expect(await screen.findByText('No titles found')).toBeTruthy()
    await waitFor(() => expect(catalogRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
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
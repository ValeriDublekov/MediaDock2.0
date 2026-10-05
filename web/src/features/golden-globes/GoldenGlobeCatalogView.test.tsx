import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getGoldenGlobeFilms } from '../../api/client'
import type { GoldenGlobeFilm, PageResponse } from '../../api/types'
import { GoldenGlobeCatalogView } from './GoldenGlobeCatalogView'

vi.mock('../../api/client', () => ({ getGoldenGlobeFilms: vi.fn() }))

const film: GoldenGlobeFilm = {
  id: '2025:A Film',
  title: 'A Film',
  year: 2025,
  imdbId: null,
  posterUrl: 'https://example.test/a-film.jpg',
  enrichmentStatus: 'pending',
  enrichmentError: null,
  nominations: [{ id: 1, year: 2025, award: 'Best Picture', isWinner: true }],
}

const page: PageResponse<GoldenGlobeFilm> = {
  items: [film],
  page: 1,
  pageSize: 20,
  totalCount: 1,
  totalPages: 1,
}

describe('GoldenGlobeCatalogView', () => {
  beforeEach(() => {
    vi.mocked(getGoldenGlobeFilms).mockReset().mockResolvedValue(page)
  })

  afterEach(() => cleanup())

  it('loads grouped film records and applies the supported filters', async () => {
    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByRole('button', { name: 'A Film' })).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Poster for A Film' }).getAttribute('src')).toBe('https://example.test/a-film.jpg')
    expect(screen.getByText('2025')).toBeTruthy()
    expect(screen.getByText('1 nomination')).toBeTruthy()
    expect(screen.getByText('Winner · 1 win')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: '  A Film  ' } })
    fireEvent.change(screen.getByLabelText('Year from'), { target: { value: '2025' } })
    fireEvent.change(screen.getByLabelText('Award'), { target: { value: 'Best Picture' } })
    fireEvent.change(screen.getByLabelText('Award result'), { target: { value: 'winner' } })
    fireEvent.change(screen.getByLabelText('OMDb status'), { target: { value: 'not_found' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    await waitFor(() => expect(getGoldenGlobeFilms).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      search: 'A Film',
      yearFrom: 2025,
      award: 'Best Picture',
      result: 'winner',
      enrichmentStatus: 'not_found',
    }))
  })
})
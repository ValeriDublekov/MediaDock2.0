import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getGoldenGlobeFilms, getMovieAwards } from '../../api/client'
import type { GoldenGlobeFilm, MovieAwardRecognition, PageResponse } from '../../api/types'
import { GoldenGlobeCatalogView } from './GoldenGlobeCatalogView'

vi.mock('../../api/client', () => ({ getGoldenGlobeFilms: vi.fn(), getMovieAwards: vi.fn().mockResolvedValue([]) }))

const film: GoldenGlobeFilm = {
  id: '2025:A Film',
  title: 'A Film',
  year: 2025,
  imdbId: null,
  imdbRating: null,
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
    vi.mocked(getMovieAwards).mockReset().mockResolvedValue([])
  })

  afterEach(() => cleanup())

  it('loads grouped film records and applies the supported filters', async () => {
    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByRole('button', { name: 'A Film' })).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Poster for A Film' }).getAttribute('src')).toBe('https://example.test/a-film.jpg')
    expect(screen.getByText('2025')).toBeTruthy()
    const awardSummary = within(screen.getByLabelText('Awards and nominations'))
    expect(awardSummary.getByText('Golden Globes')).toBeTruthy()
    expect(awardSummary.getByText('1 win · 1 nomination')).toBeTruthy()

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

  it('shows the reason for Golden Globes enrichment problems', async () => {
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({
      ...page,
      items: [{ ...film, enrichmentStatus: 'problem', enrichmentError: 'year_mismatch:2016' }],
    })

    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByText('OMDb matched a film from 2016; this ceremony accepts films from 2024 or 2025.')).toBeTruthy()
  })

  it('opens combined award details from the shared poster card', async () => {
    const matchedFilm = { ...film, imdbId: 'tt1234567', imdbRating: 7.4 }
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({ ...page, items: [matchedFilm] })
    vi.mocked(getMovieAwards).mockResolvedValue([{
      id: 12, imdbId: 'tt1234567', source: 'oscars', filmYear: 2024, ceremonyYear: null,
      ceremony: 97, award: 'Best Picture', name: null, nominees: 'Producer', detail: null, isWinner: true,
    } satisfies MovieAwardRecognition])
    render(<GoldenGlobeCatalogView />)

    fireEvent.click(await screen.findByRole('button', { name: 'View A Film details' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('heading', { name: 'A Film' })).toBeTruthy()
    expect(within(dialog).getByRole('heading', { name: 'Awards and nominations' })).toBeTruthy()
    expect(within(dialog).getByText('Ceremony year 2025')).toBeTruthy()
    expect(within(dialog).getByRole('link', { name: 'Open A Film on IMDb (opens in new tab)' }).textContent).toContain('7.4')
    expect(within(dialog).getAllByText('Best Picture')).toHaveLength(2)
  })

  it('explains not-found and temporary provider errors', async () => {
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({
      ...page,
      items: [
        { ...film, enrichmentStatus: 'not_found', enrichmentError: 'not_found' },
        { ...film, id: '2025:Timeout', title: 'Timeout', enrichmentStatus: 'temporary_error', enrichmentError: 'timeout' },
      ],
    })

    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByText('OMDb did not find a matching title.')).toBeTruthy()
    expect(await screen.findByText('The OMDb request timed out.')).toBeTruthy()
  })
})
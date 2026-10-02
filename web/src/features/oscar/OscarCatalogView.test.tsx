import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { addFavorite, getFavorites, getOscarFilm, getOscarFilms, getTitleOccurrences, getTitleOscars, updateFavorite } from '../../api/client'
import type { FavoriteMovie, OscarFilm, OscarNomination, PageResponse } from '../../api/types'
import { FavoriteProvider } from '../favorites/FavoriteContext'
import { OscarCatalogView } from './OscarCatalogView'

vi.mock('../../api/client', () => ({
  getOscarFilm: vi.fn(),
  getOscarFilms: vi.fn(),
  getTitleOscars: vi.fn(),
  getTitleOccurrences: vi.fn(),
  getFavorites: vi.fn(),
  addFavorite: vi.fn(),
  updateFavorite: vi.fn(),
}))

const filmRequest = vi.mocked(getOscarFilms)
const detailsRequest = vi.mocked(getOscarFilm)
const titleOscarsRequest = vi.mocked(getTitleOscars)
const occurrencesRequest = vi.mocked(getTitleOccurrences)
const favoritesRequest = vi.mocked(getFavorites)
const addRequest = vi.mocked(addFavorite)
const updateRequest = vi.mocked(updateFavorite)

function page(items: OscarFilm[], number = 1, totalPages = 1): PageResponse<OscarFilm> {
  return { items, page: number, pageSize: 20, totalCount: items.length, totalPages }
}

const nominations: OscarNomination[] = [
  {
    id: 11,
    ceremony: 90,
    class: 'feature film',
    canonicalCategory: 'BEST PICTURE',
    category: 'Best Picture',
    name: 'The Shape of Water',
    nominees: 'Guillermo del Toro and J. Miles Dale',
    nomineeIds: '',
    detail: 'Fox Searchlight Pictures',
    isWinner: true,
  },
  {
    id: 12,
    ceremony: 90,
    class: 'feature film',
    canonicalCategory: 'DIRECTING',
    category: 'Directing',
    name: 'Guillermo del Toro',
    nominees: 'Guillermo del Toro',
    nomineeIds: '',
    detail: '',
    isWinner: false,
  },
]

const film: OscarFilm = {
  id: 7,
  titleId: 19,
  title: 'The Shape of Water',
  metadataTitle: 'The Shape of Water',
  metadataYear: 2017,
  filmYear: 2017,
  imdbId: 'tt5580390',
  enrichmentStatus: 'enriched',
  enrichmentAttemptCount: 1,
  lastEnrichmentAttemptAt: '2026-09-28T12:00:00Z',
  nextEnrichmentAttemptAt: null,
  lastEnrichmentError: null,
  mediaType: 'movie',
  imdbRating: 7.3,
  imdbVotes: 450000,
  metascore: 87,
  genres: ['Drama', 'Fantasy', 'Romance'],
  countries: ['USA'],
  director: 'Guillermo del Toro',
  plot: 'A woman discovers a mysterious amphibious creature.',
  posterUrl: null,
  runtime: '123 min',
  awards: '4 wins',
  boxOffice: '$63,859,435',
  nominations,
}

describe('OscarCatalogView', () => {
  beforeEach(() => {
    filmRequest.mockReset()
    detailsRequest.mockReset()
    titleOscarsRequest.mockReset()
    occurrencesRequest.mockReset()
    favoritesRequest.mockReset()
    addRequest.mockReset()
    updateRequest.mockReset()
  })
  afterEach(() => cleanup())

  it('shows the winner, film details, and IMDb link in both list modes', async () => {
    filmRequest.mockResolvedValue(page([film]))
    render(<OscarCatalogView />)

    expect(await screen.findByText('Winner · 1 win')).toBeTruthy()
    const posterButton = screen.getByRole('button', { name: 'View The Shape of Water Oscar details' })
    const posterTooltip = screen.getByRole('tooltip')
    expect(posterTooltip.textContent).toBe('Won Best Picture; Nominated for Directing')
    expect(posterButton.getAttribute('aria-describedby')).toBe(posterTooltip.id)
    expect(screen.getByText('2 nominations').className).toContain('oscar-nomination-count')
    expect(screen.getByText(film.plot!)).toBeTruthy()
    expect(screen.getByText(`Director: ${film.director}`)).toBeTruthy()
    expect(screen.getByText('Drama · Fantasy · Romance')).toBeTruthy()
    const posterRatingLink = screen.getByRole('link', { name: 'Open The Shape of Water on IMDb (opens in new tab)' })
    expect(posterRatingLink.getAttribute('href')).toBe('https://www.imdb.com/title/tt5580390/')
    expect(posterRatingLink.getAttribute('target')).toBe('_blank')

    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByRole('columnheader', { name: 'OMDB' })).toBeTruthy()
    expect(screen.getByText('Winner')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Open The Shape of Water on IMDb (opens in new tab)' })).toBeTruthy()
    expect(filmRequest).toHaveBeenCalledTimes(1)
  })

  it('labels a film with no winning nomination as a nominee', async () => {
    filmRequest.mockResolvedValue(page([{
      ...film,
      nominations: film.nominations.map((nomination) => ({ ...nomination, isWinner: false })),
    }]))
    render(<OscarCatalogView />)

    expect(await screen.findByText('Nominee · 2 nominations')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByText('Nominee')).toBeTruthy()
  })

  it('applies award filters and renders the empty state', async () => {
    filmRequest.mockResolvedValue(page([]))
    render(<OscarCatalogView />)

    fireEvent.click(screen.getByText('More filters'))
    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: '  Oppenheimer  ' } })
    fireEvent.change(screen.getByLabelText('Year from'), { target: { value: '2022' } })
    fireEvent.change(screen.getByLabelText('Year to'), { target: { value: '2024' } })
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'BEST PICTURE' } })
    fireEvent.change(screen.getByLabelText('Award result'), { target: { value: 'winner' } })
    fireEvent.change(screen.getByLabelText('OMDb status'), { target: { value: 'pending' } })
    expect(screen.getByText('More filters (5 active)')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    expect(await screen.findByText('No Oscar films found')).toBeTruthy()
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      search: 'Oppenheimer',
      yearFrom: 2022,
      yearTo: 2024,
      category: 'BEST PICTURE',
      result: 'winner',
      enrichmentStatus: 'pending',
    }))
  })

  it('opens the film details and displays all nominations and winners', async () => {
    const awardFilm: OscarFilm = {
      ...film,
      awards: '2 wins & 14 nominations total',
      nominations: [
        ...nominations,
        {
          ...nominations[0], id: 13, canonicalCategory: 'WRITING (Original Screenplay)',
          category: 'Writing (Original Screenplay)', isWinner: true,
        },
        {
          ...nominations[1], id: 14, canonicalCategory: 'WRITING (Adapted Screenplay)',
          category: 'Writing (Adapted Screenplay)', isWinner: false,
        },
        {
          ...nominations[1], id: 15, canonicalCategory: 'CINEMATOGRAPHY',
          category: 'Cinematography', isWinner: false,
        },
      ],
    }
    filmRequest.mockResolvedValue(page([film]))
    detailsRequest.mockResolvedValue(awardFilm)
    titleOscarsRequest.mockResolvedValue([awardFilm])
    occurrencesRequest.mockResolvedValue({ items: [], page: 1, pageSize: 5, totalCount: 0, totalPages: 0 })
    render(<OscarCatalogView />)

    fireEvent.click(await screen.findByRole('button', { name: 'View The Shape of Water Oscar details' }))
    const dialog = await screen.findByRole('dialog')
    expect(detailsRequest).toHaveBeenCalledWith(film.id)
    const awardSummary = within(dialog).getByText('2 wins & 3 nominations')
    const detailTooltip = within(dialog).getByRole('tooltip')
    expect(awardSummary.getAttribute('aria-describedby')).toBe(detailTooltip.id)
    expect(detailTooltip.textContent).toBe(
      'Won Best Picture; Nominated for Directing; Won Writing (Original Screenplay); Nominated for Writing (Adapted Screenplay); Nominated for Cinematography',
    )
    expect(within(dialog).queryByText('2 wins & 14 nominations total')).toBeNull()
    expect(await within(dialog).findAllByText('Guillermo del Toro and J. Miles Dale')).toHaveLength(2)
    expect(within(dialog).getAllByText('Fox Searchlight Pictures')).toHaveLength(2)
    expect(within(dialog).getAllByText('Winner')).toHaveLength(2)
    expect(within(dialog).getAllByText('Nominee')).toHaveLength(3)
  })

  it('requests the next page from the pagination controls', async () => {
    filmRequest.mockResolvedValue(page([film], 1, 2))
    render(<OscarCatalogView />)

    expect(await screen.findByRole('button', { name: 'View The Shape of Water Oscar details' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 }))
  })

  it('adds from Oscar, preserves a disabled marker, and keeps state after a failed save', async () => {
    const favorite: FavoriteMovie = {
      titleId: film.titleId, title: film.title, year: film.filmYear, mediaType: 'movie',
      imdbRating: film.imdbRating, posterUrl: null, toWatch: true, toDownload: false,
      addedFromOscar: true, addedFromCatalog: false, createdAt: '2026-09-30T00:00:00Z',
      updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 1, nominationCount: 2,
      winCount: 1, occurrenceCount: 0, lastSeenAt: null,
    }
    filmRequest.mockResolvedValue(page([film]))
    favoritesRequest.mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 })
    addRequest.mockResolvedValue(favorite)
    updateRequest.mockRejectedValue(new Error('Save failed'))
    render(<FavoriteProvider><OscarCatalogView /></FavoriteProvider>)

    fireEvent.click(await screen.findByRole('button', { name: 'Add to favorites' }))
    await waitFor(() => expect(addRequest).toHaveBeenCalledWith(film.titleId, 'oscar'))
    const watch = await screen.findByRole('checkbox', { name: 'To watch' }) as HTMLInputElement
    expect(watch.checked).toBe(true)
    expect((screen.getByRole('checkbox', { name: 'To download' }) as HTMLInputElement).disabled).toBe(true)
    fireEvent.click(watch)
    expect(await screen.findByText('Save failed')).toBeTruthy()
    expect(watch.checked).toBe(true)
  })

  it('records Oscar origin on an existing catalog favorite without clearing download', async () => {
    const existing: FavoriteMovie = {
      titleId: film.titleId, title: film.title, year: film.filmYear, mediaType: 'movie',
      imdbRating: film.imdbRating, posterUrl: null, toWatch: false, toDownload: true,
      addedFromOscar: false, addedFromCatalog: true, createdAt: '2026-09-30T00:00:00Z',
      updatedAt: '2026-09-30T00:00:00Z', oscarFilmCount: 1, nominationCount: 2,
      winCount: 1, occurrenceCount: 1, lastSeenAt: '2026-09-30T00:00:00Z',
    }
    filmRequest.mockResolvedValue(page([film]))
    favoritesRequest.mockResolvedValue({ items: [existing], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 })
    addRequest.mockResolvedValue({ ...existing, toWatch: true, addedFromOscar: true })
    render(<FavoriteProvider><OscarCatalogView /></FavoriteProvider>)

    fireEvent.click(await screen.findByRole('button', { name: 'Add from Oscar' }))
    await waitFor(() => expect(addRequest).toHaveBeenCalledWith(film.titleId, 'oscar'))
    expect((screen.getByRole('checkbox', { name: 'To download' }) as HTMLInputElement).checked).toBe(true)
    expect((screen.getByRole('checkbox', { name: 'To watch' }) as HTMLInputElement).checked).toBe(true)
  })
})
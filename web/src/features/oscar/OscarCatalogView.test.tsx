import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { addFavorite, getFavorites, getOscarCategories, getOscarFilm, getOscarFilms, getTitleOccurrences, getTitleOscars, updateFavorite } from '../../api/client'
import type { FavoriteMovie, OscarFilm, OscarNomination, PageResponse } from '../../api/types'
import { FavoriteProvider } from '../favorites/FavoriteContext'
import { OscarCatalogView } from './OscarCatalogView'

vi.mock('../../api/client', () => ({
  getOscarFilm: vi.fn(),
  getOscarCategories: vi.fn(),
  getOscarFilms: vi.fn(),
  getTitleOscars: vi.fn(),
  getMovieAwards: vi.fn().mockResolvedValue([]),
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
  return {
    items,
    page: number,
    pageSize: 20,
    totalCount: items.length,
    totalPages,
    yearBounds: { minYear: 2021, maxYear: 2023 },
    imdbRatingBounds: { minRating: 6.2, maxRating: 9.6 },
  }
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
    vi.mocked(getOscarCategories).mockReset().mockResolvedValue([
      'BEST PICTURE', 'DIRECTING', 'WRITING (Original Screenplay)', 'WRITING (Adapted Screenplay)', 'CINEMATOGRAPHY',
    ])
    detailsRequest.mockReset()
    titleOscarsRequest.mockReset()
    occurrencesRequest.mockReset()
    favoritesRequest.mockReset()
    addRequest.mockReset()
    updateRequest.mockReset()
  })
  afterEach(() => cleanup())

  it('shows the common award summary, film details, and IMDb link in both list modes', async () => {
    filmRequest.mockResolvedValue(page([{ ...film, posterUrl: 'https://example.test/poster.jpg' }]))
    render(<OscarCatalogView />)

    expect(await screen.findByText('Awards and nominations')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'View The Shape of Water Oscar details' })).toBeTruthy()
    const awardSummary = within(screen.getByLabelText('Awards and nominations'))
    expect(awardSummary.getByText('Oscars')).toBeTruthy()
    expect(awardSummary.getByText('1 win · 2 nominations')).toBeTruthy()
    expect(screen.getByText(film.plot!)).toBeTruthy()
    expect(screen.getByText(`Director: ${film.director}`)).toBeTruthy()
    expect(screen.getByText('Drama')).toBeTruthy()
    expect(screen.getByText('Fantasy')).toBeTruthy()
    expect(screen.getByText('Romance')).toBeTruthy()
    const posterRatingLink = screen.getByRole('link', { name: 'Open The Shape of Water on IMDb (opens in new tab)' })
    expect(posterRatingLink.getAttribute('href')).toBe('https://www.imdb.com/title/tt5580390/')
    expect(posterRatingLink.getAttribute('target')).toBe('_blank')

    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByRole('img', { name: 'Poster for The Shape of Water' })).toBeTruthy()
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

    const awardSummary = within(await screen.findByLabelText('Awards and nominations'))
    expect(awardSummary.getByText('0 wins · 2 nominations')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByText('Nominee')).toBeTruthy()
  })

  it('applies award filters and renders the empty state', async () => {
    filmRequest.mockResolvedValue(page([]))
    render(<OscarCatalogView />)

    fireEvent.click(screen.getByText('More filters'))
    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: '  Oppenheimer  ' } })
    expect(filmRequest).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('button', { name: 'Apply filters' })).toBeNull()
    fireEvent.change(screen.getByLabelText('Award result'), { target: { value: 'winner' } })
    expect(filmRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, result: 'winner' })
    fireEvent.change(screen.getByLabelText('OMDb status'), { target: { value: 'pending' } })
    expect(filmRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, result: 'winner', enrichmentStatus: 'pending' })
    fireEvent.click(await screen.findByLabelText('Categories: All categories (5)'))
    fireEvent.click(screen.getByLabelText('BEST PICTURE'))
    expect(screen.getByText('More filters (3 active)')).toBeTruthy()

    expect(await screen.findByText('No Oscar films found')).toBeTruthy()
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      search: 'Oppenheimer',
      categories: ['DIRECTING', 'WRITING (Original Screenplay)', 'WRITING (Adapted Screenplay)', 'CINEMATOGRAPHY'],
      categoryFilter: true,
      result: 'winner',
      enrichmentStatus: 'pending',
    }))
  })

  it('applies the keyboard-released film year range without submitting unfinished filters', async () => {
    filmRequest.mockResolvedValue(page([film], 1, 2))
    render(<OscarCatalogView />)

    expect(await screen.findByRole('button', { name: 'View The Shape of Water Oscar details' })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: 'Shape of Water' } })
    expect(filmRequest).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, search: 'Shape of Water' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, search: 'Shape of Water' }))

    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: 'Unsubmitted search' } })
    const minimum = screen.getByRole('slider', { name: 'Film year minimum' }) as HTMLInputElement
    expect(minimum.min).toBe('2021')
    expect(minimum.max).toBe('2023')
    fireEvent.change(minimum, { target: { value: '2022' } })
    expect(screen.getByText('2022 – 2023')).toBeTruthy()
    expect(filmRequest).toHaveBeenCalledTimes(3)
    fireEvent.keyUp(minimum, { key: 'ArrowRight' })

    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, search: 'Shape of Water', yearFrom: 2022,
    }))
  })

  it('applies an IMDb rating maximum using the database-provided bounds', async () => {
    filmRequest.mockResolvedValue(page([film]))
    render(<OscarCatalogView />)

    expect(await screen.findByRole('button', { name: 'View The Shape of Water Oscar details' })).toBeTruthy()
    const maximum = screen.getByRole('slider', { name: 'IMDb rating maximum' }) as HTMLInputElement
    expect(maximum.min).toBe('6.2')
    expect(maximum.max).toBe('9.6')
    fireEvent.change(maximum, { target: { value: '8.4' } })
    fireEvent.keyUp(maximum, { key: 'ArrowLeft' })

    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, imdbRatingTo: 8.4,
    }))
  })

  it('supports an empty category selection and restoring all categories', async () => {
    filmRequest.mockResolvedValue(page([film], 1, 2))
    render(<OscarCatalogView />)

    fireEvent.click(await screen.findByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 }))

    const summary = await screen.findByLabelText('Categories: All categories (5)')
    expect((screen.getAllByRole('checkbox') as HTMLInputElement[]).every((checkbox) => checkbox.checked)).toBe(true)
    fireEvent.click(summary)
    for (const category of [
      'BEST PICTURE', 'DIRECTING', 'WRITING (Original Screenplay)',
      'WRITING (Adapted Screenplay)', 'CINEMATOGRAPHY',
    ]) {
      fireEvent.click(screen.getByLabelText(category))
    }

    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      categories: [],
      categoryFilter: true,
    }))
    expect(screen.getByLabelText('Categories: 0 of 5 selected')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Select all' }))
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20 }))
    expect(screen.getByLabelText('Categories: All categories (5)')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: 'temporary search' } })
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(filmRequest).toHaveBeenLastCalledWith({ page: 1, pageSize: 20 }))
    expect((screen.getByLabelText('Search films') as HTMLInputElement).value).toBe('')
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
    expect(within(dialog).getByRole('heading', { name: 'Awards and nominations' })).toBeTruthy()
    expect(within(dialog).queryByText('2 wins & 14 nominations total')).toBeNull()
    expect(await within(dialog).findAllByText('Guillermo del Toro and J. Miles Dale')).toHaveLength(2)
    expect(within(dialog).getAllByText('Fox Searchlight Pictures')).toHaveLength(2)
    const awardsSection = within(dialog).getByRole('heading', { name: 'Awards and nominations' }).closest('section')
    expect(within(awardsSection as HTMLElement).getAllByText('Winner')).toHaveLength(2)
    expect(within(awardsSection as HTMLElement).getAllByText('Nominee')).toHaveLength(3)
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
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  addManualImdbTitle,
  addFavorite,
  addIgnoredMovie,
  addPersonalRating,
  deleteImdbTitle,
  getFavorites,
  getIgnoredMovies,
  getImdbTitles,
  getMovieAwards,
  lookupManualImdbTitleCandidate,
  removeFavorite,
  removeIgnoredMovie,
  updateFavorite,
} from '../../api/client'
import type {
  FavoriteTitle,
  IgnoredMovie,
  ImdbTitleCatalogItem,
  ImdbTitlesPage,
  MovieAwardRecognition,
  PageResponse,
} from '../../api/types'
import { FavoriteProvider } from '../favorites/FavoriteContext'
import { IgnoredMoviesProvider } from '../ignored-movies/IgnoredMoviesContext'
import { ImdbTitlesCatalogView } from './ImdbTitlesCatalogView'

vi.mock('../../api/client', () => ({
  addManualImdbTitle: vi.fn(),
  addFavorite: vi.fn(),
  addIgnoredMovie: vi.fn(),
  addPersonalRating: vi.fn(),
  deleteImdbTitle: vi.fn(),
  getFavorites: vi.fn(),
  getIgnoredMovies: vi.fn(),
  getImdbTitles: vi.fn(),
  getMovieAwards: vi.fn(),
  lookupManualImdbTitleCandidate: vi.fn(),
  removeFavorite: vi.fn(),
  removeIgnoredMovie: vi.fn(),
  updateFavorite: vi.fn(),
}))

const episode: ImdbTitleCatalogItem = {
  imdbId: 'tt9300001',
  title: 'Episode file',
  year: 2024,
  mediaType: 'episode',
  imdbRating: 8.3,
  imdbVotes: 1500,
  metascore: 72,
  genres: ['Drama'],
  countries: ['Canada'],
  director: 'A. Director',
  plot: 'A story in one episode.',
  posterUrl: null,
  runtime: '45 min',
  awards: 'One nomination',
  boxOffice: null,
  metadataStatus: 'enriched',
  isFavorite: true,
  toWatch: true,
  toDownload: false,
  isIgnored: true,
  personalRating: 8,
}

const untyped: ImdbTitleCatalogItem = {
  ...episode,
  imdbId: 'tt9300002',
  title: 'Untyped file',
  countries: ['USA'],
  mediaType: null,
  isFavorite: false,
  toWatch: false,
  isIgnored: false,
  personalRating: null,
}

const pending: ImdbTitleCatalogItem = {
  ...untyped,
  imdbId: 'tt9300003',
  title: 'Pending title',
  mediaType: 'game',
  metadataStatus: 'pending',
}

const award: MovieAwardRecognition = {
  id: 1,
  imdbId: episode.imdbId,
  source: 'oscars',
  filmYear: 2024,
  ceremonyYear: null,
  ceremony: 97,
  award: 'Best Picture',
  name: null,
  nominees: 'Episode file',
  detail: null,
  isWinner: false,
}

const favorite: FavoriteTitle = {
  imdbId: episode.imdbId,
  torrentMovieId: null,
  title: episode.title,
  year: episode.year,
  mediaType: episode.mediaType,
  metadataStatus: 'enriched',
  imdbRating: episode.imdbRating,
  posterUrl: episode.posterUrl,
  toWatch: true,
  toDownload: false,
  addedFromOscar: false,
  addedFromCatalog: true,
  addedFromRecommendations: false,
  addedFromManual: false,
  createdAt: '2026-11-05T00:00:00Z',
  updatedAt: '2026-11-05T00:00:00Z',
  oscarFilmCount: 0,
  nominationCount: 0,
  winCount: 0,
  occurrenceCount: 0,
  lastSeenAt: null,
}

const ignored: IgnoredMovie = {
  imdbId: episode.imdbId,
  title: episode.title,
  year: episode.year,
  mediaType: episode.mediaType,
  imdbRating: episode.imdbRating,
  posterUrl: episode.posterUrl,
  createdAt: '2026-11-05T00:00:00Z',
}

function page(items: ImdbTitleCatalogItem[], requestedPage = 1, totalCount = items.length): ImdbTitlesPage {
  return {
    items,
    page: requestedPage,
    pageSize: 24,
    totalCount,
    totalPages: totalCount === 0 ? 0 : Math.ceil(totalCount / 24),
    yearBounds: { minYear: 1980, maxYear: 2025 },
    imdbRatingBounds: { minRating: 5.2, maxRating: 9.0 },
    imdbVotesBounds: { minVotes: 100, maxVotes: 1_000_000 },
    mediaTypes: ['episode', 'game'],
    hasUntypedTitles: true,
    genres: ['Drama', 'Sci-Fi'],
    countries: [...new Set(items.flatMap((item) => item.countries))].sort(),
    metadataStatuses: ['pending', 'enriched', 'not_found', 'temporary_error'],
  }
}

function personalPage<T>(items: T[]): PageResponse<T> {
  return { items, page: 1, pageSize: 24, totalCount: items.length, totalPages: items.length ? 1 : 0 }
}

function renderCatalog(canDeleteTitles = false) {
  return render(<IgnoredMoviesProvider accountKey="issuer:user">
    <FavoriteProvider accountKey="issuer:user">
      <ImdbTitlesCatalogView canDeleteTitles={canDeleteTitles} hasActiveAccount />
    </FavoriteProvider>
  </IgnoredMoviesProvider>)
}

describe('IMDb titles catalog view', () => {
  beforeEach(() => {
    vi.mocked(getImdbTitles).mockReset().mockResolvedValue(page([episode, untyped, pending]))
    vi.mocked(getMovieAwards).mockReset().mockResolvedValue([award])
    vi.mocked(getFavorites).mockReset().mockResolvedValue(personalPage([favorite]))
    vi.mocked(getIgnoredMovies).mockReset().mockResolvedValue(personalPage([ignored]))
    vi.mocked(addFavorite).mockReset().mockResolvedValue(favorite)
    vi.mocked(updateFavorite).mockReset().mockResolvedValue(favorite)
    vi.mocked(removeFavorite).mockReset().mockResolvedValue(undefined)
    vi.mocked(addIgnoredMovie).mockReset().mockResolvedValue(ignored)
    vi.mocked(removeIgnoredMovie).mockReset().mockResolvedValue(undefined)
    vi.mocked(addPersonalRating).mockReset().mockResolvedValue(undefined)
    vi.mocked(deleteImdbTitle).mockReset().mockResolvedValue({ unlinkedTorrentTitles: 1 })
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.resetAllMocks()
  })

  it('renders open media types, all personal states, compact awards, and only eligible rating actions', async () => {
    renderCatalog()

    expect(await screen.findByRole('heading', { name: 'IMDb titles' })).toBeTruthy()
    expect(screen.getByText('3 titles')).toBeTruthy()
    expect(screen.getByRole('slider', { name: 'Number of votes minimum' })).toBeTruthy()
    expect(screen.getByRole('slider', { name: 'Number of votes maximum' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Refresh results' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Add' }).hasAttribute('disabled')).toBe(false)
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeTruthy()
    expect(await screen.findByRole('button', { name: 'View Episode file details' })).toBeTruthy()
    expect(screen.getByLabelText('Personal state for Episode file').textContent).toContain('Favorite')
    expect(screen.getByLabelText('Personal state for Episode file').textContent).toContain('Ignored')
    expect(screen.getByLabelText('Personal state for Episode file').textContent).toContain('Your rating 8/10')
    expect(screen.getByRole('button', { name: 'Rate Untyped file' })).toBeTruthy()
    expect(screen.getAllByRole('button', { name: 'Rate Untyped file' })).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Rate Pending title' })).toBeNull()
    expect(screen.getAllByRole('button', { name: 'Ignore title' })).toHaveLength(2)
    expect(screen.queryByText('No personal state')).toBeNull()
    expect(screen.queryByText('No linked nominations')).toBeNull()
    expect(screen.queryByText('Enriched')).toBeNull()
    expect(screen.getAllByText('1,500 votes')).toHaveLength(3)
    const awardSummary = (await screen.findAllByLabelText('Awards and nominations'))[0]
    expect(await within(awardSummary!).findByText('Oscars 0 wins / 1 nomination')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(await screen.findByRole('row', { name: /Episode file/ })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'View Episode file details' }))
    expect(await screen.findByText('Best Picture')).toBeTruthy()
    expect(screen.getAllByText('Untyped file').length).toBeGreaterThan(0)
  })

  it('opens the manual IMDb picker and reloads after adding a confirmed ID', async () => {
    vi.mocked(lookupManualImdbTitleCandidate).mockResolvedValue({
      status: 'found',
      candidates: [{ title: 'Quiet River', year: 2004, imdbId: 'tt12345678', sourceType: 'movie' }],
      totalResults: 1,
      errorCode: null,
    })
    vi.mocked(addManualImdbTitle).mockResolvedValue({ imdbId: 'tt12345678', title: 'Quiet River', year: 2004, mediaType: 'movie' })
    renderCatalog()
    await screen.findByRole('button', { name: 'View Episode file details' })
    const requestCount = vi.mocked(getImdbTitles).mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(await screen.findByRole('heading', { name: 'Add to IMDb titles' })).toBeTruthy()
    fireEvent.click(screen.getByRole('tab', { name: 'IMDb ID' }))
    fireEvent.change(screen.getByLabelText('IMDb ID'), { target: { value: 'tt12345678' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load IMDb info' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Add to IMDb titles' }))

    await waitFor(() => expect(addManualImdbTitle).toHaveBeenCalledWith('tt12345678'))
    await waitFor(() => expect(getImdbTitles).toHaveBeenCalledTimes(requestCount + 1))
    expect(screen.queryByRole('heading', { name: 'Add to IMDb titles' })).toBeNull()
  })

  it('confirms IMDb deletion and reports that its torrent title is no longer enriched', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValue(true)
    renderCatalog(true)
    expect(await screen.findByRole('button', { name: 'View Untyped file details' })).toBeTruthy()

    const posterActions = screen.getByRole('button', { name: 'View Untyped file details' }).closest('.movie-tile')?.querySelector('.poster-overlay-actions')
    expect(posterActions).not.toBeNull()
    const actionLabels = within(posterActions as HTMLElement).getAllByRole('button').map((button) => button.getAttribute('aria-label'))
    expect(actionLabels).toEqual(['Add to favorites', 'Rate Untyped file', 'Ignore title', 'Delete Untyped file'])

    const requestCount = vi.mocked(getImdbTitles).mock.calls.length
    fireEvent.click(screen.getByRole('button', { name: 'Delete Untyped file' }))
    expect(confirm).toHaveBeenCalledWith('Delete Untyped file from IMDb titles? A linked torrent title will remain in the catalog without IMDb metadata.')
    expect(deleteImdbTitle).not.toHaveBeenCalled()
    expect(getImdbTitles).toHaveBeenCalledTimes(requestCount)

    fireEvent.click(screen.getByRole('button', { name: 'Delete Untyped file' }))
    await waitFor(() => expect(deleteImdbTitle).toHaveBeenCalledWith(untyped.imdbId))
    expect(await screen.findByText('Deleted Untyped file from IMDb titles. Its linked torrent title is now unmatched (not enriched).')).toBeTruthy()
    await waitFor(() => expect(getImdbTitles).toHaveBeenCalledTimes(requestCount + 1))
  })

  it('shows the delete action in table view for administrators only', async () => {
    renderCatalog()
    await screen.findByRole('button', { name: 'View Untyped file details' })
    expect(screen.queryByRole('button', { name: 'Delete Untyped file' })).toBeNull()

    cleanup()
    renderCatalog(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Table' }))
    const row = await screen.findByRole('row', { name: /Untyped file/ })
    expect(within(row).getByRole('button', { name: 'Delete Untyped file' })).toBeTruthy()
  })

  it('sends visibility, sort, and explicit empty-genre filters to the server and resets the page', async () => {
    vi.mocked(getImdbTitles).mockImplementation(async (query) => page([episode, untyped, pending], query.page, 30))
    renderCatalog()
    expect(await screen.findByRole('button', { name: 'View Episode file details' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })))
    fireEvent.change(screen.getByLabelText('Sort results'), { target: { value: 'rating' } })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, sort: 'rating' })))

    fireEvent.click(screen.getByText(/All genres/))
    fireEvent.click(within(screen.getByText(/All genres/).closest('details')!).getByRole('button', { name: 'Select none' }))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, genreFilter: true, genres: [] })))

    fireEvent.click(screen.getByLabelText('Countries: 0 of 2 selected'))
    expect(screen.getByLabelText('Canada')).toBeTruthy()
    expect(screen.queryByLabelText('United Kingdom')).toBeNull()
    fireEvent.click(screen.getByLabelText('USA'))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, countryFilter: true, countries: ['USA'] })))
    fireEvent.click(within(screen.getByLabelText('USA').closest('details')!).getByRole('button', { name: 'Select none' }))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, countryFilter: true, countries: [] })))

    fireEvent.click(screen.getByLabelText('Show ignored'))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, showIgnored: false, showWatched: true })))
    fireEvent.click(screen.getByLabelText('Show watched / rated'))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, showIgnored: false, showWatched: false })))

    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({
      page: 1,
      sort: 'year',
      showIgnored: true,
      showWatched: true,
      genreFilter: false,
      countryFilter: false,
    })))
  })

  it('submits title, year, type, status, and range-boundary filters', async () => {
    vi.mocked(getImdbTitles).mockImplementation(async (query) => page([episode], query.page))
    renderCatalog()
    expect(await screen.findByRole('button', { name: 'View Episode file details' })).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Media type'), { target: { value: '__untagged__' } })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ mediaTypeIsNull: true })))
    fireEvent.change(screen.getByLabelText('Metadata status'), { target: { value: 'pending' } })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ metadataStatus: 'pending' })))

    const search = screen.getByLabelText('Search titles')
    fireEvent.change(search, { target: { value: '2024' } })
    fireEvent.submit(search.closest('form')!)
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ search: '2024' })))

    const minimumYear = screen.getByRole('slider', { name: 'Release year minimum' })
    fireEvent.change(minimumYear, { target: { value: '2000' } })
    fireEvent.keyUp(minimumYear, { key: 'ArrowRight' })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ yearFrom: 2000 })))

    const maximumRating = screen.getByRole('slider', { name: 'IMDb rating maximum' })
    fireEvent.change(maximumRating, { target: { value: '8' } })
    fireEvent.keyUp(maximumRating, { key: 'ArrowLeft' })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ imdbRatingTo: 8 })))

    const minimumVotes = screen.getByRole('slider', { name: 'Number of votes minimum' })
    fireEvent.change(minimumVotes, { target: { value: '500000' } })
    fireEvent.keyUp(minimumVotes, { key: 'ArrowRight' })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ imdbVotesFrom: 500000 })))

    const maximumVotes = screen.getByRole('slider', { name: 'Number of votes maximum' })
    fireEvent.change(maximumVotes, { target: { value: '900000' } })
    fireEvent.keyUp(maximumVotes, { key: 'ArrowLeft' })
    await waitFor(() => expect(getImdbTitles).toHaveBeenLastCalledWith(expect.objectContaining({ imdbVotesFrom: 500000, imdbVotesTo: 900000 })))
  })

  it('shows loading, error, and empty states', async () => {
    vi.mocked(getImdbTitles).mockReturnValueOnce(new Promise<ImdbTitlesPage>(() => {}))
    const loadingView = renderCatalog()
    expect(screen.getByText('Loading IMDb titles')).toBeTruthy()
    loadingView.unmount()

    vi.mocked(getImdbTitles).mockReset().mockRejectedValueOnce(new Error('Catalog unavailable'))
    const errorView = renderCatalog()
    expect(await screen.findByText('Catalog unavailable')).toBeTruthy()
    errorView.unmount()

    vi.mocked(getImdbTitles).mockReset().mockResolvedValueOnce(page([], 1, 0))
    renderCatalog()
    expect(await screen.findByText('No IMDb titles found')).toBeTruthy()
  })

  it('allows rating only for enriched titles and reloads the personal state after saving', async () => {
    const unrated = { ...untyped, metadataStatus: 'enriched' }
    vi.mocked(getImdbTitles).mockImplementation(async (query) => page(
      vi.mocked(getImdbTitles).mock.calls.length > 1 ? [{ ...unrated, personalRating: 9 }] : [unrated, pending],
      query.page,
    ))
    renderCatalog()

    fireEvent.click(await screen.findByRole('button', { name: 'Rate Untyped file' }))
    fireEvent.change(screen.getByLabelText('Your rating (1-10)'), { target: { value: '9' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))

    await waitFor(() => expect(addPersonalRating).toHaveBeenCalledWith(unrated.imdbId, 9))
    expect(await screen.findByText('Your rating 9/10')).toBeTruthy()
  })

})
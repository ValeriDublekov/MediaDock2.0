import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getBackgroundJob, getGoldenGlobeCategories, getGoldenGlobeFilms, getMovieAwards, setGoldenGlobeImdbId } from '../../api/client'
import type { GoldenGlobeFilm, MovieAwardRecognition, PageResponse } from '../../api/types'
import { GoldenGlobeCatalogView } from './GoldenGlobeCatalogView'

vi.mock('../../api/client', () => ({ getBackgroundJob: vi.fn(), getGoldenGlobeCategories: vi.fn(), getGoldenGlobeFilms: vi.fn(), getMovieAwards: vi.fn().mockResolvedValue([]), setGoldenGlobeImdbId: vi.fn() }))

const film: GoldenGlobeFilm = {
  filmId: '2025:movie:A Film',
  title: 'A Film',
  year: 2025,
  nomineeType: 'movie',
  imdbId: null,
  isImdbIdManual: false,
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
  yearBounds: { minYear: 2024, maxYear: 2025 },
}

describe('GoldenGlobeCatalogView', () => {
  beforeEach(() => {
    vi.mocked(getGoldenGlobeFilms).mockReset().mockResolvedValue(page)
    vi.mocked(getGoldenGlobeCategories).mockReset().mockResolvedValue(['Best Director', 'Best Picture'])
    vi.mocked(getMovieAwards).mockReset().mockResolvedValue([])
    vi.mocked(getBackgroundJob).mockReset()
    vi.mocked(setGoldenGlobeImdbId).mockReset()
  })

  afterEach(() => cleanup())

  it('loads grouped film records and applies the supported filters', async () => {
    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByRole('button', { name: 'A Film' })).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Poster for A Film' }).getAttribute('src')).toBe('https://example.test/a-film.jpg')
    expect(screen.queryByText('Genres unavailable')).toBeNull()
    expect(screen.getByTitle('Ceremony year 2025')).toBeTruthy()
    const awardSummary = within(screen.getByLabelText('Awards and nominations'))
    expect(awardSummary.getByText('Golden Globes')).toBeTruthy()
    expect(awardSummary.getByText('1 win · 1 nomination')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Table' }))
    expect(screen.getByRole('img', { name: 'Poster for A Film' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'View A Film Golden Globes details' })).toBeTruthy()
    expect(within(screen.getByLabelText('Awards and nominations')).getByText('Golden Globes')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: '  A Film  ' } })
    const minimum = screen.getByRole('slider', { name: 'Ceremony year minimum' })
    fireEvent.change(minimum, { target: { value: '2025' } })
    fireEvent.pointerUp(minimum)
    fireEvent.change(screen.getByLabelText('Award result'), { target: { value: 'winner' } })
    fireEvent.change(screen.getByLabelText('OMDb status'), { target: { value: 'not_found' } })
    fireEvent.click(screen.getByLabelText('Categories: All categories (2)'))
    fireEvent.click(screen.getByLabelText('Best Director'))

    await waitFor(() => expect(getGoldenGlobeFilms).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      search: 'A Film',
      yearFrom: 2025,
      categories: ['Best Picture'],
      categoryFilter: true,
      result: 'winner',
      enrichmentStatus: 'not_found',
    }))
  })

  it('applies a touch-released ceremony year range while preserving applied filters', async () => {
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({ ...page, totalPages: 2 })
    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByRole('button', { name: 'A Film' })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: 'A Film' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(getGoldenGlobeFilms).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, search: 'A Film' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(getGoldenGlobeFilms).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, search: 'A Film' }))

    fireEvent.change(screen.getByLabelText('Search films'), { target: { value: 'Unsubmitted search' } })
    const maximum = screen.getByRole('slider', { name: 'Ceremony year maximum' }) as HTMLInputElement
    expect(maximum.min).toBe('2024')
    expect(maximum.max).toBe('2025')
    fireEvent.change(maximum, { target: { value: '2024' } })
    expect(screen.getByText('2024 – 2024')).toBeTruthy()
    expect(getGoldenGlobeFilms).toHaveBeenCalledTimes(3)
    fireEvent.pointerUp(maximum, { pointerType: 'touch' })

    await waitFor(() => expect(getGoldenGlobeFilms).toHaveBeenLastCalledWith({
      page: 1, pageSize: 20, search: 'A Film', yearTo: 2024,
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

  it('shows specific explanations and safe error codes in the film list', async () => {
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({
      ...page,
      items: [
        { ...film, enrichmentStatus: 'problem', enrichmentError: 'no_confident_match' },
        { ...film, filmId: '2025:movie:No Award Match', title: 'No Award Match', enrichmentStatus: 'problem', enrichmentError: 'no_golden_globe_match' },
      ],
      totalCount: 2,
    })

    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByText('OMDb Search returned candidates, but none could be verified safely. Check the title, type, and year, or set the IMDb ID manually.')).toBeTruthy()
    expect(screen.getByText('The closest title did not list a Golden Globe in OMDb Awards. Verify the match or set the IMDb ID manually.')).toBeTruthy()
    expect(screen.getByText('no_confident_match', { selector: 'code' })).toBeTruthy()
    expect(screen.getByText('no_golden_globe_match', { selector: 'code' })).toBeTruthy()
  })

  it('distinguishes movie and series groups with the same title and ceremony year', async () => {
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({
      ...page,
      items: [film, { ...film, filmId: '2025:series:A Film', nomineeType: 'series' }],
      totalCount: 2,
    })

    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByText('movie')).toBeTruthy()
    expect(screen.getByText('series')).toBeTruthy()
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

  it('sets a manual IMDb ID and reports the targeted refresh completion', async () => {
    vi.mocked(setGoldenGlobeImdbId).mockResolvedValue({
      imdbId: 'tt12345678',
      refreshJob: { id: 71, status: 'queued', statusUrl: '/api/background-jobs/71' },
    })
    vi.mocked(getBackgroundJob).mockResolvedValue({
      id: 71,
      jobType: 'golden_globe_manual_refresh',
      trigger: 'manual',
      status: 'succeeded',
      enqueuedAt: '2026-10-07T10:00:00Z',
      startedAt: '2026-10-07T10:00:01Z',
      finishedAt: '2026-10-07T10:00:02Z',
      currentStage: 'succeeded',
      currentSource: null,
      progressUpdatedAt: '2026-10-07T10:00:02Z',
      errorCode: null,
      resultSummary: { outcome: 'enriched', imdbId: 'tt12345678' },
      scanRunId: null,
      inputFileName: null,
    })

    render(<GoldenGlobeCatalogView />)
    fireEvent.click(await screen.findByRole('button', { name: 'View A Film details' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('IMDb ID'), { target: { value: 'TT12345678' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save and refresh' }))

    await waitFor(() => expect(setGoldenGlobeImdbId).toHaveBeenCalledWith('2025:movie:A Film', 'tt12345678'))
    expect(await within(dialog).findByText('Metadata refresh queued.')).toBeTruthy()
    expect(await within(dialog).findByText('Metadata refresh complete.', {}, { timeout: 3000 })).toBeTruthy()
    expect(getBackgroundJob).toHaveBeenCalledWith(71)
  })

  it('confirms before replacing an existing manual IMDb link', async () => {
    const linkedFilm = { ...film, imdbId: 'tt12345678', isImdbIdManual: true }
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({ ...page, items: [linkedFilm] })
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    render(<GoldenGlobeCatalogView />)
    fireEvent.click(await screen.findByRole('button', { name: 'View A Film details' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('IMDb ID'), { target: { value: 'tt87654321' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save and refresh' }))

    expect(confirm).toHaveBeenCalledWith('Replace the linked IMDb ID? Metadata linked to the current ID will be replaced.')
    expect(setGoldenGlobeImdbId).not.toHaveBeenCalled()
    confirm.mockRestore()
  })

  it('requeues a refresh when retrying the same manual IMDb ID', async () => {
    const linkedFilm = { ...film, imdbId: 'tt12345678', isImdbIdManual: true, enrichmentStatus: 'temporary_error' as const }
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({ ...page, items: [linkedFilm] })
    vi.mocked(setGoldenGlobeImdbId).mockResolvedValue({
      imdbId: 'tt12345678',
      refreshJob: { id: 72, status: 'queued', statusUrl: '/api/background-jobs/72' },
    })
    render(<GoldenGlobeCatalogView />)
    fireEvent.click(await screen.findByRole('button', { name: 'View A Film details' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save and refresh' }))

    await waitFor(() => expect(setGoldenGlobeImdbId).toHaveBeenCalledWith('2025:movie:A Film', 'tt12345678'))
  })

  it('explains not-found and temporary provider errors', async () => {
    vi.mocked(getGoldenGlobeFilms).mockResolvedValue({
      ...page,
      items: [
        { ...film, enrichmentStatus: 'not_found', enrichmentError: 'not_found' },
        { ...film, filmId: '2025:movie:Timeout', title: 'Timeout', enrichmentStatus: 'temporary_error', enrichmentError: 'timeout' },
      ],
    })

    render(<GoldenGlobeCatalogView />)

    expect(await screen.findByText('OMDb did not find a matching title.')).toBeTruthy()
    expect(await screen.findByText('The OMDb request timed out.')).toBeTruthy()
  })
})
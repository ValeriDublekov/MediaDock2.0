import { describe, expect, it } from 'vitest'
import {
  ApiError,
  addFavorite,
  addIgnoredMovie,
  applyCatalogTitleImdbCandidate,
  applyParseLogImdbCandidate,
  enqueueFailedEntryRecheck,
  enqueueGoldenGlobeEnrichment,
  enqueueGoldenGlobeImport,
  enqueueManualScan,
  enqueueOscarImport,
  getActiveBackgroundJob,
  getBackgroundJobEvents,
  getCatalog,
  getCurrentSession,
  getIgnoredMovies,
  getAdminUsers,
  decideRegistrationRequest,
  setAdminUserStatus,
  deleteCatalogTitle,
  deleteOldOperationalHistory,
  getDeploymentStatus,
  getGoldenGlobeCategories,
  getGoldenGlobeFilms,
  setGoldenGlobeImdbId,
  getMovieAwards,
  getMovieTablesDashboard,
  getOscarFilm,
  getOscarCategories,
  getOscarFilms,
  getRecommendations,
  getRecommendationGenres,
  markRecommendationWatched,
  getRecommendationSettings,
  getPersonalRatings,
  getPersonalRatingsEnrichmentJob,
  getProviderSettings,
  lookupCatalogTitleImdbCandidate,
  lookupParseLogImdbCandidate,
  enqueuePersonalRatingsEnrichment,
  importPersonalRatings,
  requestJson,
  requestRegistration,
  removeIgnoredMovie,
  removePersonalRating,
  recalculateRecommendationVoteWeights,
  searchCatalogTitleImdbCandidates,
  searchParseLogImdbCandidates,
  runDeploymentAction,
  updateRecommendationSettings,
  updateProviderSettings,
  updateFavorite,
} from './client'
import type { CatalogTitle, GoldenGlobeFilm, Recommendation, OscarFilm, PageResponse, PersonalRating, ProviderSettings, ProviderSettingsInput, RecommendationSettings, RecommendationSettingsInput, VoteWeightRecalculationResponse } from './types'

function response(status: number, value: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => value,
    text: async () => value === undefined || value === null ? '' : JSON.stringify(value),
  } as Response
}

function fetchStub(result: Response | Error) {
  const calls: Array<{ input: RequestInfo | URL; init?: RequestInit }> = []
  const fetcher: typeof fetch = async (input, init) => {
    calls.push({ input, init })
    if (result instanceof Error) throw result
    return result
  }
  return { fetcher, calls }
}

describe('typed API client', () => {
  it('deletes a catalog title with the selected IMDb metadata option', async () => {
    const stub = fetchStub(response(200, { imdbInfoDeleted: true }))

    await deleteCatalogTitle(7, true, stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/titles/7?deleteImdbInfo=true')
    expect(stub.calls[0]?.init?.method).toBe('DELETE')
  })

  it('deletes operational history using the selected retention period', async () => {
    const stub = fetchStub(response(200, {
      olderThanDays: 90,
      cutoff: '2026-07-23T12:00:00Z',
      deletedScanRuns: 2,
      deletedParseLogs: 14,
    }))

    await deleteOldOperationalHistory(90, stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/operation-history?olderThanDays=90')
    expect(stub.calls[0]?.init?.method).toBe('DELETE')
  })

  it('searches and applies explicit IMDb candidates from catalog and parse history', async () => {
    const searchResponse = {
      status: 'found' as const,
      candidates: [{ title: 'Quiet River', year: 2004, imdbId: 'tt12345678', sourceType: 'movie' }],
      totalResults: 1,
      errorCode: null,
    }
    const stub = fetchStub(response(200, searchResponse))

    await searchCatalogTitleImdbCandidates(7, 'Quiet River', 2, stub.fetcher)
    await lookupCatalogTitleImdbCandidate(7, 'tt12345678', stub.fetcher)
    await applyCatalogTitleImdbCandidate(7, 'tt12345678', stub.fetcher)
    await searchParseLogImdbCandidates(42, 'Quiet River', 1, stub.fetcher)
    await lookupParseLogImdbCandidate(42, 'tt12345678', stub.fetcher)
    await applyParseLogImdbCandidate(42, 'tt12345678', stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/titles/7/imdb-candidates?page=2')
    expect(stub.calls[0]?.init?.method).toBe('POST')
    expect(JSON.parse(String(stub.calls[0]?.init?.body))).toEqual({ title: 'Quiet River' })
    expect(String(stub.calls[1]?.input)).toBe('/api/titles/7/imdb-candidate')
    expect(JSON.parse(String(stub.calls[1]?.init?.body))).toEqual({ imdbId: 'tt12345678' })
    expect(String(stub.calls[2]?.input)).toBe('/api/titles/7/imdb-match')
    expect(String(stub.calls[3]?.input)).toBe('/api/parse-logs/42/imdb-candidates?page=1')
    expect(JSON.parse(String(stub.calls[3]?.init?.body))).toEqual({ title: 'Quiet River' })
    expect(String(stub.calls[4]?.input)).toBe('/api/parse-logs/42/imdb-candidate')
    expect(String(stub.calls[5]?.input)).toBe('/api/parse-logs/42/imdb-match')
  })

  it('uses IMDb IDs for favorite create and marker requests', async () => {
    const stub = fetchStub(response(200, {}))

    await addFavorite('tt12345678', 'oscar', stub.fetcher)
    await updateFavorite('tt12345678', { toDownload: true }, stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/favorites')
    expect(JSON.parse(String(stub.calls[0]?.init?.body))).toEqual({ imdbId: 'tt12345678', from: 'oscar' })
    expect(String(stub.calls[1]?.input)).toBe('/api/favorites/tt12345678')
  })

  it('lists, adds, and removes ignored movies by IMDb ID', async () => {
    const stub = fetchStub(response(200, {}))

    await getIgnoredMovies({ page: 2, pageSize: 100 }, stub.fetcher)
    await addIgnoredMovie('tt12345678', stub.fetcher)
    await removeIgnoredMovie('tt12345678', stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/ignored-movies?page=2&pageSize=100')
    expect(String(stub.calls[1]?.input)).toBe('/api/ignored-movies')
    expect(stub.calls[1]?.init?.method).toBe('POST')
    expect(JSON.parse(String(stub.calls[1]?.init?.body))).toEqual({ imdbId: 'tt12345678' })
    expect(String(stub.calls[2]?.input)).toBe('/api/ignored-movies/tt12345678')
    expect(stub.calls[2]?.init?.method).toBe('DELETE')
  })

  it('serializes IMDb IDs for the bounded combined-awards lookup', async () => {
    const stub = fetchStub(response(200, []))

    await getMovieAwards(['tt1234567', 'tt7654321'], stub.fetcher)

    const requestUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(requestUrl.pathname).toBe('/api/movie-awards')
    expect(requestUrl.searchParams.get('imdbIds')).toBe('tt1234567,tt7654321')
  })

  it('serializes pagination and catalog filters into the Step 6 query contract', async () => {
    const page: PageResponse<CatalogTitle> = { items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 }
    const stub = fetchStub(response(200, page))

    await getCatalog({ page: 2, pageSize: 20, feedTypes: ['series_complete', 'series_ongoing'], search: 'quiet river', mediaType: 'series', yearFrom: 1998, imdbRatingFrom: 6.5, imdbRatingTo: 8.4, genre: 'drama' }, stub.fetcher)

    const requestUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(requestUrl.pathname).toBe('/api/catalog')
    expect(requestUrl.searchParams.get('page')).toBe('2')
    expect(requestUrl.searchParams.get('pageSize')).toBe('20')
    expect(requestUrl.searchParams.get('feedTypes')).toBe('series_complete,series_ongoing')
    expect(requestUrl.searchParams.get('search')).toBe('quiet river')
    expect(requestUrl.searchParams.get('mediaType')).toBe('series')
    expect(requestUrl.searchParams.get('yearFrom')).toBe('1998')
    expect(requestUrl.searchParams.get('imdbRatingFrom')).toBe('6.5')
    expect(requestUrl.searchParams.get('imdbRatingTo')).toBe('8.4')
    expect(requestUrl.searchParams.get('genre')).toBe('drama')
  })

  it('serializes personal ratings pagination and rating sort', async () => {
    const page: PageResponse<PersonalRating> = { items: [], page: 2, pageSize: 25, totalCount: 26, totalPages: 2 }
    const stub = fetchStub(response(200, page))

    await getPersonalRatings({ page: 2, pageSize: 25, sort: 'asc' }, stub.fetcher)

    const requestUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(requestUrl.pathname).toBe('/api/personal-ratings')
    expect(requestUrl.searchParams.get('page')).toBe('2')
    expect(requestUrl.searchParams.get('pageSize')).toBe('25')
    expect(requestUrl.searchParams.get('sort')).toBe('asc')

    const dateStub = fetchStub(response(200, page))
    await getPersonalRatings({ page: 1, pageSize: 25, sort: 'vote_date_desc', manuallyAddedOnly: true }, dateStub.fetcher)
    const dateRequestUrl = new URL(String(dateStub.calls[0]?.input), 'http://localhost')
    expect(dateRequestUrl.searchParams.get('sort')).toBe('vote_date_desc')
    expect(dateRequestUrl.searchParams.get('manuallyAddedOnly')).toBe('true')
  })

  it('removes a personal rating by IMDb ID', async () => {
    const stub = fetchStub(response(204, undefined))

    await removePersonalRating('tt12345678', stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/personal-ratings/tt12345678')
    expect(stub.calls[0]?.init?.method).toBe('DELETE')
  })

  it('loads and updates global recommendation settings', async () => {
    const settings: RecommendationSettings = {
      medianPeriodYears: 5,
      bayesianDefaultC: 5.5,
      bayesianMRatio: 0.25,
      oscarWinPoints: 3,
      oscarNomPoints: 1,
      ggWinPoints: 2,
      ggNomPoints: 0.5,
      requiresVoteRecalculation: true,
      lastVoteRecalculationAt: null,
      lastVoteRecalculationPeriodYears: null,
      awardWeights: [{ awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 3, nominationPoints: 1, isOverride: false }],
    }
    const input: RecommendationSettingsInput = {
      medianPeriodYears: 7,
      bayesianDefaultC: 6,
      bayesianMRatio: 0.5,
      oscarWinPoints: 4,
      oscarNomPoints: 1.5,
      ggWinPoints: 2,
      ggNomPoints: 0.5,
      awardWeights: [{ awardSource: 'oscar', categoryKey: 'best_picture', winPoints: 4, nominationPoints: 1.5, isOverride: true }],
    }
    const stub = fetchStub(response(200, settings))

    await getRecommendationSettings(stub.fetcher)
    await updateRecommendationSettings(input, stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/settings/recommendations')
    expect(String(stub.calls[1]?.input)).toBe('/api/settings/recommendations')
    expect(stub.calls[1]?.init?.method).toBe('PUT')
    expect(JSON.parse(String(stub.calls[1]?.init?.body))).toEqual(input)
  })

  it('supports synchronous and accepted vote-weight recalculation responses', async () => {
    const completed: VoteWeightRecalculationResponse = {
      calculatedAt: '2026-10-22T10:00:00Z',
      durationMilliseconds: 850,
      medianPeriodYears: 5,
      movieCount: 100,
      moviesWithVotesCount: 92,
      periodCount: 8,
      fallbackMedianCount: 2,
      missingVoteCount: 8,
    }
    const completedStub = fetchStub(response(200, completed))
    const acceptedStub = fetchStub(response(202, { id: 17, status: 'queued', statusUrl: '/api/background-jobs/17' }))

    expect(await recalculateRecommendationVoteWeights(completedStub.fetcher)).toEqual(completed)
    expect(await recalculateRecommendationVoteWeights(acceptedStub.fetcher)).toEqual({
      id: 17,
      status: 'queued',
      statusUrl: '/api/background-jobs/17',
    })
    expect(String(completedStub.calls[0]?.input)).toBe('/api/settings/recommendations/recalculate-votes')
    expect(completedStub.calls[0]?.init?.method).toBe('POST')
    expect(String(acceptedStub.calls[0]?.input)).toBe('/api/settings/recommendations/recalculate-votes')
  })

  it('sends required recommendation coefficients with the requested page', async () => {
    const item: Recommendation = {
      imdbId: 'tt12345678',
      title: 'Quiet River',
      year: 2004,
      mediaType: 'movie',
      isWatched: false,
      imdbRating: 8.1,
      imdbVotes: 2500,
      posterUrl: null,
      genres: ['Drama'],
      ratingComponent: 8.0,
      popularityComponent: 7.2,
      awardComponent: 3.5,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      finalScore: 6.675,
      awardEvents: [],
    }
    const page: PageResponse<Recommendation> = { items: [item], page: 2, pageSize: 25, totalCount: 26, totalPages: 2 }
    const stub = fetchStub(response(200, page))

    await getRecommendations({
      mediaType: 'series',
      page: 2,
      pageSize: 25,
      ratingWeight: 0.5,
      votesWeight: 0.25,
      awardsWeight: 0.25,
      yearFrom: 2000,
      yearTo: 2020,
      genres: ['Drama', 'Comedy'],
    }, stub.fetcher)

    const requestUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(requestUrl.pathname).toBe('/api/recommendations')
    expect(requestUrl.searchParams.get('page')).toBe('2')
    expect(requestUrl.searchParams.get('pageSize')).toBe('25')
    expect(requestUrl.searchParams.get('ratingWeight')).toBe('0.5')
    expect(requestUrl.searchParams.get('votesWeight')).toBe('0.25')
    expect(requestUrl.searchParams.get('awardsWeight')).toBe('0.25')
    expect(requestUrl.searchParams.get('mediaType')).toBe('series')
    expect(requestUrl.searchParams.get('yearFrom')).toBe('2000')
    expect(requestUrl.searchParams.get('yearTo')).toBe('2020')
    expect(requestUrl.searchParams.getAll('genres')).toEqual(['Drama', 'Comedy'])
  })

  it('loads IMDb genres for recommendation filters', async () => {
    const stub = fetchStub(response(200, ['Comedy', 'Drama']))

    await getRecommendationGenres('series', stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/recommendations/genres?mediaType=series')
  })

  it('includes the recommendation media type when marking a title watched', async () => {
    const calls: Array<{ input: RequestInfo | URL; init?: RequestInit }> = []
    const fetcher: typeof fetch = async (input, init) => {
      calls.push({ input, init })
      return String(input) === '/api/auth/antiforgery'
        ? response(200, { requestToken: 'recommendation-csrf' })
        : response(204, undefined)
    }

    await markRecommendationWatched('tt12345678', 8, 'series', fetcher)

    expect(String(calls[1]?.input)).toBe('/api/recommendations/tt12345678/watched')
    expect(JSON.parse(String(calls[1]?.init?.body))).toEqual({ rating: 8, mediaType: 'series' })
  })

  it('protects personal ratings enrichment enqueue with an antiforgery token', async () => {
    const calls: Array<{ input: RequestInfo | URL; init?: RequestInit }> = []
    const fetcher: typeof fetch = async (input, init) => {
      calls.push({ input, init })
      return String(input) === '/api/auth/antiforgery'
        ? response(200, { requestToken: 'ratings-enrichment-csrf' })
        : response(202, { jobId: 12, status: 'queued' })
    }

    await enqueuePersonalRatingsEnrichment(fetcher)

    expect(String(calls[0]?.input)).toBe('/api/auth/antiforgery')
    expect(String(calls[1]?.input)).toBe('/api/personal-ratings/enrichment')
    expect(calls[1]?.init?.method).toBe('POST')
    expect(new Headers(calls[1]?.init?.headers).get('RequestVerificationToken')).toBe('ratings-enrichment-csrf')
  })

  it('loads personal ratings enrichment status from its owner-scoped route', async () => {
    const stub = fetchStub(response(200, { id: 12, jobType: 'imdb_title_enrichment', status: 'running' }))

    await getPersonalRatingsEnrichmentJob(12, stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/personal-ratings/enrichment/12')
  })

  it('serializes Oscar filters and requests one film with its nomination details', async () => {
    const page: PageResponse<OscarFilm> = { items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 }
    const stub = fetchStub(response(200, page))

    await getOscarFilms({
      page: 2,
      pageSize: 20,
      search: 'Oppenheimer',
      yearFrom: 2022,
      yearTo: 2024,
      imdbRatingFrom: 7.1,
      imdbRatingTo: 8.2,
      categories: ['BEST PICTURE', 'DIRECTING'],
      categoryFilter: true,
      result: 'winner',
      enrichmentStatus: 'pending',
    }, stub.fetcher)
    await getOscarFilm(42, stub.fetcher)

    const listUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(listUrl.pathname).toBe('/api/oscars')
    expect(listUrl.searchParams.get('yearFrom')).toBe('2022')
    expect(listUrl.searchParams.get('yearTo')).toBe('2024')
    expect(listUrl.searchParams.get('imdbRatingFrom')).toBe('7.1')
    expect(listUrl.searchParams.get('imdbRatingTo')).toBe('8.2')
    expect(listUrl.searchParams.getAll('categories')).toEqual(['BEST PICTURE', 'DIRECTING'])
    expect(listUrl.searchParams.get('categoryFilter')).toBe('true')
    expect(listUrl.searchParams.get('result')).toBe('winner')
    expect(listUrl.searchParams.get('enrichmentStatus')).toBe('pending')
    expect(String(stub.calls[1]?.input)).toBe('/api/oscars/42')

    await getOscarCategories(stub.fetcher)
    expect(String(stub.calls[2]?.input)).toBe('/api/oscars/categories')
  })

  it('serializes Golden Globes catalog filters and queues import and enrichment jobs', async () => {
    const page: PageResponse<GoldenGlobeFilm> = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
    const stub = fetchStub(response(202, page))
    const file = new File(['dataset'], 'golden-globes.csv', { type: 'text/csv' })

    await getGoldenGlobeFilms({ page: 1, pageSize: 20, search: 'A Film', yearFrom: 2025, imdbRatingFrom: 7.4, imdbRatingTo: 8.1, categories: ['Best Picture', 'Best Director'], categoryFilter: true, result: 'winner' }, stub.fetcher)
    await enqueueGoldenGlobeEnrichment(stub.fetcher)
    await enqueueGoldenGlobeImport(file, 1980, stub.fetcher)

    const catalogUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(catalogUrl.pathname).toBe('/api/golden-globes')
    expect(catalogUrl.searchParams.get('yearFrom')).toBe('2025')
    expect(catalogUrl.searchParams.get('imdbRatingFrom')).toBe('7.4')
    expect(catalogUrl.searchParams.get('imdbRatingTo')).toBe('8.1')
    expect(catalogUrl.searchParams.getAll('categories')).toEqual(['Best Picture', 'Best Director'])
    expect(catalogUrl.searchParams.get('categoryFilter')).toBe('true')
    expect(catalogUrl.searchParams.get('result')).toBe('winner')
    expect(String(stub.calls[1]?.input)).toBe('/api/background-jobs/golden-globe-enrichment')
    expect(String(stub.calls[2]?.input)).toBe('/api/background-jobs/golden-globe-import')
    const formData = stub.calls[2]?.init?.body as FormData
    expect(formData.get('File')).toBe(file)
    expect(formData.get('YearAfter')).toBe('1980')

    await getGoldenGlobeCategories(stub.fetcher)
    expect(String(stub.calls[3]?.input)).toBe('/api/golden-globes/categories')
  })

  it('preserves an explicitly empty award category selection', async () => {
    const stub = fetchStub(response(200, { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }))

    await getOscarFilms({ page: 1, pageSize: 20, categories: [], categoryFilter: true }, stub.fetcher)

    const requestUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(requestUrl.searchParams.get('categoryFilter')).toBe('true')
    expect(requestUrl.searchParams.getAll('categories')).toEqual([])
  })

  it('sends manual Golden Globes IMDb link changes as a JSON mutation', async () => {
    const result = {
      imdbId: 'tt12345678',
      refreshJob: { id: 55, status: 'queued', statusUrl: '/api/background-jobs/55' },
    }
    const stub = fetchStub(response(200, result))

    await setGoldenGlobeImdbId('2025:movie:A Film', 'tt12345678', stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/golden-globes/imdb-link')
    expect(stub.calls[0]?.init?.method).toBe('PUT')
    expect(JSON.parse(String(stub.calls[0]?.init?.body))).toEqual({ filmId: '2025:movie:A Film', imdbId: 'tt12345678' })
  })

  it('loads provider settings and sends a write-only key update', async () => {
    const providerSettings: ProviderSettings = {
      omdbApiKeyConfigured: true,
      omdbDailyRequestLimit: 25,
      updatedAt: null,
    }
    const input: ProviderSettingsInput = {
      omdbApiKey: 'new-key-value',
      clearOmdbApiKey: false,
      omdbDailyRequestLimit: 25,
    }
    const stub = fetchStub(response(200, providerSettings))

    await getProviderSettings(stub.fetcher)
    await updateProviderSettings(input, stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/settings/providers/omdb')
    expect(stub.calls[1]?.init?.method).toBe('PUT')
    expect(JSON.parse(String(stub.calls[1]?.init?.body))).toEqual(input)
  })

  it('loads the auth session and protects registration submission with an antiforgery token', async () => {
    const session = {
      authenticated: false,
      identity: null,
      user: null,
      accountState: 'anonymous',
      signInEnabled: true,
      authorizationEnforced: false,
      registrationRequest: null,
    }
    const stub = fetchStub(response(200, session))
    const tokenStub = fetchStub(response(200, { requestToken: 'csrf-token' }))
    const registrationStub = fetchStub(response(200, { status: 'pending', requestedAt: '2026-10-06T10:00:00Z', decidedAt: null }))
    const fetcher: typeof fetch = async (input, init) => {
      const path = String(input)
      if (path === '/api/auth/session') return stub.fetcher(input, init)
      if (path === '/api/auth/antiforgery') return tokenStub.fetcher(input, init)
      return registrationStub.fetcher(input, init)
    }

    await getCurrentSession(fetcher)
    await requestRegistration(fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/auth/session')
    expect(String(tokenStub.calls[0]?.input)).toBe('/api/auth/antiforgery')
    expect(String(registrationStub.calls[0]?.input)).toBe('/api/auth/registration-requests')
    expect(registrationStub.calls[0]?.init?.method).toBe('POST')
    expect(new Headers(registrationStub.calls[0]?.init?.headers).get('RequestVerificationToken')).toBe('csrf-token')
  })

  it('filters admin users and protects account decisions with antiforgery tokens', async () => {
    const calls: Array<{ input: RequestInfo | URL; init?: RequestInit }> = []
    const fetcher: typeof fetch = async (input, init) => {
      calls.push({ input, init })
      if (String(input) === '/api/auth/antiforgery') return response(200, { requestToken: 'admin-csrf' })
      if (init?.method === 'GET' || !init?.method) return response(200, { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 })
      return response(204, undefined)
    }

    await getAdminUsers({ state: 'requested', search: 'new@example.com', page: 2, pageSize: 50 }, fetcher)
    await decideRegistrationRequest(17, 'approve', fetcher)
    await setAdminUserStatus(21, 'deactivated', fetcher)

    const usersUrl = new URL(String(calls[0]?.input), 'http://localhost')
    expect(usersUrl.pathname).toBe('/api/admin/users')
    expect(usersUrl.searchParams.get('state')).toBe('requested')
    expect(usersUrl.searchParams.get('search')).toBe('new@example.com')
    expect(usersUrl.searchParams.get('page')).toBe('2')
    expect(String(calls[2]?.input)).toBe('/api/admin/registration-requests/17/decision')
    expect(calls[2]?.init?.method).toBe('POST')
    expect(JSON.parse(String(calls[2]?.init?.body))).toEqual({ decision: 'approve' })
    expect(new Headers(calls[2]?.init?.headers).get('RequestVerificationToken')).toBe('admin-csrf')
    expect(String(calls[4]?.input)).toBe('/api/admin/users/21/status')
    expect(calls[4]?.init?.method).toBe('PUT')
    expect(JSON.parse(String(calls[4]?.init?.body))).toEqual({ status: 'deactivated' })
  })

  it('queues a scan and fetches job events with a cursor', async () => {
    const queued = { id: 7, status: 'queued', statusUrl: '/api/background-jobs/7' }
    const stub = fetchStub(response(202, queued))

    await enqueueManualScan(stub.fetcher)
    await getBackgroundJobEvents(7, { afterId: 12, pageSize: 25 }, stub.fetcher)

    expect(stub.calls[0]?.init?.method).toBe('POST')
    const eventsUrl = new URL(String(stub.calls[1]?.input), 'http://localhost')
    expect(eventsUrl.pathname).toBe('/api/background-jobs/7/events')
    expect(eventsUrl.searchParams.get('afterId')).toBe('12')
    expect(eventsUrl.searchParams.get('pageSize')).toBe('25')
  })

  it('queues a failed torrent recheck', async () => {
    const stub = fetchStub(response(202, { id: 12, status: 'queued', statusUrl: '/api/background-jobs/12' }))

    await enqueueFailedEntryRecheck(stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/background-jobs/recheck-failed')
    expect(stub.calls[0]?.init?.method).toBe('POST')
  })

  it('loads deployment status and sends only the selected deployment action', async () => {
    const status = { activeState: 'inactive', deployedSha: 'a'.repeat(40) }
    const stub = fetchStub(response(200, status))

    await getDeploymentStatus(stub.fetcher)
    await runDeploymentAction('retry_failed_gate', stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/deployment')
    expect(String(stub.calls[1]?.input)).toBe('/api/deployment/run')
    expect(stub.calls[1]?.init?.method).toBe('POST')
    expect(JSON.parse(String(stub.calls[1]?.init?.body))).toEqual({ action: 'retry_failed_gate' })
  })

  it('sends Oscar files as multipart without setting a boundary-less content type', async () => {
    const stub = fetchStub(response(202, { id: 9, status: 'queued', statusUrl: '/api/background-jobs/9' }))
    const file = new File(['csv contents'], 'awards.csv', { type: 'text/csv' })

    await enqueueOscarImport(file, 1980, stub.fetcher)

    const init = stub.calls[0]?.init
    const body = init?.body
    expect(init?.method).toBe('POST')
    expect(body).toBeInstanceOf(FormData)
    expect(new Headers(init?.headers).has('Content-Type')).toBe(false)
    expect((body as FormData).get('YearAfter')).toBe('1980')
    expect((body as FormData).get('File')).toBe(file)
  })

  it('sends one personal ratings JSON file as multipart', async () => {
    const tokenStub = fetchStub(response(200, { requestToken: 'ratings-csrf' }))
    const importStub = fetchStub(response(200, {
      ratingsInFile: 1,
      added: 1,
      updated: 0,
      unchanged: 0,
      totalRatings: 1,
      importedAt: '2026-10-02T10:00:00Z',
      errors: [],
    }))
    const fetcher: typeof fetch = async (input, init) =>
      String(input) === '/api/auth/antiforgery'
        ? tokenStub.fetcher(input, init)
        : importStub.fetcher(input, init)
    const file = new File(['[{"id":"tt14452776","rating":8}]'], 'ratings.json', { type: 'application/json' })

    await importPersonalRatings(file, fetcher)

    const init = importStub.calls[0]?.init
    const body = init?.body
    expect(String(tokenStub.calls[0]?.input)).toBe('/api/auth/antiforgery')
    expect(String(importStub.calls[0]?.input)).toBe('/api/personal-ratings/import')
    expect(init?.method).toBe('POST')
    expect(body).toBeInstanceOf(FormData)
    expect(new Headers(init?.headers).has('Content-Type')).toBe(false)
    expect(new Headers(init?.headers).get('RequestVerificationToken')).toBe('ratings-csrf')
    expect((body as FormData).get('File')).toBe(file)
  })

  it('treats an empty active-job response as no active job', async () => {
    const stub = fetchStub(response(204, undefined))
    await expect(getActiveBackgroundJob(stub.fetcher)).resolves.toBeNull()
  })

  it('surfaces ProblemDetails validation errors with their HTTP status', async () => {
    const stub = fetchStub(response(400, {
      title: 'One or more validation errors occurred.',
      errors: { PageSize: ['The field PageSize must be between 1 and 100.'] },
    }))

    await expect(requestJson('/catalog?pageSize=500', {}, stub.fetcher)).rejects.toMatchObject({
      name: 'ApiError',
      status: 400,
      message: 'The field PageSize must be between 1 and 100.',
    })
  })

  it('preserves the endpoint and server trace ID for authentication diagnostics', async () => {
    const fetcher: typeof fetch = async () => new Response(JSON.stringify({ title: 'Unauthorized' }), {
      status: 401,
      headers: { 'Content-Type': 'application/problem+json', 'X-MediaDock-TraceId': 'trace-auth-123' },
    })

    await expect(requestJson('/auth/session', {}, fetcher)).rejects.toMatchObject({
      name: 'ApiError',
      status: 401,
      endpoint: '/api/auth/session',
      traceId: 'trace-auth-123',
    })
  })

  it('reports the HTTP status when an auth error has an empty response body', async () => {
    const fetcher: typeof fetch = async () => new Response(null, { status: 401 })

    await expect(requestJson('/auth/session', {}, fetcher)).rejects.toMatchObject({
      name: 'ApiError',
      status: 401,
      message: 'The API request failed with status 401.',
    })
  })

  it('reports a retryable connection error when the API is unavailable', async () => {
    const stub = fetchStub(new TypeError('offline'))

    await expect(requestJson('/catalog', {}, stub.fetcher)).rejects.toMatchObject({
      name: 'ApiError',
      status: 0,
      message: 'Could not reach the API. Check the server connection and retry.',
    } satisfies Partial<ApiError>)
  })

  it('fetches movie tables dashboard statistics from the admin endpoint', async () => {
    const payload = {
      imdb: {
        totalCount: 100,
        byMediaType: { movie: 80, series: 20 },
        byMetadataStatus: { enriched: 95, pending: 4, temporary_error: 1 },
        enrichedCount: 95,
        pendingCount: 4,
      },
      goldenGlobes: {
        totalCount: 50,
        byNomineeType: { movie: 40, series: 10 },
        byEnrichmentStatus: { enriched: 50 },
        withImdbLinkCount: 48,
        withoutImdbLinkCount: 2,
        totalNominationsCount: 120,
      },
      oscar: {
        totalCount: 30,
        byEnrichmentStatus: { enriched: 30 },
        withImdbLinkCount: 30,
        withoutImdbLinkCount: 0,
        totalNominationsCount: 150,
      },
      generatedAtUtc: '2026-10-24T10:00:00Z',
    }
    const stub = fetchStub(response(200, payload))

    const result = await getMovieTablesDashboard(stub.fetcher)

    expect(String(stub.calls[0]?.input)).toBe('/api/admin/movie-tables-dashboard')
    expect(result.imdb.totalCount).toBe(100)
    expect(result.goldenGlobes.withImdbLinkCount).toBe(48)
    expect(result.oscar.totalNominationsCount).toBe(150)
  })

  it('explains a non-JSON proxy failure without hiding the HTTP status', async () => {
    const stub = fetchStub(response(502, null))

    await expect(requestJson('/oscars', {}, stub.fetcher)).rejects.toMatchObject({
      name: 'ApiError',
      status: 502,
      message: 'The local API is unavailable. Check the server connection and retry.',
    })
  })
})
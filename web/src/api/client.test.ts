import { describe, expect, it } from 'vitest'
import {
  ApiError,
  enqueueFailedEntryRecheck,
  enqueueGoldenGlobeEnrichment,
  enqueueGoldenGlobeImport,
  enqueueManualScan,
  enqueueOscarImport,
  getActiveBackgroundJob,
  getBackgroundJobEvents,
  getCatalog,
  getCurrentSession,
  getDeploymentStatus,
  getGoldenGlobeFilms,
  setGoldenGlobeImdbId,
  getMovieAwards,
  getOscarFilm,
  getOscarFilms,
  getProviderSettings,
  importPersonalRatings,
  requestJson,
  requestRegistration,
  runDeploymentAction,
  updateProviderSettings,
} from './client'
import type { CatalogTitle, GoldenGlobeFilm, OscarFilm, PageResponse, ProviderSettings, ProviderSettingsInput } from './types'

function response(status: number, value: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
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

    await getCatalog({ page: 2, pageSize: 20, feedTypes: ['series_complete', 'series_ongoing'], search: 'quiet river', mediaType: 'series', yearFrom: 1998, genre: 'drama' }, stub.fetcher)

    const requestUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(requestUrl.pathname).toBe('/api/catalog')
    expect(requestUrl.searchParams.get('page')).toBe('2')
    expect(requestUrl.searchParams.get('pageSize')).toBe('20')
    expect(requestUrl.searchParams.get('feedTypes')).toBe('series_complete,series_ongoing')
    expect(requestUrl.searchParams.get('search')).toBe('quiet river')
    expect(requestUrl.searchParams.get('mediaType')).toBe('series')
    expect(requestUrl.searchParams.get('yearFrom')).toBe('1998')
    expect(requestUrl.searchParams.get('genre')).toBe('drama')
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
      category: 'BEST PICTURE',
      result: 'winner',
      enrichmentStatus: 'pending',
    }, stub.fetcher)
    await getOscarFilm(42, stub.fetcher)

    const listUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(listUrl.pathname).toBe('/api/oscars')
    expect(listUrl.searchParams.get('yearFrom')).toBe('2022')
    expect(listUrl.searchParams.get('yearTo')).toBe('2024')
    expect(listUrl.searchParams.get('category')).toBe('BEST PICTURE')
    expect(listUrl.searchParams.get('result')).toBe('winner')
    expect(listUrl.searchParams.get('enrichmentStatus')).toBe('pending')
    expect(String(stub.calls[1]?.input)).toBe('/api/oscars/42')
  })

  it('serializes Golden Globes catalog filters and queues import and enrichment jobs', async () => {
    const page: PageResponse<GoldenGlobeFilm> = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
    const stub = fetchStub(response(202, page))
    const file = new File(['dataset'], 'golden-globes.csv', { type: 'text/csv' })

    await getGoldenGlobeFilms({ page: 1, pageSize: 20, search: 'A Film', yearFrom: 2025, award: 'Best Picture', result: 'winner' }, stub.fetcher)
    await enqueueGoldenGlobeEnrichment(stub.fetcher)
    await enqueueGoldenGlobeImport(file, 1980, stub.fetcher)

    const catalogUrl = new URL(String(stub.calls[0]?.input), 'http://localhost')
    expect(catalogUrl.pathname).toBe('/api/golden-globes')
    expect(catalogUrl.searchParams.get('yearFrom')).toBe('2025')
    expect(catalogUrl.searchParams.get('award')).toBe('Best Picture')
    expect(catalogUrl.searchParams.get('result')).toBe('winner')
    expect(String(stub.calls[1]?.input)).toBe('/api/background-jobs/golden-globe-enrichment')
    expect(String(stub.calls[2]?.input)).toBe('/api/background-jobs/golden-globe-import')
    const formData = stub.calls[2]?.init?.body as FormData
    expect(formData.get('File')).toBe(file)
    expect(formData.get('YearAfter')).toBe('1980')
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
    const stub = fetchStub(response(200, {
      ratingsInFile: 1,
      added: 1,
      updated: 0,
      unchanged: 0,
      totalRatings: 1,
      importedAt: '2026-10-02T10:00:00Z',
      errors: [],
    }))
    const file = new File(['[{"id":"tt14452776","rating":8}]'], 'ratings.json', { type: 'application/json' })

    await importPersonalRatings(file, stub.fetcher)

    const init = stub.calls[0]?.init
    const body = init?.body
    expect(String(stub.calls[0]?.input)).toBe('/api/personal-ratings/import')
    expect(init?.method).toBe('POST')
    expect(body).toBeInstanceOf(FormData)
    expect(new Headers(init?.headers).has('Content-Type')).toBe(false)
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

  it('reports a retryable connection error when the API is unavailable', async () => {
    const stub = fetchStub(new TypeError('offline'))

    await expect(requestJson('/catalog', {}, stub.fetcher)).rejects.toMatchObject({
      name: 'ApiError',
      status: 0,
      message: 'Could not reach the API. Check the server connection and retry.',
    } satisfies Partial<ApiError>)
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
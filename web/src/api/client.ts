import type {
  AdminUserQuery,
  AdminUsersPage,
  CatalogQuery,
  CatalogTitle,
  CurrentSession,
  DeleteTitleResponse,
  RegistrationRequestStatus,
  BackgroundJob,
  BackgroundJobAccepted,
  BackgroundJobEvents,
  DeploymentActionResult,
  DeploymentStatus,
  FavoriteTitle,
  IgnoredMovie,
  AppliedImdbCandidate,
  ImdbCandidateSearch,
  OmdbDiagnostic,
  OmdbDailyUsage,
  PersonalRatingsImportResult,
  PersonalRating,
  PersonalRatingsEnrichmentAccepted,
  PersonalRatingsSort,
  OscarCatalogQuery,
  OscarFilm,
  GoldenGlobeCatalogQuery,
  GoldenGlobeFilm,
  GoldenGlobeImdbLinkResult,
  MovieAwardRecognition,
  MovieTablesDashboard,
  Occurrence,
  OperationalHistoryCleanupResponse,
  PageResponse,
  ParseLog,
  ParseLogQuery,
  ProviderSettings,
  ProviderSettingsInput,
  Recommendation,
  RecommendationMediaType,
  RecommendationQuery,
  RecommendationSettings,
  RecommendationSettingsInput,
  VoteWeightRecalculationResult,
  ScanRun,
  ScanRunQuery,
  Settings,
  SettingsInput,
  FeedType,
  SourceProfile,
  SourceUrl,
  SystemVersion,
  TitleDetails,
} from './types'

const API_ROOT = '/api'

export class ApiError extends Error {
  readonly status: number
  readonly endpoint: string
  readonly traceId: string | null

  constructor(message: string, status: number, endpoint = 'unknown', traceId: string | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.endpoint = endpoint
    this.traceId = traceId
  }
}

function problemMessage(problem: unknown): string | null {
  if (typeof problem === 'string' && problem.trim()) return problem.trim()
  if (problem && typeof problem === 'object') {
    const fields = problem as Record<string, unknown>
    if (typeof fields.detail === 'string' && fields.detail.trim()) return fields.detail
    if (typeof fields.message === 'string' && fields.message.trim()) return fields.message
    if (fields.errors && typeof fields.errors === 'object') {
      const messages = Object.values(fields.errors as Record<string, unknown>)
        .flatMap((value) => Array.isArray(value) ? value : [value])
        .filter((value): value is string => typeof value === 'string')
      if (messages.length > 0) return messages.join(' ')
    }
    if (typeof fields.title === 'string' && fields.title.trim()) return fields.title
  }
  return null
}

export async function requestJson<T>(
  path: string,
  init: RequestInit = {},
  fetcher: typeof fetch = fetch,
): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (typeof init.body === 'string') headers.set('Content-Type', 'application/json')

  let response: Response
  try {
    response = await fetcher(`${API_ROOT}${path}`, { ...init, headers })
  } catch {
    throw new ApiError('Could not reach the API. Check the server connection and retry.', 0, `${API_ROOT}${path}`)
  }

  if (!response.ok) {
    let problem: unknown
    let responseText = ''
    try {
      responseText = await response.text()
      problem = responseText ? JSON.parse(responseText) : undefined
    } catch {
      problem = responseText
    }
    const message = response.status === 502 && !responseText
      ? 'The local API is unavailable. Check the server connection and retry.'
      : problemMessage(problem) ?? `The API request failed with status ${response.status}.`
    throw new ApiError(message,
    response.status,
    `${API_ROOT}${path}`,
    response.headers.get('X-MediaDock-TraceId'))
  }

  if (response.status === 204) return undefined as T
  try {
    return await response.json() as T
  } catch {
    throw new ApiError('The API returned an invalid response.', response.status, `${API_ROOT}${path}`, response.headers.get('X-MediaDock-TraceId'))
  }
}

function withQuery(path: string, query: object, repeatedKeys: readonly string[] = []): string {
  const params = new URLSearchParams()
  const repeatedKeySet = new Set(repeatedKeys)
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) {
      if (repeatedKeySet.has(key)) {
        for (const item of value) params.append(key, String(item))
      } else if (value.length > 0) {
        params.set(key, value.join(','))
      }
      continue
    }
    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
      params.set(key, String(value))
    }
  }
  const serialized = params.toString()
  return serialized ? `${path}?${serialized}` : path
}

export function getCatalog(query: CatalogQuery, fetcher?: typeof fetch) {
  return requestJson<PageResponse<CatalogTitle>>(withQuery('/catalog', query), {}, fetcher)
}

export function getOscarFilms(query: OscarCatalogQuery, fetcher?: typeof fetch) {
  return requestJson<PageResponse<OscarFilm>>(withQuery('/oscars', query, ['categories']), {}, fetcher)
}

export function getOscarCategories(fetcher?: typeof fetch) {
  return requestJson<string[]>('/oscars/categories', {}, fetcher)
}

export function getOscarFilm(id: number, fetcher?: typeof fetch) {
  return requestJson<OscarFilm>(`/oscars/${id}`, {}, fetcher)
}

export function getTitleOscars(id: number, fetcher?: typeof fetch) {
  return requestJson<OscarFilm[]>(`/titles/${id}/oscars`, {}, fetcher)
}

export function getFavorites(query: { status?: 'all' | 'to_watch' | 'to_download'; page: number; pageSize: number }, fetcher?: typeof fetch) {
  return requestJson<PageResponse<FavoriteTitle>>(withQuery('/favorites', query), {}, fetcher)
}

export function getIgnoredMovies(query: { page: number; pageSize: number }, fetcher?: typeof fetch) {
  return requestJson<PageResponse<IgnoredMovie>>(withQuery('/ignored-movies', query), {}, fetcher)
}

export function addIgnoredMovie(imdbId: string, fetcher?: typeof fetch) {
  return requestJson<IgnoredMovie>('/ignored-movies', { method: 'POST', body: JSON.stringify({ imdbId }) }, fetcher)
}

export function removeIgnoredMovie(imdbId: string, fetcher?: typeof fetch) {
  return requestJson<void>(`/ignored-movies/${encodeURIComponent(imdbId)}`, { method: 'DELETE' }, fetcher)
}

export function getPersonalRatings(
  query: { page: number; pageSize: number; sort: PersonalRatingsSort },
  fetcher?: typeof fetch,
) {
  return requestJson<PageResponse<PersonalRating>>(withQuery('/personal-ratings', query), {}, fetcher)
}

export function addFavorite(imdbId: string, from: 'oscar' | 'catalog' | 'recommendation', fetcher?: typeof fetch) {
  return requestJson<FavoriteTitle>('/favorites', { method: 'POST', body: JSON.stringify({ imdbId, from }) }, fetcher)
}

export function updateFavorite(imdbId: string, input: { toWatch?: boolean; toDownload?: boolean }, fetcher?: typeof fetch) {
  return requestJson<FavoriteTitle>(`/favorites/${encodeURIComponent(imdbId)}`, { method: 'PATCH', body: JSON.stringify(input) }, fetcher)
}

export function removeFavorite(imdbId: string, fetcher?: typeof fetch) {
  return requestJson<void>(`/favorites/${encodeURIComponent(imdbId)}`, { method: 'DELETE' }, fetcher)
}

export function getTitle(id: number, fetcher?: typeof fetch) {
  return requestJson<TitleDetails>(`/titles/${id}`, {}, fetcher)
}

export function deleteCatalogTitle(id: number, deleteImdbInfo: boolean, fetcher?: typeof fetch) {
  return requestJson<DeleteTitleResponse>(withQuery(`/titles/${id}`, { deleteImdbInfo }), { method: 'DELETE' }, fetcher)
}

export function searchCatalogTitleImdbCandidates(id: number, title: string, page = 1, fetcher?: typeof fetch) {
  return requestJson<ImdbCandidateSearch>(withQuery(`/titles/${id}/imdb-candidates`, { page }), {
    method: 'POST',
    body: JSON.stringify({ title }),
  }, fetcher)
}

export function lookupCatalogTitleImdbCandidate(id: number, imdbId: string, fetcher?: typeof fetch) {
  return requestJson<ImdbCandidateSearch>(`/titles/${id}/imdb-candidate`, {
    method: 'POST',
    body: JSON.stringify({ imdbId }),
  }, fetcher)
}

export function applyCatalogTitleImdbCandidate(id: number, imdbId: string, fetcher?: typeof fetch) {
  return requestJson<AppliedImdbCandidate>(`/titles/${id}/imdb-match`, {
    method: 'POST',
    body: JSON.stringify({ imdbId }),
  }, fetcher)
}

export function getTitleOccurrences(
  id: number,
  query: { page: number; pageSize: number },
  fetcher?: typeof fetch,
) {
  return requestJson<PageResponse<Occurrence>>(withQuery(`/titles/${id}/occurrences`, query), {}, fetcher)
}

export function getSources(fetcher?: typeof fetch) {
  return requestJson<SourceProfile[]>('/sources', {}, fetcher)
}

export function addSourceUrl(profileId: FeedType, url: string, fetcher?: typeof fetch) {
  return requestJson<SourceUrl>(
    `/sources/${encodeURIComponent(profileId)}/urls`,
    { method: 'POST', body: JSON.stringify({ url }) },
    fetcher,
  )
}

export function replaceSourceUrl(profileId: FeedType, id: number, url: string, fetcher?: typeof fetch) {
  return requestJson<SourceUrl>(
    `/sources/${encodeURIComponent(profileId)}/urls/${id}`,
    { method: 'PUT', body: JSON.stringify({ url }) },
    fetcher,
  )
}

export function removeSourceUrl(profileId: FeedType, id: number, fetcher?: typeof fetch) {
  return requestJson<void>(`/sources/${encodeURIComponent(profileId)}/urls/${id}`, { method: 'DELETE' }, fetcher)
}

export function getSettings(fetcher?: typeof fetch) {
  return requestJson<Settings>('/settings', {}, fetcher)
}

export function updateSettings(input: SettingsInput, fetcher?: typeof fetch) {
  return requestJson<Settings>('/settings', { method: 'PUT', body: JSON.stringify(input) }, fetcher)
}

export function getRecommendationSettings(fetcher?: typeof fetch) {
  return requestJson<RecommendationSettings>('/settings/recommendations', {}, fetcher)
}

export function updateRecommendationSettings(input: RecommendationSettingsInput, fetcher?: typeof fetch) {
  return requestJson<RecommendationSettings>(
    '/settings/recommendations',
    { method: 'PUT', body: JSON.stringify(input) },
    fetcher,
  )
}

export function recalculateRecommendationVoteWeights(fetcher?: typeof fetch) {
  return requestJson<VoteWeightRecalculationResult>(
    '/settings/recommendations/recalculate-votes',
    { method: 'POST' },
    fetcher,
  )
}

export function getRecommendations(
  query: RecommendationQuery,
  fetcher?: typeof fetch,
) {
  return requestJson<PageResponse<Recommendation>>(withQuery('/recommendations', query, ['genres']), {}, fetcher)
}

export function getRecommendationGenres(mediaType: RecommendationMediaType, fetcher?: typeof fetch) {
  return requestJson<string[]>(withQuery('/recommendations/genres', { mediaType }), {}, fetcher)
}

export async function markRecommendationWatched(
  imdbId: string,
  rating: number,
  mediaType: RecommendationMediaType,
  fetcher?: typeof fetch,
) {
  const requestToken = await getAuthRequestToken(fetcher)
  return requestJson<void>(`/recommendations/${encodeURIComponent(imdbId)}/watched`, {
    method: 'POST',
    body: JSON.stringify({ rating, mediaType }),
    headers: { RequestVerificationToken: requestToken },
  }, fetcher)
}

export function getProviderSettings(fetcher?: typeof fetch) {
  return requestJson<ProviderSettings>('/settings/providers/omdb', {}, fetcher)
}

export function getOmdbDailyUsage(fetcher?: typeof fetch) {
  return requestJson<OmdbDailyUsage[]>('/settings/providers/omdb/usage', {}, fetcher)
}

export function testOmdbApi(fetcher?: typeof fetch) {
  return requestJson<OmdbDiagnostic>('/settings/providers/omdb/test', { method: 'POST' }, fetcher)
}

export function getVersion(fetcher?: typeof fetch) {
  return requestJson<SystemVersion>('/version', {}, fetcher)
}

export function getCurrentSession(fetcher?: typeof fetch) {
  return requestJson<CurrentSession>('/auth/session', {}, fetcher)
}

async function getAuthRequestToken(fetcher?: typeof fetch) {
  const response = await requestJson<{ requestToken: string }>('/auth/antiforgery', {}, fetcher)
  return response.requestToken
}

async function postAuthChange<T>(path: string, fetcher?: typeof fetch) {
  const requestToken = await getAuthRequestToken(fetcher)
  return requestJson<T>(path, {
    method: 'POST',
    headers: { RequestVerificationToken: requestToken },
  }, fetcher)
}

export function confirmGoogleAccountLink(fetcher?: typeof fetch) {
  return postAuthChange<void>('/auth/google/link', fetcher)
}

export function requestRegistration(fetcher?: typeof fetch) {
  return postAuthChange<RegistrationRequestStatus>('/auth/registration-requests', fetcher)
}

export function signOut(fetcher?: typeof fetch) {
  return postAuthChange<void>('/auth/logout', fetcher)
}

export function getAdminUsers(query: AdminUserQuery = {}, fetcher?: typeof fetch) {
  return requestJson<AdminUsersPage>(withQuery('/admin/users', query), {}, fetcher)
}

async function adminMutation(path: string, method: 'POST' | 'PUT', body: object, fetcher?: typeof fetch) {
  const requestToken = await getAuthRequestToken(fetcher)
  return requestJson<void>(path, {
    method,
    body: JSON.stringify(body),
    headers: { RequestVerificationToken: requestToken },
  }, fetcher)
}

export function decideRegistrationRequest(id: number, decision: 'approve' | 'reject', fetcher?: typeof fetch) {
  return adminMutation(`/admin/registration-requests/${id}/decision`, 'POST', { decision }, fetcher)
}

export function setAdminUserStatus(id: number, status: 'active' | 'deactivated', fetcher?: typeof fetch) {
  return adminMutation(`/admin/users/${id}/status`, 'PUT', { status }, fetcher)
}

export function getDeploymentStatus(fetcher?: typeof fetch) {
  return requestJson<DeploymentStatus>('/deployment', {}, fetcher)
}

export function runDeploymentAction(action: 'check' | 'retry_failed_gate', fetcher?: typeof fetch) {
  return requestJson<DeploymentActionResult>('/deployment/run', {
    method: 'POST',
    body: JSON.stringify({ action }),
  }, fetcher)
}

export function updateProviderSettings(input: ProviderSettingsInput, fetcher?: typeof fetch) {
  return requestJson<ProviderSettings>(
    '/settings/providers/omdb',
    { method: 'PUT', body: JSON.stringify(input) },
    fetcher,
  )
}

export function getScanRuns(query: ScanRunQuery, fetcher?: typeof fetch) {
  return requestJson<PageResponse<ScanRun>>(withQuery('/scan-runs', query), {}, fetcher)
}

export function deleteOldOperationalHistory(olderThanDays: number, fetcher?: typeof fetch) {
  return requestJson<OperationalHistoryCleanupResponse>(
    withQuery('/operation-history', { olderThanDays }),
    { method: 'DELETE' },
    fetcher,
  )
}

export function getParseLogs(query: ParseLogQuery, fetcher?: typeof fetch) {
  return requestJson<PageResponse<ParseLog>>(withQuery('/parse-logs', query), {}, fetcher)
}

export function searchParseLogImdbCandidates(id: number, title: string, page = 1, fetcher?: typeof fetch) {
  return requestJson<ImdbCandidateSearch>(withQuery(`/parse-logs/${id}/imdb-candidates`, { page }), {
    method: 'POST',
    body: JSON.stringify({ title }),
  }, fetcher)
}

export function lookupParseLogImdbCandidate(id: number, imdbId: string, fetcher?: typeof fetch) {
  return requestJson<ImdbCandidateSearch>(`/parse-logs/${id}/imdb-candidate`, {
    method: 'POST',
    body: JSON.stringify({ imdbId }),
  }, fetcher)
}

export function applyParseLogImdbCandidate(id: number, imdbId: string, fetcher?: typeof fetch) {
  return requestJson<AppliedImdbCandidate>(`/parse-logs/${id}/imdb-match`, {
    method: 'POST',
    body: JSON.stringify({ imdbId }),
  }, fetcher)
}

export async function getActiveBackgroundJob(fetcher?: typeof fetch) {
  return (await requestJson<BackgroundJob | null>('/background-jobs/active', {}, fetcher)) ?? null
}

export function getBackgroundJob(id: number, fetcher?: typeof fetch) {
  return requestJson<BackgroundJob>(`/background-jobs/${id}`, {}, fetcher)
}

export function getPersonalRatingsEnrichmentJob(id: number, fetcher?: typeof fetch) {
  return requestJson<BackgroundJob>(`/personal-ratings/enrichment/${id}`, {}, fetcher)
}

export function getBackgroundJobEvents(
  id: number,
  query: { afterId: number; pageSize: number },
  fetcher?: typeof fetch,
) {
  return requestJson<BackgroundJobEvents>(withQuery(`/background-jobs/${id}/events`, query), {}, fetcher)
}

export function enqueueManualScan(fetcher?: typeof fetch) {
  return requestJson<BackgroundJobAccepted>('/background-jobs/scans', { method: 'POST' }, fetcher)
}

export function enqueueFailedEntryRecheck(fetcher?: typeof fetch) {
  return requestJson<BackgroundJobAccepted>('/background-jobs/recheck-failed', { method: 'POST' }, fetcher)
}

export function enqueueOscarEnrichment(fetcher?: typeof fetch) {
  return requestJson<BackgroundJobAccepted>('/background-jobs/oscar-enrichment', { method: 'POST' }, fetcher)
}

export function enqueueGoldenGlobeEnrichment(fetcher?: typeof fetch) {
  return requestJson<BackgroundJobAccepted>('/background-jobs/golden-globe-enrichment', { method: 'POST' }, fetcher)
}

export function enqueueOscarImport(file: File, yearAfter: number, fetcher?: typeof fetch) {
  const body = new FormData()
  body.append('File', file)
  body.append('YearAfter', String(yearAfter))
  return requestJson<BackgroundJobAccepted>('/background-jobs/oscar-import', { method: 'POST', body }, fetcher)
}

export function enqueueGoldenGlobeImport(file: File, yearAfter: number, fetcher?: typeof fetch) {
  const body = new FormData()
  body.append('File', file)
  body.append('YearAfter', String(yearAfter))
  return requestJson<BackgroundJobAccepted>('/background-jobs/golden-globe-import', { method: 'POST', body }, fetcher)
}

export function getGoldenGlobeFilms(query: GoldenGlobeCatalogQuery, fetcher?: typeof fetch) {
  return requestJson<PageResponse<GoldenGlobeFilm>>(withQuery('/golden-globes', query, ['categories']), {}, fetcher)
}

export function getGoldenGlobeCategories(fetcher?: typeof fetch) {
  return requestJson<string[]>('/golden-globes/categories', {}, fetcher)
}

export function setGoldenGlobeImdbId(filmId: string, imdbId: string | null, fetcher?: typeof fetch) {
  return requestJson<GoldenGlobeImdbLinkResult>(
    '/golden-globes/imdb-link',
    { method: 'PUT', body: JSON.stringify({ filmId, imdbId }) },
    fetcher,
  )
}

export function getMovieAwards(imdbIds: string[], fetcher?: typeof fetch) {
  return requestJson<MovieAwardRecognition[]>(withQuery('/movie-awards', { imdbIds }), {}, fetcher)
}

export async function importPersonalRatings(file: File, fetcher?: typeof fetch) {
  const requestToken = await getAuthRequestToken(fetcher)
  const body = new FormData()
  body.append('File', file)
  return requestJson<PersonalRatingsImportResult>('/personal-ratings/import', {
    method: 'POST',
    body,
    headers: { RequestVerificationToken: requestToken },
  }, fetcher)
}

export function enqueuePersonalRatingsEnrichment(fetcher?: typeof fetch) {
  return postAuthChange<PersonalRatingsEnrichmentAccepted | undefined>('/personal-ratings/enrichment', fetcher)
}

export function getMovieTablesDashboard(fetcher?: typeof fetch) {
  return requestJson<MovieTablesDashboard>('/admin/movie-tables-dashboard', {}, fetcher)
}

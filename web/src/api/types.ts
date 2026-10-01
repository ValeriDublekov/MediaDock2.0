export interface PageResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export type MediaType = 'movie' | 'series' | 'documentary' | 'short'
export type MediaSourceType = 'movie' | 'series'
export type FeedType = 'movie' | 'series_complete' | 'series_ongoing'

export interface CatalogTitle {
  id: number
  title: string
  year: number | null
  mediaType: MediaType
  sourceType: MediaSourceType | null
  contentKind: string | null
  imdbRating: number | null
  posterUrl: string | null
  genres: string[]
  countries: string[]
  lastSeenAt: string | null
  occurrenceCount: number
}

export interface TitleDetails extends CatalogTitle {
  broadcastRangeStartYear: number | null
  broadcastRangeEndYear: number | null
  broadcastRangeRaw: string | null
  imdbId: string | null
  imdbVotes: number | null
  metascore: number | null
  director: string | null
  plot: string | null
  runtime: string | null
  awards: string | null
  boxOffice: string | null
  firstSeenAt: string | null
  updatedAt: string
}

export type OscarEnrichmentStatus = 'pending' | 'enriched' | 'not_found' | 'temporary_error'

export interface OscarNomination {
  id: number
  ceremony: number
  class: string
  canonicalCategory: string
  category: string
  name: string
  nominees: string
  nomineeIds: string
  detail: string
  isWinner: boolean
}

export interface OscarFilm {
  id: number
  titleId: number
  title: string
  metadataTitle: string
  metadataYear: number | null
  filmYear: number
  imdbId: string | null
  enrichmentStatus: OscarEnrichmentStatus
  enrichmentAttemptCount: number
  lastEnrichmentAttemptAt: string | null
  nextEnrichmentAttemptAt: string | null
  lastEnrichmentError: string | null
  mediaType: MediaType
  imdbRating: number | null
  imdbVotes: number | null
  metascore: number | null
  genres: string[]
  countries: string[]
  director: string | null
  plot: string | null
  posterUrl: string | null
  runtime: string | null
  awards: string | null
  boxOffice: string | null
  nominations: OscarNomination[]
}

export interface Occurrence {
  id: number
  titleId: number
  sourceId: number
  sourceName: string
  sourceItemKey: string
  feedEntryId: string | null
  torrentUrl: string
  rawTitle: string
  sourceFeedName: string
  feedType: FeedType | null
  sourcePublishedAt: string | null
  observedAt: string | null
  quality: string | null
  ripType: string | null
  firstSeenAt: string
  lastSeenAt: string
}

export interface SourceUrl {
  id: number
  url: string
}

export interface SourceProfile {
  id: FeedType
  name: string
  urls: SourceUrl[]
}

export interface Settings {
  excludedGenres: string[]
  excludedCountries: string[]
  minMovieRating: number
  minSeriesRating: number
  minImdbVotes: number
  updatedAt: string | null
}

export interface SettingsInput {
  excludedGenres: string[]
  excludedCountries: string[]
  minMovieRating: number
  minSeriesRating: number
  minImdbVotes: number
}

export interface ProviderSettings {
  omdbApiKeyConfigured: boolean
  omdbDailyRequestLimit: number
  oscarEnrichmentMaxFilmsPerRun: number
  oscarEnrichmentMaxRequestsPerDay: number
  updatedAt: string | null
}

export interface OmdbDailyUsage {
  utcDate: string
  totalRequests: number
  rssRequests: number
  oscarRequests: number
  dailyRequestLimitReached: boolean
  providerQuotaExceeded: boolean
  lastErrorCode: string | null
}

export interface ProviderSettingsInput {
  omdbApiKey: string | null
  clearOmdbApiKey: boolean
  omdbDailyRequestLimit: number
  oscarEnrichmentMaxFilmsPerRun: number
  oscarEnrichmentMaxRequestsPerDay: number
}

export interface ParseLog {
  id: number
  sourceId: number | null
  sourceName: string | null
  sourceItemKey: string | null
  rawTitle: string
  feedName: string
  parsedSuccessfully: boolean
  parsedTitle: string | null
  parsedYear: number | null
  omdbStatus: string
  ignored: boolean
  ignoreReason: string | null
  errorMessage: string | null
  decision: string | null
  processedAt: string
  retryState: string
  attemptCount: number
  lastAttemptAt: string | null
  feedType: FeedType | null
  sourcePublishedAt: string | null
  observedAt: string | null
  eventKind: string | null
}

export interface ScanRun {
  id: number
  startedAt: string
  finishedAt: string | null
  status: 'running' | 'succeeded' | 'partial' | 'failed'
  trigger: 'schedule' | 'manual' | 'local'
  feedsProcessed: number
  entriesSeen: number
  knownEntriesSkipped: number
  titlesCreated: number
  occurrencesCreated: number
  cacheHits: number
  omdbRequests: number
  ignoredEntries: number
  errorCount: number
  errorSummary: string[]
}

export interface CatalogQuery {
  page: number
  pageSize: number
  search?: string
  mediaType?: MediaType
  sourceType?: MediaSourceType
  contentKind?: 'standard' | 'documentary' | 'short'
  yearFrom?: number
  yearTo?: number
  genre?: string
  country?: string
  sourceId?: number
}

export interface OscarCatalogQuery {
  page: number
  pageSize: number
  search?: string
  yearFrom?: number
  yearTo?: number
  category?: string
  result?: 'winner' | 'nominee'
  enrichmentStatus?: OscarEnrichmentStatus
}

export interface ParseLogQuery {
  page: number
  pageSize: number
  sourceId?: number
  parsedSuccessfully?: boolean
  ignored?: boolean
  retryState?: 'retryable' | 'terminal' | 'resolved'
  search?: string
}

export interface ScanRunQuery {
  page: number
  pageSize: number
  status?: ScanRun['status']
  trigger?: ScanRun['trigger']
}

export interface FavoriteMovie {
  titleId: number
  title: string
  year: number | null
  mediaType: MediaType
  imdbRating: number | null
  posterUrl: string | null
  toWatch: boolean
  toDownload: boolean
  addedFromOscar: boolean
  addedFromCatalog: boolean
  createdAt: string
  updatedAt: string
  oscarFilmCount: number
  nominationCount: number
  winCount: number
  occurrenceCount: number
  lastSeenAt: string | null
}

export interface SystemVersion {
  version: string
  commitSha: string
  commitDateUtc: string | null
}

export type BackgroundJobStatus = 'queued' | 'running' | 'succeeded' | 'partial' | 'failed'

export interface BackgroundJob {
  id: number
  jobType: 'rss_scan' | 'oscar_import' | 'oscar_enrichment'
  trigger: 'manual' | 'schedule'
  status: BackgroundJobStatus
  enqueuedAt: string
  startedAt: string | null
  finishedAt: string | null
  currentStage: string | null
  currentSource: string | null
  progressUpdatedAt: string | null
  errorCode: string | null
  resultSummary: Record<string, unknown> | null
  scanRunId: number | null
  inputFileName: string | null
}

export interface BackgroundJobAccepted {
  id: number
  status: BackgroundJobStatus
  statusUrl: string
}

export interface BackgroundJobEvent {
  id: number
  occurredAt: string
  level: 'information' | 'warning' | 'error'
  eventCode: string
  message: string
  data: Record<string, unknown> | null
}

export interface BackgroundJobEvents {
  items: BackgroundJobEvent[]
  nextAfterId: number
}
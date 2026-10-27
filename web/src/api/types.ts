export interface PageResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  yearBounds?: YearBounds
  imdbRatingBounds?: ImdbRatingBounds
}

export interface YearBounds {
  minYear: number
  maxYear: number
}

export interface ImdbRatingBounds {
  minRating: number
  maxRating: number
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
  imdbId: string | null
  imdbRating: number | null
  posterUrl: string | null
  genres: string[]
  countries: string[]
  lastSeenAt: string | null
  occurrenceCount: number
  metadataStatus: string | null
  director: string | null
  plot: string | null
}

export interface TitleDetails extends CatalogTitle {
  broadcastRangeStartYear: number | null
  broadcastRangeEndYear: number | null
  broadcastRangeRaw: string | null
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

export interface DeleteTitleResponse {
  imdbInfoDeleted: boolean
}

export interface ImdbCandidate {
  title: string
  year: number | null
  imdbId: string | null
  sourceType: string
}

export interface ImdbCandidateSearch {
  status: 'found' | 'not_found' | 'quota_exceeded' | 'budget_exhausted' | 'transport_failure' | 'authentication_failure' | 'invalid_request' | 'provider_failure'
  candidates: ImdbCandidate[]
  totalResults: number
  errorCode: string | null
}

export interface AppliedImdbCandidate {
  torrentTitleId: number
  imdbId: string
}

export type OscarEnrichmentStatus = 'pending' | 'enriched' | 'problem' | 'not_found' | 'temporary_error'
export type GoldenGlobeEnrichmentStatus = OscarEnrichmentStatus

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
  title: string
  metadataTitle: string
  metadataYear: number | null
  filmYear: number
  imdbId: string | null
  enrichmentStatus: OscarEnrichmentStatus
  metadataStatus: string | null
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

export interface GoldenGlobeNomination {
  id: number
  year: number
  award: string
  isWinner: boolean
}

export type MovieAwardSource = 'oscars' | 'golden_globes'

export interface MovieAwardRecognition {
  id: number
  imdbId: string | null
  source: MovieAwardSource
  filmYear: number | null
  ceremonyYear: number | null
  ceremony: number | null
  award: string
  name: string | null
  nominees: string | null
  detail: string | null
  isWinner: boolean
}

export interface GoldenGlobeFilm {
  filmId: string
  title: string
  year: number
  nomineeType: 'movie' | 'series'
  imdbId: string | null
  isImdbIdManual: boolean
  imdbRating: number | null
  posterUrl: string | null
  enrichmentStatus: OscarEnrichmentStatus
  metadataStatus: string | null
  enrichmentError: string | null
  genres: string[]
  director: string | null
  plot: string | null
  nominations: GoldenGlobeNomination[]
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

export interface RssFeedPreviewItem {
  title: string | null
  feedEntryId: string | null
  torrentUrl: string | null
  publishedAt: string | null
}

export interface RssFeedPreviewSource {
  sourceId: number
  sourceUrl: string
  originalContent: string
  fetchDurationMs: number
  parseDurationMs: number
  items: RssFeedPreviewItem[]
}

export interface RssFeedPreview {
  profileId: FeedType
  profileName: string
  cachedAt: string | null
  feeds: RssFeedPreviewSource[]
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

export type RecommendationMediaType = 'movie' | 'series'

export type RecommendationAwardSource = 'oscar' | 'golden_globes'

export interface RecommendationAwardWeight {
  awardSource: RecommendationAwardSource
  categoryKey: string
  winPoints: number
  nominationPoints: number
  isOverride: boolean
}

export interface RecommendationSettings {
  medianPeriodYears: number
  bayesianDefaultC: number
  bayesianMRatio: number
  oscarWinPoints: number
  oscarNomPoints: number
  ggWinPoints: number
  ggNomPoints: number
  requiresVoteRecalculation: boolean
  lastVoteRecalculationAt: string | null
  lastVoteRecalculationPeriodYears: number | null
  awardWeights: RecommendationAwardWeight[]
}

export interface RecommendationSettingsInput {
  medianPeriodYears: number
  bayesianDefaultC: number
  bayesianMRatio: number
  oscarWinPoints: number
  oscarNomPoints: number
  ggWinPoints: number
  ggNomPoints: number
  awardWeights: RecommendationAwardWeight[]
}

export interface VoteWeightRecalculationResponse {
  calculatedAt: string
  durationMilliseconds: number
  medianPeriodYears: number
  movieCount: number
  moviesWithVotesCount: number
  periodCount: number
  fallbackMedianCount: number
  missingVoteCount: number
}

export type VoteWeightRecalculationResult = VoteWeightRecalculationResponse | BackgroundJobAccepted

export interface RecommendationCoefficients {
  ratingWeight: number
  votesWeight: number
  awardsWeight: number
}

export interface RecommendationQuery extends RecommendationCoefficients {
  mediaType: RecommendationMediaType
  page: number
  pageSize: number
  yearFrom?: number
  yearTo?: number
  genres?: string[]
  excludeWatched?: boolean
}

export interface RecommendationAwardEvent {
  awardSource: RecommendationAwardSource
  categoryKey: string
  categoryName: string
  ceremony: number | null
  ceremonyYear: number | null
  outcome: string
  basePoints: number
  tasteMultiplier: number
  adjustedPoints: number
}

export interface Recommendation {
  imdbId: string
  title: string
  year: number | null
  mediaType: RecommendationMediaType
  isWatched: boolean
  imdbRating: number
  imdbVotes: number | null
  posterUrl: string | null
  genres: string[]
  ratingComponent: number
  popularityComponent: number
  awardComponent: number
  ratingWeight: number
  votesWeight: number
  awardsWeight: number
  finalScore: number
  awardEvents: RecommendationAwardEvent[]
}

export type MovieRecommendation = Recommendation

export interface ProviderSettings {
  omdbApiKeyConfigured: boolean
  omdbDailyRequestLimit: number
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

export interface OmdbDiagnostic {
  imdbId: string
  status: string
  isSuccessful: boolean
  message: string
  title: string | null
  year: number | null
  httpAttempts: number
}

export interface ProviderSettingsInput {
  omdbApiKey: string | null
  clearOmdbApiKey: boolean
  omdbDailyRequestLimit: number
}

export interface PersonalRatingsImportResult {
  ratingsInFile: number
  added: number
  updated: number
  unchanged: number
  totalRatings: number
  importedAt: string
  errors: Array<{
    id: string
    message: string
  }>
}

export type PersonalRatingsSort = 'asc' | 'desc'

export interface PersonalRating {
  imdbId: string
  rating: number
  isManuallyAdded: boolean
  title: string
  year: number | null
  mediaType: string | null
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
  metadataStatus: string
}

export interface PersonalRatingsEnrichmentAccepted {
  jobId: number
  status: string
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
  lookupTitles: string[]
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
  canRetryMatch: boolean
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
  feedTypes?: FeedType[]
  search?: string
  mediaType?: MediaType
  sourceType?: MediaSourceType
  contentKind?: 'standard' | 'documentary' | 'short'
  yearFrom?: number
  yearTo?: number
  imdbRatingFrom?: number
  imdbRatingTo?: number
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
  imdbRatingFrom?: number
  imdbRatingTo?: number
  category?: string
  categories?: string[]
  categoryFilter?: boolean
  result?: 'winner' | 'nominee'
  enrichmentStatus?: OscarEnrichmentStatus
}

export interface GoldenGlobeCatalogQuery {
  page: number
  pageSize: number
  search?: string
  yearFrom?: number
  yearTo?: number
  imdbRatingFrom?: number
  imdbRatingTo?: number
  award?: string
  categories?: string[]
  categoryFilter?: boolean
  result?: 'winner' | 'nominee'
  enrichmentStatus?: GoldenGlobeEnrichmentStatus
}

export interface ParseLogQuery {
  page: number
  pageSize: number
  sourceId?: number
  parsedSuccessfully?: boolean
  ignored?: boolean
  retryState?: 'retryable' | 'terminal' | 'resolved'
  unresolvedMoviesOnly?: boolean
  search?: string
}

export interface ScanRunQuery {
  page: number
  pageSize: number
  status?: ScanRun['status']
  trigger?: ScanRun['trigger']
}

export interface OperationalHistoryCleanupResponse {
  olderThanDays: number
  cutoff: string
  deletedScanRuns: number
  deletedParseLogs: number
}

export interface FavoriteTitle {
  imdbId: string
  torrentMovieId: number | null
  title: string
  year: number | null
  mediaType: MediaType
  metadataStatus: string | null
  imdbRating: number | null
  posterUrl: string | null
  toWatch: boolean
  toDownload: boolean
  addedFromOscar: boolean
  addedFromCatalog: boolean
  addedFromRecommendations: boolean
  createdAt: string
  updatedAt: string
  oscarFilmCount: number
  nominationCount: number
  winCount: number
  occurrenceCount: number
  lastSeenAt: string | null
}

export interface IgnoredMovie {
  imdbId: string
  title: string
  year: number | null
  mediaType: MediaType | null
  imdbRating: number | null
  posterUrl: string | null
  createdAt: string
}

export interface SystemVersion {
  version: string
  commitSha: string
  commitDateUtc: string | null
}

export interface CurrentSessionIdentity {
  issuer: string
  subject: string
  email: string
  givenName: string | null
  familyName: string | null
  profileComplete: boolean
}

export interface CurrentSessionUser {
  email: string
  givenName: string
  familyName: string
  status: string
  role: 'admin' | 'user' | null
}

export interface RegistrationRequestStatus {
  status: 'pending' | 'approved' | 'rejected'
  requestedAt: string
  decidedAt: string | null
}

export type AdminUserState = 'all' | 'requested' | 'active' | 'deactivated'

export interface AdminUserQuery {
  state?: AdminUserState
  search?: string
  page?: number
  pageSize?: number
}

export interface AdminUser {
  id: number
  email: string
  givenName: string
  familyName: string
  status: 'pending' | 'active' | 'deactivated'
  role: 'admin' | 'user' | null
  createdAt: string
  registrationRequest: (RegistrationRequestStatus & { id: number }) | null
}

export interface AdminUsersPage extends PageResponse<AdminUser> {}

export interface CurrentSession {
  authenticated: boolean
  identity: CurrentSessionIdentity | null
  user: CurrentSessionUser | null
  accountState: 'anonymous' | 'linked' | 'link_confirmation_required' | 'unmatched' | 'account_conflict'
  signInEnabled: boolean
  authorizationEnforced: boolean
  registrationRequest: RegistrationRequestStatus | null
}

export interface AuthDiagnostic {
  operation: string
  endpoint: string
  status: number | null
  message: string
  traceId: string | null
  occurredAt: string
}

export interface DeploymentStatus {
  isRunning: boolean
  activeState: string
  subState: string
  result: string
  exitCode: string
  startedAt: string
  finishedAt: string
  deployedSha: string | null
  gateFailedSha: string | null
  recoveryRequired: boolean
  failureTargetSha: string | null
  recentOutput: string[]
}

export interface DeploymentActionResult {
  message: string
}

export type BackgroundJobStatus = 'queued' | 'running' | 'succeeded' | 'partial' | 'failed'

export interface BackgroundJob {
  id: number
  jobType: 'rss_scan' | 'oscar_import' | 'golden_globe_import' | 'oscar_enrichment' | 'golden_globe_enrichment' | 'golden_globe_manual_refresh' | 'imdb_title_enrichment'
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

export interface GoldenGlobeImdbLinkResult {
  imdbId: string | null
  refreshJob: BackgroundJobAccepted | null
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

export interface MovieTablesDashboard {
  imdb: ImdbTitlesSummary
  goldenGlobes: GoldenGlobeTitlesSummary
  oscar: OscarMoviesSummary
  generatedAtUtc: string
}

export interface ImdbTitlesSummary {
  totalCount: number
  byMediaType: Record<string, number>
  byMetadataStatus: Record<string, number>
  enrichedCount: number
  pendingCount: number
}

export interface GoldenGlobeTitlesSummary {
  totalCount: number
  byNomineeType: Record<string, number>
  byEnrichmentStatus: Record<string, number>
  withImdbLinkCount: number
  withoutImdbLinkCount: number
  totalNominationsCount: number
}

export interface OscarMoviesSummary {
  totalCount: number
  byEnrichmentStatus: Record<string, number>
  withImdbLinkCount: number
  withoutImdbLinkCount: number
  totalNominationsCount: number
}

import type {
  GoldenGlobeFilm,
  MovieAwardRecognition,
  OscarFilm,
} from '../api/types'

export function getOscarRecognitions(film: OscarFilm): MovieAwardRecognition[] {
  return film.nominations.map((nomination) => ({
    id: nomination.id,
    imdbId: film.imdbId,
    source: 'oscars',
    filmYear: film.filmYear,
    ceremonyYear: null,
    ceremony: nomination.ceremony,
    award: nomination.category,
    name: nomination.name,
    nominees: nomination.nominees,
    detail: nomination.detail,
    isWinner: nomination.isWinner,
  }))
}

export function getGoldenGlobeRecognitions(film: GoldenGlobeFilm): MovieAwardRecognition[] {
  return film.nominations.map((nomination) => ({
    id: nomination.id,
    imdbId: film.imdbId,
    source: 'golden_globes',
    filmYear: null,
    ceremonyYear: nomination.year,
    ceremony: null,
    award: nomination.award,
    name: null,
    nominees: null,
    detail: null,
    isWinner: nomination.isWinner,
  }))
}

export function combineMovieAwards(
  localAwards: MovieAwardRecognition[],
  allAwards: MovieAwardRecognition[],
  imdbId: string | null,
): MovieAwardRecognition[] {
  const normalizedId = imdbId?.toLowerCase() ?? null
  const matchingAwards = normalizedId
    ? allAwards.filter((award) => award.imdbId?.toLowerCase() === normalizedId)
    : []
  const byIdentity = new Map<string, MovieAwardRecognition>()
  for (const award of localAwards) byIdentity.set(`${award.source}:${award.id}`, award)
  for (const award of matchingAwards) byIdentity.set(`${award.source}:${award.id}`, award)
  return [...byIdentity.values()]
}

export function movieAwardsRequestKey(imdbIds: Array<string | null | undefined>): string {
  return [...new Set(imdbIds.filter((id): id is string => Boolean(id)).map((id) => id.toLowerCase()))]
    .sort()
    .join(',')
}
import { useEffect, useState } from 'react'
import { getMovieAwards, getOscarFilm } from '../../api/client'
import type { MovieAwardRecognition, OscarFilm } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { MovieAwardsList } from '../../components/MoviePresentation'
import { MovieDetailsTemplate } from '../../components/MovieDetailsTemplate'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions } from '../../shared/movieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'

interface OscarFilmDetailsDialogProps {
  filmId: number
  onClose: () => void
  onIgnored?: () => void
}

export function OscarFilmDetailsDialog({ filmId, onClose, onIgnored }: OscarFilmDetailsDialogProps) {
  const [film, setFilm] = useState<OscarFilm | null>(null)
  const [awards, setAwards] = useState<MovieAwardRecognition[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    let current = true
    getOscarFilm(filmId)
      .then(async (response) => {
        const combinedAwards = response.imdbId
          ? await getMovieAwards([response.imdbId]).catch(() => [])
          : []
        if (current) {
          setFilm(response)
          setAwards(combineMovieAwards(getOscarRecognitions(response), combinedAwards, response.imdbId))
        }
      })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The Oscar film request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [filmId, attempt])

  function retry() {
    setLoading(true)
    setError(null)
    setAttempt((current) => current + 1)
  }

  return <MovieDetailsTemplate
    actions={film && !loading && !error && <>
      <FavoriteControls from="oscar" imdbId={film.imdbId} metadataStatus={film.metadataStatus} mediaType={film.mediaType} occurrenceCount={0} />
      <IgnoredMovieControl imdbId={film.imdbId} mediaType={film.mediaType} onIgnored={onIgnored} />
    </>}
    className="oscar-detail-dialog"
    countries={film?.countries}
    director={film?.director}
    eyebrow="ACADEMY AWARDS"
    facts={film && <>
      <div><dt>IMDb ID</dt><dd>{film.imdbId ?? 'Not listed'}</dd></div>
      <div><dt>Matched title</dt><dd>{film.metadataTitle}{film.metadataYear ? ` (${film.metadataYear})` : ''}</dd></div>
      <div><dt>Metascore</dt><dd>{film.metascore ?? 'Not listed'}</dd></div>
      {film.enrichmentStatus !== 'enriched' && <div><dt>OMDb status</dt><dd><span className={`state-pill is-${film.enrichmentStatus.replaceAll('_', '-')}`}>{formatWords(film.enrichmentStatus)}</span></dd></div>}
      <div><dt>Last attempt</dt><dd>{formatDate(film.lastEnrichmentAttemptAt)}</dd></div>
      <div><dt>Next attempt</dt><dd>{formatDate(film.nextEnrichmentAttemptAt)}</dd></div>
      <div><dt>Box office</dt><dd>{film.boxOffice ?? 'Not listed'}</dd></div>
      {film.lastEnrichmentError && <div><dt>Last error</dt><dd>{film.lastEnrichmentError}</dd></div>}
    </>}
    genres={film?.genres}
    imdbId={film?.imdbId ?? null}
    imdbRating={film?.imdbRating ?? null}
    imdbVotes={film?.imdbVotes}
    onClose={onClose}
    plot={film?.plot}
    posterLabel="OSC"
    posterUrl={film?.posterUrl ?? null}
    runtime={film?.runtime}
    showOverview={!loading && !error && Boolean(film)}
    title={film?.title ?? 'Oscar film'}
    typeLabel={film ? formatWords(film.mediaType) : null}
    year={film?.filmYear}
  >
    {loading && <LoadingState label="Loading Oscar film details" />}
    {!loading && error && <ErrorState message={error} onRetry={retry} />}
    {!loading && !error && film && <MovieAwardsList awards={awards} />}
  </MovieDetailsTemplate>
}
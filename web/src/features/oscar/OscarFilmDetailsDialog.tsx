import { useEffect, useState } from 'react'
import { getMovieAwards, getOscarFilm } from '../../api/client'
import type { MovieAwardRecognition, OscarFilm } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { MovieAwardsList, MovieDetailsIntro } from '../../components/MoviePresentation'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions } from '../../shared/movieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'

interface OscarFilmDetailsDialogProps {
  filmId: number
  onClose: () => void
  onFavoriteAdded?: () => void
  onIgnored?: () => void
}

export function OscarFilmDetailsDialog({ filmId, onClose, onFavoriteAdded, onIgnored }: OscarFilmDetailsDialogProps) {
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

  return (
    <div className="detail-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <section aria-labelledby="oscar-film-details-heading" aria-modal="true" className="detail-dialog oscar-detail-dialog" role="dialog">
        <header className="detail-header">
          <div>
            <p className="eyebrow">ACADEMY AWARDS</p>
            <h2 id="oscar-film-details-heading">{film?.title ?? 'Oscar film'}</h2>
            {film && (
              <div className="detail-meta">
                {film.filmYear} | {film.metadataTitle}{film.enrichmentStatus !== 'enriched' && ` | ${formatWords(film.enrichmentStatus)}`}
              </div>
            )}
          </div>
          <button aria-label="Close Oscar film details" className="button button-secondary" onClick={onClose} type="button">Close</button>
        </header>

        <div className="detail-body">
          {loading && <LoadingState label="Loading Oscar film details" />}
          {!loading && error && <ErrorState message={error} onRetry={retry} />}
          {!loading && !error && film && (
            <>
              <MovieDetailsIntro imdbId={film.imdbId} imdbRating={film.imdbRating} posterLabel="OSC" posterUrl={film.posterUrl} title={film.title}>
                {film.plot && <p className="detail-description">{film.plot}</p>}
              </MovieDetailsIntro>
              <dl className="detail-facts oscar-detail-facts">
                <div><dt>IMDb</dt><dd>{film.imdbId ?? 'Not listed'}</dd></div>
                <div><dt>IMDb votes</dt><dd>{film.imdbVotes?.toLocaleString() ?? 'Not listed'}</dd></div>
                <div><dt>Metascore</dt><dd>{film.metascore ?? 'Not listed'}</dd></div>
                <div><dt>Director</dt><dd>{film.director ?? 'Not listed'}</dd></div>
                <div><dt>Runtime</dt><dd>{film.runtime ?? 'Not listed'}</dd></div>
                <div><dt>Genres</dt><dd>{film.genres.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>Countries</dt><dd>{film.countries.join(', ') || 'Not tagged'}</dd></div>
                {film.enrichmentStatus !== 'enriched' && <div><dt>OMDb status</dt><dd><span className={`state-pill is-${film.enrichmentStatus.replaceAll('_', '-')}`}>{formatWords(film.enrichmentStatus)}</span></dd></div>}
                <div><dt>Last attempt</dt><dd>{formatDate(film.lastEnrichmentAttemptAt)}</dd></div>
                <div><dt>Next attempt</dt><dd>{formatDate(film.nextEnrichmentAttemptAt)}</dd></div>
                <div><dt>Box office</dt><dd>{film.boxOffice ?? 'Not listed'}</dd></div>
                {film.lastEnrichmentError && <div><dt>Last error</dt><dd>{film.lastEnrichmentError}</dd></div>}
              </dl>
              <FavoriteControls from="oscar" imdbId={film.imdbId} metadataStatus={film.metadataStatus} mediaType={film.mediaType} occurrenceCount={0} onAdded={onFavoriteAdded} />
              <IgnoredMovieControl imdbId={film.imdbId} mediaType={film.mediaType} onIgnored={onIgnored} />

              <MovieAwardsList awards={awards} />
            </>
          )}
        </div>
      </section>
    </div>
  )
}
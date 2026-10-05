import { useEffect, useState } from 'react'
import { getMovieAwards, getOscarFilm, getTitleOccurrences, getTitleOscars } from '../../api/client'
import type { MovieAwardRecognition, OscarFilm } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { MovieAwardsList, MovieImdbLink } from '../../components/MoviePresentation'
import { Poster } from '../../components/Poster'
import { formatDate, formatWords } from '../../shared/format'
import { combineMovieAwards, getOscarRecognitions } from '../../shared/movieAwards'
import { FavoriteControls } from '../favorites/FavoriteControls'

interface OscarFilmDetailsDialogProps {
  filmId: number
  onClose: () => void
}

export function OscarFilmDetailsDialog({ filmId, onClose }: OscarFilmDetailsDialogProps) {
  const [film, setFilm] = useState<OscarFilm | null>(null)
  const [awards, setAwards] = useState<MovieAwardRecognition[]>([])
  const [occurrenceCount, setOccurrenceCount] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  useEffect(() => {
    let current = true
    getOscarFilm(filmId)
      .then(async (response) => {
        const [relatedAwards, sources, combinedAwards] = await Promise.all([
          getTitleOscars(response.titleId),
          getTitleOccurrences(response.titleId, { page: 1, pageSize: 5 }),
          response.imdbId ? getMovieAwards([response.imdbId]).catch(() => []) : Promise.resolve([]),
        ])
        if (current) {
          setFilm(response)
          setAwards(combineMovieAwards([response, ...relatedAwards].flatMap(getOscarRecognitions), combinedAwards, response.imdbId))
          setOccurrenceCount(sources.totalCount)
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
                {film.filmYear} | {film.metadataTitle} | {formatWords(film.enrichmentStatus)}
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
              <div className="film-details-intro">
                <Poster className="detail-poster" label="OSC" src={film.posterUrl} title={film.title} />
                <div className="film-details-summary">
                  <MovieImdbLink imdbId={film.imdbId} rating={film.imdbRating} title={film.title} />
                  {film.plot && <p className="detail-description">{film.plot}</p>}
                </div>
              </div>
              <dl className="detail-facts oscar-detail-facts">
                <div><dt>IMDb</dt><dd>{film.imdbId ?? 'Not listed'}</dd></div>
                <div><dt>IMDb votes</dt><dd>{film.imdbVotes?.toLocaleString() ?? 'Not listed'}</dd></div>
                <div><dt>Metascore</dt><dd>{film.metascore ?? 'Not listed'}</dd></div>
                <div><dt>Director</dt><dd>{film.director ?? 'Not listed'}</dd></div>
                <div><dt>Runtime</dt><dd>{film.runtime ?? 'Not listed'}</dd></div>
                <div><dt>Genres</dt><dd>{film.genres.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>Countries</dt><dd>{film.countries.join(', ') || 'Not tagged'}</dd></div>
                <div><dt>OMDb status</dt><dd><span className={`state-pill is-${film.enrichmentStatus.replaceAll('_', '-')}`}>{formatWords(film.enrichmentStatus)}</span></dd></div>
                <div><dt>Last attempt</dt><dd>{formatDate(film.lastEnrichmentAttemptAt)}</dd></div>
                <div><dt>Next attempt</dt><dd>{formatDate(film.nextEnrichmentAttemptAt)}</dd></div>
                <div><dt>Box office</dt><dd>{film.boxOffice ?? 'Not listed'}</dd></div>
                {film.lastEnrichmentError && <div><dt>Last error</dt><dd>{film.lastEnrichmentError}</dd></div>}
              </dl>
              <FavoriteControls from="oscar" mediaType={film.mediaType} occurrenceCount={occurrenceCount} titleId={film.titleId} />

              <MovieAwardsList awards={awards} />
            </>
          )}
        </div>
      </section>
    </div>
  )
}
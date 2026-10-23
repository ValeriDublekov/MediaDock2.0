import { useIgnoredMovies } from './IgnoredMoviesContext'

export function IgnoredMovieControl({ imdbId, mediaType }: { imdbId: string | null; mediaType: string | null }) {
  const { available, movies, pending, loading, error, ignore, remove, retry } = useIgnoredMovies()
  if (!available || (mediaType !== 'movie' && mediaType !== 'series') || !imdbId) return null

  const isIgnored = Boolean(movies[imdbId])
  const titleType = mediaType === 'series' ? 'series' : 'movie'
  const disabled = loading || pending !== null || Boolean(error)
  return <div className="favorite-controls ignored-movie-controls">
    <button className="text-button" disabled={disabled} onClick={() => void (isIgnored ? remove(imdbId) : ignore(imdbId))} type="button">
      {isIgnored ? 'Remove from ignore list' : `Ignore ${titleType}`}
    </button>
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}
import { useIgnoredMovies } from './IgnoredMoviesContext'
import { Eye, EyeOff, RotateCw } from 'lucide-react'

export function IgnoredMovieControl({ imdbId, mediaType, placement = 'inline' }: { imdbId: string | null; mediaType: string | null; placement?: 'inline' | 'poster' }) {
  const { available, movies, pending, loading, error, ignore, remove, retry } = useIgnoredMovies()
  if (!available || (mediaType !== 'movie' && mediaType !== 'series') || !imdbId) return null

  const isIgnored = Boolean(movies[imdbId])
  const titleType = mediaType === 'series' ? 'series' : 'movie'
  const disabled = loading || pending !== null || Boolean(error)
  if (placement === 'poster') {
    const label = isIgnored ? 'Remove from ignore list' : `Ignore ${titleType}`
    return <>
      <button
        aria-label={label}
        className={`tile-icon-action ignored-icon${isIgnored ? ' is-active' : ''}`}
        data-tooltip={label}
        disabled={disabled}
        onClick={() => void (isIgnored ? remove(imdbId) : ignore(imdbId))}
        type="button"
      >{isIgnored ? <Eye aria-hidden="true" size={16} /> : <EyeOff aria-hidden="true" size={16} />}</button>
      {error && <span className="tile-action-error" role="alert">{error}
        <button aria-label="Retry ignored title action" className="tile-icon-action" onClick={retry} type="button"><RotateCw aria-hidden="true" size={14} /></button>
      </span>}
    </>
  }

  return <div className="favorite-controls ignored-movie-controls">
    <button className="text-button" disabled={disabled} onClick={() => void (isIgnored ? remove(imdbId) : ignore(imdbId))} type="button">
      {isIgnored ? 'Remove from ignore list' : `Ignore ${titleType}`}
    </button>
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}
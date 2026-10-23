import { EmptyState, ErrorState, LoadingState } from '../../components/Feedback'
import { Poster } from '../../components/Poster'
import { useIgnoredMovies } from './IgnoredMoviesContext'

export function IgnoredMoviesView() {
  const { available, movies, pending, loading, error, remove, retry } = useIgnoredMovies()
  const items = Object.values(movies).sort((left, right) => left.title.localeCompare(right.title))

  return <section aria-label="Ignored titles" className="media-view ignored-movies-view">
    <div className="section-toolbar">
      <span className="result-count">{available && !error ? `${items.length} titles` : 'Ignored titles'}</span>
    </div>
    {!available && <EmptyState title="Sign in to view ignored titles" message="An active linked account is required." />}
    {available && loading && <LoadingState label="Loading ignored movies" />}
    {available && !loading && error && <ErrorState message={error} onRetry={retry} />}
    {available && !loading && !error && items.length === 0 && (
      <EmptyState title="No ignored titles" message="Your list is empty." />
    )}
    {available && !loading && !error && items.length > 0 && <div className="table-wrap">
      <table className="data-table ignored-movies-table">
        <thead><tr><th scope="col">TITLE</th><th scope="col">IMDB</th><th scope="col">RATING</th><th scope="col">ADDED</th><th scope="col">ACTION</th></tr></thead>
        <tbody>{items.map((movie) => <tr key={movie.imdbId}>
          <td><div className="title-cell">
            <Poster className="poster-small" src={movie.posterUrl} title={movie.title} />
            <span className="movie-table-title-details">
              <strong className="ignored-movie-title">{movie.title}</strong>
              <span className="subtle-line">{movie.year ?? 'Year unknown'} · {movie.mediaType ?? 'Unknown type'}</span>
            </span>
          </div></td>
          <td><a className="title-link" href={`https://www.imdb.com/title/${encodeURIComponent(movie.imdbId)}/`} rel="noreferrer" target="_blank">{movie.imdbId}</a></td>
          <td>{movie.imdbRating === null ? 'Not rated' : movie.imdbRating.toFixed(1)}</td>
          <td>{new Date(movie.createdAt).toLocaleDateString()}</td>
          <td><button className="text-button" disabled={pending !== null} onClick={() => void remove(movie.imdbId)} type="button">
            {pending === movie.imdbId ? 'Removing…' : 'Remove'}
          </button></td>
        </tr>)}</tbody>
      </table>
    </div>}
  </section>
}
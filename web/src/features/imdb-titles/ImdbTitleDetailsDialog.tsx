import type { ImdbTitleCatalogItem, MovieAwardRecognition } from '../../api/types'
import { MovieAwardsList, MovieDetailsIntro } from '../../components/MoviePresentation'
import { formatWords } from '../../shared/format'
import { FavoriteControls } from '../favorites/FavoriteControls'
import { IgnoredMovieControl } from '../ignored-movies/IgnoredMovieControl'

interface ImdbTitleDetailsDialogProps {
  item: ImdbTitleCatalogItem
  awards: MovieAwardRecognition[]
  hasActiveAccount: boolean
  onClose: () => void
  onRate: () => void
  onUpdated: () => void
}

export function ImdbTitleDetailsDialog({
  item,
  awards,
  hasActiveAccount,
  onClose,
  onRate,
  onUpdated,
}: ImdbTitleDetailsDialogProps) {
  const titleType = item.mediaType ? formatWords(item.mediaType) : 'Type unknown'

  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget) onClose()
  }}>
    <section aria-labelledby="imdb-title-details-heading" aria-modal="true" className="detail-dialog" role="dialog">
      <header className="detail-header">
        <div>
          <p className="eyebrow">IMDb TITLE</p>
          <h2 id="imdb-title-details-heading">{item.title}</h2>
          <div className="detail-meta">{titleType} | {item.year ?? 'Year unknown'} | {formatWords(item.metadataStatus)}</div>
        </div>
        <button aria-label="Close IMDb title details" className="button button-secondary" onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        <MovieDetailsIntro imdbId={item.imdbId} imdbRating={item.imdbRating} posterLabel={item.mediaType?.slice(0, 3).toUpperCase() ?? 'IMDb'} posterUrl={item.posterUrl} title={item.title}>
          {item.genres.length > 0 && <span>{item.genres.join(' · ')}</span>}
          {item.plot && <p className="detail-description">{item.plot}</p>}
        </MovieDetailsIntro>
        <dl className="detail-facts imdb-title-detail-facts">
          <div><dt>IMDb ID</dt><dd>{item.imdbId}</dd></div>
          <div><dt>IMDb votes</dt><dd>{item.imdbVotes?.toLocaleString() ?? 'Not listed'}</dd></div>
          <div><dt>Metascore</dt><dd>{item.metascore?.toFixed(0) ?? 'Not listed'}</dd></div>
          <div><dt>Director</dt><dd>{item.director ?? 'Not listed'}</dd></div>
          <div><dt>Runtime</dt><dd>{item.runtime ?? 'Not listed'}</dd></div>
          <div><dt>Genres</dt><dd>{item.genres.join(', ') || 'Not tagged'}</dd></div>
          <div><dt>Countries</dt><dd>{item.countries.join(', ') || 'Not tagged'}</dd></div>
          <div><dt>Box office</dt><dd>{item.boxOffice ?? 'Not listed'}</dd></div>
          <div><dt>Your rating</dt><dd>{item.personalRating === null ? 'Not rated' : `${item.personalRating} / 10`}</dd></div>
          <div><dt>Favorite</dt><dd>{item.isFavorite ? 'Yes' : 'No'}</dd></div>
          <div><dt>Ignored</dt><dd>{item.isIgnored ? 'Yes' : 'No'}</dd></div>
        </dl>
        <div className="imdb-title-detail-actions">
          <FavoriteControls
            from="imdb-titles"
            imdbId={item.imdbId}
            metadataStatus={item.metadataStatus}
            mediaType={item.mediaType}
            occurrenceCount={0}
            onAdded={onUpdated}
            onRemoved={onUpdated}
            onUpdated={onUpdated}
          />
          <IgnoredMovieControl imdbId={item.imdbId} mediaType={item.mediaType} onChanged={onUpdated} />
          {hasActiveAccount && item.metadataStatus === 'enriched' && item.personalRating === null && (
            <button className="button button-secondary" onClick={onRate} type="button">Rate title</button>
          )}
        </div>
        {item.awards && <section className="imdb-title-omdb-awards">
          <h2>OMDb awards</h2>
          <p>{item.awards}</p>
        </section>}
        <MovieAwardsList awards={awards} />
      </div>
    </section>
  </div>
}
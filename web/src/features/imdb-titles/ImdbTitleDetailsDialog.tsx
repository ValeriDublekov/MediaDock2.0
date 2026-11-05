import type { ImdbTitleCatalogItem, MovieAwardRecognition } from '../../api/types'
import { MovieAwardsList } from '../../components/MoviePresentation'
import { MovieDetailsTemplate } from '../../components/MovieDetailsTemplate'
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

  return <MovieDetailsTemplate
    actions={<>
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
    </>}
    eyebrow="IMDb TITLE"
    facts={<>
      <div><dt>IMDb ID</dt><dd>{item.imdbId}</dd></div>
      <div><dt>Metascore</dt><dd>{item.metascore?.toFixed(0) ?? 'Not listed'}</dd></div>
      <div><dt>Your rating</dt><dd>{item.personalRating === null ? 'Not rated' : `${item.personalRating} / 10`}</dd></div>
      <div><dt>Favorite</dt><dd>{item.isFavorite ? 'Yes' : 'No'}</dd></div>
      <div><dt>Ignored</dt><dd>{item.isIgnored ? 'Yes' : 'No'}</dd></div>
      <div><dt>Box office</dt><dd>{item.boxOffice ?? 'Not listed'}</dd></div>
    </>}
    genres={item.genres}
    imdbId={item.imdbId}
    imdbRating={item.imdbRating}
    imdbVotes={item.imdbVotes}
    onClose={onClose}
    posterLabel={item.mediaType?.slice(0, 3).toUpperCase() ?? 'IMDb'}
    posterUrl={item.posterUrl}
    plot={item.plot}
    title={item.title}
    typeLabel={titleType}
    year={item.year}
    director={item.director}
    countries={item.countries}
    runtime={item.runtime}
  >
    {item.awards && <section className="imdb-title-omdb-awards">
      <h2>OMDb awards</h2>
      <p>{item.awards}</p>
    </section>}
    <MovieAwardsList awards={awards} />
  </MovieDetailsTemplate>
}
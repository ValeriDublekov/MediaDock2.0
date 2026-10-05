import type { GoldenGlobeFilm, MovieAwardRecognition } from '../../api/types'
import { MovieAwardsList, MovieDetailsIntro } from '../../components/MoviePresentation'
import { formatWords } from '../../shared/format'

interface GoldenGlobeFilmDetailsDialogProps {
  film: GoldenGlobeFilm
  awards: MovieAwardRecognition[]
  onClose: () => void
}

export function GoldenGlobeFilmDetailsDialog({ film, awards, onClose }: GoldenGlobeFilmDetailsDialogProps) {
  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget) onClose()
  }}>
    <section aria-labelledby="golden-globe-film-details-heading" aria-modal="true" className="detail-dialog" role="dialog">
      <header className="detail-header">
        <div>
          <p className="eyebrow">GOLDEN GLOBES</p>
          <h2 id="golden-globe-film-details-heading">{film.title}</h2>
          <div className="detail-meta">Ceremony year {film.year} | {formatWords(film.enrichmentStatus)}</div>
        </div>
        <button aria-label="Close Golden Globes details" className="button button-secondary" onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        <MovieDetailsIntro imdbId={film.imdbId} imdbRating={film.imdbRating} posterLabel="GLO" posterUrl={film.posterUrl} title={film.title}>
          {film.enrichmentError && <p className="golden-globe-enrichment-error">{formatWords(film.enrichmentError.replaceAll('_', ' '))}</p>}
        </MovieDetailsIntro>
        <dl className="detail-facts">
          <div><dt>IMDb ID</dt><dd>{film.imdbId ?? 'Not listed'}</dd></div>
          <div><dt>IMDb rating</dt><dd>{film.imdbRating?.toFixed(1) ?? 'Not rated'}</dd></div>
          <div><dt>OMDb status</dt><dd>{formatWords(film.enrichmentStatus)}</dd></div>
        </dl>
        <MovieAwardsList awards={awards} />
      </div>
    </section>
  </div>
}
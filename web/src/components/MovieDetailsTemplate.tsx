import { useId, type ReactNode } from 'react'
import { MovieDetailsIntro } from './MoviePresentation'

interface MovieDetailsTemplateProps {
  eyebrow: string
  title: string
  year?: number | null
  yearLabel?: string
  typeLabel?: string | null
  statusLabel?: string | null
  posterUrl: string | null
  posterLabel: string
  imdbId: string | null
  imdbRating: number | null
  imdbVotes?: number | null
  runtime?: string | null
  genres?: string[]
  director?: string | null
  countries?: string[]
  plot?: string | null
  posterPreviewOnHover?: boolean
  showOverview?: boolean
  summaryContent?: ReactNode
  facts?: ReactNode
  details?: ReactNode
  actions?: ReactNode
  actionsBeforeFacts?: boolean
  className?: string
  onClose: () => void
  children?: ReactNode
}

export function MovieDetailsTemplate({
  eyebrow,
  title,
  year,
  yearLabel = 'Year unknown',
  typeLabel,
  statusLabel,
  posterUrl,
  posterLabel,
  imdbId,
  imdbRating,
  imdbVotes,
  runtime,
  genres,
  director,
  countries,
  plot,
  posterPreviewOnHover = false,
  showOverview = true,
  summaryContent,
  facts,
  details,
  actions,
  actionsBeforeFacts = false,
  className = '',
  onClose,
  children,
}: MovieDetailsTemplateProps) {
  const headingId = useId()
  const yearText = year === undefined ? null : year === null ? yearLabel : String(year)
  const metadata = [typeLabel, statusLabel].filter(Boolean).join(' | ')

  return <div className="detail-backdrop" onMouseDown={(event) => {
    if (event.target === event.currentTarget) onClose()
  }}>
    <section
      aria-labelledby={headingId}
      aria-modal="true"
      className={`detail-dialog movie-detail-dialog ${className}`.trim()}
      onKeyDown={(event) => { if (event.key === 'Escape') onClose() }}
      role="dialog"
      tabIndex={-1}
    >
      <header className="detail-header">
        <div>
          <h2 id={headingId}>{title}{yearText !== null && <span className="movie-detail-year"> ({yearText})</span>}</h2>
          {metadata && <div className="detail-meta">{metadata}</div>}
        </div>
        <button aria-label={`Close ${eyebrow.toLowerCase()} details`} className="button button-secondary" onClick={onClose} type="button">Close</button>
      </header>
      <div className="detail-body">
        {showOverview && <MovieDetailsIntro
          imdbId={imdbId}
          imdbRating={imdbRating}
          posterLabel={posterLabel}
          posterPreviewOnHover={posterPreviewOnHover}
          posterUrl={posterUrl}
          title={title}
        >
          {(imdbVotes !== undefined || runtime) && <span className="movie-detail-summary-meta">
            {imdbVotes !== undefined && (imdbVotes === null ? 'Votes not listed' : `${imdbVotes.toLocaleString()} votes`)}
            {imdbVotes !== undefined && runtime && ' · '}
            {runtime}
          </span>}
          {genres !== undefined && <span className="movie-detail-genres">{genres.join(' · ') || 'Genres unavailable'}</span>}
          {plot?.trim() && <p className="detail-description">{plot}</p>}
          {(director !== undefined || countries !== undefined) && <dl className="detail-facts movie-detail-inline-facts">
            {director !== undefined && <div><dt>Director</dt><dd>{director?.trim() || 'Not listed'}</dd></div>}
            {countries !== undefined && <div><dt>Countries</dt><dd>{countries.join(', ') || 'Not tagged'}</dd></div>}
          </dl>}
          {summaryContent && <div className="movie-detail-summary-extra">{summaryContent}</div>}
        </MovieDetailsIntro>}
        {actionsBeforeFacts && actions && <div aria-label="Movie actions" className="movie-detail-actions" role="group">{actions}</div>}
        {facts && <dl className="detail-facts movie-detail-extra-facts">{facts}</dl>}
        {!actionsBeforeFacts && actions && <div aria-label="Movie actions" className="movie-detail-actions" role="group">{actions}</div>}
        {details}
        {children}
      </div>
    </section>
  </div>
}
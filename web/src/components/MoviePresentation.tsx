import type { ReactNode } from 'react'
import type { MovieAwardRecognition, MovieAwardSource } from '../api/types'
import { formatWords } from '../shared/format'
import { Poster } from './Poster'

interface MovieTitleMetadata {
  director: string | null
  plot: string | null
}

function MovieTitleFacts({ director, plot }: MovieTitleMetadata) {
  const synopsis = plot?.trim()
  return <div className="movie-title-metadata">
    <span className="movie-title-director">Director: {director?.trim() || 'Not listed'}</span>
    <p className="movie-title-plot" title={synopsis || undefined}>{synopsis || 'Plot unavailable'}</p>
  </div>
}

interface MoviePosterCardProps {
  title: string
  year: number | null
  yearLabel?: string
  mediaType: string | null
  imdbId: string | null
  imdbRating: number | null
  posterUrl: string | null
  posterLabel?: string
  genres?: string[]
  metadata?: MovieTitleMetadata
  awards: MovieAwardRecognition[]
  showAwards?: boolean
  onOpen?: () => void
  openLabel?: string
  titleHref?: string
  posterBadge?: ReactNode
  posterActions?: ReactNode
  children?: ReactNode
  footer?: ReactNode
}

export function MoviePosterCard({
  title,
  year,
  yearLabel = 'Film year',
  mediaType,
  imdbId,
  imdbRating,
  posterUrl,
  posterLabel = 'FILM',
  genres,
  metadata,
  awards,
  showAwards = true,
  onOpen,
  openLabel,
  titleHref,
  posterBadge,
  posterActions,
  children,
  footer,
}: MoviePosterCardProps) {
  const poster = <>
    <Poster label={posterLabel} src={posterUrl} title={title} />
    {mediaType && <span className="poster-type-badge">{formatWords(mediaType)}</span>}
    {posterBadge}
  </>

  return <article className={`movie-tile${onOpen ? ' is-clickable' : ''}`} onClick={onOpen ? (event) => {
    if (event.target instanceof Element && event.target.closest('a, button, input, label, select, textarea, summary')) return
    onOpen()
  } : undefined}>
    <div className="poster-stage">
      {onOpen
        ? <button aria-label={openLabel ?? `View ${title} details`} className="poster-action" onClick={onOpen} type="button">{poster}</button>
        : <div className="poster-display">{poster}</div>}
      {posterActions && <div className="poster-overlay-actions">{posterActions}</div>}
    </div>
    <div className="movie-tile-info">
      <div className="tile-heading">
        {onOpen
          ? <button className="tile-title" onClick={onOpen} type="button">{title}</button>
          : titleHref
            ? <a className="tile-title" href={titleHref} rel="noopener noreferrer" target="_blank">{title}</a>
            : <h3 className="tile-title">{title}</h3>}
        <span className="tile-year" title={year === null ? `${yearLabel} unknown` : `${yearLabel} ${year}`}>{year ?? '—'}</span>
      </div>
      <MovieImdbLink imdbId={imdbId} rating={imdbRating} title={title} />
      {metadata && <MovieTitleFacts director={metadata.director} plot={metadata.plot} />}
      <div className="tile-genres">
        {genres === undefined ? null : genres.length > 0
          ? genres.slice(0, 3).map((genre) => <span className="tile-genre" key={genre}>{genre}</span>)
          : <span>Genres unavailable</span>}
      </div>
      {showAwards && <MovieAwardsSummary awards={awards} />}
      {children}
      {footer}
    </div>
  </article>
}

interface MovieTableTitleProps {
  title: string
  posterUrl: string | null
  posterLabel: string
  openLabel: string
  subtitle?: ReactNode
  onOpen: () => void
  metadata?: MovieTitleMetadata
  children?: ReactNode
}

export function MovieTableTitle({ title, posterUrl, posterLabel, openLabel, subtitle, onOpen, metadata, children }: MovieTableTitleProps) {
  return <div className="title-cell">
    <Poster className="poster-small" label={posterLabel} src={posterUrl} title={title} />
    <span className="movie-table-title-details">
      <button aria-label={openLabel} className="title-link" onClick={onOpen} type="button">{title}</button>
      {subtitle !== undefined && <span className="subtle-line">{subtitle}</span>}
      {metadata && <MovieTitleFacts director={metadata.director} plot={metadata.plot} />}
      {children}
    </span>
  </div>
}

interface MovieDetailsIntroProps {
  title: string
  posterUrl: string | null
  posterLabel: string
  imdbId: string | null
  imdbRating: number | null
  children?: ReactNode
}

export function MovieDetailsIntro({ title, posterUrl, posterLabel, imdbId, imdbRating, children }: MovieDetailsIntroProps) {
  return <div className="film-details-intro">
    <Poster className="detail-poster" label={posterLabel} src={posterUrl} title={title} />
    <div className="film-details-summary">
      <MovieImdbLink imdbId={imdbId} rating={imdbRating} title={title} />
      {children}
    </div>
  </div>
}

interface MovieImdbLinkProps {
  imdbId: string | null
  rating: number | null
  title: string
  className?: string
}

export function MovieImdbLink({ imdbId, rating, title, className = '' }: MovieImdbLinkProps) {
  const value = rating === null ? 'Not rated' : rating.toFixed(1)
  const content = <>
    <span>IMDb</span>
    <strong>{value}</strong>
    {rating !== null && <span className="tile-rating-scale">/ 10</span>}
  </>
  const classes = `tile-rating movie-imdb-link ${className}`.trim()

  return imdbId
    ? <a aria-label={`Open ${title} on IMDb (opens in new tab)`} className={classes} href={`https://www.imdb.com/title/${imdbId}/`} rel="noopener noreferrer" target="_blank">{content}</a>
    : <span aria-label={`IMDb rating for ${title}: ${value}`} className={classes}>{content}</span>
}

const sourceLabels: Record<MovieAwardSource, string> = {
  oscars: 'Oscars',
  golden_globes: 'Golden Globes',
}

export function MovieAwardsSummary({ awards, compact = false }: { awards: MovieAwardRecognition[]; compact?: boolean }) {
  const className = `movie-awards-summary${compact ? ' is-compact' : ''}`
  if (awards.length === 0) {
    return <div aria-label="Awards and nominations" className={className}>
      <strong className="movie-awards-summary-title">Awards</strong>
      <span className="movie-awards-empty">No linked nominations</span>
    </div>
  }

  const sources: MovieAwardSource[] = ['oscars', 'golden_globes']
  return <div aria-label="Awards and nominations" className={className}>
    <strong className="movie-awards-summary-title">Awards and nominations</strong>
    {sources.map((source) => {
      const records = awards.filter((award) => award.source === source)
      if (records.length === 0) return null
      const wins = records.filter((award) => award.isWinner).length
      const detailText = records.map((award) => {
        const details = [award.isWinner ? 'Winner' : 'Nominee', award.award]
        if (award.filmYear !== null) details.push(`Film year ${award.filmYear}`)
        if (award.ceremonyYear !== null) details.push(`Ceremony year ${award.ceremonyYear}`)
        if (award.ceremony !== null) details.push(`Ceremony ${award.ceremony}`)
        if (award.nominees) details.push(award.nominees)
        if (award.name && award.name !== award.nominees) details.push(award.name)
        if (award.detail) details.push(award.detail)
        return details.join(' · ')
      }).join('\n')
      return <div aria-label={`${sourceLabels[source]} awards and nominations`} className="movie-awards-source" key={source} role="group" tabIndex={0} title={detailText}>
        <div className="movie-awards-source-heading">
          <strong>{sourceLabels[source]}</strong>
          <span>{wins} {wins === 1 ? 'win' : 'wins'} · {records.length} {records.length === 1 ? 'nomination' : 'nominations'}</span>
        </div>
      </div>
    })}
  </div>
}

export function MovieAwardsList({ awards }: { awards: MovieAwardRecognition[] }) {
  return <section className="movie-awards-list">
    <div className="section-title-row"><h2>Awards and nominations</h2></div>
    {awards.length === 0
      ? <p className="movie-awards-empty">No linked Oscar or Golden Globes nominations.</p>
      : <div className="movie-awards-records">
        {awards.map((award) => <article className="movie-award-record" key={`${award.source}:${award.id}`}>
          <div className="movie-award-record-meta">
            <span>{sourceLabels[award.source]}</span>
            {award.filmYear !== null && <span>Film year {award.filmYear}</span>}
            {award.ceremonyYear !== null && <span>Ceremony year {award.ceremonyYear}</span>}
            {award.ceremony !== null && <span>Ceremony {award.ceremony}</span>}
            <span className={`state-pill ${award.isWinner ? 'is-winner' : 'is-muted'}`}>{award.isWinner ? 'Winner' : 'Nominee'}</span>
          </div>
          <h3>{award.award}</h3>
          {award.nominees && <p>{award.nominees}</p>}
          {award.name && award.name !== award.nominees && <p className="movie-award-name">{award.name}</p>}
          {award.detail && <p className="movie-award-detail">{award.detail}</p>}
        </article>)}
      </div>}
  </section>
}
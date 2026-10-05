import type { ReactNode } from 'react'
import type { MovieAwardRecognition, MovieAwardSource } from '../api/types'
import { formatWords } from '../shared/format'
import { Poster } from './Poster'

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
  awards: MovieAwardRecognition[]
  onOpen?: () => void
  openLabel?: string
  posterBadge?: ReactNode
  children?: ReactNode
  actions?: ReactNode
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
  awards,
  onOpen,
  openLabel,
  posterBadge,
  children,
  actions,
  footer,
}: MoviePosterCardProps) {
  const poster = <>
    <Poster label={posterLabel} src={posterUrl} title={title} />
    {mediaType && <span className="poster-type-badge">{formatWords(mediaType)}</span>}
    {posterBadge}
  </>

  return <article className="movie-tile">
    {onOpen
      ? <button aria-label={openLabel ?? `View ${title} details`} className="poster-action" onClick={onOpen} type="button">{poster}</button>
      : <div className="poster-display">{poster}</div>}
    <div className="movie-tile-info">
      <div className="tile-heading">
        {onOpen
          ? <button className="tile-title" onClick={onOpen} type="button">{title}</button>
          : <h3 className="tile-title">{title}</h3>}
        <span className="tile-year" title={year === null ? `${yearLabel} unknown` : `${yearLabel} ${year}`}>{year ?? '—'}</span>
      </div>
      <MovieImdbLink imdbId={imdbId} rating={imdbRating} title={title} />
      <div className="tile-genres">
        {genres === undefined ? null : genres.length > 0
          ? genres.slice(0, 3).map((genre) => <span className="tile-genre" key={genre}>{genre}</span>)
          : <span>Genres unavailable</span>}
      </div>
      <MovieAwardsSummary awards={awards} />
      {children}
      {actions && <div className="tile-actions">{actions}</div>}
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
  children?: ReactNode
}

export function MovieTableTitle({ title, posterUrl, posterLabel, openLabel, subtitle, onOpen, children }: MovieTableTitleProps) {
  return <div className="title-cell">
    <Poster className="poster-small" label={posterLabel} src={posterUrl} title={title} />
    <span className="movie-table-title-details">
      <button aria-label={openLabel} className="title-link" onClick={onOpen} type="button">{title}</button>
      {subtitle !== undefined && <span className="subtle-line">{subtitle}</span>}
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
      const names = [...new Set(records.map((award) => award.award))]
      return <div className="movie-awards-source" key={source}>
        <div className="movie-awards-source-heading">
          <strong>{sourceLabels[source]}</strong>
          <span>{wins} {wins === 1 ? 'win' : 'wins'} · {records.length} {records.length === 1 ? 'nomination' : 'nominations'}</span>
        </div>
        <span className="movie-awards-categories">
          {names.slice(0, 2).join(' · ')}{names.length > 2 ? ` · +${names.length - 2}` : ''}
        </span>
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
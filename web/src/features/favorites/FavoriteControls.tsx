import { useFavorites } from './FavoriteContext'
import { Star } from 'lucide-react'

export function FavoriteControls({ imdbId, metadataStatus, from, mediaType, occurrenceCount, placement = 'inline' }: {
  imdbId: string | null
  metadataStatus: string | null
  from: 'oscar' | 'catalog'
  mediaType: string
  occurrenceCount: number
  placement?: 'inline' | 'poster' | 'status'
}) {
  const { available, titles, pending, loading, error, add, update, remove, retry } = useFavorites()
  if (!available || !imdbId) return null
  const isMovie = mediaType === 'movie'
  const favorite = titles[imdbId]
  if (!favorite && (!isMovie || metadataStatus !== 'enriched')) return null
  const disabled = pending === imdbId || loading || Boolean(error)
  const canAddFromHere = isMovie && metadataStatus === 'enriched'
  const selectedFromHere = from === 'oscar' ? favorite?.addedFromOscar : favorite?.addedFromCatalog

  if (placement === 'poster') {
    const removing = Boolean(favorite && (selectedFromHere || !canAddFromHere))
    const label = removing ? 'Remove from favorites' : favorite ? `Add from ${from === 'oscar' ? 'Oscar' : 'catalog'} to favorites` : 'Add to favorites'
    return <button
      aria-label={label}
      className={`tile-icon-action favorite-icon${favorite ? ' is-active' : ''}`}
      data-tooltip={label}
      disabled={disabled}
      onClick={() => void (removing ? remove(imdbId) : add(imdbId, from))}
      type="button"
    ><Star aria-hidden="true" fill={favorite ? 'currentColor' : 'none'} size={16} strokeWidth={2} /></button>
  }

  if (placement === 'status') {
    if (!favorite && !error) return null
    return <div className="favorite-card-statuses">
      {favorite && <>
        <label><input checked={favorite.toWatch} disabled={disabled} onChange={(event) => void update(imdbId, { toWatch: event.target.checked })} type="checkbox" /> To watch</label>
        {isMovie && <>
          <label title={occurrenceCount === 0 && favorite.occurrenceCount === 0 ? 'No torrent source available' : undefined}>
            <input checked={favorite.toDownload} disabled={disabled} onChange={(event) => void update(imdbId, { toDownload: event.target.checked })} type="checkbox" /> To download
          </label>
          {occurrenceCount === 0 && favorite.occurrenceCount === 0 && <span className="subtle-line">No torrent source available</span>}
        </>}
        {!selectedFromHere && canAddFromHere && <button className="text-button" disabled={disabled} onClick={() => void add(imdbId, from)} type="button">
          Add from {from === 'oscar' ? 'Oscar' : 'catalog'}
        </button>}
        <button className="text-button" disabled={disabled} onClick={() => void remove(imdbId)} type="button">Remove from favorites</button>
      </>}
      {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
    </div>
  }

  return <div className="favorite-controls">
    {!selectedFromHere && canAddFromHere && <button className="text-button" disabled={disabled} onClick={() => void add(imdbId, from)} type="button">
      {favorite ? `Add from ${from === 'oscar' ? 'Oscar' : 'catalog'}` : 'Add to favorites'}
    </button>}
    {favorite && <button className="text-button" disabled={disabled} onClick={() => void remove(imdbId)} type="button">Remove from favorites</button>}
    {favorite && <>
      <label><input checked={favorite.toWatch} disabled={disabled} onChange={(event) => void update(imdbId, { toWatch: event.target.checked })} type="checkbox" /> To watch</label>
      {isMovie && <>
        <label title={occurrenceCount === 0 && favorite.occurrenceCount === 0 ? 'No torrent source available' : undefined}>
          <input checked={favorite.toDownload} disabled={disabled} onChange={(event) => void update(imdbId, { toDownload: event.target.checked })} type="checkbox" /> To download
        </label>
        {occurrenceCount === 0 && favorite.occurrenceCount === 0 && <span className="subtle-line">No torrent source available</span>}
      </>}
    </>}
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}

export function RecommendationWatchlistControl({ imdbId, title, onAdded }: {
  imdbId: string
  title: string
  onAdded: () => void
}) {
  const { available, titles, pending, loading, error, add, retry } = useFavorites()
  if (!available) return null
  if (titles[imdbId]?.toWatch) return <span className="subtle-line">In watch list</span>

  return <div className="favorite-controls recommendation-watchlist-control">
    <button
      aria-label={`Add ${title} to watch list`}
      className="button button-secondary"
      disabled={pending === imdbId || loading || Boolean(error)}
      onClick={() => { void add(imdbId, 'recommendation').then((added) => { if (added) onAdded() }) }}
      type="button"
    >Add to watch list</button>
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}
import { useFavorites } from './FavoriteContext'
import { BookmarkCheck, BookmarkPlus, Star } from 'lucide-react'

export function FavoriteControls({ imdbId, metadataStatus, from, mediaType, occurrenceCount, placement = 'inline', onAdded, onRemoved, onUpdated }: {
  imdbId: string | null
  metadataStatus: string | null
  from: 'oscar' | 'catalog'
  mediaType: string
  occurrenceCount: number
  placement?: 'inline' | 'poster' | 'status'
  onAdded?: () => void
  onRemoved?: () => void
  onUpdated?: (input: { toWatch?: boolean; toDownload?: boolean }) => void
}) {
  const { available, titles, pending, loading, error, add, update, remove, retry } = useFavorites()
  if (!available || !imdbId) return null
  const favoriteImdbId = imdbId
  const isMovie = mediaType === 'movie'
  const favorite = titles[favoriteImdbId]
  if (!favorite && (!isMovie || metadataStatus !== 'enriched')) return null
  const disabled = pending === favoriteImdbId || loading || Boolean(error)
  const canAddFromHere = isMovie && metadataStatus === 'enriched'
  const selectedFromHere = from === 'oscar' ? favorite?.addedFromOscar : favorite?.addedFromCatalog
  function addFromHere() {
    void add(favoriteImdbId, from).then((added) => { if (added) onAdded?.() })
  }
  function removeFromFavorites() {
    void remove(favoriteImdbId).then((removed) => { if (removed) onRemoved?.() })
  }
  function updateFavorite(input: { toWatch?: boolean; toDownload?: boolean }) {
    void update(favoriteImdbId, input).then((updated) => { if (updated) onUpdated?.(input) })
  }

  if (placement === 'poster') {
    const removing = Boolean(favorite && (selectedFromHere || !canAddFromHere))
    const label = removing ? 'Remove from favorites' : favorite ? `Add from ${from === 'oscar' ? 'Oscar' : 'catalog'} to favorites` : 'Add to favorites'
    return <button
      aria-label={label}
      className={`tile-icon-action favorite-icon${favorite ? ' is-active' : ''}`}
      data-tooltip={label}
      disabled={disabled}
      onClick={() => { if (removing) removeFromFavorites(); else addFromHere() }}
      type="button"
    ><Star aria-hidden="true" fill={favorite ? 'currentColor' : 'none'} size={16} strokeWidth={2} /></button>
  }

  if (placement === 'status') {
    if (!favorite && !error) return null
    return <div className="favorite-card-statuses">
      {favorite && <>
        <label><input checked={favorite.toWatch} disabled={disabled} onChange={(event) => updateFavorite({ toWatch: event.target.checked })} type="checkbox" /> To watch</label>
        {isMovie && <>
          <label title={occurrenceCount === 0 && favorite.occurrenceCount === 0 ? 'No torrent source available' : undefined}>
            <input checked={favorite.toDownload} disabled={disabled} onChange={(event) => updateFavorite({ toDownload: event.target.checked })} type="checkbox" /> To download
          </label>
          {occurrenceCount === 0 && favorite.occurrenceCount === 0 && <span className="subtle-line">No torrent source available</span>}
        </>}
        {!selectedFromHere && canAddFromHere && <button className="text-button" disabled={disabled} onClick={addFromHere} type="button">
          Add from {from === 'oscar' ? 'Oscar' : 'catalog'}
        </button>}
        {!selectedFromHere && canAddFromHere && <button className="text-button" disabled={disabled} onClick={removeFromFavorites} type="button">Remove from favorites</button>}
      </>}
      {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
    </div>
  }

  return <div className="favorite-controls">
    {!selectedFromHere && canAddFromHere && <button className="text-button" disabled={disabled} onClick={addFromHere} type="button">
      {favorite ? `Add from ${from === 'oscar' ? 'Oscar' : 'catalog'}` : 'Add to favorites'}
    </button>}
    {favorite && <button className="text-button" disabled={disabled} onClick={removeFromFavorites} type="button">Remove from favorites</button>}
    {favorite && <>
      <label><input checked={favorite.toWatch} disabled={disabled} onChange={(event) => updateFavorite({ toWatch: event.target.checked })} type="checkbox" /> To watch</label>
      {isMovie && <>
        <label title={occurrenceCount === 0 && favorite.occurrenceCount === 0 ? 'No torrent source available' : undefined}>
          <input checked={favorite.toDownload} disabled={disabled} onChange={(event) => updateFavorite({ toDownload: event.target.checked })} type="checkbox" /> To download
        </label>
        {occurrenceCount === 0 && favorite.occurrenceCount === 0 && <span className="subtle-line">No torrent source available</span>}
      </>}
    </>}
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}

export function RecommendationWatchlistControl({ imdbId, title, onAdded, placement = 'inline' }: {
  imdbId: string
  title: string
  onAdded: () => void
  placement?: 'inline' | 'poster'
}) {
  const { available, titles, pending, loading, error, add, retry } = useFavorites()
  if (!available) return null
  const isInWatchList = Boolean(titles[imdbId]?.toWatch)

  if (placement === 'poster') {
    const label = isInWatchList ? `${title} is in watch list` : `Add ${title} to watch list`
    return <button
      aria-label={label}
      className={`tile-icon-action favorite-icon${isInWatchList ? ' is-active' : ''}`}
      data-tooltip={label}
      disabled={isInWatchList || pending === imdbId || loading || Boolean(error)}
      onClick={() => { void add(imdbId, 'recommendation').then((added) => { if (added) onAdded() }) }}
      type="button"
    >{isInWatchList ? <BookmarkCheck aria-hidden="true" size={16} /> : <BookmarkPlus aria-hidden="true" size={16} />}</button>
  }
  if (isInWatchList) return <span className="subtle-line">In watch list</span>

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
import { useFavorites } from './FavoriteContext'

export function FavoriteControls({ titleId, from, mediaType, occurrenceCount }: {
  titleId: number
  from: 'oscar' | 'catalog'
  mediaType: string
  occurrenceCount: number
}) {
  const { movies, pending, loading, error, add, update, remove, retry } = useFavorites()
  if (mediaType !== 'movie') return null
  const favorite = movies[titleId]
  const disabled = pending === titleId || loading || Boolean(error)
  const selectedFromHere = from === 'oscar' ? favorite?.addedFromOscar : favorite?.addedFromCatalog
  return <div className="favorite-controls">
    {!selectedFromHere && <button className="text-button" disabled={disabled} onClick={() => void add(titleId, from)} type="button">
      {favorite ? `Add from ${from === 'oscar' ? 'Oscar' : 'catalog'}` : 'Add to favorites'}
    </button>}
    {favorite && <button className="text-button" disabled={disabled} onClick={() => void remove(titleId)} type="button">Remove from favorites</button>}
    {favorite && <>
      <label><input checked={favorite.toWatch} disabled={disabled} onChange={(event) => void update(titleId, { toWatch: event.target.checked })} type="checkbox" /> To watch</label>
      <label title={occurrenceCount === 0 && favorite.occurrenceCount === 0 ? 'No torrent source available' : undefined}>
        <input checked={favorite.toDownload} disabled={disabled || (occurrenceCount === 0 && favorite.occurrenceCount === 0 && !favorite.toDownload)} onChange={(event) => void update(titleId, { toDownload: event.target.checked })} type="checkbox" /> To download
      </label>
      {occurrenceCount === 0 && favorite.occurrenceCount === 0 && <span className="subtle-line">No torrent source available</span>}
    </>}
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}
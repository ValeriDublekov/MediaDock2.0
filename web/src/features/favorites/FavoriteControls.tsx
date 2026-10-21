import { useFavorites } from './FavoriteContext'

export function FavoriteControls({ imdbId, metadataStatus, from, mediaType, occurrenceCount }: {
  imdbId: string | null
  metadataStatus: string | null
  from: 'oscar' | 'catalog'
  mediaType: string
  occurrenceCount: number
}) {
  const { available, movies, pending, loading, error, add, update, remove, retry } = useFavorites()
  if (mediaType !== 'movie' || !available || !imdbId) return null
  const favorite = movies[imdbId]
  if (!favorite && metadataStatus !== 'enriched') return null
  const disabled = pending === imdbId || loading || Boolean(error)
  const canAddFromHere = metadataStatus === 'enriched'
  const selectedFromHere = from === 'oscar' ? favorite?.addedFromOscar : favorite?.addedFromCatalog
  return <div className="favorite-controls">
    {!selectedFromHere && canAddFromHere && <button className="text-button" disabled={disabled} onClick={() => void add(imdbId, from)} type="button">
      {favorite ? `Add from ${from === 'oscar' ? 'Oscar' : 'catalog'}` : 'Add to favorites'}
    </button>}
    {favorite && <button className="text-button" disabled={disabled} onClick={() => void remove(imdbId)} type="button">Remove from favorites</button>}
    {favorite && <>
      <label><input checked={favorite.toWatch} disabled={disabled} onChange={(event) => void update(imdbId, { toWatch: event.target.checked })} type="checkbox" /> To watch</label>
      <label title={occurrenceCount === 0 && favorite.occurrenceCount === 0 ? 'No torrent source available' : undefined}>
        <input checked={favorite.toDownload} disabled={disabled} onChange={(event) => void update(imdbId, { toDownload: event.target.checked })} type="checkbox" /> To download
      </label>
      {occurrenceCount === 0 && favorite.occurrenceCount === 0 && <span className="subtle-line">No torrent source available</span>}
    </>}
    {error && <span role="alert">{error} <button className="text-button" onClick={retry} type="button">Retry</button></span>}
  </div>
}
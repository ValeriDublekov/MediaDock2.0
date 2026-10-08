import type { ImdbRatingBounds } from '../api/types'

interface ImdbRatingRangeFilterProps {
  bounds: ImdbRatingBounds
  id: string
  valueFrom: number
  valueTo: number
  onPreview: (from: number, to: number) => void
  onCommit: (from: number, to: number) => void
}

const commitKeys = new Set(['ArrowDown', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'End', 'Home', 'PageDown', 'PageUp'])
const formatRating = (rating: number) => Number(rating.toFixed(1)).toString()

export function ImdbRatingRangeFilter({ bounds, id, valueFrom, valueTo, onPreview, onCommit }: ImdbRatingRangeFilterProps) {
  function preview(from: number, to: number) {
    onPreview(from, to)
  }

  function finish() {
    onCommit(valueFrom, valueTo)
  }

  return (
    <fieldset className="year-range-filter imdb-rating-range-filter">
      <legend>IMDb rating</legend>
      <div className="year-range-readout">
        <span>{formatRating(bounds.minRating)}</span>
        <output aria-label="IMDb rating selected range" aria-live="polite">
          {formatRating(valueFrom)} – {formatRating(valueTo)}
        </output>
        <span>{formatRating(bounds.maxRating)}</span>
      </div>
      <div className="year-range-slider">
        <span aria-hidden="true" className="year-range-track" />
        <input
          aria-label="IMDb rating minimum"
          className="year-range-input"
          disabled={bounds.minRating === bounds.maxRating}
          id={`${id}-minimum`}
          max={valueTo}
          min={bounds.minRating}
          onChange={(event) => preview(Math.min(Number(event.target.value), valueTo), valueTo)}
          onKeyUp={(event) => { if (commitKeys.has(event.key)) finish() }}
          onPointerCancel={finish}
          onPointerUp={finish}
          step="0.1"
          type="range"
          value={valueFrom}
        />
        <input
          aria-label="IMDb rating maximum"
          className="year-range-input year-range-input-upper"
          disabled={bounds.minRating === bounds.maxRating}
          id={`${id}-maximum`}
          max={bounds.maxRating}
          min={valueFrom}
          onChange={(event) => preview(valueFrom, Math.max(Number(event.target.value), valueFrom))}
          onKeyUp={(event) => { if (commitKeys.has(event.key)) finish() }}
          onPointerCancel={finish}
          onPointerUp={finish}
          step="0.1"
          type="range"
          value={valueTo}
        />
      </div>
      <div aria-hidden="true" className="year-range-endpoints">
        <span>From</span><span>To</span>
      </div>
    </fieldset>
  )
}
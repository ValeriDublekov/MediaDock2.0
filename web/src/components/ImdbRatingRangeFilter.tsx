import { DualRangeFilter } from './DualRangeFilter'
import type { ImdbRatingBounds } from '../api/types'

interface ImdbRatingRangeFilterProps {
  bounds: ImdbRatingBounds
  id: string
  valueFrom: number
  valueTo: number
  onPreview: (from: number, to: number) => void
  onCommit: (from: number, to: number) => void
}

const formatRating = (rating: number) => Number(rating.toFixed(1)).toString()

export function ImdbRatingRangeFilter({ bounds, id, valueFrom, valueTo, onPreview, onCommit }: ImdbRatingRangeFilterProps) {
  return (
    <DualRangeFilter
      className="year-range-filter imdb-rating-range-filter"
      formatValue={formatRating}
      id={id}
      label="IMDb rating"
      max={bounds.maxRating}
      min={bounds.minRating}
      onCommit={onCommit}
      onPreview={onPreview}
      outputLabel="IMDb rating selected range"
      step={0.1}
      valueFrom={valueFrom}
      valueTo={valueTo}
    />
  )
}
import { DualRangeFilter } from './DualRangeFilter'
import type { YearBounds } from '../api/types'

interface YearRangeFilterProps {
  bounds: YearBounds
  id: string
  label: string
  valueFrom: number
  valueTo: number
  onPreview: (from: number, to: number) => void
  onCommit: (from: number, to: number) => void
}

export function YearRangeFilter({ bounds, id, label, valueFrom, valueTo, onPreview, onCommit }: YearRangeFilterProps) {
  return (
    <DualRangeFilter
      className="year-range-filter"
      id={id}
      label={label}
      max={bounds.maxYear}
      min={bounds.minYear}
      onCommit={onCommit}
      onPreview={onPreview}
      outputLabel={`${label} selected years`}
      valueFrom={valueFrom}
      valueTo={valueTo}
    />
  )
}
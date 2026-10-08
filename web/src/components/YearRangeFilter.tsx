import { useEffect, useState } from 'react'
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

const commitKeys = new Set(['ArrowDown', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'End', 'Home', 'PageDown', 'PageUp'])

export function YearRangeFilter({ bounds, id, label, valueFrom, valueTo, onPreview, onCommit }: YearRangeFilterProps) {
  const [range, setRange] = useState({ from: valueFrom, to: valueTo })

  useEffect(() => {
    setRange({ from: valueFrom, to: valueTo })
  }, [bounds.minYear, bounds.maxYear, valueFrom, valueTo])

  function preview(from: number, to: number) {
    setRange({ from, to })
    onPreview(from, to)
  }

  function finish() {
    onCommit(range.from, range.to)
  }

  return (
    <fieldset className="year-range-filter">
      <legend>{label}</legend>
      <div className="year-range-readout">
        <span>{bounds.minYear}</span>
        <output aria-label={`${label} selected years`} aria-live="polite">
          {range.from} – {range.to}
        </output>
        <span>{bounds.maxYear}</span>
      </div>
      <div className="year-range-slider">
        <span aria-hidden="true" className="year-range-track" />
        <input
          aria-label={`${label} minimum`}
          className="year-range-input"
          disabled={bounds.minYear === bounds.maxYear}
          id={`${id}-minimum`}
          max={range.to}
          min={bounds.minYear}
          onChange={(event) => preview(Math.min(Number(event.target.value), range.to), range.to)}
          onKeyUp={(event) => { if (commitKeys.has(event.key)) finish() }}
          onPointerCancel={finish}
          onPointerUp={finish}
          type="range"
          value={range.from}
        />
        <input
          aria-label={`${label} maximum`}
          className="year-range-input year-range-input-upper"
          disabled={bounds.minYear === bounds.maxYear}
          id={`${id}-maximum`}
          max={bounds.maxYear}
          min={range.from}
          onChange={(event) => preview(range.from, Math.max(Number(event.target.value), range.from))}
          onKeyUp={(event) => { if (commitKeys.has(event.key)) finish() }}
          onPointerCancel={finish}
          onPointerUp={finish}
          type="range"
          value={range.to}
        />
      </div>
      <div aria-hidden="true" className="year-range-endpoints">
        <span>From</span><span>To</span>
      </div>
    </fieldset>
  )
}
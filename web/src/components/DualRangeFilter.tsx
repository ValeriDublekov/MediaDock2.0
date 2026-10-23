import { useEffect, useState } from 'react'

interface DualRangeFilterProps {
  className: string
  id: string
  label: string
  max: number
  min: number
  onPreview: (from: number, to: number) => void
  onCommit: (from: number, to: number) => void
  outputLabel: string
  step?: number
  valueFrom: number
  valueTo: number
  formatValue?: (value: number) => string
}

const commitKeys = new Set(['ArrowDown', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'End', 'Home', 'PageDown', 'PageUp'])

function clampRange(from: number, to: number, min: number, max: number) {
  const clampedFrom = Math.min(Math.max(from, min), max)
  const clampedTo = Math.min(Math.max(to, clampedFrom), max)
  return { from: clampedFrom, to: clampedTo }
}

export function DualRangeFilter({
  className,
  id,
  label,
  max,
  min,
  onPreview,
  onCommit,
  outputLabel,
  step = 1,
  valueFrom,
  valueTo,
  formatValue = String,
}: DualRangeFilterProps) {
  const [range, setRange] = useState(() => clampRange(valueFrom, valueTo, min, max))

  useEffect(() => {
    setRange(clampRange(valueFrom, valueTo, min, max))
  }, [max, min, valueFrom, valueTo])

  function previewFrom(from: number) {
    const nextRange = { from: Math.min(Math.max(from, min), range.to), to: range.to }
    setRange(nextRange)
    onPreview(nextRange.from, nextRange.to)
  }

  function previewTo(to: number) {
    const nextRange = { from: range.from, to: Math.max(Math.min(to, max), range.from) }
    setRange(nextRange)
    onPreview(nextRange.from, nextRange.to)
  }

  function finish() {
    onCommit(range.from, range.to)
  }

  return (
    <fieldset className={className}>
      <legend>{label}</legend>
      <div className="year-range-readout">
        <span>{formatValue(min)}</span>
        <output aria-label={outputLabel} aria-live="polite">
          {formatValue(range.from)} – {formatValue(range.to)}
        </output>
        <span>{formatValue(max)}</span>
      </div>
      <div className="year-range-slider">
        <span aria-hidden="true" className="year-range-track" />
        <input
          aria-label={`${label} minimum`}
          className="year-range-input"
          disabled={min === max}
          id={`${id}-minimum`}
          max={max}
          min={min}
          onChange={(event) => previewFrom(Number(event.currentTarget.value))}
          onKeyUp={(event) => { if (commitKeys.has(event.key)) finish() }}
          onPointerCancel={finish}
          onPointerUp={finish}
          step={step}
          type="range"
          value={range.from}
        />
        <input
          aria-label={`${label} maximum`}
          className="year-range-input year-range-input-upper"
          disabled={min === max}
          id={`${id}-maximum`}
          max={max}
          min={min}
          onChange={(event) => previewTo(Number(event.currentTarget.value))}
          onKeyUp={(event) => { if (commitKeys.has(event.key)) finish() }}
          onPointerCancel={finish}
          onPointerUp={finish}
          step={step}
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
export type ViewMode = 'posters' | 'table'

export function ViewModeControl({ value, onChange }: { value: ViewMode; onChange: (mode: ViewMode) => void }) {
  return <div aria-label="View mode" className="segment-control view-mode-control" role="group">
    <button aria-pressed={value === 'posters'} onClick={() => onChange('posters')} type="button">Posters</button>
    <button aria-pressed={value === 'table'} onClick={() => onChange('table')} type="button">Table</button>
  </div>
}
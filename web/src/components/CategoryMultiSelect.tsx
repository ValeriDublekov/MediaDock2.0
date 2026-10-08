interface CategoryMultiSelectProps {
  label: string
  options: string[]
  selectedValues: string[] | null
  loading?: boolean
  error?: string | null
  onChange: (values: string[] | null) => void
}

export function CategoryMultiSelect({
  label,
  options,
  selectedValues,
  loading = false,
  error = null,
  onChange,
}: CategoryMultiSelectProps) {
  const allSelected = selectedValues === null
  const selectedCount = allSelected ? options.length : selectedValues.length

  function toggleOption(option: string) {
    const current = allSelected ? options : selectedValues
    const next = current.includes(option)
      ? current.filter((value) => value !== option)
      : [...current, option]
    onChange(next.length === options.length ? null : next)
  }

  const summary = loading
    ? 'Loading categories'
    : error
      ? 'Categories unavailable'
      : options.length === 0
        ? 'No categories available'
        : allSelected
          ? `All categories (${options.length})`
          : `${selectedCount} of ${options.length} selected`

  return (
    <fieldset className="category-multiselect" disabled={loading || Boolean(error) || options.length === 0}>
      <legend>{label}</legend>
      {loading || error || options.length === 0 ? (
        <p className={error ? 'category-multiselect-error' : 'category-multiselect-status'} role={error ? 'alert' : 'status'}>
          {error ? `Could not load categories. ${error}` : summary}
        </p>
      ) : (
        <details className="category-multiselect-menu">
          <summary aria-label={`${label}: ${summary}`}>{summary}</summary>
          <div className="category-multiselect-options">
            {!allSelected && (
              <button className="category-multiselect-select-all" onClick={() => onChange(null)} type="button">
                Select all
              </button>
            )}
            {options.map((option) => (
              <label className="category-multiselect-option" key={option}>
                <input
                  checked={allSelected || selectedValues.includes(option)}
                  onChange={() => toggleOption(option)}
                  type="checkbox"
                />
                <span>{option}</span>
              </label>
            ))}
          </div>
        </details>
      )}
    </fieldset>
  )
}
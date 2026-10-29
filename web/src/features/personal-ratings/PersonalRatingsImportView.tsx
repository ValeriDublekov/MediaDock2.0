import { useState, type FormEvent } from 'react'
import { importPersonalRatings } from '../../api/client'
import type { PersonalRatingsImportResult } from '../../api/types'

const maximumPersonalRatingsUploadBytes = 1024 * 1024

export function PersonalRatingsImportView() {
  const [importingRatings, setImportingRatings] = useState(false)
  const [ratingsImportError, setRatingsImportError] = useState<string | null>(null)
  const [ratingsImportResult, setRatingsImportResult] = useState<PersonalRatingsImportResult | null>(null)

  async function importRatings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const fileInput = form.elements.namedItem('personal-ratings-file') as HTMLInputElement | null
    const file = fileInput?.files?.[0]
    if (!file) {
      setRatingsImportError('Select a CSV file to import.')
      return
    }
    if (file.size > maximumPersonalRatingsUploadBytes) {
      setRatingsImportError('The CSV file cannot exceed 1 MiB.')
      return
    }

    setImportingRatings(true)
    setRatingsImportError(null)
    setRatingsImportResult(null)
    try {
      const result = await importPersonalRatings(file)
      setRatingsImportResult(result)
      form.reset()
    } catch (requestError) {
      setRatingsImportError(requestError instanceof Error ? requestError.message : 'Could not import IMDb ratings.')
    } finally {
      setImportingRatings(false)
    }
  }

  return (
    <section aria-labelledby="personal-ratings-import-heading" className="management-section">
      <h2 id="personal-ratings-import-heading">Import IMDb ratings</h2>
      <p className="section-caption">Upload one IMDb CSV export at a time. Re-imports add or update ratings by IMDb ID and keep ratings not included in the file. Maximum file size: 1 MiB.</p>
      <form className="settings-form" onSubmit={importRatings}>
        <div className="field">
          <label htmlFor="personal-ratings-file">IMDb ratings CSV</label>
          <input accept=".csv,text/csv" id="personal-ratings-file" name="personal-ratings-file" required type="file" />
          <p className="section-caption">If your IMDb account is linked to an Amazon account, download your full Amazon data archive. The ratings file is located at <code>All Data Categories.zip\Additional Data\IMDbRatings.UserRatingsData</code> inside the archive.</p>
          <p className="section-caption">CSV format example:</p>
          <pre><code>{'Title ID,Rating,Last Modified Date\ntt14452776,8,2026-08-30T21:09:30.554Z'}</code></pre>
        </div>
        {ratingsImportError && <p className="form-error" role="alert">{ratingsImportError}</p>}
        {ratingsImportResult && <>
          <p className="form-message" role="status">
            Imported {ratingsImportResult.ratingsInFile}: {ratingsImportResult.added} added, {ratingsImportResult.updated} updated, {ratingsImportResult.unchanged} unchanged. {ratingsImportResult.totalRatings} ratings stored.
          </p>
          {ratingsImportResult.errors.length > 0 && <p className="form-error" role="alert">
            Skipped entries with missing ratings: {ratingsImportResult.errors.map(error => `${error.id} (${error.message})`).join(', ')}.
          </p>}
        </>}
        <div className="form-actions">
          <button className="button" disabled={importingRatings} type="submit">{importingRatings ? 'Importing...' : 'Import ratings'}</button>
        </div>
      </form>
    </section>
  )
}

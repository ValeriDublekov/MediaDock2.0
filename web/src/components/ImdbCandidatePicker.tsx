import { useState, type FormEvent } from 'react'
import {
  addManualImdbTitle,
  applyCatalogTitleImdbCandidate,
  applyParseLogImdbCandidate,
  lookupCatalogTitleImdbCandidate,
  lookupManualImdbTitleCandidate,
  lookupParseLogImdbCandidate,
  searchCatalogTitleImdbCandidates,
  searchManualImdbTitleCandidates,
  searchParseLogImdbCandidates,
} from '../api/client'
import type { ImdbCandidate, ImdbCandidateSearch } from '../api/types'
import { LoadingState } from './Feedback'
import { formatWords } from '../shared/format'

export type ImdbCandidateTarget =
  | { source: 'catalog'; id: number; title: string; rawTitle: string }
  | { source: 'parse-log'; id: number; title: string; rawTitle: string }
  | { source: 'manual'; list: 'favorites' | 'ignored' | 'personal-ratings' }

export function ImdbCandidatePicker({
  target,
  onClose,
  onApplied,
}: {
  target: ImdbCandidateTarget
  onClose: () => void
  onApplied: (candidate?: ImdbCandidate, rating?: number) => void | Promise<void>
}) {
  const [mode, setMode] = useState<'title' | 'imdb-id'>('title')
  const [titleQuery, setTitleQuery] = useState(target.source === 'manual' ? '' : target.title)
  const [searchedTitle, setSearchedTitle] = useState<string | null>(null)
  const [imdbIdQuery, setImdbIdQuery] = useState('')
  const [searchResult, setSearchResult] = useState<ImdbCandidateSearch | null>(null)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [applyError, setApplyError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [applyingImdbId, setApplyingImdbId] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [ratingQuery, setRatingQuery] = useState('8')
  const manual = target.source === 'manual'
  const rating = Number(ratingQuery)
  const validRating = Number.isInteger(rating) && rating >= 1 && rating <= 10
  const manualActionLabel = target.source !== 'manual' ? 'Use match' : target.list === 'favorites'
    ? 'Add to favorites'
    : target.list === 'ignored' ? 'Ignore title' : 'Add rating'
  const dialogTitle = target.source !== 'manual' ? 'Change IMDb match' : target.list === 'favorites'
    ? 'Add to favorites'
    : target.list === 'ignored' ? 'Add to ignore list' : 'Add personal rating'

  function setLookupResult(result: ImdbCandidateSearch) {
    setSearchResult(result)
    if (result.status !== 'found' && result.status !== 'not_found') {
      setSearchError(result.errorCode
        ? `OMDb lookup failed: ${formatWords(result.errorCode)}.`
        : `OMDb lookup failed: ${formatWords(result.status)}.`)
    }
  }

  async function searchByTitle(title: string, nextPage = 1) {
    const normalizedTitle = title.trim()
    if (!normalizedTitle) {
      setSearchError('Enter a title to search IMDb.')
      setSearchResult(null)
      return
    }
    setLoading(true)
    setSearchError(null)
    setSearchResult(null)
    setApplyError(null)
    setPage(nextPage)
    setSearchedTitle(normalizedTitle)
    try {
      const result = target.source === 'catalog'
        ? await searchCatalogTitleImdbCandidates(target.id, normalizedTitle, nextPage)
        : target.source === 'parse-log'
          ? await searchParseLogImdbCandidates(target.id, normalizedTitle, nextPage)
          : await searchManualImdbTitleCandidates(normalizedTitle, nextPage)
      setLookupResult(result)
    } catch (requestError) {
      setSearchError(requestError instanceof Error ? requestError.message : 'Could not search IMDb records.')
    } finally {
      setLoading(false)
    }
  }

  async function loadByImdbId(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const normalizedImdbId = imdbIdQuery.trim()
    if (!normalizedImdbId) {
      setSearchError('Enter an IMDb ID to load its information.')
      setSearchResult(null)
      return
    }
    setLoading(true)
    setSearchError(null)
    setSearchResult(null)
    setApplyError(null)
    setPage(1)
    try {
      const result = target.source === 'catalog'
        ? await lookupCatalogTitleImdbCandidate(target.id, normalizedImdbId)
        : target.source === 'parse-log'
          ? await lookupParseLogImdbCandidate(target.id, normalizedImdbId)
          : await lookupManualImdbTitleCandidate(normalizedImdbId)
      setLookupResult(result)
    } catch (requestError) {
      setSearchError(requestError instanceof Error ? requestError.message : 'Could not load the IMDb record.')
    } finally {
      setLoading(false)
    }
  }

  function changeMode(nextMode: 'title' | 'imdb-id') {
    setMode(nextMode)
    setSearchResult(null)
    setSearchError(null)
    setApplyError(null)
    setPage(1)
  }

  async function applyCandidate(candidate: ImdbCandidate) {
    if (!candidate.imdbId) return
    setApplyingImdbId(candidate.imdbId)
    setApplyError(null)
    try {
      if (target.source === 'manual') {
        if (target.list === 'personal-ratings' && !validRating) return
        await addManualImdbTitle(candidate.imdbId)
        if (target.list === 'personal-ratings') await onApplied(candidate, rating)
        else await onApplied(candidate)
      } else if (target.source === 'catalog') {
        await applyCatalogTitleImdbCandidate(target.id, candidate.imdbId)
        await onApplied()
      } else {
        await applyParseLogImdbCandidate(target.id, candidate.imdbId)
        await onApplied()
      }
    } catch (requestError) {
      setApplyError(requestError instanceof Error ? requestError.message : 'Could not apply the IMDb match.')
    } finally {
      setApplyingImdbId(null)
    }
  }

  return (
    <div className="detail-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <section aria-labelledby="imdb-candidate-heading" aria-modal="true" className="detail-dialog imdb-candidate-dialog" role="dialog">
        <header className="detail-header">
          <div>
            <p className="eyebrow">{manual ? 'MANUAL ADD' : 'IMDb MATCH'}</p>
            <h2 id="imdb-candidate-heading">{dialogTitle}</h2>
          </div>
          <button aria-label={manual ? 'Close manual add dialog' : 'Reject IMDb candidates'} className="button button-secondary" onClick={onClose} type="button">{manual ? 'Close' : 'Reject'}</button>
        </header>

        <div className="detail-body">
          {target.source !== 'manual' && <div className="imdb-match-raw">
            <strong>RAW TORRENT TEXT</strong>
            <p>{target.rawTitle}</p>
          </div>}
          <div aria-label="IMDb match method" className="imdb-match-tabs" role="tablist">
            <button aria-selected={mode === 'title'} className={mode === 'title' ? 'is-active' : ''} disabled={loading} onClick={() => changeMode('title')} role="tab" type="button">Search by title</button>
            <button aria-selected={mode === 'imdb-id'} className={mode === 'imdb-id' ? 'is-active' : ''} disabled={loading} onClick={() => changeMode('imdb-id')} role="tab" type="button">IMDb ID</button>
          </div>
          {mode === 'title' ? (
            <form className="imdb-match-form" onSubmit={(event) => { event.preventDefault(); void searchByTitle(titleQuery) }}>
              <label className="field" htmlFor="imdb-title-query">
                <span>Title to search</span>
                <input id="imdb-title-query" onChange={(event) => setTitleQuery(event.target.value)} value={titleQuery} />
              </label>
              <button className="button" disabled={loading} type="submit">Search</button>
            </form>
          ) : (
            <form className="imdb-match-form" onSubmit={(event) => { void loadByImdbId(event) }}>
              <label className="field" htmlFor="imdb-id-query">
                <span>IMDb ID</span>
                <input autoComplete="off" id="imdb-id-query" onChange={(event) => setImdbIdQuery(event.target.value)} placeholder="tt1234567" value={imdbIdQuery} />
              </label>
              <button className="button" disabled={loading} type="submit">Load IMDb info</button>
            </form>
          )}
          {target.source === 'manual' && target.list === 'personal-ratings' && <label className="field imdb-manual-rating" htmlFor="imdb-manual-rating">
            <span>Your rating (1-10)</span>
            <input id="imdb-manual-rating" max="10" min="1" onChange={(event) => setRatingQuery(event.target.value)} step="1" type="number" value={ratingQuery} />
          </label>}
          {loading && <LoadingState label={mode === 'title' ? 'Searching OMDb' : 'Loading IMDb information'} />}
          {!loading && searchError && (
            <div className="imdb-candidate-error" role="alert">
              <span>{searchError}</span>
            </div>
          )}
          {!loading && !searchError && searchResult && searchResult.candidates.length === 0 && (
            <p className="imdb-candidate-empty" role="status">No IMDb matches found.</p>
          )}
          {!loading && searchResult && searchResult.candidates.length > 0 && (
            <>
              <p className="imdb-candidate-count">
                Showing {searchResult.candidates.length} of {searchResult.totalResults} matches
              </p>
              <ol className="imdb-candidate-list">
                {searchResult.candidates.map((candidate) => (
                  <li key={candidate.imdbId ?? `${candidate.title}-${candidate.year}`}>
                    <div className="imdb-candidate-copy">
                      <strong>{candidate.title}</strong>
                      <span>{candidate.year ?? 'Year unknown'} | {formatWords(candidate.sourceType)}</span>
                    </div>
                    <div className="imdb-candidate-actions">
                      {candidate.imdbId && <a href={`https://www.imdb.com/title/${candidate.imdbId}/`} rel="noopener noreferrer" target="_blank">IMDb</a>}
                      <button
                        className="button"
                        disabled={!candidate.imdbId || applyingImdbId !== null || (target.source === 'manual' && target.list === 'personal-ratings' && !validRating)}
                        onClick={() => { void applyCandidate(candidate) }}
                        type="button"
                      >{applyingImdbId === candidate.imdbId ? 'Adding' : manualActionLabel}</button>
                    </div>
                  </li>
                ))}
              </ol>
              {mode === 'title' && searchResult.totalResults > 10 && <div aria-label="IMDb candidate pages" className="imdb-candidate-pagination">
                <button aria-label="Previous candidate page" className="button button-secondary" disabled={loading || page <= 1} onClick={() => { if (searchedTitle) void searchByTitle(searchedTitle, page - 1) }} type="button">Previous</button>
                <span>Page {page} of {Math.min(100, Math.ceil(searchResult.totalResults / 10))}</span>
                <button aria-label="Next candidate page" className="button button-secondary" disabled={loading || page >= Math.min(100, Math.ceil(searchResult.totalResults / 10))} onClick={() => { if (searchedTitle) void searchByTitle(searchedTitle, page + 1) }} type="button">Next</button>
              </div>}
            </>
          )}
          {applyError && <p className="imdb-candidate-error" role="alert">{applyError}</p>}
        </div>
      </section>
    </div>
  )
}
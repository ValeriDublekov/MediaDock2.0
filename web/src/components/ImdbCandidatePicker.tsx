import { useState, type FormEvent } from 'react'
import {
  applyCatalogTitleImdbCandidate,
  applyParseLogImdbCandidate,
  lookupCatalogTitleImdbCandidate,
  lookupParseLogImdbCandidate,
  searchCatalogTitleImdbCandidates,
  searchParseLogImdbCandidates,
} from '../api/client'
import type { ImdbCandidate, ImdbCandidateSearch } from '../api/types'
import { LoadingState } from './Feedback'
import { formatWords } from '../shared/format'

export type ImdbCandidateTarget =
  | { source: 'catalog'; id: number; title: string; rawTitle: string }
  | { source: 'parse-log'; id: number; title: string; rawTitle: string }

export function ImdbCandidatePicker({
  target,
  onClose,
  onApplied,
}: {
  target: ImdbCandidateTarget
  onClose: () => void
  onApplied: () => void
}) {
  const [mode, setMode] = useState<'title' | 'imdb-id'>('title')
  const [titleQuery, setTitleQuery] = useState(target.title)
  const [searchedTitle, setSearchedTitle] = useState<string | null>(null)
  const [imdbIdQuery, setImdbIdQuery] = useState('')
  const [searchResult, setSearchResult] = useState<ImdbCandidateSearch | null>(null)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [applyError, setApplyError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [applyingImdbId, setApplyingImdbId] = useState<string | null>(null)
  const [page, setPage] = useState(1)

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
        : await searchParseLogImdbCandidates(target.id, normalizedTitle, nextPage)
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
        : await lookupParseLogImdbCandidate(target.id, normalizedImdbId)
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
      if (target.source === 'catalog') {
        await applyCatalogTitleImdbCandidate(target.id, candidate.imdbId)
      } else {
        await applyParseLogImdbCandidate(target.id, candidate.imdbId)
      }
      onApplied()
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
            <p className="eyebrow">IMDb MATCH</p>
            <h2 id="imdb-candidate-heading">Change IMDb match</h2>
          </div>
          <button aria-label="Reject IMDb candidates" className="button button-secondary" onClick={onClose} type="button">Reject</button>
        </header>

        <div className="detail-body">
          <div className="imdb-match-raw">
            <strong>RAW TORRENT TEXT</strong>
            <p>{target.rawTitle}</p>
          </div>
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
                        disabled={!candidate.imdbId || applyingImdbId !== null}
                        onClick={() => { void applyCandidate(candidate) }}
                        type="button"
                      >{applyingImdbId === candidate.imdbId ? 'Applying' : 'Use match'}</button>
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
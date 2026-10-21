import { useEffect, useState } from 'react'
import {
  applyCatalogTitleImdbCandidate,
  applyParseLogImdbCandidate,
  searchCatalogTitleImdbCandidates,
  searchParseLogImdbCandidates,
} from '../api/client'
import type { ImdbCandidate, ImdbCandidateSearch } from '../api/types'
import { LoadingState } from './Feedback'
import { formatWords } from '../shared/format'

export type ImdbCandidateTarget =
  | { source: 'catalog'; id: number; title: string }
  | { source: 'parse-log'; id: number; title: string }

export function ImdbCandidatePicker({
  target,
  onClose,
  onApplied,
}: {
  target: ImdbCandidateTarget
  onClose: () => void
  onApplied: () => void
}) {
  const [searchResult, setSearchResult] = useState<ImdbCandidateSearch | null>(null)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [applyError, setApplyError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [applyingImdbId, setApplyingImdbId] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let current = true
    const request = target.source === 'catalog'
      ? searchCatalogTitleImdbCandidates(target.id, page)
      : searchParseLogImdbCandidates(target.id, page)

    request
      .then((result) => {
        if (!current) return
        setSearchResult(result)
        if (result.status !== 'found' && result.status !== 'not_found') {
          setSearchError(result.errorCode
            ? `OMDb search failed: ${formatWords(result.errorCode)}.`
            : `OMDb search failed: ${formatWords(result.status)}.`)
        }
      })
      .catch((requestError: unknown) => {
        if (current) setSearchError(requestError instanceof Error ? requestError.message : 'Could not search IMDb records.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [target.id, target.source, page, attempt])

  function changePage(nextPage: number) {
    setLoading(true)
    setSearchError(null)
    setSearchResult(null)
    setApplyError(null)
    setPage(nextPage)
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
            <h2 id="imdb-candidate-heading">Choose a match</h2>
            <div className="detail-meta">{target.title}</div>
          </div>
          <button aria-label="Reject IMDb candidates" className="button button-secondary" onClick={onClose} type="button">Reject</button>
        </header>

        <div className="detail-body">
          {loading && <LoadingState label="Searching OMDb" />}
          {!loading && searchError && (
            <div className="imdb-candidate-error" role="alert">
              <span>{searchError}</span>
              <button className="text-button" onClick={() => {
                setLoading(true)
                setSearchError(null)
                setSearchResult(null)
                setAttempt((current) => current + 1)
              }} type="button">Search again</button>
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
              {searchResult.totalResults > 10 && <div aria-label="IMDb candidate pages" className="imdb-candidate-pagination">
                <button aria-label="Previous candidate page" className="button button-secondary" disabled={page <= 1} onClick={() => changePage(page - 1)} type="button">Previous</button>
                <span>Page {page} of {Math.min(100, Math.ceil(searchResult.totalResults / 10))}</span>
                <button aria-label="Next candidate page" className="button button-secondary" disabled={page >= Math.min(100, Math.ceil(searchResult.totalResults / 10))} onClick={() => changePage(page + 1)} type="button">Next</button>
              </div>}
            </>
          )}
          {applyError && <p className="imdb-candidate-error" role="alert">{applyError}</p>}
        </div>
      </section>
    </div>
  )
}
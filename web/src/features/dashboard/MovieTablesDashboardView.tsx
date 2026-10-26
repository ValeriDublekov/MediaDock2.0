import { useEffect, useState } from 'react'
import { getMovieTablesDashboard } from '../../api/client'
import type { MovieTablesDashboard } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { formatWords } from '../../shared/format'

function statusPillClass(status: string): string {
  switch (status.toLowerCase()) {
    case 'enriched':
    case 'succeeded':
      return 'state-pill is-enriched'
    case 'pending':
    case 'queued':
      return 'state-pill is-pending'
    case 'failed':
    case 'problem':
    case 'temporary_error':
      return 'state-pill is-problem'
    case 'not_found':
      return 'state-pill is-not-found'
    default:
      return 'state-pill is-muted'
  }
}

function calculatePercentage(count: number, total: number): string {
  return total <= 0 ? '0%' : `${Math.round((count / total) * 100)}%`
}

function BreakdownTable({
  heading,
  columnHeading,
  entries,
  total,
  statuses = false,
}: {
  heading: string
  columnHeading: string
  entries: Record<string, number>
  total: number
  statuses?: boolean
}) {
  const rows = Object.entries(entries).sort((left, right) => right[1] - left[1])

  return (
    <div>
      <h3>{heading}</h3>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">{columnHeading}</th>
              <th scope="col">Count</th>
              <th scope="col">Share</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={3}>No data recorded.</td></tr>
            ) : rows.map(([value, count]) => (
              <tr key={value}>
                <td>
                  {statuses
                    ? <span className={statusPillClass(value)}>{formatWords(value)}</span>
                    : <strong>{formatWords(value)}</strong>}
                </td>
                <td>{count.toLocaleString()}</td>
                <td>{calculatePercentage(count, total)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

export function MovieTablesDashboardView() {
  const [dashboard, setDashboard] = useState<MovieTablesDashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshCount, setRefreshCount] = useState(0)

  useEffect(() => {
    let current = true

    getMovieTablesDashboard()
      .then((data) => {
        if (current) {
          setDashboard(data)
          setError(null)
        }
      })
      .catch((requestError: unknown) => {
        if (current) {
          setError(requestError instanceof Error ? requestError.message : 'Could not load movie tables dashboard.')
        }
      })
      .finally(() => {
        if (current) setLoading(false)
      })

    return () => {
      current = false
    }
  }, [refreshCount])

  const refresh = () => {
    setLoading(true)
    setRefreshCount((count) => count + 1)
  }

  if (loading && !dashboard) {
    return <LoadingState label="Loading movie tables state and counts..." />
  }

  if (error && !dashboard) {
    return <ErrorState message={error} onRetry={refresh} />
  }

  if (!dashboard) {
    return <p className="section-caption">No table data available.</p>
  }

  const { imdb, goldenGlobes, oscar, generatedAtUtc } = dashboard

  return (
    <div className="movie-tables-dashboard" data-testid="movie-tables-dashboard">
      <div className="section-toolbar">
        <span className="result-count">
          Summary generated at {generatedAtUtc ? new Date(generatedAtUtc).toLocaleTimeString() : 'now'}
        </span>
        <button className="button button-secondary" disabled={loading} onClick={refresh} type="button">
          {loading ? 'Refreshing...' : 'Refresh statistics'}
        </button>
      </div>

      {error && <p className="auth-message" role="alert">{error}</p>}

      <section aria-labelledby="tables-overview-heading" className="management-section">
        <h2 id="tables-overview-heading">System movie tables overview</h2>
        <p className="section-caption">
          Current row counts and enrichment state across canonical IMDb titles, Golden Globes, and Academy Awards.
        </p>
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th scope="col">System table</th>
                <th scope="col">Entity</th>
                <th scope="col">Total records</th>
                <th scope="col">Enriched / linked</th>
                <th scope="col">Pending</th>
                <th scope="col">Nominations</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td><strong>imdb_titles</strong></td>
                <td>IMDb Titles (Canonical)</td>
                <td><strong>{imdb.totalCount.toLocaleString()}</strong></td>
                <td>{imdb.enrichedCount.toLocaleString()}</td>
                <td>{imdb.pendingCount.toLocaleString()}</td>
                <td>&mdash;</td>
              </tr>
              <tr>
                <td><strong>golden_globe_titles</strong></td>
                <td>Golden Globes Titles</td>
                <td><strong>{goldenGlobes.totalCount.toLocaleString()}</strong></td>
                <td>{goldenGlobes.withImdbLinkCount.toLocaleString()}</td>
                <td>{(goldenGlobes.byEnrichmentStatus.pending ?? 0).toLocaleString()}</td>
                <td>{goldenGlobes.totalNominationsCount.toLocaleString()}</td>
              </tr>
              <tr>
                <td><strong>oscar_movies</strong></td>
                <td>Oscar Movies</td>
                <td><strong>{oscar.totalCount.toLocaleString()}</strong></td>
                <td>{oscar.withImdbLinkCount.toLocaleString()}</td>
                <td>{(oscar.byEnrichmentStatus.pending ?? 0).toLocaleString()}</td>
                <td>{oscar.totalNominationsCount.toLocaleString()}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="imdb-table-heading" className="management-section">
        <h2 id="imdb-table-heading">IMDb titles (imdb_titles)</h2>
        <p className="section-caption">Canonical metadata for catalog, award, and personal rating records.</p>
        <BreakdownTable
          heading="Counts by media type"
          columnHeading="Media type"
          entries={imdb.byMediaType}
          total={imdb.totalCount}
        />
        <BreakdownTable
          heading="Counts by metadata enrichment status"
          columnHeading="Status"
          entries={imdb.byMetadataStatus}
          total={imdb.totalCount}
          statuses
        />
      </section>

      <section aria-labelledby="gg-table-heading" className="management-section">
        <h2 id="gg-table-heading">Golden Globes titles (golden_globe_titles)</h2>
        <p className="section-caption">
          Golden Globe title identities grouped by title, year, and nominee type.
        </p>
        <BreakdownTable
          heading="Counts by nominee type"
          columnHeading="Nominee type"
          entries={goldenGlobes.byNomineeType}
          total={goldenGlobes.totalCount}
        />
        <BreakdownTable
          heading="Counts by enrichment status"
          columnHeading="Status"
          entries={goldenGlobes.byEnrichmentStatus}
          total={goldenGlobes.totalCount}
          statuses
        />
      </section>

      <section aria-labelledby="oscar-table-heading" className="management-section">
        <h2 id="oscar-table-heading">Oscar movies (oscar_movies)</h2>
        <p className="section-caption">Academy Award source film records and linked nominations.</p>
        <BreakdownTable
          heading="Counts by enrichment status"
          columnHeading="Status"
          entries={oscar.byEnrichmentStatus}
          total={oscar.totalCount}
          statuses
        />
      </section>
    </div>
  )
}
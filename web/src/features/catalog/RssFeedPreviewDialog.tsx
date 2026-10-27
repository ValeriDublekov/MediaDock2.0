import { useEffect, useState } from 'react'
import { getRssFeedPreview } from '../../api/client'
import type { FeedType, RssFeedPreview } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { formatDate } from '../../shared/format'

interface RssFeedPreviewDialogProps {
  profileId: FeedType
  onClose: () => void
}

function formatDuration(durationMs: number): string {
  return durationMs >= 1000 ? `${(durationMs / 1000).toFixed(2)} s` : `${durationMs.toFixed(1)} ms`
}

function FeedPreviewMeta({ feed }: { feed: RssFeedPreview['feeds'][number] }) {
  return (
    <div className="rss-preview-feed-meta">
      <a href={feed.sourceUrl} rel="noopener noreferrer" target="_blank">Open original RSS</a>
      <span>Fetch: {formatDuration(feed.fetchDurationMs)}</span>
      <span>Parse: {formatDuration(feed.parseDurationMs)}</span>
    </div>
  )
}

export function RssFeedPreviewDialog({ profileId, onClose }: RssFeedPreviewDialogProps) {
  const [previews, setPreviews] = useState<RssFeedPreview[]>([])
  const [view, setView] = useState<'formatted' | 'original'>('formatted')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let current = true
    setLoading(true)
    setError(null)
    getRssFeedPreview(profileId)
      .then((response) => { if (current) setPreviews([response]) })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The RSS preview request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [profileId, attempt])

  return (
    <div className="detail-backdrop" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <section
        aria-labelledby="rss-preview-heading"
        aria-modal="true"
        className="detail-dialog rss-preview-dialog"
        onKeyDown={(event) => { if (event.key === 'Escape') onClose() }}
        role="dialog"
        tabIndex={-1}
      >
        <header className="detail-header">
          <div>
            <p className="eyebrow">CATALOG FEEDS</p>
            <h2 id="rss-preview-heading">RSS contents</h2>
          </div>
          <button aria-label="Close RSS contents" className="button button-secondary" onClick={onClose} type="button">Close</button>
        </header>
        <div className="detail-body">
          {loading && <LoadingState label="Loading RSS contents" />}
          {!loading && error && <ErrorState message={error} onRetry={() => setAttempt((current) => current + 1)} />}
          {!loading && !error && <>
            <div aria-label="RSS view" className="rss-preview-tabs" role="tablist">
              <button aria-controls="rss-preview-formatted" aria-selected={view === 'formatted'} className={view === 'formatted' ? 'is-active' : ''} id="rss-preview-formatted-tab" onClick={() => setView('formatted')} role="tab" type="button">Parsed items</button>
              <button aria-controls="rss-preview-original" aria-selected={view === 'original'} className={view === 'original' ? 'is-active' : ''} id="rss-preview-original-tab" onClick={() => setView('original')} role="tab" type="button">Original XML</button>
            </div>
            {view === 'formatted' ? (
              <div aria-labelledby="rss-preview-formatted-tab" id="rss-preview-formatted" role="tabpanel">
                {previews.map((preview) => (
                  <section aria-label={preview.profileName} className="rss-preview-profile" key={preview.profileId}>
                    <h3>{preview.profileName}</h3>
                    {preview.feeds.length === 0 ? <p className="section-caption">No RSS URLs configured.</p> : preview.feeds.map((feed, feedIndex) => (
                      <section aria-label={`Feed ${feedIndex + 1}`} className="rss-preview-feed" key={feed.sourceId}>
                        <FeedPreviewMeta feed={feed} />
                        <h4>Feed {feedIndex + 1}</h4>
                        {feed.items.length === 0 ? <p className="section-caption">No items in this feed.</p> : (
                          <ol className="rss-preview-items">
                            {feed.items.map((item, itemIndex) => (
                              <li className="rss-preview-item" key={`${item.feedEntryId ?? item.torrentUrl ?? item.title ?? 'item'}-${itemIndex}`}>
                                <strong>{item.title ?? 'Untitled RSS item'}</strong>
                                {item.publishedAt && <time dateTime={item.publishedAt}>{formatDate(item.publishedAt)}</time>}
                                {item.torrentUrl && <span className="rss-preview-url">{item.torrentUrl}</span>}
                              </li>
                            ))}
                          </ol>
                        )}
                      </section>
                    ))}
                  </section>
                ))}
              </div>
            ) : (
              <div aria-labelledby="rss-preview-original-tab" className="rss-preview-originals" id="rss-preview-original" role="tabpanel">
                {previews.map((preview) => (
                  <section aria-label={preview.profileName} className="rss-preview-profile" key={preview.profileId}>
                    <h3>{preview.profileName}</h3>
                    {preview.feeds.length === 0 ? <p className="section-caption">No RSS URLs configured.</p> : preview.feeds.map((feed, feedIndex) => (
                      <section aria-label={`Feed ${feedIndex + 1}`} className="rss-preview-feed" key={feed.sourceId}>
                        <FeedPreviewMeta feed={feed} />
                        <h4>Feed {feedIndex + 1}</h4>
                        <pre aria-label={`Original RSS feed ${feedIndex + 1}`} className="rss-preview-original"><code>{feed.originalContent}</code></pre>
                      </section>
                    ))}
                  </section>
                ))}
              </div>
            )}
          </>}
        </div>
      </section>
    </div>
  )
}
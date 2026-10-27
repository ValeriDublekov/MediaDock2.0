import { useEffect, useState } from 'react'
import { getRssFeedPreview } from '../../api/client'
import type { FeedType, RssFeedPreview } from '../../api/types'
import { ErrorState, LoadingState } from '../../components/Feedback'
import { formatDate } from '../../shared/format'

interface RssFeedPreviewDialogProps {
  profileIds: FeedType[]
  onClose: () => void
}

export function RssFeedPreviewDialog({ profileIds, onClose }: RssFeedPreviewDialogProps) {
  const [previews, setPreviews] = useState<RssFeedPreview[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let current = true
    setLoading(true)
    setError(null)
    Promise.all(profileIds.map((profileId) => getRssFeedPreview(profileId)))
      .then((response) => { if (current) setPreviews(response) })
      .catch((requestError: unknown) => {
        if (current) setError(requestError instanceof Error ? requestError.message : 'The RSS preview request failed.')
      })
      .finally(() => { if (current) setLoading(false) })
    return () => { current = false }
  }, [profileIds, attempt])

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
          {!loading && !error && previews.map((preview) => (
            <section aria-label={preview.profileName} className="rss-preview-profile" key={preview.profileId}>
              <h3>{preview.profileName}</h3>
              {preview.feeds.length === 0 ? <p className="section-caption">No RSS URLs configured.</p> : preview.feeds.map((feed, feedIndex) => (
                <section aria-label={`Feed ${feedIndex + 1}`} className="rss-preview-feed" key={feed.sourceId}>
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
      </section>
    </div>
  )
}
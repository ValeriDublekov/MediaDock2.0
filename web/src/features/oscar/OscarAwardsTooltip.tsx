import type { OscarNomination } from '../../api/types'

interface OscarAwardsTooltipProps {
  id: string
  nominations: OscarNomination[]
}

function formatCategory(category: string) {
  return category.toLowerCase().replace(/\b[a-z]/g, (letter) => letter.toUpperCase())
}

export function OscarAwardsTooltip({ id, nominations }: OscarAwardsTooltipProps) {
  return (
    <span className="oscar-award-tooltip" id={id} role="tooltip">
      <span className="oscar-award-tooltip-heading">Oscar category results</span>
      {nominations.length > 0 ? (
        <span className="oscar-award-tooltip-list" role="list">
          {nominations.map((nomination) => {
            const status = nomination.isWinner ? 'Won' : 'Nominated for'
            const category = formatCategory(nomination.category)
            return (
              <span aria-label={`${status} ${category}`} className="oscar-award-tooltip-row" key={nomination.id} role="listitem">
                <span className={`oscar-award-tooltip-status${nomination.isWinner ? ' is-winner' : ' is-nominee'}`}>
                  {nomination.isWinner ? 'Winner' : 'Nominee'}
                </span>
                <span className="oscar-award-tooltip-category">{category}</span>
              </span>
            )
          })}
        </span>
      ) : (
        <span className="oscar-award-tooltip-empty">No nominations in the selected categories</span>
      )}
    </span>
  )
}
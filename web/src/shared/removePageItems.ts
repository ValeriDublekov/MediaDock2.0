import type { PageResponse } from '../api/types'

export function removePageItems<T>(page: PageResponse<T> | null, shouldRemove: (item: T) => boolean): PageResponse<T> | null {
  if (!page) return page
  const items = page.items.filter((item) => !shouldRemove(item))
  const removedCount = page.items.length - items.length
  if (removedCount === 0) return page

  const totalCount = Math.max(0, page.totalCount - removedCount)
  return {
    ...page,
    items,
    totalCount,
    totalPages: totalCount === 0 ? 0 : Math.max(page.page, Math.ceil(totalCount / page.pageSize)),
  }
}
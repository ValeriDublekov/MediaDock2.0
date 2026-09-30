import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { addFavorite, getFavorites, removeFavorite, updateFavorite } from '../../api/client'
import type { FavoriteMovie } from '../../api/types'

interface FavoriteState {
  movies: Record<number, FavoriteMovie>
  pending: number | null
  loading: boolean
  error: string | null
  retry: () => void
  add: (titleId: number, from: 'oscar' | 'catalog') => Promise<void>
  update: (titleId: number, input: { toWatch?: boolean; toDownload?: boolean }) => Promise<void>
  remove: (titleId: number) => Promise<void>
}

const FavoriteContext = createContext<FavoriteState | null>(null)
const emptyState: FavoriteState = {
  movies: {}, pending: null, loading: false, error: null, retry: () => {},
  add: async () => {}, update: async () => {}, remove: async () => {},
}

export function FavoriteProvider({ children }: { children: ReactNode }) {
  const [movies, setMovies] = useState<Record<number, FavoriteMovie>>({})
  const [pending, setPending] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    async function load() {
      try {
        const first = await getFavorites({ page: 1, pageSize: 100 })
        const pages = await Promise.all(Array.from({ length: first.totalPages - 1 }, (_, index) =>
          getFavorites({ page: index + 2, pageSize: 100 })))
        if (active) {
          setMovies(Object.fromEntries([first, ...pages].flatMap((page) => page.items).map((movie) => [movie.titleId, movie])))
          setError(null)
        }
      } catch (reason) {
        if (active) setError(reason instanceof Error ? reason.message : 'Favorites could not be loaded.')
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [attempt])

  async function mutate(titleId: number, action: () => Promise<FavoriteMovie | void>) {
    setPending(titleId)
    setError(null)
    try {
      const movie = await action()
      setMovies((current) => {
        const next = { ...current }
        if (movie) next[titleId] = movie
        else delete next[titleId]
        return next
      })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save favorite. Retry the action.')
    } finally {
      setPending(null)
    }
  }

  return <FavoriteContext.Provider value={{ movies, pending, loading, error, retry: () => { setLoading(true); setAttempt((value) => value + 1) },
    add: (titleId, from) => mutate(titleId, () => addFavorite(titleId, from)),
    update: (titleId, input) => mutate(titleId, () => updateFavorite(titleId, input)),
    remove: (titleId) => mutate(titleId, () => removeFavorite(titleId)),
  }}>{children}</FavoriteContext.Provider>
}

export function useFavorites() {
  return useContext(FavoriteContext) ?? emptyState
}
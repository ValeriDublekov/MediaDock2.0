import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { addFavorite, getFavorites, removeFavorite, updateFavorite } from '../../api/client'
import type { FavoriteMovie } from '../../api/types'

interface FavoriteState {
  available: boolean
  movies: Record<string, FavoriteMovie>
  pending: string | null
  loading: boolean
  error: string | null
  retry: () => void
  add: (imdbId: string, from: 'oscar' | 'catalog' | 'recommendation') => Promise<boolean>
  update: (imdbId: string, input: { toWatch?: boolean; toDownload?: boolean }) => Promise<void>
  remove: (imdbId: string) => Promise<void>
}

const FavoriteContext = createContext<FavoriteState | null>(null)
const emptyState: FavoriteState = {
  available: false, movies: {}, pending: null, loading: false, error: null, retry: () => {},
  add: async () => false, update: async () => {}, remove: async () => {},
}

export function FavoriteProvider({ children, accountKey }: { children: ReactNode; accountKey: string | null }) {
  const [movies, setMovies] = useState<Record<string, FavoriteMovie>>({})
  const [pending, setPending] = useState<string | null>(null)
  const [loading, setLoading] = useState(accountKey !== null)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    if (accountKey === null) return
    async function load() {
      try {
        const first = await getFavorites({ page: 1, pageSize: 100 })
        const pages = await Promise.all(Array.from({ length: first.totalPages - 1 }, (_, index) =>
          getFavorites({ page: index + 2, pageSize: 100 })))
        if (active) {
          setMovies(Object.fromEntries([first, ...pages].flatMap((page) => page.items).map((movie) => [movie.imdbId, movie])))
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
  }, [accountKey, attempt])

  async function mutate(imdbId: string, action: () => Promise<FavoriteMovie | void>): Promise<boolean> {
    if (accountKey === null) return false
    setPending(imdbId)
    setError(null)
    try {
      const movie = await action()
      setMovies((current) => {
        const next = { ...current }
        if (movie) next[imdbId] = movie
        else delete next[imdbId]
        return next
      })
      return true
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save favorite. Retry the action.')
      return false
    } finally {
      setPending(null)
    }
  }

  return <FavoriteContext.Provider value={{ available: accountKey !== null, movies, pending, loading, error, retry: () => { setLoading(true); setAttempt((value) => value + 1) },
    add: (imdbId, from) => mutate(imdbId, () => addFavorite(imdbId, from)),
    update: async (imdbId, input) => { await mutate(imdbId, () => updateFavorite(imdbId, input)) },
    remove: async (imdbId) => { await mutate(imdbId, () => removeFavorite(imdbId)) },
  }}>{children}</FavoriteContext.Provider>
}

export function useFavorites() {
  return useContext(FavoriteContext) ?? emptyState
}
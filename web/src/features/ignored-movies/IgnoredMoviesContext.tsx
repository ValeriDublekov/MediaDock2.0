import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { addIgnoredMovie, getIgnoredMovies, removeIgnoredMovie } from '../../api/client'
import type { IgnoredMovie } from '../../api/types'
import { titlePageSize } from '../../shared/titlePagination'

interface IgnoredMoviesState {
  available: boolean
  movies: Record<string, IgnoredMovie>
  pending: string | null
  loading: boolean
  error: string | null
  retry: () => void
  ignore: (imdbId: string) => Promise<boolean>
  remove: (imdbId: string) => Promise<void>
}

const IgnoredMoviesContext = createContext<IgnoredMoviesState | null>(null)
const emptyState: IgnoredMoviesState = {
  available: false,
  movies: {},
  pending: null,
  loading: false,
  error: null,
  retry: () => {},
  ignore: async () => false,
  remove: async () => {},
}

export function IgnoredMoviesProvider({ children, accountKey }: { children: ReactNode; accountKey: string | null }) {
  const [movies, setMovies] = useState<Record<string, IgnoredMovie>>({})
  const [pending, setPending] = useState<string | null>(null)
  const [loading, setLoading] = useState(accountKey !== null)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    if (accountKey === null) {
      setMovies({})
      setLoading(false)
      return
    }

    setLoading(true)
    setError(null)
    async function load() {
      try {
        const first = await getIgnoredMovies({ page: 1, pageSize: titlePageSize })
        const pages = await Promise.all(Array.from({ length: first.totalPages - 1 }, (_, index) =>
          getIgnoredMovies({ page: index + 2, pageSize: titlePageSize })))
        if (active) {
          setMovies(Object.fromEntries([first, ...pages].flatMap((page) => page.items).map((movie) => [movie.imdbId, movie])))
          setError(null)
        }
      } catch (reason) {
        if (active) setError(reason instanceof Error ? reason.message : 'The ignore list could not be loaded.')
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [accountKey, attempt])

  async function ignore(imdbId: string): Promise<boolean> {
    if (accountKey === null) return false
    setPending(imdbId)
    setError(null)
    try {
      const movie = await addIgnoredMovie(imdbId)
      setMovies((current) => ({ ...current, [movie.imdbId]: movie }))
      return true
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not add this title to the ignore list.')
      return false
    } finally {
      setPending(null)
    }
  }

  async function remove(imdbId: string) {
    if (accountKey === null) return
    setPending(imdbId)
    setError(null)
    try {
      await removeIgnoredMovie(imdbId)
      setMovies((current) => {
        const next = { ...current }
        delete next[imdbId]
        return next
      })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not remove this title from the ignore list.')
    } finally {
      setPending(null)
    }
  }

  return <IgnoredMoviesContext.Provider value={{
    available: accountKey !== null,
    movies,
    pending,
    loading,
    error,
    retry: () => { setLoading(true); setAttempt((value) => value + 1) },
    ignore,
    remove,
  }}>{children}</IgnoredMoviesContext.Provider>
}

export function useIgnoredMovies() {
  return useContext(IgnoredMoviesContext) ?? emptyState
}
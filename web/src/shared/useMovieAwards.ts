import { useEffect, useState } from 'react'
import { getMovieAwards } from '../api/client'
import type { MovieAwardRecognition } from '../api/types'

interface AwardsResponse {
  key: string
  awards: MovieAwardRecognition[]
  error: string | null
}

export function useMovieAwards(requestKey: string) {
  const [response, setResponse] = useState<AwardsResponse>({ key: '', awards: [], error: null })

  useEffect(() => {
    let current = true
    const imdbIds = requestKey ? requestKey.split(',') : []
    if (imdbIds.length === 0) return () => { current = false }

    getMovieAwards(imdbIds)
      .then((awards) => { if (current) setResponse({ key: requestKey, awards, error: null }) })
      .catch((requestError: unknown) => {
        if (current) setResponse({
          key: requestKey,
          awards: [],
          error: requestError instanceof Error ? requestError.message : 'Combined awards could not be loaded.',
        })
      })
    return () => { current = false }
  }, [requestKey])

  return response.key === requestKey
    ? { awards: response.awards, error: response.error }
    : { awards: [], error: null }
}
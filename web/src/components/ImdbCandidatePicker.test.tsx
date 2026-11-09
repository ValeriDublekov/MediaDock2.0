import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  addManualImdbTitle,
  lookupManualImdbTitleCandidate,
  searchManualImdbTitleCandidates,
} from '../api/client'
import type { ImdbCandidate, ImdbCandidateSearch } from '../api/types'
import { ImdbCandidatePicker } from './ImdbCandidatePicker'

vi.mock('../api/client', () => ({
  addManualImdbTitle: vi.fn(),
  lookupManualImdbTitleCandidate: vi.fn(),
  searchManualImdbTitleCandidates: vi.fn(),
  applyCatalogTitleImdbCandidate: vi.fn(),
  applyParseLogImdbCandidate: vi.fn(),
  lookupCatalogTitleImdbCandidate: vi.fn(),
  lookupParseLogImdbCandidate: vi.fn(),
  searchCatalogTitleImdbCandidates: vi.fn(),
  searchParseLogImdbCandidates: vi.fn(),
}))

const candidate: ImdbCandidate = { title: 'Quiet River', year: 2004, imdbId: 'tt12345678', sourceType: 'movie' }
const result: ImdbCandidateSearch = { status: 'found', candidates: [candidate], totalResults: 1, errorCode: null }

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

describe('manual IMDb title picker', () => {
  it('searches by title and adds the selected title to favorites', async () => {
    vi.mocked(searchManualImdbTitleCandidates).mockResolvedValue(result)
    const onApplied = vi.fn()
    render(<ImdbCandidatePicker onApplied={onApplied} onClose={vi.fn()} target={{ source: 'manual', list: 'favorites' }} />)

    fireEvent.change(screen.getByLabelText('Title to search'), { target: { value: 'Quiet River' } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Add to favorites' }))

    await waitFor(() => expect(searchManualImdbTitleCandidates).toHaveBeenCalledWith('Quiet River', 1))
    expect(addManualImdbTitle).toHaveBeenCalledWith(candidate.imdbId)
    expect(onApplied).toHaveBeenCalledWith(candidate)
  })

  it('looks up an IMDb ID and passes a selected personal score to the parent', async () => {
    vi.mocked(lookupManualImdbTitleCandidate).mockResolvedValue(result)
    const onApplied = vi.fn()
    render(<ImdbCandidatePicker onApplied={onApplied} onClose={vi.fn()} target={{ source: 'manual', list: 'personal-ratings' }} />)

    fireEvent.click(screen.getByRole('tab', { name: 'IMDb ID' }))
    fireEvent.change(screen.getByLabelText('IMDb ID'), { target: { value: candidate.imdbId } })
    fireEvent.click(screen.getByRole('button', { name: 'Load IMDb info' }))
    fireEvent.change(screen.getByLabelText('Your rating (1-10)'), { target: { value: '9' } })
    fireEvent.click(await screen.findByRole('button', { name: 'Add rating' }))

    await waitFor(() => expect(lookupManualImdbTitleCandidate).toHaveBeenCalledWith(candidate.imdbId))
    expect(addManualImdbTitle).toHaveBeenCalledWith(candidate.imdbId)
    expect(onApplied).toHaveBeenCalledWith(candidate, 9)
  })

  it('adds a confirmed candidate to the IMDb titles catalog', async () => {
    vi.mocked(searchManualImdbTitleCandidates).mockResolvedValue(result)
    const onApplied = vi.fn()
    render(<ImdbCandidatePicker onApplied={onApplied} onClose={vi.fn()} target={{ source: 'manual', list: 'imdb-titles' }} />)

    fireEvent.change(screen.getByLabelText('Title to search'), { target: { value: 'Quiet River' } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Add to IMDb titles' }))

    await waitFor(() => expect(addManualImdbTitle).toHaveBeenCalledWith(candidate.imdbId))
    expect(onApplied).toHaveBeenCalledWith(candidate)
  })

})
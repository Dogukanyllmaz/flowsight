import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { fixtureRequest, memorySource, renderWithSource } from '../../../test/renderWithSource'
import { RequestTracker } from './RequestTracker'

beforeEach(() => localStorage.clear())

async function search(value: string) {
  const user = userEvent.setup()
  await user.clear(screen.getByLabelText('Request number'))
  await user.type(screen.getByLabelText('Request number'), value)
  await user.click(screen.getByRole('button', { name: 'Show' }))
  return user
}

describe('RequestTracker', () => {
  it('shows the strip for a known request', async () => {
    renderWithSource(<RequestTracker />)
    await search(' pr-001783 ')

    expect(await screen.findByRole('heading', { name: /PR-001783/ })).toBeInTheDocument()
    expect(screen.getAllByRole('list', { name: 'PR-001783' })[0].querySelectorAll('li')).toHaveLength(7)
    expect(screen.getByText('Slower than 97.9% of similar requests')).toBeInTheDocument()
  })

  it('accepts a bare number', async () => {
    renderWithSource(<RequestTracker />)
    await search('1783')

    expect(await screen.findByRole('heading', { name: /PR-001783/ })).toBeInTheDocument()
  })

  it('ShowsNotFound_ForUnknownRequest', async () => {
    renderWithSource(<RequestTracker />)
    await search('PR-999999')

    expect(await screen.findByText('No request found with this number.')).toBeInTheDocument()
  })

  it('shows not found for malformed input without crashing', async () => {
    renderWithSource(<RequestTracker />)
    await search('<script>')

    expect(await screen.findByText('No request found with this number.')).toBeInTheDocument()
  })

  it('HidesComparison_WhenNull', async () => {
    renderWithSource(<RequestTracker />, memorySource([{ ...fixtureRequest, slowerThanPercent: null }]))
    await search('PR-001783')

    await screen.findByRole('heading', { name: /PR-001783/ })
    expect(screen.queryByText(/Slower than/)).not.toBeInTheDocument()
  })

  it('switches stage names to Turkish', async () => {
    renderWithSource(<RequestTracker />)
    const user = await search('PR-001783')
    await screen.findByRole('heading', { name: /PR-001783/ })

    await user.click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getAllByText('Satın alma').length).toBeGreaterThan(0)
  })

  it('uses a decimal comma in Turkish', async () => {
    renderWithSource(<RequestTracker />)
    const user = await search('PR-001783')
    await screen.findByRole('heading', { name: /PR-001783/ })

    await user.click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getByText('18,2 g')).toBeInTheDocument()
  })

  it('lists example open requests from the index', async () => {
    renderWithSource(<RequestTracker />)

    expect(await screen.findByRole('button', { name: 'PR-001783' })).toBeInTheDocument()
  })

  it('shows why an open request is waiting', async () => {
    renderWithSource(<RequestTracker />)
    await search('PR-001783')

    expect(await screen.findByText('Payments are made once a week, on Friday.')).toBeInTheDocument()
  })

  it('shows no reasons for a completed request', async () => {
    const closed = { ...fixtureRequest, status: 'completed' as const, currentStage: null, waitingReasons: [] }
    renderWithSource(<RequestTracker />, memorySource([closed]))
    await search('PR-001783')

    await screen.findByRole('heading', { name: /PR-001783/ })
    expect(screen.queryByRole('heading', { name: 'Why is it waiting?' })).not.toBeInTheDocument()
  })

  it('localises the reason in Turkish', async () => {
    renderWithSource(<RequestTracker />)
    const user = await search('PR-001783')
    await screen.findByRole('heading', { name: /PR-001783/ })

    await user.click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getByText('Ödemeler haftada bir, Cuma günü yapılıyor.')).toBeInTheDocument()
  })

  it('describes a shared queue of unknown size without a number', async () => {
    const reasons = [{ code: 'sharedQueue', params: {} }] as const
    renderWithSource(<RequestTracker />, memorySource([{ ...fixtureRequest, waitingReasons: [...reasons] }]))
    await search('PR-001783')

    expect(await screen.findByText('It is in a shared queue; someone in the group will pick it up.')).toBeInTheDocument()
  })

  it('formats every reason code', async () => {
    const reasons = [
      { code: 'assigneeAbsent', params: { untilDate: '2026-10-30' } },
      { code: 'returnedForCorrection', params: {} },
      { code: 'priceVariance', params: { percent: '-4.5' } },
      { code: 'partialDelivery', params: { received: '6', ordered: '10' } },
      { code: 'sharedQueue', params: { candidates: '6' } },
      { code: 'unknown', params: {} },
    ] as const
    renderWithSource(<RequestTracker />, memorySource([{ ...fixtureRequest, waitingReasons: [...reasons] }]))
    await search('PR-001783')

    expect(await screen.findByText(/away until 30\/10\/2026/)).toBeInTheDocument()
    expect(screen.getByText('It was returned for a correction.')).toBeInTheDocument()
    expect(screen.getByText(/differs from the order by -4.5%/)).toBeInTheDocument()
    expect(screen.getByText('Only 6 of 10 have been delivered so far.')).toBeInTheDocument()
    expect(screen.getByText(/shared queue with 6 people/)).toBeInTheDocument()
    expect(screen.getByText('No specific reason recorded.')).toBeInTheDocument()
  })
})

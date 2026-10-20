import { screen } from '@testing-library/react'
import { fixtureStage, memorySource, renderWithSource } from '../../../test/renderWithSource'
import { StageDetail } from './StageDetail'

beforeEach(() => localStorage.clear())

describe('StageDetail', () => {
  it('shows queue and work time', async () => {
    renderWithSource(<StageDetail periodId="2026-03" stage="invoice" />)

    expect((await screen.findAllByText(`${fixtureStage.medianQueueDays} d`)).length).toBeGreaterThan(0)
    expect(screen.getAllByText(`${fixtureStage.medianWorkDays} d`).length).toBeGreaterThan(0)
  })

  it('hints at a shared queue when group-queue work dominates', async () => {
    renderWithSource(<StageDetail periodId="2026-03" stage="invoice" />)

    expect(await screen.findByText(/Work is picked from a shared queue/)).toBeInTheDocument()
  })

  it('does not hint when work is routed', async () => {
    const routed = { ...fixtureStage, assignmentMix: { fieldRouted: 40 } }
    renderWithSource(<StageDetail periodId="2026-03" stage="invoice" />, memorySource(undefined, { stage: routed }))

    await screen.findAllByText(`${fixtureStage.medianQueueDays} d`)
    expect(screen.queryByText(/shared queue:/)).not.toBeInTheDocument()
  })

  it('links slow cases to the request tracker', async () => {
    renderWithSource(<StageDetail periodId="2026-03" stage="invoice" />)
    const slow = fixtureStage.slowestCompleted[0]

    expect((await screen.findAllByRole('link', { name: slow.caseId }))[0]).toHaveAttribute('href', `#/track/${slow.caseId}`)
  })

  it('shows people only by pseudonymous id, with the purpose note', async () => {
    renderWithSource(<StageDetail periodId="2026-03" stage="invoice" />)

    expect(await screen.findByText(fixtureStage.byPerson[0].key)).toBeInTheDocument()
    expect(screen.getByText(/not for rating people/)).toBeInTheDocument()
  })

  it('ShowsNotFound_ForUnknownStage', async () => {
    renderWithSource(<StageDetail periodId="last12" stage="order" />)

    expect(await screen.findByText('No data for this stage in this period.')).toBeInTheDocument()
  })
})

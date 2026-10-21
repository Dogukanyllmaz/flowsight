import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { fixtureChanges, memorySource, renderWithSource } from '../../../test/renderWithSource'
import type { ChangesView } from '../data/ownerSchemas'
import { I18nProvider, LanguageToggle } from '../i18n/I18n'
import { BeforeAfter } from './BeforeAfter'

beforeEach(() => localStorage.clear())

const change = fixtureChanges.changes[0]

function renderChanges(index?: number, changes: ChangesView = fixtureChanges) {
  return renderWithSource(
    <I18nProvider>
      <LanguageToggle />
      <BeforeAfter index={index} />
    </I18nProvider>,
    memorySource(undefined, { changes }),
  )
}

describe('BeforeAfter', () => {
  it('lists changes with their verdict', async () => {
    renderChanges()

    const list = await screen.findByRole('list', { name: 'Process changes' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(fixtureChanges.changes.length)
    expect(within(items[0]).getByText(change.stepName, { exact: false })).toBeInTheDocument()
    expect(within(items[0]).getByText('improved')).toBeInTheDocument()
  })

  it('shows before and after with the change', async () => {
    renderChanges(0)

    expect(await screen.findByText(`Median time in the step fell by ${Math.abs(change.medianChangePercent!)}% — improved.`)).toBeInTheDocument()
    expect(screen.getByText(/90 days before vs 90 days after 3 Jun 2026/)).toBeInTheDocument()
    const table = screen.getByRole('table', { name: /Before and after/ })
    expect(within(table).getByText(`${change.beforeStats.medianDays} d`)).toBeInTheDocument()
    expect(within(table).getByText(`${change.afterStats.medianDays} d`)).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Median time in the step per month/ })).toBeInTheDocument()
  })

  it('uses the exact median change in the table', async () => {
    renderChanges(0)

    const table = await screen.findByRole('table', { name: /Before and after/ })
    const median = within(table).getAllByRole('row').find((r) => r.querySelector('th')?.textContent === 'Median')!
    expect(within(median).getByText(`−${Math.abs(change.medianChangePercent!)}%`)).toBeInTheDocument()
  })

  it('formats every number for the language', async () => {
    const changes: ChangesView = { changes: [{ ...change, beforeStats: { ...change.beforeStats, averageWaiting: 138.3 } }] }
    renderChanges(0, changes)
    await screen.findByRole('table', { name: /Before and after/ })

    await userEvent.setup().click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getByText('138,3')).toBeInTheDocument()
  })

  it('keeps the sign when the change is small', async () => {
    renderChanges(0, { changes: [{ ...change, verdict: 'unchanged', medianChangePercent: -18 }] })

    expect(await screen.findByText('Median time in the step changed little (−18%).')).toBeInTheDocument()
  })

  it('shows a dash when there is no average to show', async () => {
    renderChanges(0, { changes: [{ ...change, afterStats: { ...change.afterStats, averageWaiting: null } }] })

    const table = await screen.findByRole('table', { name: /Before and after/ })
    const waiting = within(table).getAllByRole('row').find((r) => r.querySelector('th')?.textContent === 'Average items waiting')!
    expect(within(waiting).getAllByText('–').length).toBe(2)
  })

  it('ShowsNotEnoughData', async () => {
    const thin: ChangesView = {
      changes: [{
        ...change,
        verdict: 'insufficientData',
        medianChangePercent: null,
        afterStats: { ...change.afterStats, steps: 3, medianDays: null, p90Days: null, medianQueueDays: null },
      }],
    }
    renderChanges(0, thin)

    expect(await screen.findByText('Not enough data to judge.')).toBeInTheDocument()
    expect(within(screen.getByRole('table', { name: /Before and after/ })).getAllByText('–').length).toBeGreaterThan(0)
  })

  it('ShowsNotFound_ForUnknownChange', async () => {
    renderChanges(5)

    expect(await screen.findByText('This change does not exist.')).toBeInTheDocument()
  })

  it('switches to Turkish', async () => {
    renderChanges(0)
    await screen.findByRole('table', { name: /Before and after/ })

    await userEvent.setup().click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getByRole('heading', { name: 'İşe yaradı mı?' })).toBeInTheDocument()
    expect(screen.getAllByText('iyileşti').length).toBeGreaterThan(0)
  })
})

import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { fixtureOverview, memorySource, renderWithSource } from '../../../test/renderWithSource'
import type { OverviewView } from '../data/ownerSchemas'
import { I18nProvider, LanguageToggle } from '../i18n/I18n'
import { strings } from '../i18n/strings'
import { FlowOverview } from './FlowOverview'

beforeEach(() => localStorage.clear())

function withStage(stage: string, change: Partial<OverviewView['stages'][number]>): OverviewView {
  return { ...fixtureOverview, stages: fixtureOverview.stages.map((s) => (s.stage === stage ? { ...s, ...change } : s)) }
}

function renderOverview(overview: OverviewView = fixtureOverview, periodId = overview.periodId) {
  return renderWithSource(
    <I18nProvider>
      <LanguageToggle />
      <FlowOverview periodId={periodId} onPeriodChange={() => {}} />
    </I18nProvider>,
    memorySource(undefined, { overview }),
  )
}

async function bottleneckRows() {
  const table = await screen.findByRole('table', { name: /bottleneck/i })
  return within(table).getAllByRole('row').slice(1)
}

describe('FlowOverview', () => {
  it('lists stages in bottleneck order', async () => {
    renderOverview()
    const first = [...fixtureOverview.stages].sort((a, b) => a.rank - b.rank)[0]

    const rows = await bottleneckRows()
    expect(rows).toHaveLength(7)
    expect(within(rows[0]).getByText(strings.en[`stage.${first.stage}`])).toBeInTheDocument()
  })

  it('marks much slower stages', async () => {
    renderOverview(withStage('invoice', { pace: 'muchSlower' }))

    await bottleneckRows()
    expect(document.querySelector('[data-stage="invoice"][data-pace="muchSlower"]')).not.toBeNull()
    expect(screen.getAllByText('much slower').length).toBeGreaterThan(0)
  })

  it('ShowsDash_ForMissingMedian', async () => {
    renderOverview(withStage('payment', { medianDays: null, rank: 1, sharePercent: 99 }))

    const rows = await bottleneckRows()
    expect(within(rows[0]).getAllByText('–').length).toBeGreaterThan(0)
  })

  it('links each stage to its drill-down', async () => {
    renderOverview()

    await bottleneckRows()
    expect(screen.getAllByRole('link', { name: 'Invoice' })[0]).toHaveAttribute('href', '#/flow/last12/invoice')
  })

  it('ShowsNotFound_ForUnknownPeriod', async () => {
    renderOverview(fixtureOverview, '2020-01')

    expect(await screen.findByText('No data for this period.')).toBeInTheDocument()
  })

  it('offers the last 12 months and every month as periods', async () => {
    renderOverview()

    const select = await screen.findByLabelText('Period')
    const options = await within(select).findAllByRole('option')
    expect(options[0]).toHaveValue('last12')
    expect(options).toHaveLength(13)
  })

  it('switches labels to Turkish', async () => {
    renderOverview()
    await bottleneckRows()

    await userEvent.setup().click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getAllByText('Fatura').length).toBeGreaterThan(0)
    expect(screen.getByRole('table', { name: /darboğaz/i })).toBeInTheDocument()
  })
})

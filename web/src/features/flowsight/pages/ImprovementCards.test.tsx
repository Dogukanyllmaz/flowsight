import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { fixtureCards, memorySource, renderWithSource } from '../../../test/renderWithSource'
import { I18nProvider, LanguageToggle } from '../i18n/I18n'
import { ImprovementCards } from './ImprovementCards'

beforeEach(() => localStorage.clear())

function renderCards(cardId?: string, cards = fixtureCards) {
  return renderWithSource(
    <I18nProvider>
      <LanguageToggle />
      <ImprovementCards periodId="2026-03" cardId={cardId} onPeriodChange={() => {}} />
    </I18nProvider>,
    memorySource(undefined, { cards }),
  )
}

describe('ImprovementCards', () => {
  it('lists cards with expected saving', async () => {
    renderCards()

    const list = await screen.findByRole('list', { name: 'Improvement cards' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(fixtureCards.cards.length)
    expect(within(items[0]).getByText('Shared queue')).toBeInTheDocument()
    const first = fixtureCards.cards[0]
    expect(within(items[0]).getByText(new RegExp(`${first.expectedSavingDaysPerLine} working days per line · ${first.affectedLines} lines`))).toBeInTheDocument()
  })

  it('opens a card with evidence, cause and change', async () => {
    renderCards('sharedQueueSearch:invoice-matching')

    expect(await screen.findByRole('heading', { name: 'What we see' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Why' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'What to change' })).toBeInTheDocument()
    expect(screen.getByText(/sends every item to a queue shared by the whole group/)).toBeInTheDocument()
    expect(screen.getByText(/Route each item straight to the responsible person/)).toBeInTheDocument()
    expect(screen.getByText(/of the step time is waiting in the queue/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /stage in detail/ })).toHaveAttribute('href', '#/flow/2026-03/invoice')
  })

  it('links a card to the before/after of its step', async () => {
    renderCards('sharedQueueSearch:invoice-matching')

    expect(await screen.findByRole('link', { name: /see before and after/ })).toHaveAttribute('href', '#/changes/0')
  })

  it('shows the segment when a pattern is limited to one category', async () => {
    renderCards('reworkLoop:sourcing')

    expect(await screen.findByText(/Only in: Maintenance service/)).toBeInTheDocument()
  })

  it('ShowsNotFound_ForUnknownCard', async () => {
    renderCards('nope:nothing')

    expect(await screen.findByText('This card does not exist in the selected period.')).toBeInTheDocument()
  })

  it('notes checks that could not run', async () => {
    renderCards(undefined, { ...fixtureCards, insufficientData: ['singlePointOfFailure:payment-run'] })

    expect(await screen.findByText(/Single point of failure.*absence records/)).toBeInTheDocument()
  })

  it('switches to Turkish', async () => {
    renderCards('sharedQueueSearch:invoice-matching')
    await screen.findByRole('heading', { name: 'What we see' })

    await userEvent.setup().click(screen.getByRole('button', { name: 'TR' }))

    expect(screen.getByRole('heading', { name: 'Ne değişmeli' })).toBeInTheDocument()
    expect(screen.getAllByText('Ortak kuyruk').length).toBeGreaterThan(0)
  })
})

import { render, screen, within } from '@testing-library/react'
import type { StageView } from '../data/schemas'
import { stageNames } from '../data/schemas'
import { StageStrip } from './StageStrip'

function stages(overrides: Partial<Record<string, Partial<StageView>>> = {}): StageView[] {
  return stageNames.map((stage) => ({
    stage,
    state: 'notStarted',
    enteredAt: null,
    leftAt: null,
    workingDays: 0,
    calendarDays: 0,
    typicalWorkingDays: null,
    ...overrides[stage],
  }))
}

beforeEach(() => localStorage.clear())

describe('StageStrip', () => {
  it('renders seven stages in order with working days', () => {
    render(<StageStrip stages={stages({ request: { state: 'completed', workingDays: 1.5 } })} currentStage={null} />)

    const items = screen.getAllByRole('listitem')
    expect(items.map((i) => i.dataset.stage)).toEqual([...stageNames])
    expect(within(items[0]).getByText('Request')).toBeInTheDocument()
    expect(within(items[0]).getByText('1.5 d')).toBeInTheDocument()
  })

  it('marks the current stage as in progress', () => {
    render(
      <StageStrip
        stages={stages({ request: { state: 'completed' }, approval: { state: 'inProgress', workingDays: 1, typicalWorkingDays: 1 } })}
        currentStage="approval"
      />,
    )

    const current = screen.getAllByRole('listitem')[1]
    expect(current).toHaveAttribute('aria-current', 'step')
    expect(current.dataset.state).toBe('inProgress')
  })

  it('marks a stage red when it exceeds twice its typical duration', () => {
    render(
      <StageStrip
        stages={stages({ invoice: { state: 'inProgress', workingDays: 12, typicalWorkingDays: 3 } })}
        currentStage="invoice"
      />,
    )

    expect(screen.getAllByRole('listitem')[5].dataset.state).toBe('delayed')
  })

  it('does not mark delay without a typical duration', () => {
    render(<StageStrip stages={stages({ invoice: { state: 'inProgress', workingDays: 12 } })} currentStage="invoice" />)

    expect(screen.getAllByRole('listitem')[5].dataset.state).toBe('inProgress')
  })
})

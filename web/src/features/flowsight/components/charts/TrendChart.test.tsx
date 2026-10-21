import { render, screen } from '@testing-library/react'
import { I18nProvider } from '../../i18n/I18n'
import { TrendChart } from './TrendChart'

beforeEach(() => localStorage.clear())

describe('TrendChart', () => {
  it('formats axis ticks for the language', () => {
    localStorage.setItem('flowsight.lang', 'tr')
    render(
      <I18nProvider>
        <TrendChart
          label="Trend"
          summary="Summary"
          columns="Month"
          xLabels={['A', 'B']}
          format={(v) => String(v)}
          series={[{ key: 's', label: 'S', slot: 1, values: [0.4, 0.9] }]}
        />
      </I18nProvider>,
    )

    expect(screen.getByText('0,25')).toBeInTheDocument()
  })
})

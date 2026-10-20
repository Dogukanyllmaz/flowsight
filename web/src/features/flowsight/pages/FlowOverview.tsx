import { BarChart, toneIcon } from '../components/charts/BarChart'
import { TrendChart } from '../components/charts/TrendChart'
import { useOverview, usePeriods } from '../data/hooks'
import type { Pace } from '../data/ownerSchemas'
import { useStrings } from '../i18n/I18n'
import { formatRoute } from '../../../routing'

const paces: Pace[] = ['muchSlower', 'slower', 'normal', 'faster', 'unknown']

/** "2026-03" → "Mar 2026" in the active language; "last12" → its label. */
export function usePeriodLabel() {
  const { t, language } = useStrings()
  return (id: string) => {
    const match = /^(\d{4})-(\d{2})$/.exec(id)
    if (!match) return t('last12')
    const date = new Date(Number(match[1]), Number(match[2]) - 1, 1)
    return date.toLocaleDateString(language === 'tr' ? 'tr-TR' : 'en-GB', { month: 'short', year: 'numeric' })
  }
}

export function FlowOverview({ periodId, onPeriodChange }: { periodId: string; onPeriodChange: (periodId: string) => void }) {
  const { t, stage } = useStrings()
  const periodLabel = usePeriodLabel()
  const periods = usePeriods()
  const overview = useOverview(periodId)

  const days = (value: number | null) => (value === null ? '–' : t('workingDaysShort', { days: value }))
  const options = periods.data
    ? [...periods.data.periods].sort((a, b) => (a.id === 'last12' ? -1 : b.id === 'last12' ? 1 : b.id.localeCompare(a.id)))
    : []

  return (
    <div className="fs-page fs-wide">
      <header className="fs-header">
        <div>
          <h1>{t('flowTitle')}</h1>
          <p className="fs-muted">{t('flowSubtitle')}</p>
        </div>
        <label className="fs-period">
          <span>{t('period')}</span>
          <select value={periodId} onChange={(e) => onPeriodChange(e.target.value)}>
            {options.map((p) => (
              <option key={p.id} value={p.id}>
                {periodLabel(p.id)}
              </option>
            ))}
          </select>
        </label>
      </header>

      {overview.isPending && <p className="fs-muted">{t('loading')}</p>}
      {overview.isError && <p role="alert">{t('loadError')}</p>}
      {overview.data === null && <p role="alert">{t('periodNotFound')}</p>}

      {overview.data && (() => {
        const ranked = [...overview.data.stages].sort((a, b) => a.rank - b.rank || b.sharePercent - a.sharePercent)
        const top = ranked[0]
        return (
          <>
            <div className="fs-tiles">
              <div className="fs-tile">
                <span className="fs-muted">{t('wipTotal')}</span>
                <strong>{overview.data.stages.reduce((sum, s) => sum + s.wipEnd, 0)}</strong>
              </div>
              <div className="fs-tile">
                <span className="fs-muted">{t('biggestBottleneck')}</span>
                <strong>{stage(top.stage)}</strong>
                <span className="fs-muted">{t('shareOfLeadTimeOf', { percent: top.sharePercent })}</span>
              </div>
            </div>

            <BarChart
              label={t('chartWip')}
              summary={t('chartWipSummary')}
              columns={[t('colStage'), t('colWip'), t('colPace')]}
              bars={overview.data.stages.map((s) => ({
                key: s.stage,
                label: stage(s.stage),
                value: s.wipEnd,
                tone: s.pace,
                detail: `${stage(s.stage)}: ${s.wipEnd} · ${t('colMedian')} ${days(s.medianDays)} · ${t(`pace.${s.pace}`)}`,
                href: formatRoute({ page: 'stage', periodId, stage: s.stage }),
              }))}
            />
            <div className="fs-legend">
              <span className="fs-muted">{t('paceLegend')}:</span>
              {paces.map((p) => (
                <span key={p}>
                  <i className={`fs-swatch fs-tone-${p}`} />
                  {toneIcon[p] && <span aria-hidden="true">{toneIcon[p]} </span>}
                  <span>{t(`pace.${p}`)}</span>
                </span>
              ))}
            </div>

            {periods.data && (
              <TrendChart
                label={t('chartTrend')}
                summary={t('chartTrendSummary')}
                columns={t('colMonth')}
                xLabels={periods.data.trend.map((p) => periodLabel(p.periodId))}
                format={days}
                series={[
                  { key: 'median', label: t('median'), slot: 1, values: periods.data.trend.map((p) => p.medianLeadDays) },
                  { key: 'p90', label: t('p90'), slot: 2, values: periods.data.trend.map((p) => p.p90LeadDays) },
                ]}
              />
            )}

            <div className="fs-table-wrap">
            <table className="fs-table">
              <caption>{t('bottleneckTable')}</caption>
              <thead>
                <tr>
                  <th scope="col">{t('colRank')}</th>
                  <th scope="col">{t('colStage')}</th>
                  <th scope="col">{t('colShare')}</th>
                  <th scope="col">{t('colMedian')}</th>
                  <th scope="col">{t('colP90')}</th>
                  <th scope="col">{t('colWip')}</th>
                  <th scope="col">{t('colGrowth')}</th>
                  <th scope="col">{t('colPace')}</th>
                </tr>
              </thead>
              <tbody>
                {ranked.map((s) => {
                  const growth = s.wipEnd - s.wipStart
                  return (
                    <tr key={s.stage} data-pace={s.pace}>
                      <td>{s.rank}</td>
                      <th scope="row">
                        <a href={formatRoute({ page: 'stage', periodId, stage: s.stage })}>{stage(s.stage)}</a>
                      </th>
                      <td>{s.sharePercent}%</td>
                      <td>{days(s.medianDays)}</td>
                      <td>{days(s.p90Days)}</td>
                      <td>{s.wipEnd}</td>
                      <td>{growth > 0 ? `+${growth}` : growth < 0 ? `−${-growth}` : '0'}</td>
                      <td>
                        <span className={`fs-pace fs-tone-${s.pace}`}>
                          {toneIcon[s.pace] && <span aria-hidden="true">{toneIcon[s.pace]} </span>}
                          <span>{t(`pace.${s.pace}`)}</span>
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
            </div>
          </>
        )
      })()}
    </div>
  )
}

import { formatRoute } from '../../../routing'
import { TrendChart } from '../components/charts/TrendChart'
import { useChanges } from '../data/hooks'
import type { ChangeImpactView, Verdict } from '../data/ownerSchemas'
import { useStrings } from '../i18n/I18n'
import { usePeriodLabel } from './FlowOverview'

/** Verdicts carry an icon as well as a colour, so the result is never colour alone. */
const verdictIcon: Record<Verdict, string> = { improved: '▼', unchanged: '●', worse: '▲', insufficientData: '○' }

export function useDateLabel() {
  const { language } = useStrings()
  return (isoDate: string) =>
    new Date(`${isoDate.slice(0, 10)}T12:00:00`).toLocaleDateString(language === 'tr' ? 'tr-TR' : 'en-GB', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    })
}

export function BeforeAfter({ index }: { index?: number }) {
  const { t } = useStrings()
  const changes = useChanges()
  const date = useDateLabel()

  const list = changes.data?.changes ?? []
  const selected = index === undefined ? list[0] : list.find((c) => c.index === index)

  return (
    <div className="fs-page fs-wide">
      <header className="fs-header">
        <div>
          <h1>{t('changesTitle')}</h1>
          <p className="fs-muted">{t('changesSubtitle')}</p>
        </div>
      </header>

      {changes.isPending && <p className="fs-muted">{t('loading')}</p>}
      {changes.isError && <p role="alert">{t('loadError')}</p>}

      {changes.data && (list.length === 0 ? (
        <p>{t('noChanges')}</p>
      ) : (
        <div className="fs-cards">
          <ul className="fs-card-list" aria-label={t('changesList')}>
            {list.map((c) => (
              <li key={c.index}>
                <a href={formatRoute({ page: 'changes', index: c.index })} aria-current={c.index === selected?.index ? 'true' : undefined}>
                  <strong>
                    {date(c.date)} · {c.stepName}
                  </strong>
                  <span className="fs-muted">{c.description}</span>
                  <span className={`fs-verdict fs-verdict-${c.verdict}`}>
                    <span aria-hidden="true">{verdictIcon[c.verdict]} </span>
                    <span>{t(`verdict.${c.verdict}`)}</span>
                  </span>
                </a>
              </li>
            ))}
          </ul>
          {selected ? <ChangeDetail change={selected} /> : <p role="alert">{t('changeNotFound')}</p>}
        </div>
      ))}
    </div>
  )
}

function ChangeDetail({ change }: { change: ChangeImpactView }) {
  const { t, stage, language } = useStrings()
  const date = useDateLabel()
  const periodLabel = usePeriodLabel()
  const num = (value: number | null) => value === null ? '–' : new Intl.NumberFormat(language === 'tr' ? 'tr-TR' : 'en-GB', { maximumFractionDigits: 1 }).format(value)

  const days = (value: number | null) => (value === null ? '–' : t('workingDaysShort', { days: value }))
  const pct = (before: number | null, after: number | null) =>
    before === null || after === null || before === 0 ? '–' : signed(Math.round((after / before - 1) * 1000) / 10)
  const signed = (value: number) => `${value > 0 ? '+' : value < 0 ? '−' : ''}${t('percentValue', { percent: Math.abs(value) })}`
  const windowDays = Math.round((Date.parse(change.before.end) - Date.parse(change.before.start)) / 86_400_000)

  const verdictText =
    change.verdict === 'insufficientData' || change.medianChangePercent === null
      ? t('verdictSentence.insufficientData')
      : change.verdict === 'unchanged'
        ? t('verdictSentence.unchanged', { change: signed(change.medianChangePercent) })
        : t(`verdictSentence.${change.verdict}`, { percent: Math.abs(change.medianChangePercent) })

  const rows: [string, string, string, string][] = [
    [t('colSteps'), num(change.beforeStats.steps), num(change.afterStats.steps), pct(change.beforeStats.steps, change.afterStats.steps)],
    // The median row uses the exact change behind the verdict, not one recomputed from rounded days.
    [t('colMedian'), days(change.beforeStats.medianDays), days(change.afterStats.medianDays),
      change.medianChangePercent === null ? '–' : signed(change.medianChangePercent)],
    [t('colP90'), days(change.beforeStats.p90Days), days(change.afterStats.p90Days), pct(change.beforeStats.p90Days, change.afterStats.p90Days)],
    [t('medianQueue'), days(change.beforeStats.medianQueueDays), days(change.afterStats.medianQueueDays), pct(change.beforeStats.medianQueueDays, change.afterStats.medianQueueDays)],
    [t('averageWaiting'), num(change.beforeStats.averageWaiting), num(change.afterStats.averageWaiting), pct(change.beforeStats.averageWaiting, change.afterStats.averageWaiting)],
  ]

  const markerIndex = change.monthly.findIndex((m) => m.periodId === change.date.slice(0, 7))
  const latestMonth = change.monthly.at(-1)?.periodId

  return (
    <article className="fs-card fs-card-detail" aria-labelledby="fs-change-title">
      <h2 id="fs-change-title">
        {change.stepName}
        {change.stage && ` · ${stage(change.stage)}`}
      </h2>
      <p className="fs-muted">
        {date(change.date)} — {change.description}
      </p>

      <p className={`fs-saving fs-verdict-${change.verdict}`}>
        <span aria-hidden="true">{verdictIcon[change.verdict]} </span>
        {verdictText}
      </p>
      <p className="fs-muted">{t('windowsCompared', { days: windowDays, date: date(change.date) })}</p>

      <div className="fs-table-wrap">
        <table className="fs-table">
          <caption>{t('beforeAfterTable')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('colMeasure')}</th>
              <th scope="col">{t('colBefore')}</th>
              <th scope="col">{t('colAfter')}</th>
              <th scope="col">{t('colChange')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(([label, before, after, change]) => (
              <tr key={label}>
                <th scope="row">{label}</th>
                <td>{before}</td>
                <td>{after}</td>
                <td>{change}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {change.monthly.length > 0 && (
        <TrendChart
          label={t('chartChange')}
          summary={t('chartChangeSummary')}
          columns={t('colMonth')}
          xLabels={change.monthly.map((m) => periodLabel(m.periodId))}
          format={days}
          series={[{ key: 'median', label: t('median'), slot: 1, values: change.monthly.map((m) => m.medianDays) }]}
          marker={markerIndex >= 0 ? { index: markerIndex, label: t('changeMarker') } : undefined}
        />
      )}

      {change.stage && latestMonth && (
        <a href={formatRoute({ page: 'stage', periodId: latestMonth, stage: change.stage })}>{t('openStage')} →</a>
      )}
    </article>
  )
}

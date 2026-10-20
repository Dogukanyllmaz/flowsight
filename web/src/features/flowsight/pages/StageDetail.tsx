import type { ReactNode } from 'react'
import { formatRoute } from '../../../routing'
import { BarChart } from '../components/charts/BarChart'
import { useStageDetail } from '../data/hooks'
import { assignmentKinds, type StageDetailView } from '../data/ownerSchemas'
import type { StageName } from '../data/schemas'
import { useStrings } from '../i18n/I18n'
import { usePeriodLabel } from './FlowOverview'

const ageBuckets = ['upTo2', 'from3To5', 'from6To10', 'over10'] as const

export function StageDetail({ periodId, stage: stageName }: { periodId: string; stage: StageName }) {
  const { t, stage } = useStrings()
  const periodLabel = usePeriodLabel()
  const detail = useStageDetail(periodId, stageName)

  return (
    <div className="fs-page fs-wide">
      <a className="fs-back" href={formatRoute({ page: 'flow', periodId })}>
        {t('backToFlow')}
      </a>
      <h1>{t('stageHeading', { stage: stage(stageName), period: periodLabel(periodId) })}</h1>
      <a className="fs-back" href={formatRoute({ page: 'cards', periodId })}>
        {t('seeCards')} →
      </a>

      {detail.isPending && <p className="fs-muted">{t('loading')}</p>}
      {detail.isError && <p role="alert">{t('loadError')}</p>}
      {detail.data === null && <p role="alert">{t('stageNotFound')}</p>}
      {detail.data && <Body d={detail.data} />}
    </div>
  )
}

function Body({ d }: { d: StageDetailView }) {
  const { t } = useStrings()
  const days = (value: number | null) => (value === null ? '–' : t('workingDaysShort', { days: value }))
  const steps = Object.values(d.assignmentMix).reduce((sum, n) => sum + (n ?? 0), 0)
  const sharedQueue = steps > 0 && (d.assignmentMix.groupQueue ?? 0) / steps > 0.5
  const waiting = ageBuckets.reduce((sum, b) => sum + d.ageing[b], 0)
  const queue = d.medianQueueDays ?? 0
  const work = d.medianWorkDays ?? 0

  return (
    <>
      <div className="fs-tiles">
        <Tile label={t('medianQueue')} value={days(d.medianQueueDays)} />
        <Tile label={t('medianWork')} value={days(d.medianWorkDays)} />
        <Tile label={t('waitingAtEnd')} value={String(waiting)} />
      </div>

      {queue + work > 0 && (
        <section className="fs-chart">
          <h2 className="fs-chart-title">{t('queueVsWork')}</h2>
          <div className="fs-split" role="img" aria-label={`${t('waitingPart')} ${days(d.medianQueueDays)}, ${t('workingPart')} ${days(d.medianWorkDays)}`}>
            <span className="fs-split-queue" style={{ flexGrow: queue }} />
            <span className="fs-split-work" style={{ flexGrow: work }} />
          </div>
          <div className="fs-legend">
            <span><i className="fs-swatch fs-split-queue" />{t('waitingPart')} <strong>{days(d.medianQueueDays)}</strong></span>
            <span><i className="fs-swatch fs-split-work" />{t('workingPart')} <strong>{days(d.medianWorkDays)}</strong></span>
          </div>
        </section>
      )}

      <section className="fs-chart">
        <h2 className="fs-chart-title">{t('assignmentTitle')}</h2>
        <ul className="fs-mix">
          {assignmentKinds.filter((k) => d.assignmentMix[k]).map((k) => (
            <li key={k}>
              <strong>{d.assignmentMix[k]}</strong> {t(`assign.${k}`)}
            </li>
          ))}
        </ul>
        {sharedQueue && <p className="fs-hint">{t('sharedQueueHint')}</p>}
      </section>

      <BarChart
        label={t('ageingTitle')}
        summary={t('ageingSummary')}
        columns={[t('colAge'), t('colCount'), t('colCount')]}
        bars={ageBuckets.map((b) => ({
          key: b,
          label: t(`age.${b}`),
          value: d.ageing[b],
          tone: 'normal',
          detail: `${t(`age.${b}`)}: ${d.ageing[b]}`,
        }))}
      />

      <Table title={t('byRoleTitle')} head={[t('colRole'), t('colSteps'), t('colMedian'), t('colTotal')]}>
        {d.byRole.map((g) => (
          <tr key={g.key}>
            <th scope="row">{g.key}</th>
            <td>{g.steps}</td>
            <td>{days(g.medianDays)}</td>
            <td>{days(g.totalDays)}</td>
          </tr>
        ))}
      </Table>

      <Table title={t('byPersonTitle')} note={t('personNote')} head={[t('colWho'), t('colRole'), t('colSteps'), t('colMedian'), t('colTotal')]}>
        {d.byPerson.map((g) => (
          <tr key={g.key}>
            <th scope="row">{g.key}</th>
            <td>{g.role}</td>
            <td>{g.steps}</td>
            <td>{days(g.medianDays)}</td>
            <td>{days(g.totalDays)}</td>
          </tr>
        ))}
      </Table>

      <SlowList title={t('slowestOpenTitle')} cases={d.slowestOpen} />
      <SlowList title={t('slowestCompletedTitle')} cases={d.slowestCompleted} />
    </>
  )
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="fs-tile">
      <span className="fs-muted">{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function Table({ title, note, head, children }: { title: string; note?: string; head: string[]; children: ReactNode[] }) {
  const { t } = useStrings()
  return (
    <div className="fs-table-wrap">
      <table className="fs-table">
        <caption>
          {title}
          {note && <span className="fs-caption-note">{note}</span>}
        </caption>
        <thead>
          <tr>
            {head.map((h) => (
              <th key={h} scope="col">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {children.length > 0 ? children : (
            <tr>
              <td colSpan={head.length} className="fs-muted">{t('none')}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}

function SlowList({ title, cases }: { title: string; cases: StageDetailView['slowestOpen'] }) {
  const { t } = useStrings()
  return (
    <Table title={title} head={[t('colCase'), t('colLine'), t('category'), t('colDays')]}>
      {cases.map((c) => (
        <tr key={`${c.caseId}/${c.lineId}`}>
          <th scope="row">
            <a href={formatRoute({ page: 'track', caseId: c.caseId })}>{c.caseId}</a>
          </th>
          <td>{c.lineId}</td>
          <td>{c.itemCategory}</td>
          <td>{t('workingDaysShort', { days: c.workingDays })}</td>
        </tr>
      ))}
    </Table>
  )
}

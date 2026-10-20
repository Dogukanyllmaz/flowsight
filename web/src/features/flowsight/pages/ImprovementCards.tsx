import { formatRoute } from '../../../routing'
import { useCards, useMeta, usePeriods } from '../data/hooks'
import type { CardView } from '../data/ownerSchemas'
import { useStrings } from '../i18n/I18n'
import { strings, type StringKey } from '../i18n/strings'
import { usePeriodLabel } from './FlowOverview'

const weekdays = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'] as const
/** Evidence codes whose value is a ratio (shown as %), a count, or working minutes (shown as days). */
const ratioCodes = new Set(['queueShare', 'returnedShare', 'weekdayShare', 'exceptionShare'])
const countCodes = new Set(['candidates', 'approvalsInChain', 'absenceDays'])

export function ImprovementCards({ periodId, cardId, onPeriodChange }: {
  periodId: string
  cardId?: string
  onPeriodChange: (periodId: string) => void
}) {
  const { t, stage } = useStrings()
  const periodLabel = usePeriodLabel()
  const periods = usePeriods()
  const cards = useCards(periodId)

  const options = periods.data
    ? [...periods.data.periods].sort((a, b) => (a.id === 'last12' ? -1 : b.id === 'last12' ? 1 : b.id.localeCompare(a.id)))
    : []

  const data = cards.data
  const selected = data ? (cardId ? data.cards.find((c) => c.id === cardId) : data.cards[0]) : undefined

  return (
    <div className="fs-page fs-wide">
      <header className="fs-header">
        <div>
          <h1>{t('cardsTitle')}</h1>
          <p className="fs-muted">{t('cardsSubtitle')}</p>
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

      {cards.isPending && <p className="fs-muted">{t('loading')}</p>}
      {cards.isError && <p role="alert">{t('loadError')}</p>}
      {data === null && <p role="alert">{t('periodNotFound')}</p>}

      {data && (
        <>
          {data.cards.length === 0 ? (
            <p>{t('noCards')}</p>
          ) : (
            <div className="fs-cards">
              <ul className="fs-card-list" aria-label={t('cardsList')}>
                {data.cards.map((c) => (
                  <li key={c.id}>
                    <a href={formatRoute({ page: 'cards', periodId, cardId: c.id })} aria-current={c.id === selected?.id ? 'true' : undefined}>
                      <strong>{t(`pattern.${c.pattern}`)}</strong>
                      <span className="fs-muted">
                        {stage(c.stage)} · {c.stepName}
                        {c.segment && ` · ${c.segment}`}
                      </span>
                      <span className="fs-muted"> — {saving(c, t)}</span>
                    </a>
                  </li>
                ))}
              </ul>
              {cardId && !selected ? (
                <p role="alert">{t('cardNotFound')}</p>
              ) : selected ? (
                <CardDetail card={selected} periodId={periodId} />
              ) : (
                <p className="fs-muted">{t('selectCard')}</p>
              )}
            </div>
          )}

          {data.insufficientData.length > 0 && (
            <div className="fs-note">
              {t('checksSkipped')}
              <ul>
                {data.insufficientData.map((key) => {
                  const [pattern, step] = key.split(':')
                  const known = ['singlePointOfFailure', 'exceptionQueue', 'sharedQueueSearch'].includes(pattern)
                  return (
                    <li key={key}>
                      {known ? t(`pattern.${pattern}` as StringKey) : pattern} ({step}) — {known ? t(`needs.${pattern}` as StringKey) : ''}
                    </li>
                  )
                })}
              </ul>
            </div>
          )}
          {data.unmappedActivities.length > 0 && (
            <p className="fs-note">
              {t('unmapped')} {data.unmappedActivities.join(', ')}
            </p>
          )}
        </>
      )}
    </div>
  )
}

function saving(c: CardView, t: ReturnType<typeof useStrings>['t']) {
  return c.expectedSavingDaysPerLine === null
    ? t('savingUnknown')
    : t('expectedSaving', { days: c.expectedSavingDaysPerLine, count: c.affectedLines })
}

function CardDetail({ card, periodId }: { card: CardView; periodId: string }) {
  const { t, stage } = useStrings()
  const meta = useMeta()
  const minutesPerDay = meta.data?.minutesPerDay ?? 540
  const days = (minutes: number) => t('workingDaysShort', { days: Math.round((minutes / minutesPerDay) * 10) / 10 })

  const evidence = (code: string, values: Record<string, number>) => {
    const value = values.value ?? 0
    const params: Record<string, string | number> = ratioCodes.has(code)
      ? { percent: Math.round(value * 1000) / 10 }
      : countCodes.has(code)
        ? { count: value }
        : { days: days(value) }
    if (code === 'weekdayShare') params.day = t(`day.${weekdays[values.weekday ?? 5]}`)
    const key = `ev.${code}` as StringKey
    // An evidence code the UI does not know yet still shows, as code and value.
    return key in strings.en ? t(key, params) : `${code}: ${value}`
  }

  const approvals = card.evidence.find((e) => e.code === 'approvalsInChain')?.values.value ?? 3

  return (
    <article className="fs-card fs-card-detail" aria-labelledby="fs-card-title">
      <h2 id="fs-card-title">{t(`pattern.${card.pattern}`)}</h2>
      <p className="fs-muted">
        {stage(card.stage)} · {card.stepName}
        {card.segment && ` · ${t('onlyIn', { segment: card.segment })}`}
      </p>

      <h3>{t('whatWeSee')}</h3>
      <ul>
        {card.evidence.map((e) => (
          <li key={e.code}>{evidence(e.code, e.values)}</li>
        ))}
      </ul>

      <h3>{t('why')}</h3>
      <p>{t(`cause.${card.pattern}`, { step: card.stepName, count: approvals })}</p>
      <p className="fs-code">{t('mapElement', { property: card.mapProperty })}</p>

      <h3>{t('whatToChange')}</h3>
      <p>{t(`change.${card.proposedChange}`)}</p>

      <h3>{t('expectedEffect')}</h3>
      <p className="fs-saving">{saving(card, t)}</p>
      {card.expectedSavingDaysTotal !== null && <p className="fs-muted">{t('expectedSavingTotal', { days: card.expectedSavingDaysTotal })}</p>}

      <a href={formatRoute({ page: 'stage', periodId, stage: card.stage })}>{t('openStage')} →</a>
    </article>
  )
}

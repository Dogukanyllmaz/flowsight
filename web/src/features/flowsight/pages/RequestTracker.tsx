import { useState, type FormEvent } from 'react'
import { StageStrip } from '../components/StageStrip'
import { useRequest, useRequestIndex } from '../data/hooks'
import type { RequestView } from '../data/schemas'
import { I18nProvider, useStrings } from '../i18n/I18n'
import '../flowsight.css'

/** Accepts "PR-001783", "pr-1783" or "1783"; anything else is passed through and simply won't be found. */
export function normaliseCaseId(input: string): string {
  const value = input.trim().toUpperCase()
  const match = /^(?:PR-?)?(\d{1,6})$/.exec(value)
  return match ? `PR-${match[1].padStart(6, '0')}` : value
}

export function RequestTracker() {
  return (
    <I18nProvider>
      <Tracker />
    </I18nProvider>
  )
}

function Tracker() {
  const { t, language, setLanguage } = useStrings()
  const [input, setInput] = useState('')
  const [caseId, setCaseId] = useState<string | null>(null)

  const show = (value: string) => {
    setInput(value)
    setCaseId(normaliseCaseId(value))
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    if (input.trim()) show(input)
  }

  return (
    <div className="fs-page">
      <header className="fs-header">
        <div>
          <h1>{t('title')}</h1>
          <p className="fs-muted">{t('subtitle')}</p>
        </div>
        <div className="fs-lang" role="group" aria-label="Language">
          {(['en', 'tr'] as const).map((l) => (
            <button key={l} type="button" aria-pressed={language === l} onClick={() => setLanguage(l)}>
              {l.toUpperCase()}
            </button>
          ))}
        </div>
      </header>

      <form className="fs-search" onSubmit={onSubmit}>
        <label htmlFor="fs-case">{t('searchLabel')}</label>
        <input
          id="fs-case"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder={t('searchPlaceholder')}
          autoComplete="off"
        />
        <button type="submit">{t('search')}</button>
      </form>

      <Examples onPick={show} />
      {caseId !== null && <Result caseId={caseId} />}
    </div>
  )
}

function Examples({ onPick }: { onPick: (caseId: string) => void }) {
  const { t } = useStrings()
  const index = useRequestIndex()
  const open = (index.data ?? []).filter((r) => r.status === 'open').slice(0, 6)
  if (open.length === 0) return null

  return (
    <div className="fs-examples">
      <span className="fs-muted">{t('examples')}:</span>
      {open.map((r) => (
        <button key={r.caseId} type="button" className="fs-chip" onClick={() => onPick(r.caseId)}>
          {r.caseId}
        </button>
      ))}
    </div>
  )
}

function Result({ caseId }: { caseId: string }) {
  const { t } = useStrings()
  const request = useRequest(caseId)

  if (request.isPending) return <p className="fs-muted">{t('loading')}</p>
  if (request.isError) return <p role="alert">{t('loadError')}</p>
  if (request.data === null) return <p role="alert">{t('notFound')}</p>
  return <RequestCard request={request.data} />
}

function RequestCard({ request }: { request: RequestView }) {
  const { t, stage, language } = useStrings()
  const current = request.stages.find((s) => s.stage === request.currentStage)
  const diverged = request.lines.length > 1 && new Set(request.lines.map((l) => l.currentStage ?? l.status)).size > 1
  const asOf = new Date(request.asOf).toLocaleDateString(language === 'tr' ? 'tr-TR' : 'en-GB')

  return (
    <section className="fs-card">
      <h2>{request.caseId}</h2>
      <p className="fs-status">
        {request.status === 'open' && current
          ? t('inStage', { stage: stage(current.stage), days: current.workingDays })
          : t('closedStatus', { status: t(`status.${request.status}`) })}
      </p>

      <StageStrip stages={request.stages} currentStage={request.currentStage} label={request.caseId} />

      <p className="fs-muted">
        {t('total', { days: request.totalWorkingDays })}
        {request.slowerThanPercent !== null && (
          <>
            {' · '}
            <strong>{t('slowerThan', { percent: request.slowerThanPercent })}</strong>
          </>
        )}
      </p>

      {diverged && (
        <table className="fs-lines">
          <caption>{t('lines')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('line')}</th>
              <th scope="col">{t('category')}</th>
              <th scope="col">{t('currentStage')}</th>
            </tr>
          </thead>
          <tbody>
            {request.lines.map((line) => (
              <tr key={line.lineId}>
                <td>{line.lineId}</td>
                <td>{line.itemCategory}</td>
                <td>{line.currentStage ? stage(line.currentStage) : t(`status.${line.status}`)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="fs-muted fs-small">{t('asOf', { date: asOf })}</p>
    </section>
  )
}

import type { StageName, StageView } from '../data/schemas'
import { useStrings } from '../i18n/I18n'

type DisplayState = StageView['state'] | 'delayed'

/** A stage still in progress is "delayed" once it has taken more than twice what similar requests typically take. */
function displayState(view: StageView): DisplayState {
  if (view.state === 'inProgress' && view.typicalWorkingDays !== null && view.workingDays > 2 * view.typicalWorkingDays) {
    return 'delayed'
  }
  return view.state
}

export function StageStrip({
  stages,
  currentStage,
  label,
}: {
  stages: StageView[]
  currentStage: StageName | null
  label?: string
}) {
  const { t, stage } = useStrings()

  return (
    <ol className="fs-strip" aria-label={label}>
      {stages.map((view) => {
        const state = displayState(view)
        const showTypical = view.state === 'inProgress' && view.typicalWorkingDays !== null
        return (
          <li
            key={view.stage}
            className={`fs-strip-stage fs-${state}`}
            data-stage={view.stage}
            data-state={state}
            aria-current={view.stage === currentStage ? 'step' : undefined}
            title={t(`state.${state}`)}
          >
            <span className="fs-strip-name">{stage(view.stage)}</span>
            <span className="fs-strip-days">
              {view.state === 'notStarted' ? '–' : t('workingDaysShort', { days: view.workingDays })}
            </span>
            {showTypical && (
              <span className="fs-strip-typical">
                {t('typical', { days: t('workingDaysShort', { days: view.typicalWorkingDays! }) })}
              </span>
            )}
          </li>
        )
      })}
    </ol>
  )
}

import type { WaitingReasonView } from '../data/schemas'
import { useStrings } from '../i18n/I18n'
import type { StringKey } from '../i18n/strings'

const days = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const

/** One plain sentence per waiting reason, in the active language. Never names a person. */
export function WaitingReasons({ reasons }: { reasons: WaitingReasonView[] }) {
  const { t, language } = useStrings()
  if (reasons.length === 0) return null

  const sentence = (reason: WaitingReasonView) => {
    const p = reason.params
    const num = (key: string) => Number(p[key])
    switch (reason.code) {
      case 'assigneeAbsent':
        return t('reason.assigneeAbsent', {
          date: new Date(`${p.untilDate}T12:00:00`).toLocaleDateString(language === 'tr' ? 'tr-TR' : 'en-GB'),
        })
      case 'priceVariance':
        return t('reason.priceVariance', { percent: num('percent') })
      case 'partialDelivery':
        return t('reason.partialDelivery', { received: num('received'), ordered: num('ordered') })
      case 'sharedQueue':
        return t('reason.sharedQueue', { candidates: num('candidates') })
      case 'waitingForBatch': {
        const day = (days as readonly string[]).includes(p.day) ? t(`day.${p.day}` as StringKey) : p.day
        return t('reason.waitingForBatch', { day })
      }
      default:
        return t(`reason.${reason.code}`)
    }
  }

  return (
    <section className="fs-reasons" aria-labelledby="fs-why">
      <h3 id="fs-why">{t('whyWaiting')}</h3>
      <ul>
        {reasons.map((r) => (
          <li key={r.code}>{sentence(r)}</li>
        ))}
      </ul>
    </section>
  )
}

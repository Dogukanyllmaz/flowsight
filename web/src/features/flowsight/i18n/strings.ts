import { useCallback, useState } from 'react'
import type { StageName } from '../data/schemas'

export type Language = 'en' | 'tr'

const en = {
  title: 'Request tracker',
  subtitle: 'Where is my purchase request?',
  searchLabel: 'Request number',
  searchPlaceholder: 'e.g. PR-001783',
  search: 'Show',
  examples: 'Open requests to try',
  loading: 'Loading…',
  notFound: 'No request found with this number.',
  loadError: 'The data could not be loaded.',
  inStage: 'In {stage} for {days} working days',
  closedStatus: 'This request is {status}.',
  total: 'Total so far: {days} working days',
  slowerThan: 'Slower than {percent}% of similar requests',
  typical: 'typically {days}',
  workingDaysShort: '{days} d',
  lines: 'Lines',
  line: 'Line',
  category: 'Category',
  currentStage: 'Current stage',
  asOf: 'Data as of {date}',
  'status.open': 'open',
  'status.completed': 'completed',
  'status.rejected': 'rejected',
  'status.cancelled': 'cancelled',
  'state.notStarted': 'not started',
  'state.inProgress': 'in progress',
  'state.completed': 'completed',
  'state.delayed': 'delayed',
  'stage.request': 'Request',
  'stage.approval': 'Approval',
  'stage.purchasing': 'Purchasing',
  'stage.order': 'Order',
  'stage.goodsReceipt': 'Goods receipt',
  'stage.invoice': 'Invoice',
  'stage.payment': 'Payment',
}

export type StringKey = keyof typeof en

const tr: Record<StringKey, string> = {
  title: 'Talep takibi',
  subtitle: 'Satın alma talebim nerede?',
  searchLabel: 'Talep numarası',
  searchPlaceholder: 'örn. PR-001783',
  search: 'Göster',
  examples: 'Deneyebileceğiniz açık talepler',
  loading: 'Yükleniyor…',
  notFound: 'Bu numarayla bir talep bulunamadı.',
  loadError: 'Veri yüklenemedi.',
  inStage: '{days} iş günüdür {stage} aşamasında',
  closedStatus: 'Bu talep {status}.',
  total: 'Şu ana kadar toplam: {days} iş günü',
  slowerThan: 'Benzer taleplerin %{percent} kadarından yavaş',
  typical: 'normalde {days}',
  workingDaysShort: '{days} g',
  lines: 'Kalemler',
  line: 'Kalem',
  category: 'Kategori',
  currentStage: 'Bulunduğu aşama',
  asOf: 'Veri tarihi: {date}',
  'status.open': 'açık',
  'status.completed': 'tamamlandı',
  'status.rejected': 'reddedildi',
  'status.cancelled': 'iptal edildi',
  'state.notStarted': 'başlamadı',
  'state.inProgress': 'devam ediyor',
  'state.completed': 'tamamlandı',
  'state.delayed': 'gecikmede',
  'stage.request': 'Talep',
  'stage.approval': 'Onay',
  'stage.purchasing': 'Satın alma',
  'stage.order': 'Sipariş',
  'stage.goodsReceipt': 'Mal kabul',
  'stage.invoice': 'Fatura',
  'stage.payment': 'Ödeme',
}

export const strings: Record<Language, Record<StringKey, string>> = { en, tr }

const storageKey = 'flowsight.lang'

function readLanguage(): Language {
  try {
    return localStorage.getItem(storageKey) === 'tr' ? 'tr' : 'en'
  } catch {
    return 'en'
  }
}

export function format(template: string, values: Record<string, string | number> = {}): string {
  return template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key] ?? `{${key}}`))
}

export function useStrings() {
  const [language, setLanguageState] = useState<Language>(readLanguage)

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next)
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Storage unavailable (private mode, blocked): keep the choice for this session only.
    }
  }, [])

  const t = useCallback(
    (key: StringKey, values?: Record<string, string | number>) => format(strings[language][key], values),
    [language],
  )
  const stage = useCallback((name: StageName) => strings[language][`stage.${name}`], [language])

  return { language, setLanguage, t, stage }
}

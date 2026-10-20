import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import type { StageName } from '../data/schemas'
import { format, strings, type Language, type StringKey } from './strings'

const storageKey = 'flowsight.lang'

function readLanguage(): Language {
  try {
    return localStorage.getItem(storageKey) === 'tr' ? 'tr' : 'en'
  } catch {
    return 'en'
  }
}

interface I18n {
  language: Language
  setLanguage: (language: Language) => void
}

const I18nContext = createContext<I18n | null>(null)

/** Shares the chosen language with every FlowSight component below it. Inside another provider it does nothing,
 * so a page that brings its own provider still follows the app-wide language. */
export function I18nProvider({ children }: { children: ReactNode }) {
  const parent = useContext(I18nContext)
  return parent ? <>{children}</> : <RootI18nProvider>{children}</RootI18nProvider>
}

function RootI18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(readLanguage)

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next)
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Storage unavailable (private mode, blocked): keep the choice for this session only.
    }
  }, [])

  const value = useMemo(() => ({ language, setLanguage }), [language, setLanguage])
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

/** Translation helpers. Without a provider, English is used. */
export function useStrings() {
  const context = useContext(I18nContext)
  const language = context?.language ?? 'en'

  const t = useCallback(
    (key: StringKey, values: Record<string, string | number> = {}) => {
      const number = new Intl.NumberFormat(language === 'tr' ? 'tr-TR' : 'en-GB', { maximumFractionDigits: 1 })
      const localised = Object.fromEntries(
        Object.entries(values).map(([k, v]) => [k, typeof v === 'number' ? number.format(v) : v]),
      )
      return format(strings[language][key], localised)
    },
    [language],
  )
  const stage = useCallback((name: StageName) => strings[language][`stage.${name}`], [language])

  return { language, setLanguage: context?.setLanguage ?? (() => {}), t, stage }
}

/** EN / TR switch. */
export function LanguageToggle() {
  const { language, setLanguage } = useStrings()
  return (
    <div className="fs-lang" role="group" aria-label="Language">
      {(['en', 'tr'] as const).map((l) => (
        <button key={l} type="button" aria-pressed={language === l} onClick={() => setLanguage(l)}>
          {l.toUpperCase()}
        </button>
      ))}
    </div>
  )
}

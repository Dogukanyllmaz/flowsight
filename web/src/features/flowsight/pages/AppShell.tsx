import type { ReactNode } from 'react'
import { formatRoute, type Route } from '../../../routing'
import { I18nProvider, LanguageToggle, useStrings } from '../i18n/I18n'
import { RequestTracker } from './RequestTracker'
import '../flowsight.css'

/** Shell around every page: app name, view switch, language switch, and the page for the current route. */
export function AppShell({ route, navigate, owner }: {
  route: Route
  navigate: (route: Route) => void
  /** Renders the process-owner pages; supplied by the app so the shell stays free of page wiring. */
  owner?: (route: Extract<Route, { page: 'flow' | 'stage' | 'cards' | 'changes' }>) => ReactNode
}) {
  return (
    <I18nProvider>
      <Shell route={route} navigate={navigate} owner={owner} />
    </I18nProvider>
  )
}

function Shell({ route, navigate, owner }: Parameters<typeof AppShell>[0]) {
  const { t } = useStrings()
  const flowView = route.page === 'flow' || route.page === 'stage'

  return (
    <>
      <nav className="fs-topbar" aria-label={t('nav.label')}>
        <strong className="fs-brand">{t('appName')}</strong>
        <div className="fs-views">
          <a href={formatRoute({ page: 'track' })} aria-current={route.page === 'track' ? 'page' : undefined}>
            {t('nav.tracker')}
          </a>
          <a href={formatRoute({ page: 'flow', periodId: 'last12' })} aria-current={flowView ? 'page' : undefined}>
            {t('nav.flow')}
          </a>
          <a href={formatRoute({ page: 'cards', periodId: 'last12' })} aria-current={route.page === 'cards' ? 'page' : undefined}>
            {t('nav.cards')}
          </a>
          <a href={formatRoute({ page: 'changes' })} aria-current={route.page === 'changes' ? 'page' : undefined}>
            {t('nav.changes')}
          </a>
        </div>
        <LanguageToggle />
      </nav>

      {route.page === 'track' && (
        <RequestTracker
          initialCaseId={route.caseId}
          onShow={(caseId) => navigate({ page: 'track', caseId })}
          showLanguageToggle={false}
        />
      )}
      {(route.page === 'flow' || route.page === 'stage' || route.page === 'cards' || route.page === 'changes') && owner?.(route)}
      {route.page === 'notFound' && (
        <div className="fs-page">
          <p role="alert">{t('pageNotFound')}</p>
          <a href={formatRoute({ page: 'track' })}>{t('goHome')}</a>
        </div>
      )}
    </>
  )
}

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
  owner?: (route: Extract<Route, { page: 'flow' | 'stage' }>) => ReactNode
}) {
  return (
    <I18nProvider>
      <Shell route={route} navigate={navigate} owner={owner} />
    </I18nProvider>
  )
}

function Shell({ route, navigate, owner }: Parameters<typeof AppShell>[0]) {
  const { t } = useStrings()
  const ownerView = route.page === 'flow' || route.page === 'stage'

  return (
    <>
      <nav className="fs-topbar" aria-label={t('nav.label')}>
        <strong className="fs-brand">{t('appName')}</strong>
        <div className="fs-views">
          <a href={formatRoute({ page: 'track' })} aria-current={!ownerView && route.page === 'track' ? 'page' : undefined}>
            {t('nav.tracker')}
          </a>
          <a href={formatRoute({ page: 'flow', periodId: 'last12' })} aria-current={ownerView ? 'page' : undefined}>
            {t('nav.flow')}
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
      {(route.page === 'flow' || route.page === 'stage') && owner?.(route)}
      {route.page === 'notFound' && (
        <div className="fs-page">
          <p role="alert">{t('pageNotFound')}</p>
          <a href={formatRoute({ page: 'track' })}>{t('goHome')}</a>
        </div>
      )}
    </>
  )
}

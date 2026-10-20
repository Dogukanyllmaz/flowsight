import { useCallback, useEffect, useState } from 'react'
import { stageNames, type StageName } from './features/flowsight/data/schemas'

export type Route =
  | { page: 'track'; caseId?: string }
  | { page: 'flow'; periodId: string }
  | { page: 'stage'; periodId: string; stage: StageName }
  | { page: 'notFound' }

const periodPattern = /^(\d{4}-\d{2}|last12)$/

/** Hash routes: #/track, #/track/{caseId}, #/flow/{periodId}, #/flow/{periodId}/{stage}. */
export function parseRoute(hash: string): Route {
  const parts = hash.replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent)
  const [page, a, b, ...rest] = parts
  if (rest.length > 0) return { page: 'notFound' }

  if (page === undefined || (page === 'track' && b === undefined)) {
    return a ? { page: 'track', caseId: a } : { page: 'track' }
  }
  if (page === 'flow') {
    const periodId = a ?? 'last12'
    if (!periodPattern.test(periodId)) return { page: 'notFound' }
    if (b === undefined) return { page: 'flow', periodId }
    return (stageNames as readonly string[]).includes(b)
      ? { page: 'stage', periodId, stage: b as StageName }
      : { page: 'notFound' }
  }
  return { page: 'notFound' }
}

export function formatRoute(route: Route): string {
  switch (route.page) {
    case 'track':
      return route.caseId ? `#/track/${encodeURIComponent(route.caseId)}` : '#/track'
    case 'flow':
      return `#/flow/${route.periodId}`
    case 'stage':
      return `#/flow/${route.periodId}/${route.stage}`
    default:
      return '#/track'
  }
}

/** Current route from the location hash, plus a navigate function. */
export function useRoute(): [Route, (route: Route) => void] {
  const [route, setRoute] = useState<Route>(() => parseRoute(window.location.hash))

  useEffect(() => {
    const onChange = () => setRoute(parseRoute(window.location.hash))
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])

  const navigate = useCallback((next: Route) => {
    const hash = formatRoute(next)
    if (window.location.hash !== hash) window.location.hash = hash
    setRoute(next)
  }, [])

  return [route, navigate]
}

import { createContext, useContext, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { StageName } from './schemas'
import type { FlowSightSource } from './source'

const SourceContext = createContext<FlowSightSource | null>(null)

export function FlowSightProvider({ source, children }: { source: FlowSightSource; children: ReactNode }) {
  return <SourceContext.Provider value={source}>{children}</SourceContext.Provider>
}

function useSource(): FlowSightSource {
  const source = useContext(SourceContext)
  if (!source) throw new Error('FlowSightProvider is missing')
  return source
}

export function useMeta() {
  const source = useSource()
  return useQuery({ queryKey: ['flowsight', 'meta'], queryFn: () => source.getMeta(), staleTime: Infinity })
}

export function useRequestIndex() {
  const source = useSource()
  return useQuery({ queryKey: ['flowsight', 'index'], queryFn: () => source.getIndex(), staleTime: Infinity })
}

export function useRequest(caseId: string | null) {
  const source = useSource()
  return useQuery({
    queryKey: ['flowsight', 'request', caseId],
    queryFn: () => source.getRequest(caseId!),
    enabled: caseId !== null,
    staleTime: Infinity,
  })
}

export function usePeriods() {
  const source = useSource()
  return useQuery({ queryKey: ['flowsight', 'periods'], queryFn: () => source.getPeriods(), staleTime: Infinity })
}

export function useOverview(periodId: string) {
  const source = useSource()
  return useQuery({ queryKey: ['flowsight', 'overview', periodId], queryFn: () => source.getOverview(periodId), staleTime: Infinity })
}

export function useStageDetail(periodId: string, stage: StageName) {
  const source = useSource()
  return useQuery({
    queryKey: ['flowsight', 'stage', periodId, stage],
    queryFn: () => source.getStageDetail(periodId, stage),
    staleTime: Infinity,
  })
}

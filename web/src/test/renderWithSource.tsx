import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FlowSightProvider } from '../features/flowsight/data/hooks'
import { requestViewSchema, type RequestView } from '../features/flowsight/data/schemas'
import type { FlowSightSource } from '../features/flowsight/data/source'
import fixture from './fixtures/request.json'

export const fixtureRequest: RequestView = requestViewSchema.parse(fixture)

/** In-memory source holding the given requests. */
export function memorySource(requests: RequestView[] = [fixtureRequest]): FlowSightSource {
  return {
    getMeta: async () => ({ asOf: fixtureRequest.asOf, seed: 42, requests: requests.length, minutesPerDay: 540, stages: [] }),
    getIndex: async () => requests.map((r) => ({ caseId: r.caseId, status: r.status, currentStage: r.currentStage })),
    getRequest: async (caseId) => requests.find((r) => r.caseId === caseId) ?? null,
  }
}

export function renderWithSource(ui: ReactElement, source: FlowSightSource = memorySource()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <FlowSightProvider source={source}>{ui}</FlowSightProvider>
    </QueryClientProvider>,
  )
}

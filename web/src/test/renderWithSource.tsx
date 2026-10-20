import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FlowSightProvider } from '../features/flowsight/data/hooks'
import { requestViewSchema, type RequestView } from '../features/flowsight/data/schemas'
import type { FlowSightSource } from '../features/flowsight/data/source'
import fixture from './fixtures/request.json'
import overviewFixture from './fixtures/overview.json'
import periodsFixture from './fixtures/periods.json'
import stageFixture from './fixtures/stage.json'
import cardsFixture from './fixtures/cards.json'
import {
  cardsViewSchema,
  type CardsView,
  overviewViewSchema,
  periodsViewSchema,
  stageDetailViewSchema,
  type OverviewView,
  type StageDetailView,
} from '../features/flowsight/data/ownerSchemas'

export const fixtureRequest: RequestView = requestViewSchema.parse(fixture)
export const fixtureOverview: OverviewView = overviewViewSchema.parse(overviewFixture)
export const fixtureStage: StageDetailView = stageDetailViewSchema.parse(stageFixture)
export const fixturePeriods = periodsViewSchema.parse(periodsFixture)
export const fixtureCards: CardsView = cardsViewSchema.parse(cardsFixture)

/** In-memory source: the given requests, the last-12 overview and the March invoice drill-down. */
export function memorySource(
  requests: RequestView[] = [fixtureRequest],
  owner: { overview?: OverviewView; stage?: StageDetailView; cards?: CardsView } = {},
): FlowSightSource {
  const overview = owner.overview ?? fixtureOverview
  const stage = owner.stage ?? fixtureStage
  const cards = owner.cards ?? fixtureCards
  return {
    getCards: async (periodId) => (periodId === cards.periodId ? cards : null),
    getPeriods: async () => fixturePeriods,
    getOverview: async (periodId) => (periodId === overview.periodId ? overview : null),
    getStageDetail: async (periodId, name) => (periodId === stage.periodId && name === stage.stage ? stage : null),
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

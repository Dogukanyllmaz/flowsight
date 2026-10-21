import type { CardsView, ChangesView, OverviewView, PeriodsView, StageDetailView } from './ownerSchemas'
import type { DemoMeta, RequestIndexEntry, RequestView, StageName } from './schemas'

/**
 * Where the screens get their data. The demo reads static JSON; a host application
 * provides an implementation backed by its own API. Screens never fetch directly.
 */
export interface FlowSightSource {
  getMeta(): Promise<DemoMeta>
  getIndex(): Promise<RequestIndexEntry[]>
  /** Resolves to null when the request does not exist. */
  getRequest(caseId: string): Promise<RequestView | null>

  // Process-owner data
  getPeriods(): Promise<PeriodsView>
  /** Resolves to null for an unknown period. */
  getOverview(periodId: string): Promise<OverviewView | null>
  /** Resolves to null for an unknown period or stage. */
  getStageDetail(periodId: string, stage: StageName): Promise<StageDetailView | null>
  /** Resolves to null for an unknown period. */
  getCards(periodId: string): Promise<CardsView | null>
  getChanges(): Promise<ChangesView>
}

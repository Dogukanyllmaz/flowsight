import type { DemoMeta, RequestIndexEntry, RequestView } from './schemas'

/**
 * Where the screens get their data. The demo reads static JSON; a host application
 * provides an implementation backed by its own API. Screens never fetch directly.
 */
export interface FlowSightSource {
  getMeta(): Promise<DemoMeta>
  getIndex(): Promise<RequestIndexEntry[]>
  /** Resolves to null when the request does not exist. */
  getRequest(caseId: string): Promise<RequestView | null>
}

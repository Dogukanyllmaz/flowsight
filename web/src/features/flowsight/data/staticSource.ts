import { cardsViewSchema, changesViewSchema, overviewViewSchema, periodsViewSchema, stageDetailViewSchema } from './ownerSchemas'
import { demoMetaSchema, requestIndexSchema, requestViewSchema, stageNames } from './schemas'
import type { FlowSightSource } from './source'

const periodPattern = /^(\d{4}-\d{2}|last12)$/

/** Reads the JSON files written by `flowsight demo` from `${baseUrl}data/`. */
export function createStaticSource(baseUrl: string): FlowSightSource {
  const root = `${baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`}data/`

  async function get(path: string): Promise<unknown | null> {
    const response = await fetch(root + path)
    if (response.status === 404) return null
    if (!response.ok) throw new Error(`Could not load ${path} (${response.status})`)
    try {
      return await response.json()
    } catch {
      // Dev servers answer unknown paths with index.html; treat that as "not found".
      return null
    }
  }

  return {
    async getMeta() {
      return demoMetaSchema.parse(await get('meta.json'))
    },
    async getIndex() {
      return requestIndexSchema.parse(await get('requests/index.json'))
    },
    async getRequest(caseId) {
      if (!/^PR-\d{6}$/.test(caseId)) return null
      const data = await get(`requests/${caseId}.json`)
      return data === null ? null : requestViewSchema.parse(data)
    },
    async getPeriods() {
      return periodsViewSchema.parse(await get('owner/periods.json'))
    },
    async getOverview(periodId) {
      if (!periodPattern.test(periodId)) return null
      const data = await get(`owner/overview/${periodId}.json`)
      return data === null ? null : overviewViewSchema.parse(data)
    },
    async getStageDetail(periodId, stage) {
      if (!periodPattern.test(periodId) || !(stageNames as readonly string[]).includes(stage)) return null
      const data = await get(`owner/stages/${periodId}/${stage}.json`)
      return data === null ? null : stageDetailViewSchema.parse(data)
    },
    async getCards(periodId) {
      if (!periodPattern.test(periodId)) return null
      const data = await get(`owner/cards/${periodId}.json`)
      return data === null ? null : cardsViewSchema.parse(data)
    },
    async getChanges() {
      return changesViewSchema.parse(await get('owner/changes.json'))
    },
  }
}

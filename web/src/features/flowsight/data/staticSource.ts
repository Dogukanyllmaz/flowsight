import { demoMetaSchema, requestIndexSchema, requestViewSchema } from './schemas'
import type { FlowSightSource } from './source'

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
  }
}

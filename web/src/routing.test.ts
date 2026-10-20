import { formatRoute, parseRoute } from './routing'

describe('routing', () => {
  it('defaults to the request tracker', () => {
    expect(parseRoute('')).toEqual({ page: 'track' })
    expect(parseRoute('#/')).toEqual({ page: 'track' })
    expect(parseRoute('#/track')).toEqual({ page: 'track' })
  })

  it('parses a tracked request', () => {
    expect(parseRoute('#/track/PR-001783')).toEqual({ page: 'track', caseId: 'PR-001783' })
  })

  it('parses the flow overview and a stage', () => {
    expect(parseRoute('#/flow/last12')).toEqual({ page: 'flow', periodId: 'last12' })
    expect(parseRoute('#/flow/2026-03/invoice')).toEqual({ page: 'stage', periodId: '2026-03', stage: 'invoice' })
    expect(parseRoute('#/flow')).toEqual({ page: 'flow', periodId: 'last12' })
  })

  it('parsesUnknownRoutesAsNotFound', () => {
    expect(parseRoute('#/flow/2026-03/shipping')).toEqual({ page: 'notFound' })
    expect(parseRoute('#/nope')).toEqual({ page: 'notFound' })
    expect(parseRoute('#/flow/03-2026')).toEqual({ page: 'notFound' })
  })

  it('round-trips valid routes', () => {
    for (const hash of ['#/track', '#/track/PR-001783', '#/flow/last12', '#/flow/2026-03/goodsReceipt']) {
      expect(formatRoute(parseRoute(hash))).toBe(hash)
    }
  })
})

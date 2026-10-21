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

  it('treats malformed escapes as not found instead of crashing', () => {
    expect(parseRoute('#/track/%E0')).toEqual({ page: 'notFound' })
  })

  it('parses improvement card routes', () => {
    expect(parseRoute('#/cards')).toEqual({ page: 'cards', periodId: 'last12' })
    expect(parseRoute('#/cards/2026-03')).toEqual({ page: 'cards', periodId: '2026-03' })
    expect(parseRoute('#/cards/2026-03/sharedQueueSearch%3Ainvoice-matching')).toEqual({
      page: 'cards',
      periodId: '2026-03',
      cardId: 'sharedQueueSearch:invoice-matching',
    })
    expect(parseRoute('#/cards/march')).toEqual({ page: 'notFound' })
  })

  it('parses change routes', () => {
    expect(parseRoute('#/changes')).toEqual({ page: 'changes' })
    expect(parseRoute('#/changes/0')).toEqual({ page: 'changes', index: 0 })
    expect(parseRoute('#/changes/x')).toEqual({ page: 'notFound' })
    expect(parseRoute('#/changes/-1')).toEqual({ page: 'notFound' })
  })

  it('round-trips valid routes', () => {
    for (const hash of ['#/changes', '#/changes/2', '#/track', '#/track/PR-001783', '#/flow/last12', '#/flow/2026-03/goodsReceipt', '#/cards/2026-03', '#/cards/2026-03/reworkLoop%3Asourcing']) {
      expect(formatRoute(parseRoute(hash))).toBe(hash)
    }
  })
})

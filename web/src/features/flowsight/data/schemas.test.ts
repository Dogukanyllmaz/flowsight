import fixture from '../../../test/fixtures/request.json'
import { requestViewSchema } from './schemas'

describe('requestViewSchema', () => {
  it('parses a request exported by the CLI', () => {
    const parsed = requestViewSchema.parse(fixture)
    expect(parsed.caseId).toBe('PR-001783')
    expect(parsed.stages).toHaveLength(7)
  })

  it('rejects a request without exactly seven stages', () => {
    const broken = { ...fixture, stages: fixture.stages.slice(0, 6) }
    expect(requestViewSchema.safeParse(broken).success).toBe(false)
  })

  it('strips fields that are not part of the contract', () => {
    const withActor = { ...fixture, actorId: 'A-01' }
    expect(requestViewSchema.parse(withActor)).not.toHaveProperty('actorId')
  })

  it('rejects an unknown stage name', () => {
    const broken = { ...fixture, currentStage: 'shipping' }
    expect(requestViewSchema.safeParse(broken).success).toBe(false)
  })
})

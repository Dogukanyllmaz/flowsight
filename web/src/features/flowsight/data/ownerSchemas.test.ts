import overview from '../../../test/fixtures/overview.json'
import periods from '../../../test/fixtures/periods.json'
import stage from '../../../test/fixtures/stage.json'
import changes from '../../../test/fixtures/changes.json'
import { changesViewSchema, overviewViewSchema, periodsViewSchema, stageDetailViewSchema } from './ownerSchemas'

describe('owner schemas', () => {
  it('parse the exported owner files', () => {
    expect(periodsViewSchema.parse(periods).periods.at(-1)?.id).toBe('last12')
    expect(overviewViewSchema.parse(overview).stages).toHaveLength(7)
    expect(stageDetailViewSchema.parse(stage).assignmentMix).toHaveProperty('groupQueue')
    expect(changesViewSchema.parse(changes).changes[0].verdict).toBe('improved')
  })

  it('reject an unknown pace', () => {
    const broken = { ...overview, stages: overview.stages.map((s, i) => (i === 0 ? { ...s, pace: 'glacial' } : s)) }
    expect(overviewViewSchema.safeParse(broken).success).toBe(false)
  })
})

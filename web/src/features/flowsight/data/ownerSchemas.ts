import { z } from 'zod'
import { stageName } from './schemas'

// Mirrors FlowSight.Core.Export.OwnerViews. Owner data may carry roles and pseudonymous actor ids (A-01…).

const ageing = z.object({ upTo2: z.number(), from3To5: z.number(), from6To10: z.number(), over10: z.number() })

export const pace = z.enum(['unknown', 'faster', 'normal', 'slower', 'muchSlower'])
export type Pace = z.infer<typeof pace>

export const periodsViewSchema = z.object({
  periods: z.array(z.object({ id: z.string(), start: z.string(), end: z.string() })),
  trend: z.array(
    z.object({
      periodId: z.string(),
      paid: z.number(),
      medianLeadDays: z.number().nullable(),
      p90LeadDays: z.number().nullable(),
    }),
  ),
})

export const stageStatsViewSchema = z.object({
  stage: stageName,
  rank: z.number(),
  throughput: z.number(),
  wipStart: z.number(),
  wipEnd: z.number(),
  medianDays: z.number().nullable(),
  p90Days: z.number().nullable(),
  medianInclOpenDays: z.number().nullable(),
  sharePercent: z.number(),
  ageing,
  pace,
})

export const overviewViewSchema = z.object({
  periodId: z.string(),
  start: z.string(),
  end: z.string(),
  stages: z.array(stageStatsViewSchema).length(7),
})

const groupTime = z.object({
  key: z.string(),
  role: z.string(),
  steps: z.number(),
  medianDays: z.number().nullable(),
  totalDays: z.number(),
})

const slowCase = z.object({ caseId: z.string(), lineId: z.string(), itemCategory: z.string(), workingDays: z.number() })

export const assignmentKinds = ['person', 'role', 'groupQueue', 'fieldRouted', 'system'] as const

export const stageDetailViewSchema = z.object({
  stage: stageName,
  periodId: z.string(),
  medianQueueDays: z.number().nullable(),
  medianWorkDays: z.number().nullable(),
  byRole: z.array(groupTime),
  byPerson: z.array(groupTime),
  assignmentMix: z.partialRecord(z.enum(assignmentKinds), z.number()),
  ageing,
  slowestOpen: z.array(slowCase),
  slowestCompleted: z.array(slowCase),
})

export const patternCodes = [
  'sharedQueueSearch',
  'serialApprovalStack',
  'singlePointOfFailure',
  'reworkLoop',
  'batchWaiting',
  'exceptionQueue',
] as const

export const changeCodes = [
  'routeByAttribute',
  'parallelOrThresholdApproval',
  'assignToRoleWithDelegation',
  'validateAtEntry',
  'dailyProcessingWithSla',
  'toleranceRulesUpstream',
] as const

export const cardViewSchema = z.object({
  id: z.string(),
  pattern: z.enum(patternCodes),
  stage: stageName,
  stepId: z.string(),
  stepName: z.string(),
  segment: z.string().nullable(),
  mapProperty: z.string(),
  evidence: z.array(z.object({ code: z.string(), values: z.record(z.string(), z.number()) })),
  proposedChange: z.enum(changeCodes),
  expectedSavingDaysPerLine: z.number().nullable(),
  affectedLines: z.number(),
  expectedSavingDaysTotal: z.number().nullable(),
})

export const cardsViewSchema = z.object({
  periodId: z.string(),
  cards: z.array(cardViewSchema),
  insufficientData: z.array(z.string()),
  unmappedActivities: z.array(z.string()),
})

const window = z.object({ start: z.string(), end: z.string() })
const impactStats = z.object({
  steps: z.number(),
  medianDays: z.number().nullable(),
  p90Days: z.number().nullable(),
  medianQueueDays: z.number().nullable(),
  averageWaiting: z.number().nullable(),
})

export const verdicts = ['insufficientData', 'improved', 'unchanged', 'worse'] as const

export const changeImpactViewSchema = z.object({
  index: z.number(),
  date: z.string(),
  stepId: z.string(),
  stepName: z.string(),
  stage: stageName.nullable(),
  description: z.string(),
  before: window,
  after: window,
  beforeStats: impactStats,
  afterStats: impactStats,
  medianChangePercent: z.number().nullable(),
  verdict: z.enum(verdicts),
  monthly: z.array(z.object({ periodId: z.string(), medianDays: z.number().nullable(), steps: z.number() })),
})

export const changesViewSchema = z.object({ changes: z.array(changeImpactViewSchema) })

export type ChangeImpactView = z.infer<typeof changeImpactViewSchema>
export type ChangesView = z.infer<typeof changesViewSchema>
export type Verdict = (typeof verdicts)[number]
export type CardView = z.infer<typeof cardViewSchema>
export type CardsView = z.infer<typeof cardsViewSchema>
export type PatternCode = (typeof patternCodes)[number]
export type PeriodsView = z.infer<typeof periodsViewSchema>
export type StageStatsView = z.infer<typeof stageStatsViewSchema>
export type OverviewView = z.infer<typeof overviewViewSchema>
export type StageDetailView = z.infer<typeof stageDetailViewSchema>
export type AgeingBuckets = z.infer<typeof ageing>

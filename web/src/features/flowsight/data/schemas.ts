import { z } from 'zod'

// Mirrors FlowSight.Core.Export. Unknown fields are stripped, so nothing outside the
// requester-facing contract can leak into the screens.

export const stageNames = ['request', 'approval', 'purchasing', 'order', 'goodsReceipt', 'invoice', 'payment'] as const
export const stageName = z.enum(stageNames)
export type StageName = z.infer<typeof stageName>

const status = z.enum(['open', 'completed', 'rejected', 'cancelled'])

export const waitingReasonCodes = [
  'assigneeAbsent',
  'returnedForCorrection',
  'priceVariance',
  'partialDelivery',
  'sharedQueue',
  'waitingForBatch',
  'unknown',
] as const

export const waitingReasonSchema = z.object({
  code: z.enum(waitingReasonCodes),
  params: z.record(z.string(), z.string()),
})
export type WaitingReasonView = z.infer<typeof waitingReasonSchema>

export const stageViewSchema = z.object({
  stage: stageName,
  state: z.enum(['notStarted', 'inProgress', 'completed']),
  enteredAt: z.string().nullable(),
  leftAt: z.string().nullable(),
  workingDays: z.number(),
  calendarDays: z.number(),
  typicalWorkingDays: z.number().nullable(),
})

export const lineViewSchema = z.object({
  lineId: z.string(),
  itemCategory: z.string(),
  path: z.enum(['goods', 'service']),
  status,
  currentStage: stageName.nullable(),
  stages: z.array(stageViewSchema).length(7),
  waitingReasons: z.array(waitingReasonSchema),
})

export const requestViewSchema = z.object({
  caseId: z.string(),
  status,
  currentStage: stageName.nullable(),
  totalWorkingDays: z.number(),
  slowerThanPercent: z.number().nullable(),
  stages: z.array(stageViewSchema).length(7),
  lines: z.array(lineViewSchema),
  asOf: z.string(),
  waitingReasons: z.array(waitingReasonSchema),
})

export const requestIndexSchema = z.array(
  z.object({ caseId: z.string(), status, currentStage: stageName.nullable() }),
)

export const demoMetaSchema = z.object({
  asOf: z.string(),
  seed: z.number(),
  requests: z.number(),
  minutesPerDay: z.number(),
  stages: z.array(stageName),
})

export type StageView = z.infer<typeof stageViewSchema>
export type LineView = z.infer<typeof lineViewSchema>
export type RequestView = z.infer<typeof requestViewSchema>
export type RequestIndexEntry = z.infer<typeof requestIndexSchema>[number]
export type DemoMeta = z.infer<typeof demoMetaSchema>

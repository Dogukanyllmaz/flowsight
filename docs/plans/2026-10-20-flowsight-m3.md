# FlowSight M3 Implementation Plan: why it waits and what to change

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For every waiting request the tracker says *why* it waits, and for the process owner FlowSight detects six known bottleneck patterns by combining data symptoms with the process map, and turns each into an explainable improvement card.

**Architecture:** The event log gains optional document links (order / receipt / invoice quantities and amounts) and absence records. A process map (YAML, superset of the `process-mapping-toolkit` format) describes steps, assignment, SLAs, rework edges and dated changes; `ProcessMap.AsOf(date)` gives the map as it was on a date. Waiting-reason rules and a pattern library live in Core; cards are computed, read-only, keyed by `pattern:stepId`. The generator injects one ground-truth scenario per pattern. The CLI exports reasons inside request files and cards per period; the web app adds reasons to the tracker, a cards screen and card links from the drill-down.

**Tech Stack:** as M2, plus `YamlDotNet` (parsing only) in Core.

**Spec:** `docs/design/2026-10-20-flowsight-design.md` (M3 = §7 row M3; G4, G5; §6.2 optional inputs and process map; §6.3 items 4–6; §6.4 scenarios; §6.5 item 4)

## Global Constraints

- Everything in the M1 and M2 Global Constraints still applies.
- Core may parse YAML from a **string**; it never touches the file system. Reading files stays in the CLI.
- Every finding is explainable: a reason or card carries a stable `code` plus numeric parameters; text is produced in the UI from strings (EN/TR). No free-text generated in Core.
- A pattern whose required input is missing reports `insufficientData` for that step instead of a finding.
- Requester JSON may contain waiting-reason codes and parameters, never actor ids, roles or person names.
- The demo map, scenarios, thresholds and texts are invented and generic.

## Decisions this plan fixes (builder's calls within the spec)

**New inputs (all optional, default empty):**
- `record DocumentLink(string CaseId, string LineId, DocumentType Type, string DocumentId, decimal Quantity, decimal Amount)`; `enum DocumentType { Order, Receipt, Invoice }`.
- `record Absence(string ActorId, DateOnly From, DateOnly To, string? DelegateId)` (inclusive dates).
- `EventLog` gains `IReadOnlyList<DocumentLink>? Links = null, IReadOnlyList<Absence>? Absences = null`.

**Process map** (`samples/purchase-to-pay.yaml`, embedded in the CLI as the demo map): step fields `id`, `name` (= event `Activity`), `type`, `lane`, `stage`, `next`, `assignee {type, value}` with types `person | role | group-queue | field-routed | manager_of | initiator | field | system`, `sla_hours`, `escalation`, `return_to`, `schedule` (only `weekly:<day>`). Top level `changes: [{date, step, description, before: {assignee?, sla_hours?}}]` — `before` holds the values that applied **before** `date`. `ProcessMap.AsOf(DateTimeOffset t)` reverts every change dated after `t`.

**Ground-truth scenarios** (in addition to M2's shared invoice queue):

| Pattern | Injected in synthetic data | Map structure |
|---|---|---|
| Serial approval stack | Requests with any *IT hardware* line get **IT approval → Line manager → Budget owner** (3 serial approvals, each median ~300 min) | three consecutive `approval` steps on that path |
| Single point of failure | *Payment run* at **Site East** is done by one named treasurer (`person`); absent 2026-02-02…2026-02-20 and 2026-08-03…2026-08-21 with **no delegate**; payments queued in those windows wait until the return. Site North's treasurer has one absence **with** a delegate (no spike). | `assignee.type: person` |
| Rework loop | 20 % of *Maintenance service* lines are **returned** from Sourcing (`Returned`) to a *Clarify specification* step (requester), then sourced again | `return_to` edge Sourcing → Clarify specification |
| Batch waiting | *Payment run* completes only on **Fridays** (work finishes, completion waits for the next Friday) | `schedule: weekly:friday`, no `sla_hours` |
| Exception queue | 10 % of invoices differ from the order amount by 3–10 %; their *Invoice matching* takes an extra ~1 500 min | matching step (`name: Invoice matching`) |

Partial deliveries (8 % of goods lines: two receipts, second one ~2 000 min later) and links for every order/receipt/invoice are generated so reasons can use them.

**Waiting reasons** (open lines, evaluated at as-of; several may apply; order as listed):

| Code | Rule | Params |
|---|---|---|
| `assigneeAbsent` | open step's actor is absent at as-of and the absence has no delegate | `untilDate` |
| `returnedForCorrection` | the line's latest completed step in its current stage has outcome `Returned` | — |
| `priceVariance` | in Invoice stage and invoice amount differs from order amount by > 2 % | `percent` (1 decimal, signed) |
| `partialDelivery` | in GoodsReceipt/Invoice stage and received qty < ordered qty | `received`, `ordered` |
| `sharedQueue` | open step's assignment is `GroupQueue` | `candidates` |
| `waitingForBatch` | open step's map step has `schedule: weekly:<day>` | `day` |
| `unknown` | none of the above | — |

**Pattern detection** per period and map step (map taken `AsOf(period.End)`; events = steps of that activity completed in the period; at least 20 events, else no finding). Each pattern lists **data symptom** + **map structure**:

| Pattern code | Data symptom | Map structure | Proposed change code |
|---|---|---|---|
| `sharedQueueSearch` | median queue ≥ 70 % of median step time and median `CandidateCount` ≥ 3 | assignee `group-queue` | `routeByAttribute` |
| `serialApprovalStack` | lines passing ≥ 3 approval steps of the chain: median total approval time ≥ 2 × median single approval | ≥ 3 consecutive `approval` steps | `parallelOrThresholdApproval` |
| `singlePointOfFailure` | median queue of steps queued during the actor's absences without delegate ≥ 3 × median otherwise | assignee `person` | `assignToRoleWithDelegation` |
| `reworkLoop` | ≥ 10 % of the step's lines have a `Returned` outcome | `return_to` edge from the step | `validateAtEntry` |
| `batchWaiting` | ≥ 60 % of completions fall on one weekday | `schedule` set and no `sla_hours` | `dailyProcessingWithSla` |
| `exceptionQueue` | lines with invoice/order variance > 2 % are ≥ 10 % of the step's lines and their median step time ≥ 2 × the rest | step name `Invoice matching` | `toleranceRulesUpstream` |

Missing input ⇒ `insufficientData`: absences for `singlePointOfFailure`, links for `exceptionQueue`, `CandidateCount` for `sharedQueueSearch`.

**Improvement card:** `record ImprovementCard(string Id, string Pattern, Stage Stage, string StepId, string StepName, string MapProperty, IReadOnlyList<Evidence> Evidence, string ProposedChange, double? ExpectedSavingMinutesPerLine, int AffectedLines)`; `record Evidence(string Code, IReadOnlyDictionary<string, double> Values)`. `Id = $"{pattern}:{stepId}"`. Expected saving = median minutes the symptom adds per affected line (queue part for queue patterns, difference to the rest for exception/rework/absence, approval total minus one approval for the stack); null when not estimable. Cards are sorted by `ExpectedSavingMinutesPerLine × AffectedLines`, largest first.

**Data layout:** waiting reasons go into each `data/requests/{caseId}.json` (`waitingReasons: [{code, params}]` at request level and per line); cards go to `data/owner/cards/{periodId}.json`.

**Routes:** `#/cards/{periodId}` and `#/cards/{periodId}/{cardId}` (card id URL-encoded).

## File Structure

```
samples/purchase-to-pay.yaml                       # demo process map (also embedded in the CLI)
src/FlowSight.Core/
  Model/EventLog.cs                                # DocumentLink, DocumentType, Absence; EventLog optional lists
  ProcessMaps/ProcessMap.cs                        # model, AsOf, consecutive approvals, lookups
  ProcessMaps/ProcessMapYaml.cs                    # Parse(string yaml) with validation
  Reasons/WaitingReasons.cs                        # WaitingReason + Evaluate
  Patterns/ImprovementCard.cs                      # ImprovementCard, Evidence, codes
  Patterns/PatternLibrary.cs                       # Detect(...) orchestrator
  Patterns/Detectors.cs                            # one method per pattern
  Export/RequestView.cs, RequestViewBuilder.cs     # waitingReasons
  Export/OwnerViews.cs                             # CardsView
src/FlowSight.Synthetic/Catalog.cs, Generator.cs   # scenarios, links, absences
src/FlowSight.Cli/DemoExporter.cs, FlowSight.Cli.csproj (EmbeddedResource map)
tests/FlowSight.Tests/
  ProcessMapTests.cs  ScenarioM3Tests.cs  WaitingReasonTests.cs  PatternTests.cs  CardExportTests.cs
web/src/
  routing.ts (+cards routes)
  features/flowsight/data/{schemas,ownerSchemas,source,staticSource,hooks}
  features/flowsight/i18n/strings.ts               # reason, pattern, evidence, change texts EN/TR
  features/flowsight/components/WaitingReasons.tsx
  features/flowsight/pages/{RequestTracker,StageDetail,AppShell}.tsx
  features/flowsight/pages/ImprovementCards.tsx + .test.tsx
  test/fixtures/cards.json
README.md
```

## Review Focus

1. **Map and data disagree** (an activity in the data has no map step, or a map step never occurs): no crash; unmatched activities are ignored by patterns and listed once in the export as `unmappedActivities`. → Task 4 test `UnmappedActivity_IsReportedNotFatal`.
2. **A period with too few events for a step**: no card and no `insufficientData` noise for it. → Task 4 test `FewEvents_NoFinding`.
3. **Invalid YAML or a map that references a missing step / unknown stage**: a clear `ProcessMapException` naming the problem, raised at load. → Task 2 test `InvalidMap_FailsWithMessage`.
4. **Reasons on closed requests**: completed, rejected or cancelled requests show no waiting reasons. → Task 3 test `ClosedLine_HasNoReasons`.
5. **Card id in the URL that no longer exists in the chosen period**: friendly not-found inside the cards screen. → Task 7 test `ShowsNotFound_ForUnknownCard`.

---

### Task 1: Optional inputs and the five new scenarios

**Files:** modify `Model/EventLog.cs`, `Synthetic/Catalog.cs`, `Synthetic/Generator.cs`; test `tests/FlowSight.Tests/ScenarioM3Tests.cs`.

**Interfaces:** Produces `DocumentLink`, `DocumentType`, `Absence`, `EventLog.Links`, `EventLog.Absences` (non-null after generation); activity names `IT approval`, `Clarify specification`; role `IT manager` (one per site).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ItRequests_HaveThreeSerialApprovals()          // every request with an IT hardware line: approval activities in order IT approval, Line manager approval, Budget owner approval
[Fact] public void SiteEastTreasurer_AbsentWithoutDelegate()      // absences contain the two windows, DelegateId null; Site North absence has a delegate
[Fact] public void PaymentsQueuedDuringAbsence_WaitForReturn()    // Site East payment events queued in a window: StartedAt ≥ window end
[Fact] public void MaintenanceService_IsReturnedAboutOneInFive()  // share of maintenance-service lines with a Returned sourcing step in 0.15–0.25; each is followed by Clarify specification, then Sourcing again
[Fact] public void Payments_CompleteOnFridays()                   // every completed Payment run event completes on a Friday
[Fact] public void PriceVariance_OnAboutOneInTenInvoices()        // share of invoiced lines whose invoice amount differs from order by >2 %: 0.07–0.13
[Fact] public void PartialDeliveries_HaveTwoReceipts()            // 0.05–0.11 of goods lines have two receipt links with quantities summing to the order quantity
[Fact] public void LinksExist_ForEveryOrderedLine()               // each line with a completed Order step has one Order link with Quantity = line quantity, Amount = line amount
```

- [ ] **Step 2: Run** `dotnet test --filter ScenarioM3Tests`. Expected: FAIL.
- [ ] **Step 3: Implement** per Decisions; keep one seeded `Random`; M1/M2 tests must stay green except where the plan changes behaviour (staffing change shifts actor ids: update only tests that hard-code ids).
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add documents, absences and five ground-truth scenarios to synthetic data"`

---

### Task 2: Process map model and YAML parser

**Files:** create `ProcessMaps/ProcessMap.cs`, `ProcessMaps/ProcessMapYaml.cs`, `samples/purchase-to-pay.yaml`; add `YamlDotNet` to Core; test `ProcessMapTests.cs`.

**Interfaces:** Produces `record ProcessMap(string Code, string Name, IReadOnlyList<string> Lanes, IReadOnlyList<MapStep> Steps, IReadOnlyList<MapChange> Changes, double? DefaultSlaHours)`, `record MapStep(string Id, string Name, string Type, string Lane, Stage? Stage, IReadOnlyList<string> Next, MapAssignee? Assignee, double? SlaHours, bool Escalation, string? ReturnTo, DayOfWeek? WeeklyOn)`, `record MapAssignee(string Type, string Value)`, `record MapChange(DateOnly Date, string Step, string Description, MapAssignee? AssigneeBefore, double? SlaHoursBefore)`; methods `ProcessMap.AsOf(DateTimeOffset t)`, `MapStep? StepForActivity(string activity)`, `IReadOnlyList<IReadOnlyList<MapStep>> ApprovalChains(int minLength)`; `static class ProcessMapYaml { ProcessMap Parse(string yaml); }`; `class ProcessMapException : Exception`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ParsesToolkitFormatPlusExtensions()      // samples/purchase-to-pay.yaml: invoice-matching is field-routed now; changes[0] (2026-06-03) has AssigneeBefore group-queue
[Fact] public void AsOf_RevertsLaterChanges()               // AsOf(2026-03-31) → invoice-matching assignee group-queue; AsOf(2026-09-30) → field-routed
[Fact] public void FindsSerialApprovalChain()               // ApprovalChains(3) contains [IT approval, Line manager approval, Budget owner approval]
[Fact] public void ReadsReturnEdgeAndSchedule()             // sourcing.ReturnTo == clarify-spec; payment-run.WeeklyOn == Friday and SlaHours null
[Fact] public void AcceptsAToolkitMapWithoutExtensions()    // a minimal toolkit-style YAML (no stage/changes) parses with Stage null
[Fact] public void InvalidMap_FailsWithMessage()            // next → missing step, unknown stage name, malformed YAML: ProcessMapException whose message names the step or line
```

- [ ] **Step 2: Run** `dotnet test --filter ProcessMapTests`. Expected: FAIL.
- [ ] **Step 3: Implement**; snake_case YAML keys; `schedule: weekly:friday` → `DayOfWeek.Friday`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add process map model and YAML parser"`

---

### Task 3: Waiting reasons

**Files:** create `Reasons/WaitingReasons.cs`; test `WaitingReasonTests.cs`.

**Interfaces:** Consumes `EventLog` (links, absences), `LineJourney`, `ProcessMap`. Produces `record WaitingReason(string Code, IReadOnlyDictionary<string, string> Params)`; `static class WaitingReasons { IReadOnlyList<WaitingReason> For(LineJourney line, EventLog log, ProcessMap map); }` (codes and order per Decisions; params formatted invariantly: numbers with `.` and one decimal, dates `yyyy-MM-dd`, weekdays lower-case English).

- [ ] **Step 1: Write the failing tests** — one per code (`AbsentAssignee_WithoutDelegate`, `AbsentAssignee_WithDelegate_IsNotAReason`, `ReturnedForCorrection`, `PriceVariance_Signed`, `PartialDelivery`, `SharedQueue_ReportsCandidates`, `WaitingForBatch`, `NoRuleMatches_IsUnknown`) and `ClosedLine_HasNoReasons`, `SeveralReasons_KeepOrder`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add explainable waiting reasons"`

---

### Task 4: Pattern library and improvement cards

**Files:** create `Patterns/ImprovementCard.cs`, `Patterns/Detectors.cs`, `Patterns/PatternLibrary.cs`; test `PatternTests.cs`.

**Interfaces:** Consumes `EventLog`, `LineJourney`, `ProcessMap`, `Period`, `IWorkCalendar`, `Percentiles`. Produces `ImprovementCard`, `Evidence` (per Decisions), `record PatternResult(IReadOnlyList<ImprovementCard> Cards, IReadOnlyList<string> InsufficientData, IReadOnlyList<string> UnmappedActivities)`; `static class PatternLibrary { PatternResult Detect(EventLog log, IReadOnlyList<LineJourney> lines, ProcessMap map, Period period, IWorkCalendar calendar); }`. `InsufficientData` entries are `pattern:stepId`.

Evidence codes (values in working minutes or ratios): `medianQueue`, `medianStep`, `queueShare`, `candidates`, `approvalsInChain`, `medianChainTotal`, `medianSingleApproval`, `medianQueueAbsent`, `medianQueuePresent`, `absenceDays`, `returnedShare`, `weekdayShare`(+`weekday`), `exceptionShare`, `medianException`, `medianNormal`.

- [ ] **Step 1: Write the failing tests** (synthetic default data + demo map; `Detect` for the named month)

```csharp
[Theory] [InlineData("2026-03")] public void SharedQueue_DetectedBeforeChange(string m)          // card sharedQueueSearch:invoice-matching, change routeByAttribute
[Theory] [InlineData("2026-09")] public void SharedQueue_NotDetectedAfterChange(string m)
[Fact] public void SerialApprovals_Detected()                       // 2026-03: serialApprovalStack:it-approval
[Fact] public void SinglePointOfFailure_DetectedInAbsenceMonth()    // 2026-02 and 2026-08: singlePointOfFailure:payment-run; 2026-04: none
[Fact] public void Rework_Detected()                                // 2026-03: reworkLoop:sourcing
[Fact] public void BatchWaiting_Detected()                          // 2026-03: batchWaiting:payment-run, evidence weekday=5 (Friday)
[Fact] public void ExceptionQueue_Detected()                        // 2026-09: exceptionQueue:invoice-matching (after routing, exceptions dominate the remaining delay)
[Fact] public void NoFalsePositives()                               // 2026-09: no card for order or purchase-order creation steps
[Fact] public void MissingAbsences_IsInsufficientData()             // log with Absences empty → "singlePointOfFailure:payment-run" in InsufficientData, no such card
[Fact] public void UnmappedActivity_IsReportedNotFatal()
[Fact] public void FewEvents_NoFinding()                            // 5-request log → no cards, no insufficient-data entries
[Fact] public void Cards_SortedByTotalExpectedSaving()
```

- [ ] **Step 2: Run** `dotnet test --filter PatternTests`. Expected: FAIL.
- [ ] **Step 3: Implement** one detector per pattern in `Detectors.cs`, orchestrated by `PatternLibrary.Detect`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add bottleneck pattern library and improvement cards"`

---

### Task 5: Export reasons and cards

**Files:** modify `Export/RequestView.cs`, `Export/RequestViewBuilder.cs`, `Export/OwnerViews.cs`, `Cli/DemoExporter.cs`, `Cli/FlowSight.Cli.csproj` (embed `samples/purchase-to-pay.yaml`); test `CardExportTests.cs`.

**Interfaces:** Produces `record WaitingReasonView(string Code, IReadOnlyDictionary<string, string> Params)`; `RequestView` and `LineView` gain `IReadOnlyList<WaitingReasonView> WaitingReasons` (request level = union of open lines' reasons, deduplicated by code, order kept); `record EvidenceView(string Code, IReadOnlyDictionary<string, double> Values)`; `record CardView(string Id, string Pattern, string Stage, string StepId, string StepName, string MapProperty, IReadOnlyList<EvidenceView> Evidence, string ProposedChange, double? ExpectedSavingDaysPerLine, int AffectedLines, double? ExpectedSavingDaysTotal)`; `record CardsView(string PeriodId, IReadOnlyList<CardView> Cards, IReadOnlyList<string> InsufficientData, IReadOnlyList<string> UnmappedActivities)`; file `data/owner/cards/{periodId}.json` (safe delete as before). `RequestViewBuilder.Build` gains `IReadOnlyDictionary<(string, string), IReadOnlyList<WaitingReason>> reasons`.

- [ ] **Step 1: Write the failing tests** — `Demo_WritesCardsPerPeriod`, `MarchCards_IncludeSharedQueue`, `RequestFiles_CarryReasonsWithoutActors` (no `A-`, no `"role"`, at least one open request with `sharedQueue` or `waitingForBatch`), `ClosedRequests_HaveEmptyReasons`, `CardsExport_IsDeterministic`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** `dotnet test`, then regenerate `web/public/data`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Export waiting reasons and improvement cards"`

---

### Task 6: Waiting reasons in the tracker

**Files:** create `components/WaitingReasons.tsx`; modify `data/schemas.ts`, `i18n/strings.ts`, `pages/RequestTracker.tsx`, test fixtures (`request.json` re-copied from the new export); tests in `pages/RequestTracker.test.tsx`.

**Interfaces:** `waitingReasonSchema` (`code` enum of the seven codes, `params: Record<string,string>`); `WaitingReasons({ reasons })` renders one sentence per reason (EN/TR, numbers and dates localised).

Requester copy (EN): `assigneeAbsent` "The person handling this step is away until {date} and no stand-in is set." · `returnedForCorrection` "It was returned to you for a correction." · `priceVariance` "The invoice differs from the order by {percent}%; finance is checking it." · `partialDelivery` "Only {received} of {ordered} have been delivered so far." · `sharedQueue` "It is in a shared queue with {candidates} people who may pick it up." · `waitingForBatch` "Payments are made once a week, on {day}." · `unknown` "No specific reason recorded." TR versions in the same tone.

- [ ] **Step 1: Write the failing tests** — `shows why an open request is waiting`, `shows no reasons for a completed request`, `localises the reason in Turkish`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** tests and build. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Show waiting reasons in the request tracker"`

---

### Task 7: Improvement cards screen

**Files:** create `pages/ImprovementCards.tsx` + `.test.tsx`, `test/fixtures/cards.json` (copy of `owner/cards/2026-03.json`); modify `routing.ts` (+ tests), `data/ownerSchemas.ts`, `data/source.ts`, `data/staticSource.ts`, `data/hooks.tsx`, `pages/AppShell.tsx` (third nav item "What to change?"), `App.tsx`, `pages/StageDetail.tsx` (link "See improvement cards for this stage"), `i18n/strings.ts`, `test/renderWithSource.tsx`.

**Interfaces:** routes `{ page: 'cards'; periodId: string; cardId?: string }` ↔ `#/cards/{periodId}[/{cardId}]`; `cardsViewSchema`; `FlowSightSource.getCards(periodId): Promise<CardsView | null>`; hook `useCards(periodId)`; `ImprovementCards({ periodId, cardId, onPeriodChange })`.

Screen: period selector; card list ordered as exported, each showing pattern name, stage and step, **expected saving** ("≈ N working days per line · M lines"), and the proposed change; selecting a card shows its detail: *What we see* (evidence sentences with numbers), *Why* (the map element, e.g. "Step 'Invoice matching' is assigned to a shared queue"), *What to change* (proposed change text, one paragraph per change code), *Expected effect*, links to the stage drill-down and to the step's slowest cases. When `insufficientData` is not empty, a muted note lists which checks could not run and why. Owner-only (no requester link).

Change texts (EN, short): `routeByAttribute` "Route each item straight to the responsible person by a field such as site or warehouse, instead of a queue everyone searches." · `parallelOrThresholdApproval` "Run approvals in parallel or ask only the approvals the amount requires." · `assignToRoleWithDelegation` "Assign the step to a role with at least two people, and require a stand-in during absences." · `validateAtEntry` "Make the missing information mandatory when the request is created, so it is not sent back later." · `dailyProcessingWithSla` "Process daily with an SLA and escalation instead of a weekly batch." · `toleranceRulesUpstream` "Agree tolerance rules and fix the price at order time, so matching exceptions do not pile up." TR versions in the same tone.

- [ ] **Step 1: Write the failing tests** — routing round-trip for cards routes; `lists cards with expected saving`, `opens a card with evidence, cause and change`, `ShowsNotFound_ForUnknownCard`, `notes checks that could not run`, `switches to Turkish`.
- [ ] **Step 2: Run**. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** tests, build, open `#/cards/2026-03` in the dev server; check light/dark and 375 px. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add improvement cards screen"`

---

### Task 8: Docs and release

**Files:** modify `README.md` (status M3; the six patterns table; one paragraph on how cards are explained; how to supply your own map), `docs/design` unchanged.

- [ ] **Step 1: Update README** as listed.
- [ ] **Step 2: Final checks** — `dotnet test`, `npm --prefix web run test -- --run`, `npm --prefix web run build`, private sensitive-term scan reports nothing. Expected: all green.
- [ ] **Step 3: Commit and tag** — `git commit -m "Document M3"`, `git tag v0.3.0`.

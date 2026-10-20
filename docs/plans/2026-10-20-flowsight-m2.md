# FlowSight M2 Implementation Plan: where do requests pile up?

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The process owner sees, per month and for the last 12 months, how much work waits in each stage, which stages are slower than normal, how stages rank as bottlenecks, and — for one stage — queue vs. work time, time per role and per person, ageing and the slowest cases.

**Architecture:** New `FlowSight.Core.Statistics` namespace computes period statistics, a bottleneck ranking and a stage drill-down from the M1 journeys and the event log. The synthetic generator gains its first injected scenario (a shared invoice queue that is fixed in month 8), which tests use as ground truth. The CLI exports an owner-only JSON set next to the requester files; the web app gets a hash router, an owner view toggle and two screens with dependency-free SVG charts.

**Tech Stack:** as M1 (.NET 8, xUnit, React 19 + Vite + TypeScript, TanStack Query, zod, Vitest). No chart library.

**Spec:** `docs/design/2026-10-20-flowsight-design.md` (M2 = §7 row M2; outputs §6.3 items 2–3; scenarios §6.4; screens §6.5 items 2–3; fixed periods in §6.5 builder's calls)

## Global Constraints

- Everything in the M1 plan's Global Constraints still applies (net8.0, warnings as errors, Core dependency-free, English repo + EN/TR UI, working minutes ÷ 540 = working days, deterministic data, invented names only, commit trailer).
- **Requester JSON (`data/requests/`) still never contains actor ids or roles.** Owner JSON (`data/owner/`) may contain roles and pseudonymous actor ids matching `^A-\d{2}$`, nothing else that identifies a person.
- Medians and P90 use inclusive linear interpolation (P90 of 1…10 = 9.1). A statistic over zero values is `null`, shown as "–", never 0.
- Periods: every **full calendar month** that ends on or before as-of, plus `last12` = the 12 months up to as-of (ending at as-of, so its "end of period" is *now*). Period ids: `yyyy-MM` and `last12`.
- Before writing any chart code (Tasks 7–8), load the `dataviz` skill and follow it.
- Run the private sensitive-term scan (term list kept outside this repository) before the final commit; it must report nothing.

## Decisions this plan fixes (builder's calls within the spec)

- **Ground-truth scenario (only one in M2):** invoices go to a shared queue of all accounts-payable clerks across sites until `ChangeDate = StartDate + 7 months` (2026-06-03 for the default options); from then on each invoice is routed to its site's clerk. Before: `AssignmentKind.GroupQueue`, `CandidateCount = 6`, median 6 000 working minutes, queue share 0.9. After: `AssignmentKind.FieldRouted`, `CandidateCount = 1`, median 900, queue share 0.7. The change is applied by the invoice step's `QueuedAt`.
- **Statistics of a stage in a period** use the line-level stage spans:
  - *throughput* = spans that left the stage inside the period;
  - *median / P90* = working minutes of those spans;
  - *median incl. open* = Kaplan–Meier median over spans that **entered** in the period, censoring spans still in the stage at period end at their working minutes up to period end;
  - *WIP* at an instant = spans with `EnteredAt ≤ t` and (`LeftAt` null or `> t`), counting only lines not rejected or cancelled before `t`; *WIP growth* = WIP at end − WIP at start;
  - *ageing* = WIP at end bucketed by working days in the stage up to period end: ≤ 2, 3–5, 6–10, > 10 (bucket edges on working days rounded down);
  - *share of lead time* = Σ stage minutes ÷ Σ total minutes over lines **paid** in the period (0 when none).
- **Bottleneck ranking:** rank by share of lead time, ties by WIP at end. **Pace** = period median ÷ `last12` median of the same stage: ≥ 1.5 `muchSlower`, ≥ 1.2 `slower`, < 0.8 `faster`, else `normal`; `unknown` when either median is null.
- **Drill-down events** = events of the stage completed inside the period. Queue = `QueuedAt→StartedAt`, work = `StartedAt→CompletedAt`, both in working minutes; events without `StartedAt` count in totals but not in queue/work medians.
- **Owner data layout:** `data/owner/periods.json` (periods + monthly lead-time trend), `data/owner/overview/{periodId}.json`, `data/owner/stages/{periodId}/{stage}.json`.
- **Routes:** `#/track`, `#/track/{caseId}`, `#/flow/{periodId}`, `#/flow/{periodId}/{stage}`. Default route `#/track`; owner default period `last12`.

## File Structure

```
src/FlowSight.Core/
  Journeys/JourneyBuilder.cs          # make CutOff public as ApplyCutOff(EventLog)
  Statistics/Percentiles.cs           # Percentile, Median, KaplanMeierMedian
  Statistics/Periods.cs               # Period record + Periods.For(log)
  Statistics/StageStatistics.cs       # StageStats, AgeingBuckets, Compute
  Statistics/Bottlenecks.cs           # Pace, BottleneckEntry, Rank
  Statistics/StageDrillDown.cs        # StageDetail, GroupTime, SlowCase, Compute
  Export/OwnerViews.cs                # owner JSON contract + builders
src/FlowSight.Synthetic/
  Catalog.cs, Generator.cs            # invoice scenario
src/FlowSight.Cli/DemoExporter.cs     # also writes data/owner/**
tests/FlowSight.Tests/
  ScenarioTests.cs  PercentilesTests.cs  StageStatisticsTests.cs
  BottleneckTests.cs  StageDrillDownTests.cs  OwnerExportTests.cs
web/src/
  App.tsx                              # router + shell
  routing.ts  routing.test.ts          # parse/format hash routes
  features/flowsight/
    data/ownerSchemas.ts  data/source.ts  data/staticSource.ts  data/hooks.tsx
    i18n/strings.ts
    components/charts/BarChart.tsx  components/charts/TrendChart.tsx
    pages/AppShell.tsx  pages/FlowOverview.tsx  pages/FlowOverview.test.tsx
    pages/StageDetail.tsx  pages/StageDetail.test.tsx
  test/fixtures/overview.json  test/fixtures/stage.json  test/fixtures/periods.json
README.md
```

## Review Focus

1. **A stage with nothing completed in a period** (e.g. Payment in the first month): median, P90 and pace are null/unknown and render as "–"; share of lead time is 0 with no division by zero. → Task 2 test `EmptyPeriod_GivesNullStatistics`, Task 7 test `ShowsDash_ForMissingMedian`.
2. **Rejected or cancelled lines** must stop counting as WIP once they end. → Task 2 test `RejectedLine_LeavesWipWhenItEnds`.
3. **`last12` ends at as-of**: WIP at its end equals the number of lines in each stage *now*. → Task 2 test `Last12_WipEqualsCurrentStageCounts`.
4. **Unknown period id or stage typed in the URL**: friendly "not found" message, no blank page. → Task 6 test `parsesUnknownRoutesAsNotFound`, Task 7 test `ShowsNotFound_ForUnknownPeriod`.
5. **Events without `StartedAt`**: queue and work medians ignore them instead of treating them as zero. → Task 4 test `MissingStart_IsNotZeroQueue`.

---

### Task 1: Shared invoice queue scenario

**Files:**
- Modify: `src/FlowSight.Synthetic/Catalog.cs`, `src/FlowSight.Synthetic/Generator.cs`, `src/FlowSight.Synthetic/SyntheticOptions.cs`
- Test: `tests/FlowSight.Tests/ScenarioTests.cs`

**Interfaces:**
- Produces: `SyntheticOptions.ChangeDate` (`DateOnly`, = `StartDate.AddMonths(7)`); `Catalog.SharedInvoiceQueue` and `Catalog.RoutedInvoice` step templates (values in Decisions).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void InvoicesBeforeChange_GoToSharedQueue()      // every Invoice event queued before ChangeDate: GroupQueue, CandidateCount 6
[Fact] public void InvoicesAfterChange_AreRouted()             // every Invoice event queued on/after ChangeDate: FieldRouted, CandidateCount 1, actor belongs to the line's site
[Fact] public void InvoiceWait_DropsAfterChange()              // median working minutes of completed Invoice events: before ≥ 3 × after
[Fact] public void OtherStages_AreUnchangedByScenario()        // Purchasing median before/after ratio within 0.7–1.4
```

- [ ] **Step 2: Run** `dotnet test --filter ScenarioTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the scenario per Decisions; keep the single seeded `Random` and draw order deterministic (the existing determinism tests must still pass).
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS (all).
- [ ] **Step 5: Commit** — `git commit -m "Add shared invoice queue scenario to synthetic data"`

---

### Task 2: Percentiles, periods and stage statistics

**Files:**
- Create: `src/FlowSight.Core/Statistics/Percentiles.cs`, `Periods.cs`, `StageStatistics.cs`; modify `Journeys/JourneyBuilder.cs`
- Test: `tests/FlowSight.Tests/PercentilesTests.cs`, `tests/FlowSight.Tests/StageStatisticsTests.cs`

**Interfaces:**
- Consumes: `LineJourney`, `StageSpan`, `IWorkCalendar`, `EventLog`.
- Produces:
  - `static class Percentiles { double? Percentile(IReadOnlyList<double> values, double p); double? Median(IReadOnlyList<double> values); double? KaplanMeierMedian(IReadOnlyList<(double Minutes, bool Observed)> spans); }`
  - `record Period(string Id, DateTimeOffset Start, DateTimeOffset End)`; `static class Periods { IReadOnlyList<Period> For(EventLog log); Period Last12(DateTimeOffset asOf); }` — monthly periods in the calendar offset (+03:00), oldest first, then `last12`.
  - `record AgeingBuckets(int UpTo2, int From3To5, int From6To10, int Over10)`
  - `record StageStats(Stage Stage, int Throughput, int WipStart, int WipEnd, double? MedianMinutes, double? P90Minutes, double? MedianInclOpenMinutes, double ShareOfLeadTime, AgeingBuckets Ageing)` — `ShareOfLeadTime` in 0–1.
  - `static class StageStatistics { IReadOnlyList<StageStats> Compute(IReadOnlyList<LineJourney> lines, Period period, IWorkCalendar calendar); }` — 7 entries in stage order.
  - `JourneyBuilder.ApplyCutOff(EventLog log) : EventLog` (public; `Build` uses it).

- [ ] **Step 1: Write the failing tests**

```csharp
// PercentilesTests
[Fact] public void Percentile_IsInclusiveLinear()            // 1..10: P50 5.5, P90 9.1; single value 7 → 7; empty → null
[Fact] public void KaplanMeierMedian_UsesCensoredSpans()       // (1,T),(2,T),(3,T),(4,F),(4,F) → 3 ; observed only (1,2,3) → 2
[Fact] public void KaplanMeierMedian_NullWhenNeverBelowHalf()  // (1,T),(5,F),(5,F),(5,F) → null

// StageStatisticsTests (hand-built LineJourney spans with EnteredAt/LeftAt; TestCalendar)
[Fact] public void Throughput_CountsSpansLeavingInPeriod()
[Fact] public void Wip_CountsSpansOpenAtStartAndEnd()
[Fact] public void RejectedLine_LeavesWipWhenItEnds()
[Fact] public void Ageing_BucketsWipByWorkingDaysInStage()     // in stage 1, 4, 7, 12 working days at period end → (1,1,1,1)
[Fact] public void ShareOfLeadTime_UsesLinesPaidInPeriod()      // two paid lines: Invoice 300+100 of totals 1000+1000 → Invoice share 0.2
[Fact] public void EmptyPeriod_GivesNullStatistics()            // no spans → medians null, share 0, ageing all 0
[Fact] public void Periods_AreFullMonthsPlusLast12()            // synthetic default: first "2025-11", last monthly "2026-10", then "last12" ending at AsOf
[Fact] public void Last12_WipEqualsCurrentStageCounts()         // synthetic 300 requests: WipEnd per stage == count of open lines whose CurrentStage is that stage
```

- [ ] **Step 2: Run** `dotnet test --filter "PercentilesTests|StageStatisticsTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** per Decisions. Kaplan–Meier: sort by minutes (observed before censored at ties); at each observed time `S *= (atRisk − deaths) / atRisk`; return the first time `S ≤ 0.5`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add period statistics for stages"`

---

### Task 3: Bottleneck ranking and pace

**Files:**
- Create: `src/FlowSight.Core/Statistics/Bottlenecks.cs`
- Test: `tests/FlowSight.Tests/BottleneckTests.cs`

**Interfaces:**
- Consumes: `StageStats` (Task 2).
- Produces: `enum Pace { Unknown, Faster, Normal, Slower, MuchSlower }`; `record BottleneckEntry(Stage Stage, int Rank, double ShareOfLeadTime, int WipEnd, int WipGrowth, Pace Pace)`; `static class Bottlenecks { IReadOnlyList<BottleneckEntry> Rank(IReadOnlyList<StageStats> period, IReadOnlyList<StageStats> baseline); }` — returned in rank order, ranks 1–7.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void RanksByShareThenWip()
[Fact] public void Pace_ComparesWithBaselineMedian()          // ratios 1.6 MuchSlower, 1.3 Slower, 1.0 Normal, 0.7 Faster, null Unknown
[Theory] [InlineData("2026-01")] [InlineData("2026-03")] [InlineData("2026-05")]
public void SharedQueueMonths_RankInvoiceFirst(string month)   // synthetic default data
[Theory] [InlineData("2026-08")] [InlineData("2026-10")]
public void AfterRouting_InvoiceIsNotFirstAndNotSlower(string month)
```

- [ ] **Step 2: Run** `dotnet test --filter BottleneckTests`. Expected: FAIL.
- [ ] **Step 3: Implement** per Decisions.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add bottleneck ranking with pace against the last 12 months"`

---

### Task 4: Stage drill-down

**Files:**
- Create: `src/FlowSight.Core/Statistics/StageDrillDown.cs`
- Test: `tests/FlowSight.Tests/StageDrillDownTests.cs`

**Interfaces:**
- Consumes: `EventLog`, `JourneyBuilder.ApplyCutOff`, `LineJourney`, `Period`, `AgeingBuckets`, `Percentiles`.
- Produces:
  - `record GroupTime(string Key, string Role, int Steps, double? MedianMinutes, double TotalMinutes)`
  - `record SlowCase(string CaseId, string LineId, string ItemCategory, double WorkingMinutes)`
  - `record StageDetail(Stage Stage, string PeriodId, double? MedianQueueMinutes, double? MedianWorkMinutes, IReadOnlyList<GroupTime> ByRole, IReadOnlyList<GroupTime> ByPerson, IReadOnlyDictionary<AssignmentKind, int> AssignmentMix, AgeingBuckets Ageing, IReadOnlyList<SlowCase> SlowestOpen, IReadOnlyList<SlowCase> SlowestCompleted)`
  - `static class StageDrillDown { StageDetail Compute(EventLog log, IReadOnlyList<LineJourney> lines, Stage stage, Period period, IWorkCalendar calendar); }` — `ByRole` key = role; `ByPerson` key = actor id; both sorted by `TotalMinutes` descending; slow lists top 10 descending; `SlowestOpen` = lines in the stage at period end.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SplitsQueueAndWork()                // queued 08:00, started 10:00, completed 11:00 → queue 120, work 60
[Fact] public void MissingStart_IsNotZeroQueue()       // one event without StartedAt + one with queue 120 → median queue 120
[Fact] public void GroupsByRoleAndPerson()
[Fact] public void SlowestOpen_AreLinesStillInStage()
[Fact] public void SharedQueueMonth_ShowsGroupQueueMix()   // synthetic default, Invoice, "2026-03": AssignmentMix has only GroupQueue; "2026-09": only FieldRouted
```

- [ ] **Step 2: Run** `dotnet test --filter StageDrillDownTests`. Expected: FAIL.
- [ ] **Step 3: Implement** per Decisions.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add stage drill-down: queue vs work, roles, people, slowest cases"`

---

### Task 5: Owner JSON contract and export

**Files:**
- Create: `src/FlowSight.Core/Export/OwnerViews.cs`; modify `src/FlowSight.Cli/DemoExporter.cs`
- Test: `tests/FlowSight.Tests/OwnerExportTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4.
- Produces (camelCase JSON, enum values camelCase strings, days = minutes ÷ minutesPerDay rounded to 1 decimal, share in percent rounded to 1 decimal):
  - `record PeriodsView(IReadOnlyList<PeriodInfo> Periods, IReadOnlyList<TrendPoint> Trend)`; `record PeriodInfo(string Id, DateTimeOffset Start, DateTimeOffset End)`; `record TrendPoint(string PeriodId, int Paid, double? MedianLeadDays, double? P90LeadDays)` — monthly periods only.
  - `record StageStatsView(string Stage, int Rank, int Throughput, int WipStart, int WipEnd, double? MedianDays, double? P90Days, double? MedianInclOpenDays, double SharePercent, AgeingBuckets Ageing, string Pace)`
  - `record OverviewView(string PeriodId, DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<StageStatsView> Stages)` — stages in **stage order** (rank is a field).
  - `record GroupTimeView(string Key, string Role, int Steps, double? MedianDays, double TotalDays)`; `record SlowCaseView(string CaseId, string LineId, string ItemCategory, double WorkingDays)`
  - `record StageDetailView(string Stage, string PeriodId, double? MedianQueueDays, double? MedianWorkDays, IReadOnlyList<GroupTimeView> ByRole, IReadOnlyList<GroupTimeView> ByPerson, IReadOnlyDictionary<string, int> AssignmentMix, AgeingBuckets Ageing, IReadOnlyList<SlowCaseView> SlowestOpen, IReadOnlyList<SlowCaseView> SlowestCompleted)`
  - `static class OwnerViews { PeriodsView Periods(...); OverviewView Overview(...); StageDetailView StageDetail(...); }` (exact parameters left to the implementer; the exporter is the only caller)
  - `DemoExporter.Run` additionally writes the owner layout; it deletes only `data/owner/periods.json`, `data/owner/overview/*.json`, `data/owner/stages/*/*.json` before writing.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Demo_WritesOwnerFiles()                  // 50 requests: periods.json, overview/last12.json, stages/last12/invoice.json exist; one overview per period
[Fact] public void OwnerFiles_OnlyPseudonymousActors()      // every byPerson key matches ^A-\d{2}$
[Fact] public void RequesterFiles_StillHaveNoActors()       // no "A-" and no "\"role\"" in data/requests/**
[Fact] public void Overview_StagesInStageOrderWithRanks()   // 7 stages, ranks are a permutation of 1..7
[Fact] public void OwnerExport_IsDeterministic()            // two runs, identical hashes for data/owner/**
```

- [ ] **Step 2: Run** `dotnet test --filter OwnerExportTests`. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** `dotnet test`, then `dotnet run --project src/FlowSight.Cli -- demo --out web/public/data`. Expected: PASS; `web/public/data/owner/` populated.
- [ ] **Step 5: Commit** — `git commit -m "Export owner statistics as static JSON"`

---

### Task 6: Routing, owner data layer and app shell

**Files:**
- Create: `web/src/routing.ts`, `web/src/routing.test.ts`, `web/src/features/flowsight/data/ownerSchemas.ts`, `web/src/features/flowsight/pages/AppShell.tsx`, fixtures `web/src/test/fixtures/{periods,overview,stage}.json` (copied from a seed-42 export)
- Modify: `data/source.ts`, `data/staticSource.ts`, `data/hooks.tsx`, `i18n/strings.ts`, `web/src/App.tsx`, `web/src/test/renderWithSource.tsx`, `pages/RequestTracker.tsx` (accepts `initialCaseId?: string` and `onShow?: (caseId) => void`)

**Interfaces:**
- Consumes: owner JSON (Task 5).
- Produces:
  - `type Route = { page: 'track'; caseId?: string } | { page: 'flow'; periodId: string } | { page: 'stage'; periodId: string; stage: StageName } | { page: 'notFound' }`; `parseRoute(hash: string): Route`; `formatRoute(route: Route): string`; `useRoute(): [Route, (r: Route) => void]` (listens to `hashchange`).
  - zod: `periodsViewSchema`, `overviewViewSchema`, `stageDetailViewSchema` (+ types).
  - `FlowSightSource` gains `getPeriods(): Promise<PeriodsView>`, `getOverview(periodId): Promise<OverviewView | null>`, `getStageDetail(periodId, stage): Promise<StageDetailView | null>`; hooks `usePeriods()`, `useOverview(periodId)`, `useStageDetail(periodId, stage)`.
  - `AppShell` renders the header (title, EN/TR toggle, view switch "Requester" / "Process owner") and the page for the route; wraps `I18nProvider`.

- [ ] **Step 1: Write the failing tests** — `routing.test.ts`: `''` → track; `#/track/PR-001783` → track with caseId; `#/flow/last12` → flow; `#/flow/2026-03/invoice` → stage; `parsesUnknownRoutesAsNotFound` (`#/flow/2026-03/shipping`, `#/nope`); `formatRoute(parseRoute(x)) === x` for the valid ones. Schema test: the three fixtures parse.
- [ ] **Step 2: Run** `npm --prefix web run test -- --run`. Expected: FAIL.
- [ ] **Step 3: Implement**; requester tracker keeps all M1 behaviour (its tests must still pass).
- [ ] **Step 4: Run** tests and `npm --prefix web run build`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add routing, owner data layer and app shell"`

---

### Task 7: Flow overview screen

**Files:**
- Create: `web/src/features/flowsight/components/charts/BarChart.tsx`, `components/charts/TrendChart.tsx`, `pages/FlowOverview.tsx`
- Test: `pages/FlowOverview.test.tsx`

**Interfaces:**
- Consumes: hooks and schemas (Task 6). Load the `dataviz` skill first.
- Produces: `FlowOverview({ periodId }: { periodId: string })`; `BarChart` and `TrendChart` (plain SVG, accessible: `role="img"` with an `aria-label` summary and a visually hidden data table).

Screen: period selector (`last12` first, then months newest first) that updates the route; **WIP by stage** bars coloured by pace (normal/faster neutral, slower amber, much slower red, unknown grey) with a legend; **lead-time trend** (median and P90 working days of paid lines per month) with the scenario's change visible as a drop; **bottleneck table** in rank order: stage, share of lead time %, median, P90, WIP, WIP growth (↑/↓), pace label. Clicking a stage row or bar navigates to `#/flow/{periodId}/{stage}`.

- [ ] **Step 1: Write the failing tests**

```tsx
it('lists stages in bottleneck order')                 // overview fixture: first row is the rank-1 stage
it('marks much slower stages')                          // a stage with pace muchSlower has data-pace="muchSlower"
it('ShowsDash_ForMissingMedian')                        // medianDays null → "–"
it('navigates to the stage drill-down')                 // click Invoice row → location.hash === '#/flow/last12/invoice'
it('ShowsNotFound_ForUnknownPeriod')                    // source returns null → not-found text
it('switches labels to Turkish')
```

- [ ] **Step 2: Run** tests. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** tests, build, then open `#/flow/last12` in the dev server and check light/dark and 375 px width (no horizontal page scroll). Expected: PASS; charts readable.
- [ ] **Step 5: Commit** — `git commit -m "Add flow overview screen"`

---

### Task 8: Stage drill-down screen and release

**Files:**
- Create: `web/src/features/flowsight/pages/StageDetail.tsx`; modify `README.md`
- Test: `pages/StageDetail.test.tsx`

**Interfaces:**
- Consumes: `useStageDetail`, `BarChart`, routes.
- Produces: `StageDetail({ periodId, stage })`.

Screen: back link to the overview; headline numbers (median queue, median work, WIP at end); **queue vs work** as a two-part bar with values; **assignment mix** ("shared queue", "routed", "person", "role", "system") with counts — when `groupQueue` dominates show the hint "Work is picked from a shared queue" (no recommendation yet; that is M3); **ageing** buckets; **time per role** table; **time per person** table (pseudonymous ids, with the note "Owner view: individual times are for process improvement, not performance rating"); **slowest open** and **slowest completed** lists whose case ids link to `#/track/{caseId}`.

- [ ] **Step 1: Write the failing tests**

```tsx
it('shows queue and work time')
it('hints at a shared queue when group-queue work dominates')   // stage fixture from 2026-03 invoice
it('links slow cases to the request tracker')
it('ShowsNotFound_ForUnknownStage')
```

- [ ] **Step 2: Run** tests. Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Update README** — status "M2 — flow overview and stage drill-down"; roadmap M2 ✅; one sentence on the synthetic shared-queue scenario.
- [ ] **Step 5: Final checks** — `dotnet test`, `npm --prefix web run test -- --run`, `npm --prefix web run build`, private sensitive-term scan reports nothing. Expected: all green.
- [ ] **Step 6: Commit and tag** — `git commit -m "Add stage drill-down screen"`, `git tag v0.2.0`.

# FlowSight M4 Implementation Plan: did the change work?

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For every dated change in the process map, FlowSight measures the changed step over equal-length windows before and after the change and says, with numbers, whether it improved, stayed the same or got worse — closing the continuous-improvement loop.

**Architecture:** A new `FlowSight.Core.Impact` namespace measures each `MapChange` against the event log (step-level statistics, average waiting items, monthly series). The CLI exports `data/owner/changes.json`. The web app adds a "did it work?" screen with a before/after table and a monthly trend marked at the change, and links improvement cards to the before/after of their step when a change exists.

**Tech Stack:** as M3.

**Spec:** `docs/design/2026-10-20-flowsight-design.md` (M4 = §7 row M4; G6; §6.3 item 7; §6.5 item 5)

## Global Constraints

- Everything in the M1–M3 Global Constraints still applies.
- Statistics follow M2 conventions: inclusive-linear median/P90, working minutes, `null` → "–", never 0.
- Every number on the screen traces to the export; the verdict is a rule, not a judgement call.

## Decisions this plan fixes (builder's calls within the spec)

- **Change instant** = 00:00 of `MapChange.Date` in the log's offset.
- **Window length** (days) = min(**90**, days since the first event, days until as-of, days since the previous change on the same step, days until the next change on the same step). Before = `[change − length, change)`, After = `[change, change + length)`. Length < **14** ⇒ `insufficientData`.
- **Statistics per window**, over events of the changed step's activity **completed** in the window: `Steps`, `MedianStepMinutes` (queued → completed), `P90StepMinutes`, `MedianQueueMinutes` (queued → started; events without a start left out), `AverageWaiting` = mean, over working days in the window, of the number of that step's events open at 12:00 (queued ≤ t and not completed by t).
- **Verdict:** both windows need ≥ **20** steps, else `insufficientData`. Ratio = after median ÷ before median: ≤ **0.8** `improved`, ≥ **1.2** `worse`, otherwise `unchanged`. `MedianChangePercent` = (ratio − 1) × 100, one decimal; null when either median is null.
- **Monthly series:** every full month from `Periods.For(log)` that overlaps `[Before.Start, After.End)`: median step minutes and step count of events completed in that month.
- **Export:** `data/owner/changes.json` = `{ changes: [ … ] }` in map order; each item has `index` (0-based, used in the URL), `date`, `stepId`, `stepName`, `stage`, `description`, `before`/`after` windows, `beforeStats`/`afterStats` in days (`averageWaiting` one decimal), `medianChangePercent`, `verdict`, `monthly`.
- **Routes:** `#/changes` (list; opens the first change) and `#/changes/{index}`.
- **Card link:** a card whose `stepId` has a change gets "This step was changed on {date}: see before and after".

## File Structure

```
src/FlowSight.Core/Impact/ChangeImpact.cs        # records + verdict enum
src/FlowSight.Core/Impact/ChangeImpacts.cs       # Measure(...)
src/FlowSight.Core/Export/OwnerViews.cs          # ChangesView
src/FlowSight.Cli/DemoExporter.cs                # writes owner/changes.json
tests/FlowSight.Tests/ChangeImpactTests.cs  ChangeExportTests.cs
web/src/routing.ts (+test)
web/src/features/flowsight/data/{ownerSchemas,source,staticSource,hooks}
web/src/features/flowsight/components/charts/TrendChart.tsx   # optional marker
web/src/features/flowsight/pages/{AppShell,ImprovementCards,BeforeAfter}.tsx (+ BeforeAfter.test.tsx, ImprovementCards.test.tsx)
web/src/features/flowsight/i18n/strings.ts
web/src/test/fixtures/changes.json  web/src/test/renderWithSource.tsx  web/src/App.tsx
README.md
```

## Review Focus

1. **Change dated before the first event or after as-of** ⇒ `insufficientData`, no exception. → Task 1 test `ChangeOutsideTheData_IsInsufficient`.
2. **Change on a step that never occurs in the data** ⇒ `insufficientData` with zero steps. → Task 1 test `ChangeOnUnusedStep_IsInsufficient`.
3. **Two changes on the same step close together** ⇒ windows stop at the neighbouring change, never straddle it. → Task 1 test `NeighbouringChanges_ClipTheWindow`.
4. **Unknown change index in the URL** ⇒ friendly not-found. → Task 4 test `ShowsNotFound_ForUnknownChange`.
5. **A median missing on one side** ⇒ "–" and no percentage, verdict shown as "not enough data". → Task 4 test `ShowsNotEnoughData`.

---

### Task 1: Measure the impact of map changes

**Files:** create `src/FlowSight.Core/Impact/ChangeImpact.cs`, `ChangeImpacts.cs`; test `tests/FlowSight.Tests/ChangeImpactTests.cs`.

**Interfaces:** Produces `record ImpactWindow(DateTimeOffset Start, DateTimeOffset End)`, `record ImpactStats(int Steps, double? MedianStepMinutes, double? P90StepMinutes, double? MedianQueueMinutes, double AverageWaiting)`, `enum ImpactVerdict { InsufficientData, Improved, Unchanged, Worse }`, `record MonthlyPoint(string PeriodId, double? MedianStepMinutes, int Steps)`, `record ChangeImpact(MapChange Change, string StepId, string StepName, Stage? Stage, ImpactWindow Before, ImpactWindow After, ImpactStats BeforeStats, ImpactStats AfterStats, double? MedianChangePercent, ImpactVerdict Verdict, IReadOnlyList<MonthlyPoint> Monthly)`; `static class ChangeImpacts { IReadOnlyList<ChangeImpact> Measure(EventLog log, ProcessMap map, IWorkCalendar calendar, int maxWindowDays = 90); }`.

- [ ] **Step 1: Write the failing tests** (synthetic default data + sample map unless stated)

```csharp
[Fact] public void InvoiceRouting_IsMeasuredAsImproved()       // one impact; windows 90 days each around 2026-06-03; verdict Improved; MedianChangePercent ≤ -70; AfterStats.AverageWaiting < BeforeStats.AverageWaiting
[Fact] public void MonthlySeries_CoversBothWindows()            // months 2026-03 … 2026-08 present, in order
[Fact] public void UnrelatedStep_IsUnchanged()                  // map with an extra change on create-order dated 2026-06-03 → that impact Unchanged
[Fact] public void ChangeOutsideTheData_IsInsufficient()        // change dated 2025-01-01 and 2027-01-01 → InsufficientData, no exception
[Fact] public void ChangeOnUnusedStep_IsInsufficient()          // change on clarify-spec dated 2026-06-03 in a log without maintenance services → InsufficientData, Steps 0
[Fact] public void NeighbouringChanges_ClipTheWindow()          // second invoice-matching change dated 2026-07-03 → first impact's After ends 2026-07-03 (30 days), second's Before starts 2026-06-03
[Fact] public void NoChanges_NoImpacts()
```

- [ ] **Step 2: Run** `dotnet test --filter ChangeImpactTests`. Expected: FAIL.
- [ ] **Step 3: Implement** per Decisions.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Measure the impact of dated process changes"`

---

### Task 2: Export the change impacts

**Files:** modify `Export/OwnerViews.cs`, `Cli/DemoExporter.cs`; test `ChangeExportTests.cs`.

**Interfaces:** Produces `record ImpactStatsView(int Steps, double? MedianDays, double? P90Days, double? MedianQueueDays, double AverageWaiting)`, `record MonthlyPointView(string PeriodId, double? MedianDays, int Steps)`, `record ChangeImpactView(int Index, DateOnly Date, string StepId, string StepName, string? Stage, string Description, ImpactWindowView Before, ImpactWindowView After, ImpactStatsView BeforeStats, ImpactStatsView AfterStats, double? MedianChangePercent, string Verdict, IReadOnlyList<MonthlyPointView> Monthly)`, `record ImpactWindowView(DateTimeOffset Start, DateTimeOffset End)`, `record ChangesView(IReadOnlyList<ChangeImpactView> Changes)`; `OwnerViews.Changes(IReadOnlyList<ChangeImpact>, int minutesPerDay)`; file `data/owner/changes.json` (replaced on each run).

- [ ] **Step 1: Write the failing tests** — `Demo_WritesChanges` (one change, verdict `improved`, stage `invoice`, `date` `2026-06-03`), `ChangesExport_IsDeterministic`, `ChangesExport_HasNoPersonIds`.
- [ ] **Step 2–4:** run (FAIL) → implement → run `dotnet test` (PASS), regenerate `web/public/data`.
- [ ] **Step 5: Commit** — `git commit -m "Export change impacts"`

---

### Task 3: Web data layer, routes and navigation

**Files:** modify `routing.ts` (+tests), `data/ownerSchemas.ts`, `data/source.ts`, `data/staticSource.ts`, `data/hooks.tsx`, `pages/AppShell.tsx` (fourth item "Process owner: did it work?"), `test/renderWithSource.tsx`, `i18n/strings.ts`; create `test/fixtures/changes.json`.

**Interfaces:** `{ page: 'changes'; index?: number }` ↔ `#/changes[/{index}]` (non-negative integer, else not found); `changesViewSchema`; `FlowSightSource.getChanges(): Promise<ChangesView>`; `useChanges()`.

- [ ] **Step 1: Write the failing tests** — routing parse/format/round-trip for change routes, `#/changes/x` → notFound; schema parses the fixture.
- [ ] **Step 2–4:** run → implement → run tests and build.
- [ ] **Step 5: Commit** — `git commit -m "Add change-impact data layer and routes"`

---

### Task 4: Before/after screen and card link

**Files:** create `pages/BeforeAfter.tsx`, `pages/BeforeAfter.test.tsx`; modify `components/charts/TrendChart.tsx` (optional `marker?: { index: number; label: string }` drawn as a labelled vertical rule), `pages/ImprovementCards.tsx` (+test), `App.tsx`, `i18n/strings.ts`, `flowsight.css`.

Screen: list of changes (date, step, description, verdict with icon + label: ▼ improved, ● unchanged, ▲ worse, ○ not enough data); detail: verdict sentence ("Median time in the step fell by 85.4% — improved." / "rose by …" / "barely changed" / "Not enough data to judge"), the windows ("90 days before vs 90 days after 3 Jun 2026"), a before/after table (steps, median, P90, median waiting before pick-up, average waiting items, change), and the monthly trend of the step's median with the change marked; link to the step's stage drill-down for the latest month.

- [ ] **Step 1: Write the failing tests** — `lists changes with their verdict`, `shows before and after with the change`, `ShowsNotEnoughData` (fixture variant with null medians and verdict `insufficientData`), `ShowsNotFound_ForUnknownChange`, `switches to Turkish`, and in `ImprovementCards.test.tsx` `links a card to the before/after of its step` (card with stepId `invoice-matching` → link `#/changes/0`).
- [ ] **Step 2–4:** run → implement → run tests, build; check the screen in the dev server (light/dark, 375 px).
- [ ] **Step 5: Commit** — `git commit -m "Add before/after screen"`

---

### Task 5: Docs and release

- [ ] **Step 1: Update README** — status M4; a short "Closing the loop" section (how changes are recorded in the map and how before/after is judged, with the thresholds); roadmap M4 ✅.
- [ ] **Step 2: Final checks** — `dotnet test`, web tests, build, private sensitive-term scan reports nothing.
- [ ] **Step 3: Commit and tag** — `git commit -m "Document M4"`, `git tag v0.4.0`.

# FlowSight M1 Implementation Plan: Request tracker on synthetic data

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A requester can type a request number and see where it is in the 7-stage purchase-to-pay flow and how long each stage took, computed from a deterministic synthetic dataset.

**Architecture:** A dependency-free .NET 8 engine (`FlowSight.Core`) turns an event log into line and request journeys measured in working minutes. A synthetic generator (`FlowSight.Synthetic`) produces the event log; a CLI exports the journeys as static JSON; a React feature folder reads that JSON through a swappable data source and renders the request tracker.

**Tech Stack:** .NET 8, xUnit, System.Text.Json · React 18+, Vite, TypeScript, TanStack Query, zod, Vitest, Testing Library

**Spec:** `docs/design/2026-10-20-flowsight-design.md` (M1 = §7 row M1; model §6.2; outputs §6.3 item 1; synthetic §6.4 without scenarios; screen §6.5 item 1)

## Global Constraints

- Target framework `net8.0` for every .NET project; `Nullable` and `ImplicitUsings` enabled; warnings as errors.
- `FlowSight.Core` references no ASP.NET, database or file-system packages; inputs only through `IEventSource` and `IWorkCalendar`.
- Repository language English; UI strings in English and Turkish.
- Durations computed in **working minutes**, shown as working days (= minutes ÷ 540, one decimal) and calendar days.
- Synthetic data is deterministic: same seed ⇒ byte-identical export.
- No company names, system names, people, internal codes, real thresholds or distributions anywhere (code, data, docs, commit messages). Sites, roles, categories and actor ids are invented (`Site North`, `A-07`, …).
- Requester-facing JSON and screens never contain actor ids, person names or roles of individual actors.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Decisions this plan fixes (builder's calls within the spec)

- **Scope of paths:** M1 tracks purchase requests on two paths, `Goods` and `Service`; every line passes all 7 stages. Fulfilment from stock is out of scope of the 7-stage tracker.
- **Working day:** 08:00–17:00 local time (540 minutes), no lunch deduction. Calendar offset `+03:00`. Default non-working day: Sunday.
- **Stage duration:** the **union** of the time intervals `[QueuedAt, CompletedAt ?? AsOf]` of all events in that stage, measured in working minutes. Overlapping or repeated (rework) events are never double counted.
- **Similar requests:** same `Path` and `ItemCategory`; comparisons use **completed** lines only; fewer than 10 similar lines ⇒ no comparison.
- **Static data layout:** `data/meta.json`, `data/requests/index.json`, `data/requests/{caseId}.json`.

## File Structure

```
FlowSight/
  Directory.Build.props                 # net8.0, nullable, implicit usings, warnings as errors
  FlowSight.slnx
  src/FlowSight.Core/
    Model/Stage.cs                      # enum Stage + StageOrder helpers
    Model/EventLog.cs                   # StepEvent, RequestLine, EventLog, enums
    Calendar/CalendarRules.cs           # CalendarRules, Holiday
    Calendar/WorkCalendar.cs            # IWorkCalendar + WorkCalendar
    Journeys/JourneyModels.cs           # StageSpan, LineJourney, RequestJourney, enums
    Journeys/JourneyBuilder.cs          # event log -> line journeys
    Journeys/RequestAggregator.cs       # line journeys -> request journeys
    Journeys/Similarity.cs              # percentile rank, typical stage duration
    Export/RequestView.cs               # requester-facing JSON contract
    Export/RequestViewBuilder.cs        # journeys -> RequestView
    Abstractions.cs                     # IEventSource
  src/FlowSight.Synthetic/
    SyntheticOptions.cs
    Catalog.cs                          # invented sites, roles, actors, categories, activities
    Generator.cs                        # deterministic P2P event log
  src/FlowSight.Cli/
    Program.cs                          # `demo` command
    DemoExporter.cs                     # writes data/*.json
  tests/FlowSight.Tests/
    CalendarTests.cs  JourneyBuilderTests.cs  RequestAggregatorTests.cs
    SimilarityTests.cs  GeneratorTests.cs  ExportTests.cs  TestCalendar.cs
  web/
    package.json  vite.config.ts  tsconfig.json  index.html
    src/main.tsx  src/App.tsx
    src/features/flowsight/
      data/schemas.ts  data/source.ts  data/staticSource.ts  data/hooks.ts
      i18n/strings.ts
      components/StageStrip.tsx  components/StageStrip.test.tsx
      pages/RequestTracker.tsx   pages/RequestTracker.test.tsx
    src/test/fixtures/request.json
  README.md
```

## Review Focus

1. **Timestamps outside working hours** (completed on a Sunday night, queued before 08:00): working minutes count only the working part and are never negative. → Task 1 tests.
2. **Rework and parallel steps in one stage** (an approval returned and re-approved, two approvers at once): stage time is the union of intervals, not the sum. → Task 2 test `RepeatedStage_IsUnionNotSum`.
3. **Events after the as-of date** in source data: ignored if queued after as-of; treated as still open if completed after as-of. → Task 2 test `EventsAfterAsOf_AreCutOff`.
4. **Unknown or malformed request number** typed into the tracker: friendly "not found" message, no crash, no blank screen. → Task 7 test `ShowsNotFound_ForUnknownRequest`.
5. **Too few similar requests**: no "slower than X %" claim at all (not "0 %"). → Task 3 test `PercentileRank_NullBelowTenSamples`, Task 7 test `HidesComparison_WhenNull`.

---

### Task 1: Solution scaffold and working-time calendar

**Files:**
- Create: `Directory.Build.props`, `FlowSight.slnx`, `src/FlowSight.Core/FlowSight.Core.csproj`, `src/FlowSight.Core/Calendar/CalendarRules.cs`, `src/FlowSight.Core/Calendar/WorkCalendar.cs`, `tests/FlowSight.Tests/FlowSight.Tests.csproj`, `tests/FlowSight.Tests/TestCalendar.cs`, `.gitignore` (`dotnet new gitignore` + `node_modules/`, `web/dist/`, `web/public/data/`)
- Test: `tests/FlowSight.Tests/CalendarTests.cs`

**Interfaces:**
- Produces:
  - `record Holiday(DateOnly Date, TimeOnly? OffFrom = null)` — `OffFrom == null` ⇒ whole day off; otherwise off from that time.
  - `record CalendarRules(TimeOnly DayStart, TimeOnly DayEnd, IReadOnlySet<DayOfWeek> NonWorkingDays, IReadOnlyList<Holiday> Holidays, TimeSpan UtcOffset)` with `static CalendarRules Default` = 08:00, 17:00, {Sunday}, no holidays, +03:00; and `int MinutesPerDay` (= 540 for default).
  - `interface IWorkCalendar { double WorkingMinutes(DateTimeOffset from, DateTimeOffset to, string? siteId = null); DateTimeOffset AddWorkingMinutes(DateTimeOffset from, double minutes, string? siteId = null); int MinutesPerDay { get; } }`
  - `class WorkCalendar(CalendarRules rules, IReadOnlyDictionary<string, CalendarRules>? siteOverrides = null) : IWorkCalendar`
  - `TestCalendar.Create()` (tests only): Default rules + holidays `2026-10-29` (whole day) and `2026-10-28` off from 13:00; site override `"S2"` with Saturday and Sunday off.

- [ ] **Step 1: Scaffold** — `dotnet new sln -n FlowSight`, `classlib` Core and `xunit` Tests projects (net8.0), test project references Core, `Directory.Build.props` per Global Constraints. Run `dotnet build`; expected: success, 0 warnings.

- [ ] **Step 2: Write the failing tests** in `CalendarTests.cs` (all times `+03:00` unless stated):

```csharp
[Theory]
[InlineData("2026-10-19T09:00+03:00", "2026-10-19T10:30+03:00", 90)]   // Monday, inside hours
[InlineData("2026-10-24T16:00+03:00", "2026-10-26T09:00+03:00", 120)]  // Sat 16-17 + Mon 08-09, Sunday off
[InlineData("2026-10-27T16:00+03:00", "2026-10-30T09:00+03:00", 420)]  // Tue 60 + Wed half-day 300 + Thu holiday 0 + Fri 60
[InlineData("2026-10-25T10:00+03:00", "2026-10-25T15:00+03:00", 0)]    // Sunday
[InlineData("2026-10-19T06:00+03:00", "2026-10-19T08:30+03:00", 30)]   // before start of day
[InlineData("2026-10-19T12:00+03:00", "2026-10-19T09:00+03:00", 0)]    // to before from
public void WorkingMinutes_CountsOnlyWorkingTime(string from, string to, double expected)

[Fact] public void WorkingMinutes_NormalisesOtherOffsets()
// 2026-10-19T06:00Z .. 2026-10-19T07:30Z == 90

[Fact] public void WorkingMinutes_UsesSiteOverride()
// Sat 2026-10-24 10:00..12:00: default 120, site "S2" 0

[Theory]
[InlineData("2026-10-24T16:30+03:00", 60, "2026-10-26T08:30+03:00")]   // 30 on Sat, 30 on Mon
[InlineData("2026-10-25T12:00+03:00", 10, "2026-10-26T08:10+03:00")]   // starts on Sunday
[InlineData("2026-10-28T12:00+03:00", 120, "2026-10-30T09:00+03:00")]  // 60 Wed until 13:00, Thu holiday, 60 Fri
public void AddWorkingMinutes_SkipsNonWorkingTime(string from, double minutes, string expected)

[Fact] public void AddWorkingMinutes_IsInverseOfWorkingMinutes()
// for from = Mon 2026-10-19 09:00, minutes in {1, 539, 540, 2000}: WorkingMinutes(from, Add(from, m)) == m
```

- [ ] **Step 3: Run** `dotnet test --filter CalendarTests`. Expected: FAIL (types missing).

- [ ] **Step 4: Implement** `CalendarRules`, `Holiday`, `IWorkCalendar`, `WorkCalendar`. Convert inputs to the rules' `UtcOffset`, walk day by day, intersect `[from, to]` with each day's working window (`DayStart`–`DayEnd`, cut at `OffFrom` for half-day holidays, empty for non-working days and whole-day holidays). `AddWorkingMinutes` walks forward the same way and lands on the exact minute; when it lands exactly on `DayEnd`, return `DayEnd` of that day.

- [ ] **Step 5: Run** `dotnet test --filter CalendarTests`. Expected: PASS.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "Add solution scaffold and working-time calendar"`

---

### Task 2: Event model and line journeys

**Files:**
- Create: `src/FlowSight.Core/Model/Stage.cs`, `src/FlowSight.Core/Model/EventLog.cs`, `src/FlowSight.Core/Abstractions.cs`, `src/FlowSight.Core/Journeys/JourneyModels.cs`, `src/FlowSight.Core/Journeys/JourneyBuilder.cs`
- Test: `tests/FlowSight.Tests/JourneyBuilderTests.cs`

**Interfaces:**
- Consumes: `IWorkCalendar` (Task 1).
- Produces:
  - `enum Stage { Request = 1, Approval, Purchasing, Order, GoodsReceipt, Invoice, Payment }`; `static class Stages { IReadOnlyList<Stage> All; }` in order.
  - `enum AssignmentKind { Person, Role, GroupQueue, FieldRouted, System }`; `enum StepOutcome { Approved, Rejected, Returned, Cancelled, Done }`; `enum LinePath { Goods, Service }`
  - `record StepEvent(string CaseId, string LineId, Stage Stage, string Activity, string ActorId, string Role, AssignmentKind Assignment, DateTimeOffset QueuedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, StepOutcome? Outcome, int? CandidateCount = null)`
  - `record RequestLine(string CaseId, string LineId, string SiteId, string ItemCategory, LinePath Path, decimal Quantity, decimal Amount, string Currency)`
  - `record EventLog(IReadOnlyList<RequestLine> Lines, IReadOnlyList<StepEvent> Events, DateTimeOffset AsOf)`
  - `interface IEventSource { EventLog Load(); }`
  - `enum StageState { NotStarted, InProgress, Completed }`; `enum LineStatus { Open, Completed, Rejected, Cancelled }`
  - `record StageSpan(Stage Stage, StageState State, DateTimeOffset? EnteredAt, DateTimeOffset? LeftAt, double WorkingMinutes, double CalendarDays)`
  - `record LineJourney(RequestLine Line, IReadOnlyList<StageSpan> Stages, Stage? CurrentStage, LineStatus Status, double TotalWorkingMinutes, double TotalCalendarDays)` — `Stages` always has 7 entries in stage order.
  - `static class JourneyBuilder { IReadOnlyList<LineJourney> Build(EventLog log, IWorkCalendar calendar); }`

Rules (from the plan's Decisions): events with `QueuedAt > AsOf` are dropped; `StartedAt`/`CompletedAt` after `AsOf` become `null`. A stage is `Completed` when all its events are completed and (a later stage has events or the line is terminal); `InProgress` when any event is open; `NotStarted` when it has no events. Stage `WorkingMinutes` = working minutes of the union of `[QueuedAt, CompletedAt ?? AsOf]`. `EnteredAt` = earliest `QueuedAt`; `LeftAt` = latest `CompletedAt` when `Completed`. Status: `Rejected`/`Cancelled` when any event has that outcome (then `CurrentStage = null`); `Completed` when a `Payment` event has outcome `Done` and is completed; otherwise `Open` with `CurrentStage` = latest stage that has an open event, or else the latest stage reached. Totals run from the line's first `QueuedAt` to its terminal `CompletedAt` (or `AsOf`). `CalendarDays` = elapsed calendar time in days.

- [ ] **Step 1: Write the failing tests** (`TestCalendar.Create()`, fixture helper `Ev(stage, queued, completed, outcome)` for line `R1/L1`):

```csharp
[Fact] public void OpenLine_ReportsCurrentStageAndTimeToAsOf()
// Request  Mon 19 08:00 → Mon 19 17:00 (Done)      → Completed, 540
// Approval Tue 20 08:00 → Tue 20 12:00 (Approved)  → Completed, 240
// Purchasing Tue 20 12:00 → open; AsOf Thu 22 12:00 → InProgress, 1080 (300 + 540 + 240)
// Order..Payment NotStarted, 0; Status Open; CurrentStage Purchasing; TotalWorkingMinutes 1860

[Fact] public void RepeatedStage_IsUnionNotSum()
// two Approval events same Monday: 08:00–12:00 and 10:00–14:00 → Approval WorkingMinutes 360

[Fact] public void RejectedLine_HasNoCurrentStage()
// Approval outcome Rejected → Status Rejected, CurrentStage null, Approval Completed

[Fact] public void PaidLine_IsCompleted()
// all 7 stages, Payment outcome Done → Status Completed, CurrentStage null, every stage Completed

[Fact] public void EventsAfterAsOf_AreCutOff()
// AsOf Mon 19 12:00; Request queued 08:00 completed 15:00; Approval queued Tue 08:00
// → Request InProgress with 240 minutes; Approval NotStarted; CurrentStage Request
```

- [ ] **Step 2: Run** `dotnet test --filter JourneyBuilderTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the model types and `JourneyBuilder.Build` per the rules above. Merge intervals by sorting on start and folding overlaps before calling `WorkingMinutes`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS (all tests).
- [ ] **Step 5: Commit** — `git commit -m "Add event model and line journeys"`

---

### Task 3: Request journeys and comparison with similar requests

**Files:**
- Create: `src/FlowSight.Core/Journeys/RequestAggregator.cs`, `src/FlowSight.Core/Journeys/Similarity.cs` (+ `RequestJourney` in `JourneyModels.cs`)
- Test: `tests/FlowSight.Tests/RequestAggregatorTests.cs`, `tests/FlowSight.Tests/SimilarityTests.cs`

**Interfaces:**
- Consumes: `LineJourney`, `StageSpan`, `Stage`, `LineStatus` (Task 2).
- Produces:
  - `enum RequestStatus { Open, Completed, Rejected, Cancelled }`
  - `record RequestJourney(string CaseId, IReadOnlyList<LineJourney> Lines, IReadOnlyList<StageSpan> Stages, Stage? CurrentStage, RequestStatus Status, double TotalWorkingMinutes)`
  - `static class RequestAggregator { IReadOnlyList<RequestJourney> Build(IReadOnlyList<LineJourney> lines); }`
  - `static class Similarity { double? PercentileRank(double value, IReadOnlyList<double> population); double? TypicalStageMinutes(Stage stage, LineJourney line, IReadOnlyList<LineJourney> all); double? SlowerThanPercent(LineJourney line, IReadOnlyList<LineJourney> all); }`

Rules: request `CurrentStage` = least advanced `CurrentStage` among `Open` lines. Status: `Open` if any line open; else `Completed` if any line completed; else `Rejected` if any rejected; else `Cancelled`. Request-level `StageSpan` per stage: `EnteredAt` = earliest line entry, `LeftAt` = latest line exit, `State` = `InProgress` if any open line is in it, `Completed` if every non-rejected/non-cancelled line completed it, else `NotStarted`; `WorkingMinutes` = max over lines. `TotalWorkingMinutes` = max over lines. Similar lines = same `Path` and `ItemCategory`, `Status == Completed`, excluding the line itself. `PercentileRank` = 100 × (count strictly below value) ÷ count, rounded to one decimal; `null` when count < 10. `TypicalStageMinutes` = median of that stage's minutes over similar lines; `null` when < 10. `SlowerThanPercent` = `PercentileRank(line.TotalWorkingMinutes, similar totals)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// RequestAggregatorTests
[Fact] public void CurrentStage_IsLeastAdvancedOpenLine()          // L1 Purchasing, L2 Invoice → Purchasing
[Fact] public void RejectedLine_DoesNotHoldBackRequest()           // L1 Rejected, L2 Invoice → Invoice, Open
[Fact] public void AllLinesPaid_RequestCompleted()                  // → Completed, CurrentStage null
[Fact] public void RequestStages_AlwaysSevenInOrder()

// SimilarityTests
[Fact] public void PercentileRank_CountsStrictlyBelow()             // pop 100,200,…,1000; value 850 → 80.0
[Fact] public void PercentileRank_NullBelowTenSamples()             // pop of 9 → null
[Fact] public void TypicalStageMinutes_IsMedianOfCompletedSimilar() // 10 similar lines, Invoice minutes 100..1000 → 550
[Fact] public void Similarity_IgnoresOtherCategoriesAndOpenLines()
```

- [ ] **Step 2: Run** `dotnet test --filter "RequestAggregatorTests|SimilarityTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** per the rules.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add request journeys and similar-request comparison"`

---

### Task 4: Deterministic synthetic generator

**Files:**
- Create: `src/FlowSight.Synthetic/FlowSight.Synthetic.csproj` (refs Core), `SyntheticOptions.cs`, `Catalog.cs`, `Generator.cs`
- Test: `tests/FlowSight.Tests/GeneratorTests.cs` (test project refs Synthetic)

**Interfaces:**
- Consumes: model types (Task 2), `IWorkCalendar` (Task 1).
- Produces:
  - `record SyntheticOptions(int Seed = 42, int Requests = 2000, DateOnly Start = 2025-11-03, int Months = 12, DateTimeOffset? AsOf = null)` — `AsOf` default = 08:00 on the first working day after `Start + Months`.
  - `static class Generator { EventLog Generate(SyntheticOptions options, IWorkCalendar calendar); }`
  - `class SyntheticEventSource(SyntheticOptions o, IWorkCalendar c) : IEventSource`

Content (all invented): 3 sites (`Site North`, `Site South`, `Site East`); roles per stage — Request: `Requester`; Approval: `Line manager`, `Budget owner`; Purchasing: `Buyer`; Order: `Buyer`; GoodsReceipt: `Warehouse clerk`; Invoice: `Accounts payable`; Payment: `Treasury`; ~60 actors `A-01`…`A-60` spread over sites and roles; categories `Spare parts`, `Consumables`, `Safety equipment`, `IT hardware` (Goods) and `Maintenance service`, `Transport service` (Service); 1–4 lines per request; ~80 % Goods. Requests arrive at random working minutes across the period. Each step's duration = queue + work in working minutes drawn from a log-normal per stage (pick medians that look plausible; document them in `Catalog.cs` as invented). Times are placed with `calendar.AddWorkingMinutes`, so every timestamp falls inside working hours. Outcomes: ~4 % of lines rejected at Approval, ~2 % cancelled at Purchasing. Everything after `AsOf` is cut off exactly as `JourneyBuilder` expects (events queued after `AsOf` not emitted; later completions emitted as open). Use one `System.Random(seed)` and fixed iteration order; never iterate a `HashSet`/`Dictionary` when drawing.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SameSeed_ProducesIdenticalLog()        // JSON of two runs with seed 42 is equal
[Fact] public void DifferentSeed_ProducesDifferentLog()
[Fact] public void GeneratesRequestedNumberOfRequests()   // Requests = 300 → 300 distinct CaseIds
[Fact] public void AllTimestamps_FallInWorkingTime()      // every QueuedAt/CompletedAt: WorkingMinutes(t, t+1min) == 1 or t is DayEnd
[Fact] public void StagesFollowInOrderPerLine()           // first QueuedAt of stage n ≥ last CompletedAt of stage n-1
[Fact] public void NothingAfterAsOf()                     // no QueuedAt/CompletedAt > AsOf
[Fact] public void HasRealisticMix()                      // 2000 requests: rejected lines 2–6 %, open lines > 0, both paths present
```

- [ ] **Step 2: Run** `dotnet test --filter GeneratorTests`. Expected: FAIL.
- [ ] **Step 3: Implement** per the content above.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add deterministic synthetic purchase-to-pay generator"`

---

### Task 5: Requester JSON contract and `demo` CLI

**Files:**
- Create: `src/FlowSight.Core/Export/RequestView.cs`, `src/FlowSight.Core/Export/RequestViewBuilder.cs`, `src/FlowSight.Cli/FlowSight.Cli.csproj` (console, refs Core + Synthetic), `src/FlowSight.Cli/Program.cs`, `src/FlowSight.Cli/DemoExporter.cs`
- Test: `tests/FlowSight.Tests/ExportTests.cs`

**Interfaces:**
- Consumes: `RequestJourney`, `Similarity` (Task 3), `Generator` (Task 4).
- Produces the JSON contract (System.Text.Json, camelCase, enums as camelCase strings, `null`s written):
  - `record StageView(string Stage, string State, DateTimeOffset? EnteredAt, DateTimeOffset? LeftAt, double WorkingDays, double CalendarDays, double? TypicalWorkingDays)`
  - `record LineView(string LineId, string ItemCategory, string Path, string Status, string? CurrentStage, IReadOnlyList<StageView> Stages)`
  - `record RequestView(string CaseId, string Status, string? CurrentStage, double TotalWorkingDays, double? SlowerThanPercent, IReadOnlyList<StageView> Stages, IReadOnlyList<LineView> Lines, DateTimeOffset AsOf)`
  - `record RequestIndexEntry(string CaseId, string Status, string? CurrentStage)`; `record DemoMeta(DateTimeOffset AsOf, int Seed, int Requests, int MinutesPerDay, IReadOnlyList<string> Stages)`
  - `static class RequestViewBuilder { RequestView Build(RequestJourney request, IReadOnlyList<LineJourney> allLines, int minutesPerDay); }` — days = minutes ÷ `minutesPerDay`, rounded to 1 decimal; request-level `TypicalWorkingDays` and `SlowerThanPercent` come from the request's slowest open line (or slowest line when closed).
  - `static class JsonDefaults { JsonSerializerOptions Options; }`
  - CLI: `flowsight demo --out <dir> [--seed 42] [--requests 2000]` writes `<dir>/meta.json`, `<dir>/requests/index.json`, `<dir>/requests/{caseId}.json`. Case ids look like `PR-000123`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void RequestView_HasNoActorInformation()    // serialised JSON contains neither "actorId" nor "role" nor "A-"
[Fact] public void RequestView_ConvertsMinutesToDays()     // 1080 minutes → 2.0 working days (1080/540)
[Fact] public void RequestView_UsesCamelCaseAndStringEnums()   // contains "currentStage":"purchasing"
[Fact] public void Demo_WritesMetaIndexAndOneFilePerRequest()  // DemoExporter.Run(tempDir, seed 42, requests 50) → meta.json, index.json with 50 entries, 50 request files
[Fact] public void Demo_IsByteIdenticalForSameSeed()       // two runs → identical file hashes
```

- [ ] **Step 2: Run** `dotnet test --filter ExportTests`. Expected: FAIL.
- [ ] **Step 3: Implement** builder, exporter (`DemoExporter.Run(string outDir, int seed, int requests)`) and argument parsing in `Program.cs` (no CLI library; exit code 1 with usage on bad arguments).
- [ ] **Step 4: Run** `dotnet test`, then `dotnet run --project src/FlowSight.Cli -- demo --out web/public/data`. Expected: tests PASS; folder contains `meta.json`, `requests/index.json`, 2000 request files.
- [ ] **Step 5: Commit** — `git commit -m "Add requester JSON contract and demo exporter"` (generated `web/public/data/` stays ignored).

---

### Task 6: Web scaffold and data layer

**Files:**
- Create: `web/` via `npm create vite@latest web -- --template react-ts`, then add `@tanstack/react-query`, `zod`, dev: `vitest`, `@testing-library/react`, `@testing-library/jest-dom`, `jsdom`
- Create: `web/src/features/flowsight/data/schemas.ts`, `data/source.ts`, `data/staticSource.ts`, `data/hooks.ts`, `i18n/strings.ts`, `web/src/test/fixtures/request.json` (copied from a real export of seed 42)
- Test: `web/src/features/flowsight/data/schemas.test.ts`

**Interfaces:**
- Consumes: JSON contract (Task 5).
- Produces:
  - zod: `stageViewSchema`, `lineViewSchema`, `requestViewSchema`, `requestIndexSchema`, `demoMetaSchema`; types `RequestView`, `StageView`, … via `z.infer`.
  - `interface FlowSightSource { getMeta(): Promise<DemoMeta>; getIndex(): Promise<RequestIndexEntry[]>; getRequest(caseId: string): Promise<RequestView | null> }` — `null` for unknown id (HTTP 404).
  - `createStaticSource(baseUrl: string): FlowSightSource` — reads `${baseUrl}data/...`, validates with zod.
  - `FlowSightProvider({ source, children })`, hooks `useMeta()`, `useRequestIndex()`, `useRequest(caseId: string | null)`.
  - `strings: Record<'en' | 'tr', Record<StringKey, string>>`, `useStrings()` with language from `localStorage` key `flowsight.lang` (wrapped in try/catch), default `en`. Stage names: en `Request, Approval, Purchasing, Order, Goods receipt, Invoice, Payment`; tr `Talep, Onay, Satın alma, Sipariş, Mal kabul, Fatura, Ödeme`.

- [ ] **Step 1: Write the failing test** — `schemas.test.ts`: fixture parses with `requestViewSchema`; a copy with `stages` of length 6 fails; a copy with an extra `actorId` field is stripped (schemas use `.strip()`).
- [ ] **Step 2: Run** `npm --prefix web run test -- --run`. Expected: FAIL.
- [ ] **Step 3: Implement** schemas (`stages` length exactly 7), sources, hooks, strings.
- [ ] **Step 4: Run** `npm --prefix web run test -- --run` and `npm --prefix web run build`. Expected: PASS; build succeeds.
- [ ] **Step 5: Commit** — `git commit -m "Add web scaffold and FlowSight data layer"`

---

### Task 7: Request tracker screen

**Files:**
- Create: `web/src/features/flowsight/components/StageStrip.tsx`, `web/src/features/flowsight/pages/RequestTracker.tsx`, update `web/src/App.tsx`, `web/src/main.tsx`, `README.md`
- Test: `components/StageStrip.test.tsx`, `pages/RequestTracker.test.tsx`

**Interfaces:**
- Consumes: hooks, schemas, strings (Task 6).
- Produces: `StageStrip({ stages, currentStage }: { stages: StageView[]; currentStage: string | null })`; `RequestTracker()` page.

Screen (chosen layout A in the design mockup): search field for a request number (trimmed, case-insensitive, `PR-` prefix optional) and a short list of example open requests from the index; header with request number, status and "in <stage> for N working days"; the 7-segment strip (completed = green, in progress = amber, or red when its working days exceed 2 × `typicalWorkingDays`, not started = grey) with working days under each label; line "Slower than X % of similar requests" only when `slowerThanPercent` is not null; when lines diverge (distinct `currentStage` among lines), a compact table of lines with their own current stage. Language toggle EN/TR in the header. Light and dark themes via CSS variables. No actor or role data anywhere.

- [ ] **Step 1: Write the failing tests**

```tsx
// StageStrip.test.tsx
it('renders seven stages in order with working days')
it('marks the current stage as in progress')
it('marks a stage red when it exceeds twice its typical duration')
// RequestTracker.test.tsx (with an in-memory FlowSightSource)
it('shows the strip for a known request')                 // typing "pr-000123" finds PR-000123
it('ShowsNotFound_ForUnknownRequest')                     // "PR-999999" → not-found text, no crash
it('HidesComparison_WhenNull')                            // slowerThanPercent null → no "Slower than" text
it('switches stage names to Turkish')                      // toggle TR → "Satın alma" visible
```

- [ ] **Step 2: Run** `npm --prefix web run test -- --run`. Expected: FAIL.
- [ ] **Step 3: Implement** components and page; `App.tsx` wires `FlowSightProvider` with `createStaticSource(import.meta.env.BASE_URL)`.
- [ ] **Step 4: Run** tests, then `dotnet run --project src/FlowSight.Cli -- demo --out web/public/data` and `npm --prefix web run dev`; open the page, search an example request. Expected: tests PASS; strip shows real synthetic data in EN and TR.
- [ ] **Step 5: Write `README.md`** — one paragraph on what FlowSight is, "Status: M1 — request tracker", how to run the two commands above. No mention of any company or internal system.
- [ ] **Step 6: Final checks** — `dotnet test`, `npm --prefix web run test -- --run`, `npm --prefix web run build`; then run the private sensitive-term scan (term list kept outside this repository); it must report nothing.
- [ ] **Step 7: Commit and tag** — `git commit -m "Add request tracker screen"` then `git tag v0.1.0`.

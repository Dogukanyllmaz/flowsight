# FlowSight: Design

*Status: draft for approval · 2026-10-20*

## 1. Intent

FlowSight follows every purchase request from the moment it is opened until it is paid:
**Request → Approval → Purchasing → Order → Goods receipt → Invoice → Payment.**

It answers two different questions for two different audiences:

| Audience | Question | What they see |
|---|---|---|
| Requester, line manager, director | "Where is my request?" | Current stage and how long it has been there. **No person names.** |
| Process owner / internal auditor | "Where does the process get stuck, why, and what should we change?" | Bottlenecks, time per stage, **per role and per person**, waiting reasons, improvement proposals, before/after results |

It is built on the **continuous-improvement loop**: *measure → find the bottleneck → explain the cause
from the process map → propose a countermeasure → re-measure after the change.*

### Why

The author owns process development and internal audit for a multi-site industrial company. Today,
answering "where is this request and why is it waiting" means writing SQL, running scripts and copying
results into a report by hand. Bottlenecks found this way have causes that the data alone does not show.

Real example that shaped the design: an invoice step took ~10 working days. The data only showed the delay.
The process map showed the cause: invoices went to a **shared queue** of all warehouse managers, and each
manager had to search the pile for their own warehouse's invoices. The fix was to **route each invoice to the
responsible warehouse directly**. FlowSight must reach this kind of conclusion: symptom from data + structure
from the map → known pattern → countermeasure.

### Two lives of the same code

1. **Now:** a public showcase on GitHub with a **live demo** running on synthetic data.
2. **Later:** a module inside the author's internal end-to-end platform, reading real workflow and ERP data.
   That platform is confidential and **is never named or described in this repository.**

The public version must therefore be real, not a mock-up: the same engine will run on real data later.

## 2. Goals

- G1. Reconstruct each request's end-to-end journey across the 7 stages, line by line, with the time spent in each stage measured in **working days** and **calendar days**.
- G2. Requester view: stage strip for a single request, current stage, time in stage, comparison with similar requests (e.g. "slower than 85 % of similar requests").
- G3. Process-owner view: where open requests pile up (WIP per stage), which stages are slower than normal, time per role, ageing of open items.
- G4. Explain *why* a request or a stage is waiting, using explicit rules (e.g. invoice amount differs from order, partial delivery, approver absent without delegation). When no rule matches: "reason unknown", never a guess.
- G5. Detect bottleneck **patterns** by combining data symptoms with the process map, and produce an **improvement card** (current measurement, root cause, proposed change, expected effect).
- G6. Re-measure after a change: compare a "before" and an "after" period for the same stage and show whether the improvement held.
- G7. Live demo on GitHub Pages, always available, no server cost.
- G8. Drop-in path to the internal platform: same technology, engine with no host dependencies, data sources behind an interface.

## 3. Non-goals

- Not a workflow engine. FlowSight reads history; it never routes or approves anything.
- Not a general BI or dashboard builder. Screens are purpose-built for the questions above.
- No real data connectors in the public repo (workflow platform, ERP, internal platform). They live privately.
- No authentication, user management or multi-tenancy in the demo.
- No machine-learning predictions in v1. Every finding must be explainable by a rule.

## 4. Anti-goals (failure even if it "works") · *builder's assumptions, to be confirmed*

- It becomes a **blame tool**: requesters or managers can see who is personally slow. (The process owner *does* see per-person time; that is a deliberate audit view, not a public one. In the demo, persons are pseudonymous ids.)
- Suggestions are **not explainable**: a card that cannot say which data and which map element led to it.
- The demo data or wording **resembles the real company**: real role names, thresholds, distributions, site names or process codes.
- It looks good on GitHub but **cannot be plugged into the internal platform** without a rewrite.
- It reports averages that hide the problem (e.g. mean instead of median/P90, calendar days only, closed items only).

## 5. Constraints

- **Confidentiality:** no company names, system names, server names, passwords, people, internal codes or real figures anywhere in the repo, its history, the demo data or the docs. A secret/sensitive-term scan runs in CI and before every release.
- **Technology matches the internal platform:** .NET 8 (ASP.NET Core, EF Core-friendly), React + Vite + TypeScript, TanStack Query, zod. xUnit for tests.
- **Repository language:** English (code, docs, UI default). UI ships with English and Turkish strings.
- **Working-time calendar:** durations are computed in **working minutes** (and shown as working days), plus calendar days. Weekend days, public holidays and half-day holidays (e.g. eve of a holiday from 13:00) are configurable. *Builder's call:* one company calendar with optional per-site overrides; `IWorkCalendar.WorkingMinutes(from, to, siteId?)`. Default demo calendar: Sunday off, Saturday working.
- **Determinism:** synthetic data is generated from a seed; the same seed always produces the same dataset and findings.

## 6. How it is built (decided)

### 6.1 Solution layout

```
FlowSight/
  src/
    FlowSight.Core/        # engine: model, calendar, journeys, statistics, bottlenecks, patterns, cards
    FlowSight.Synthetic/   # deterministic synthetic P2P generator with injected scenarios (ground truth)
    FlowSight.Cli/         # generate data, run analysis, export JSON for the static demo
    FlowSight.Api/         # thin ASP.NET Core API over Core (integration path; same JSON contract)
  web/                     # React + Vite + TS + TanStack Query + zod
  tests/                   # xUnit: Core and Synthetic; ground-truth detection tests
  samples/                 # process map (YAML) + small sample event log (CSV)
  docs/
```

- **FlowSight.Core** has no ASP.NET, database or file-system dependencies. Inputs come through interfaces (`IEventSource`, `IProcessMapSource`, `IWorkCalendar`).
- **Live demo without a server:** GitHub Actions builds the solution, runs the CLI on the seeded synthetic data and exports JSON. The React app is built as a static site that reads that JSON, and is deployed to GitHub Pages.
- **Integration path:** the API serves the **same JSON contract** from live data. In the internal platform, the React screens switch from static JSON to API calls; the engine is reused as-is.

### 6.2 Data model (input)

An **event log** at request-line level:

| Field | Meaning |
|---|---|
| `caseId` | Request number |
| `lineId` | Request line (item); a request can split into several lines that take different paths |
| `stage` | One of the 7 stages |
| `activity` | Step inside the stage (e.g. "Manager approval", "Invoice matching") |
| `actorId` | Pseudonymous person id |
| `role` | Role / position performing the step |
| `assignment` | How the step was assigned: `person`, `role`, `group-queue`, `field-routed`, `system` |
| `queuedAt`, `startedAt`, `completedAt` | When the work arrived, was picked up, and was finished (`startedAt` may be missing) |
| `outcome` | approved / rejected / returned / cancelled / done |
| attributes | amount, currency, site, warehouse, supplier category, item category, flags (partial delivery, price variance…) |

**Grain and links.** The unit of analysis is the **request line**. Orders, receipts and invoices are linked to lines through a
`links` table (`lineId`, `documentType`, `documentId`, `quantity`, `amount`), so one invoice can cover many lines and one line
can have several receipts. A line is in the Invoice stage until all its received quantity is invoiced. A **request's** stage is
the stage of its **least advanced open line**; the tracker shows the request strip and, below it, each line when lines diverge.
WIP and throughput are counted by line, with request counts shown alongside.

**Optional inputs** (each behind its own interface; patterns that need a missing input report "insufficient data" instead of
a finding):

| Input | Used by |
|---|---|
| `candidateCount` per step (how many people could pick it up) | Shared-queue search |
| Absence and delegation records (`actorId`, from, to, delegateId) | Single point of failure; "approver absent" waiting reason |
| Ordered / received / invoiced quantity and amount per link | Partial delivery, price variance, exception queue |

*Builder's call:* the internal platform already holds leave and delegation data and the ERP holds amounts, so these are
expected to be available for real data; the demo generator produces all of them.

A request line's **current stage** is the stage of its latest unfinished step. Waiting time = `completedAt − queuedAt`; when `startedAt` exists, it is split into *queue time* and *work time*.

A **process map** (YAML) describes stages, steps, lanes, assignment type, SLA and escalation per step. Its format is a
**superset** of the author's public `process-mapping-toolkit` format
(github.com/Dogukanyllmaz/process-mapping-toolkit, `examples/*.yaml`: `code`, `name`, `lanes`, `default_sla_hours`, and
`steps` with `id`, `name`, `type` start|task|approval|system|decision|end, `lane`, `next`, `assignee` {type, value},
`sla_hours`, `escalation`, `manual_reentry`). FlowSight adds, all optional so toolkit maps stay valid:
- `stage` per step (one of the 7 stages),
- assignee types `group-queue` and `field-routed` (the toolkit's `role`/`person`/`field`/`manager_of`/`initiator`/`system` keep their meaning),
- `return_to` edges for rework,
- a top-level `changes` list: `{date, step, description}` — dated process changes, used for before/after.

*Builder's call:* the toolkit is later updated to accept (and ignore) these fields; FlowSight never requires a field the toolkit does not know about except `stage`.

### 6.3 Engine outputs

1. **Journey** per request/line: stages with durations, current stage, status, waiting reason(s), percentile vs. similar requests (same path and item category).
2. **Stage statistics** per period: throughput, WIP, median, P90, share of total lead time, ageing buckets of open items. Open items are included through survival-style estimates, not ignored.
3. **Bottleneck ranking:** stages ordered by contribution to lead time and WIP growth.
4. **Waiting-reason rules** (G4): explicit, testable rules over attributes and events.
5. **Pattern library** (G5): each pattern = *data symptoms* + *map structure* → *cause* → *countermeasure*. v1 patterns:

| Pattern | Data symptom | Map structure | Countermeasure |
|---|---|---|---|
| Shared-queue search | Long queue time, short work time, many candidates per item, one completer | `group-queue` assignment | Route by an attribute (warehouse, site, category) |
| Serial approval stack | Several approvals in a row, each short, total long | ≥ 3 consecutive approval steps | Merge or parallelise; threshold-based approval |
| Single point of failure | Spikes when one actor is absent; no delegate acted | `person` assignment | Assign to a role; enforce delegation |
| Rework loop | Items returned and re-approved | Return edges in the map | Mandatory fields / validation at entry |
| Batch waiting | Arrivals in bursts, processed weekly | Step with no SLA | SLA + escalation; daily processing |
| Exception queue | Invoice / receipt mismatches dominate delay | Matching step | Tolerance rules; fix upstream data quality |

6. **Improvement card:** pattern, evidence (numbers + which map element), proposed change, expected effect (estimated from the share of queue time removable). *Builder's call:* cards are **computed and read-only** in Core. Owner, status and comments are the host's business (stored by the internal platform, keyed by a stable card id = pattern + step); the demo shows them without editing.
7. **Before/after:** driven by the map's `changes` list. For each dated change, compare the affected step/stage median, P90 and WIP over equal-length windows before and after the date.

### 6.4 Synthetic data (the public dataset)

- ~2,000 requests over 12 months, several fictional sites and warehouses, fictional roles.
- Realistic variety: stock vs. purchase vs. service paths, partial deliveries, price variances, rejections, cancellations, open items at the cut-off date.
- **Injected scenarios with ground truth**, one per v1 pattern (e.g. a shared invoice queue for all warehouses; from month 8, invoices are routed per warehouse → the before/after view shows the improvement).
- Tests assert that the engine finds every injected scenario and does not report it where it was not injected.
- Distributions, thresholds and names are invented, never derived from real data.

### 6.5 Screens (web)

1. **Request tracker** (requester view): search by request number; stage strip with time per stage, current stage highlighted, waiting reason, comparison with similar requests. No names. *(The user chose this layout in the mockup.)*
2. **Flow overview** (owner): WIP per stage with "slower than normal" colouring, lead-time trend, bottleneck ranking.
3. **Stage drill-down** (owner): queue vs. work time, time per role, ageing of open items, slowest cases, waiting reasons.
4. **Improvement cards** (owner): list and detail of detected patterns with evidence and proposed change.
5. **Before / after** (owner): impact of a change on the selected stage.

In the demo, the owner view is reachable from a toggle (no login). In the internal platform, the host's permissions decide who sees what.

*Builder's calls on the web layer:*
- Screens live in a **feature folder** (`web/src/features/flowsight/`) with no app-shell assumptions, so the folder can be copied into the internal platform's React app, which uses the same stack.
- All data access goes through one hook layer with two implementations: **static JSON** (demo) and **HTTP API** (integration). Screens never fetch directly.
- The static demo offers **fixed periods**: monthly, last 12 months, and the before/after windows of each dated change — all precomputed by the CLI. Arbitrary periods are an API-mode feature.

## 7. Milestones

| # | Delivers | Visible result |
|---|---|---|
| M1 | Core model, working-day calendar, synthetic generator, journeys | Request tracker works on synthetic data |
| M2 | Stage statistics, bottleneck ranking, flow overview + drill-down | Owner can see where requests pile up |
| M3 | Waiting-reason rules, process map, pattern library, improvement cards | "Why" and "what to change" |
| M4 | Before/after | Improvement shown with numbers |
| M5 | CI, sensitive-term scan, GitHub Pages deployment, README, v1.0 | Live demo link |

Each milestone is a tagged release with incremental commits.

## 8. Left to the builder

- Internal class design, JSON contract shape, chart library, styling (clean, corporate, light/dark).
- Exact statistics for open items and percentile comparisons, as long as they are explained in the docs.
- Synthetic-data distributions, as long as they look realistic and are clearly invented.
- Repository name and visual identity can change; `FlowSight` is a working name.

# FlowSight

FlowSight follows every purchase request from the moment it is opened until it is paid
(**Request → Approval → Purchasing → Order → Goods receipt → Invoice → Payment**), measures how long
each stage takes in working time, and shows where requests get stuck.

It serves two audiences:
- **Requesters and managers** see where their request is and how long it has been there, without anyone's name.
- **Process owners and internal auditors** see where work piles up, which stages are slower than normal, and — per stage — waiting vs. working time, time per role and per person, and the slowest cases. Causes and improvement proposals come next.

The engine is a dependency-free .NET library, so it can run on synthetic data for this demo or be embedded in
a host application that reads real workflow and ERP history.

> **Status: M2 — flow overview and stage drill-down.** All data in this repository is synthetic and generated from a fixed seed.

The synthetic data contains one deliberate bottleneck: until month 8, invoices land in a **queue shared by every
accounts-payable clerk**, who must search it for their own site's invoices; from then on each invoice is routed straight
to its site's clerk. The flow overview shows the invoice stage as the biggest bottleneck in the early months, the drill-down
shows why (shared queue, long waiting before pick-up), and the lead-time trend drops after the change. Tests use this
scenario as ground truth.

## Run it

```bash
dotnet run --project src/FlowSight.Cli -- demo --out web/public/data
npm --prefix web install
npm --prefix web run dev
```

Open the printed local address. **Requester view:** pick an example request or type a number such as `PR-001783`.
**Process-owner view:** switch to "where does it pile up?", choose a period, and open a stage.
The interface is available in English and Turkish.

## Tests

```bash
dotnet test
npm --prefix web run test -- --run
```

## How it works

| Part | Responsibility |
|---|---|
| `src/FlowSight.Core` | Event model, working-time calendar (weekends, public and half-day holidays), line and request journeys, comparison with similar requests, period statistics (median, P90, Kaplan–Meier median incl. open items, WIP, ageing, share of lead time), bottleneck ranking, stage drill-down, JSON contracts |
| `src/FlowSight.Synthetic` | Deterministic purchase-to-pay event log: invented sites, roles, categories and pseudonymous actors |
| `src/FlowSight.Cli` | `flowsight demo` writes the static JSON the web app reads: requester files and process-owner statistics |
| `web/` | React + TypeScript; screens read data through a swappable source (static JSON now, an HTTP API when embedded) |

Design and plans: [`docs/design`](docs/design) · [`docs/plans`](docs/plans).

## Roadmap

| Milestone | Delivers |
|---|---|
| **M1** ✅ | Request tracker on synthetic data |
| **M2** ✅ | Stage statistics, bottleneck ranking, flow overview, stage drill-down |
| M3 | Waiting reasons, process map, bottleneck patterns and improvement cards |
| M4 | Before / after measurement of process changes |
| M5 | CI, live demo on GitHub Pages, v1.0 |

## License

MIT

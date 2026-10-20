# FlowSight

FlowSight follows every purchase request from the moment it is opened until it is paid
(**Request → Approval → Purchasing → Order → Goods receipt → Invoice → Payment**), measures how long
each stage takes in working time, and shows where requests get stuck.

It serves two audiences:
- **Requesters and managers** see where their request is and how long it has been there, without anyone's name.
- **Process owners and internal auditors** see bottlenecks, their causes and improvement proposals *(coming in later milestones)*.

The engine is a dependency-free .NET library, so it can run on synthetic data for this demo or be embedded in
a host application that reads real workflow and ERP history.

> **Status: M1 — request tracker.** All data in this repository is synthetic and generated from a fixed seed.

## Run it

```bash
dotnet run --project src/FlowSight.Cli -- demo --out web/public/data
npm --prefix web install
npm --prefix web run dev
```

Open the printed local address, pick one of the example requests or type a number such as `PR-001783`.
The interface is available in English and Turkish.

## Tests

```bash
dotnet test
npm --prefix web run test -- --run
```

## How it works

| Part | Responsibility |
|---|---|
| `src/FlowSight.Core` | Event model, working-time calendar (weekends, public and half-day holidays), line and request journeys, comparison with similar requests, requester JSON contract |
| `src/FlowSight.Synthetic` | Deterministic purchase-to-pay event log: invented sites, roles, categories and pseudonymous actors |
| `src/FlowSight.Cli` | `flowsight demo` writes the static JSON the web app reads |
| `web/` | React + TypeScript; screens read data through a swappable source (static JSON now, an HTTP API when embedded) |

Design and plans: [`docs/design`](docs/design) · [`docs/plans`](docs/plans).

## Roadmap

| Milestone | Delivers |
|---|---|
| **M1** ✅ | Request tracker on synthetic data |
| M2 | Stage statistics, bottleneck ranking, flow overview |
| M3 | Waiting reasons, process map, bottleneck patterns and improvement cards |
| M4 | Before / after measurement of process changes |
| M5 | CI, live demo on GitHub Pages, v1.0 |

## License

MIT

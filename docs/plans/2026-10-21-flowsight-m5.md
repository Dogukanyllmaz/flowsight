# FlowSight M5 Implementation Plan: v1.0, ready to publish

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** FlowSight v1.0: the visible demo defects fixed, the demo export fast enough for CI, automatic tests and a private sensitive-term scan on every push, a GitHub Pages workflow that publishes the live demo once the repository is public, and a README with screenshots.

**Architecture:** No new features. Small fixes in the web screens; one performance fix in the export path chosen by measurement; two GitHub Actions workflows; documentation.

**Tech Stack:** as M4, plus GitHub Actions (`actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, `actions/upload-pages-artifact`, `actions/deploy-pages`).

**Spec:** `docs/design/2026-10-20-flowsight-design.md` (M5 = §7 row M5; G7; §5 confidentiality)

## Global Constraints

- Everything in the M1–M4 Global Constraints still applies.
- The sensitive-term list never enters the repository: CI reads it from the repository secret `SENSITIVE_TERMS` (one term per line).
- The repository is created **private**. Making it public and enabling Pages is a separate step that needs the user's explicit approval.
- Keep it simple: no new options, no new abstractions beyond what a task needs.

## Decisions this plan fixes

- **Visible defects to fix (user's choice):** (1) Turkish number formatting for the share column in the bottleneck table and the trend-chart axis ticks; (2) the "Compared: 0 days before vs 0 days after" line is hidden when the window is empty; (3) the ageing chart's hidden table has three distinct column headers (`Age`, `Lines`, `Detail`).
- **Export speed:** measure the full demo export per phase; fix only the largest cost; target **< 20 s** for 2 000 requests on this machine; output must stay byte-identical (existing determinism tests plus a hash comparison of the whole `data/` folder before and after the change).
- **CI (`.github/workflows/ci.yml`)** on push and pull request: .NET 8 build and `dotnet test`; Node 22 `npm ci`, `npm test -- --run`, `npm run build`; sensitive-term scan: `git grep -niwf terms.txt -- . ':!web/package-lock.json'` must find nothing (step skipped with a warning when the secret is absent).
- **Pages (`.github/workflows/pages.yml`)** on push to `main`: export demo data, build the web app, upload `web/dist`, deploy. The deploy job runs only when the repository is public (`github.event.repository.private == false`).
- **Repository name:** `flowsight` (private). Live-demo URL once public: `https://dogukanyllmaz.github.io/flowsight/`.

## Review Focus

1. **Turkish view** of the overview table and trend axis shows decimal commas. → Task 1 tests.
2. **Export after the speed fix** is byte-identical to before. → Task 2 step "hash comparison".
3. **CI without the secret** (e.g. a fork) does not fail the build but warns. → Task 3 step-level `if`.
4. **Pages on a private repository** is skipped, not failed. → Task 3 job-level `if`.
5. **No sensitive term** anywhere in the history pushed to GitHub. → Task 4 private scan over all commits before the first push.

---

### Task 1: Fix the visible demo defects

**Files:** `web/src/features/flowsight/pages/FlowOverview.tsx` (+test), `components/charts/TrendChart.tsx`, `pages/BeforeAfter.tsx` (+test), `pages/StageDetail.tsx` (+test), `i18n/strings.ts`.

- [ ] **Step 1: Write the failing tests** — `FlowOverview`: in Turkish the share cell reads `%29,8` (fixture value 29.8) and a trend tick with a decimal uses a comma; `BeforeAfter`: a change with 0-day windows shows no "Compared:" line; `StageDetail`: the ageing chart's hidden table headers are `Age`, `Lines`, `Detail`.
- [ ] **Step 2: Run** web tests. Expected: FAIL.
- [ ] **Step 3: Implement** — share cell via `t('percentValue', …)`; TrendChart formats tick labels with the active language; hide the windows line when the window is 0 days; add string `colDetail`.
- [ ] **Step 4: Run** web tests and build. Expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Fix visible demo defects"`

### Task 2: Faster demo export

- [ ] **Step 1: Measure** — time `DemoExporter.Run` for 2 000 requests per phase (generate, journeys, request files, owner overview, cards, drill-downs, changes); save the full `data/` hash.
- [ ] **Step 2: Fix the largest cost** with the smallest change (e.g. compute the cut-off log once and pass it in, if that dominates).
- [ ] **Step 3: Verify** — `dotnet test` passes; the `data/` hash equals the one from Step 1; export time < 20 s.
- [ ] **Step 4: Commit** — `git commit -m "Speed up the demo export"`

### Task 3: CI and Pages workflows

**Files:** create `.github/workflows/ci.yml`, `.github/workflows/pages.yml`.

- [ ] **Step 1: Write** both workflows per Decisions.
- [ ] **Step 2: Check** — YAML parses (`python -c "import yaml,sys; [yaml.safe_load(open(f)) for f in sys.argv[1:]]" .github/workflows/*.yml`); every command in them runs locally and passes.
- [ ] **Step 3: Commit** — `git commit -m "Add CI and Pages workflows"`

### Task 4: README, release and private repository

- [ ] **Step 1: Screenshots** — capture tracker, flow overview, improvement card and before/after from the dev server into `docs/screenshots/*.png`.
- [ ] **Step 2: README** — status v1.0; live-demo link (marked "available once published"); screenshots; roadmap all ✅; how CI and the private scan work.
- [ ] **Step 3: Final checks** — tests, build, private scan over **all commits** (`git rev-list --all` + `git grep`) reports nothing.
- [ ] **Step 4: Commit and tag** — `git commit -m "Release v1.0"`, `git tag v1.0.0`.
- [ ] **Step 5: Publish privately** — `gh repo create Dogukanyllmaz/flowsight --private --source . --push`, push tags, set secret `SENSITIVE_TERMS` from the local term file, watch the CI run until green.
- [ ] **Step 6: Stop** — report to the user; making the repository public and enabling Pages waits for their approval.

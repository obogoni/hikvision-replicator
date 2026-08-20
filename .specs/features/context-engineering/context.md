# Context Engineering Context

**Gathered:** 2026-08-20
**Spec:** `.specs/features/context-engineering/spec.md`
**Status:** Ready for execute (Design skipped — no architectural decision; see below)

---

## Feature Boundary

Restructure the project's **always-loaded** context — `CLAUDE.md` — into a hub-and-spoke
layout, relocating reference material into named `docs/` spokes without losing a single
rule, and adding the product purpose (WHY) that the file has never carried. Content is
**relocated and compressed, never re-legislated**.

---

## Implementation Decisions

### Line budget and shape

- Hub target **~100 lines**, hard ceiling **110** — down from 285.
- Chosen over the article's own ~60-line reference size. Reason given during discussion:
  at ~60 the universally-applicable git rules leave default context, and this repository
  has already paid for that twice (PRs #2 and #4 merged into a branch instead of `main`).
  The rules that have historically been broken stay where they are always read.
- Rejected: "restructure only" at ~180 lines — it would move the two heaviest sections
  but leave the file still near the article's ceiling, achieving shape without budget.
- Hub composition: purpose ~6 · stack ~6 · structure ~14 · commands and gates ~20 ·
  universal rules ~25 · spoke pointers ~12.

### Failure narratives

- Pattern is fixed: **one-line imperative in the hub, full incident in the spoke.**
- Worked example agreed during discussion:
  - Hub: ``Never bare `dotnet format` — it makes semantic edits. Use `dotnet format
    whitespace`. Why: docs/code-style.md``
  - Spoke: the full account — it stamped `[Obsolete]` onto `PostgresFixture` and
    `UnreachableDatabaseFixture`, silencing a real advisory by marking the consumer obsolete.
- Rejected: moving narratives entirely to spokes. A rule Claude never reads is a rule that
  gets broken, and two of these four record mistakes already made.
- Rejected: keeping them verbatim in the hub. They are the file's highest-value lines but
  also most of its bulk; the compromise keeps the deterrent at ~1 line instead of ~8.
- The four narratives that must survive intact, with their concrete detail:
  stranded PRs #2/#4 · `dotnet format` stamping `[Obsolete]` · `git push --dry-run`
  passing without testing protection · L-007's incremental-build silence.

### Spoke organisation

Topic-based with self-descriptive names, extending the convention `docs/test-patterns.md`
already established:

| Spoke | Absorbs |
|---|---|
| `docs/git-workflow.md` | AD-025 in full — commit-type table, protection payload and rejection output, verify-`main` command, `gh repo edit` settings, the `--dry-run` trap, the stacked-PR narrative |
| `docs/code-style.md` | AD-027 in full — `.editorconfig` as SSOT, `IDE0055` as error, the `dotnet format` incident, L-007, `AnalysisMode` ratchet, the `-warnaserror` prohibition, file-scoped namespaces |
| `docs/slice-anatomy.md` | Three-file slice layout, `OneOf` result pattern, layer return-type rules, the write-path flow diagram, database-is-the-authority (AD-022), `CancellationToken`, repository/specification rules, EF Core configuration |
| `docs/test-patterns.md` *(existing)* | Extended with the E2E setup notes — no browser download, no `pwsh`, `E2E_BASE_URL` override |

- Rejected: trigger-based names (`before-committing.md`, `writing-a-slice.md`). It would
  rename the existing spoke and break the `../docs/test-patterns.md` links in `STATE.md`.
- Rejected: a single `docs/conventions.md` — a grab-bag, and it forfeits the
  self-descriptive-name benefit the whole pattern depends on.
- **No `docs/spec-validation.md`.** The narrative it would carry already exists in full in
  `STATE.md` AD-028; a spoke would be a third copy of it.

### The WHY

- A short purpose block in the hub — the product, the stadium scenario, and the two
  quality attributes in priority order — plus a link to `.specs/ROADMAP.md`.
- **Scale numbers are deliberately excluded** from the hub (50k users, 10k faces/device,
  device count). They belong to ROADMAP, and a second copy is a second thing to go stale.
  This is the same reasoning AD-029 used to delete rather than migrate the architecture map.
- Rejected: a bare link to ROADMAP. The WHY would then never reach default context, which
  is precisely the gap the article warns about.

### Agent's Discretion

- Exact section ordering and heading wording inside each spoke.
- How the hub's pointer lines are phrased and grouped.
- Whether spoke content is reorganised while being moved, provided every inventoried rule
  survives (spec assumption A-8 — preservation is verified per rule, not per line).
- Whether spokes keep code blocks — permitted per A-7; the hub may not, commands excepted.

### Declined / Undiscussed Gray Areas → Assumptions

All four gray areas raised were discussed and decided. The remaining judgement calls the
agent made without asking are logged in the spec's Assumptions table as A-3, A-5, A-6,
A-7, A-8 — enforcement, validation-narrative placement, E2E-notes home, snippet policy,
and preservation granularity.

---

## Why Design Is Skipped

No architecture, no new pattern, no dependency. The one structural decision — hub and
spokes, and which spokes — was settled in this document. What remains is relocation
against a fixed inventory, which `tasks.md` sequences directly.

---

## Specific References

- The article: <https://www.humanlayer.dev/blog/writing-a-good-claude-md>. Its rules that
  drove decisions here: keep it under 300 lines and fewer is better; progressive
  disclosure into self-descriptive files; cover WHAT, WHY and HOW; prefer `file:line`
  references to code snippets; *"never send an LLM to do a linter's job"*; include only
  universally applicable context, because irrelevant content is ignored and distracts.
- On the linter point: the article recommends hooks or slash commands. AD-027 already
  answered this differently and more strongly — `EnforceCodeStyleInBuild` makes **every**
  `dotnet build` the gate, with no flag to remember and no hook to bypass. No change.
- AD-029 is the precedent for how this project handles a document that restates the code:
  the descriptive half is deleted, the judgment half is promoted.

---

## Deferred Ideas

| Idea | Why deferred |
|---|---|
| CI check enforcing the line budget and link integrity | Would be the project's third enforced gate; needs its own decision. Until then the ceiling is documentary — recorded as spec assumption A-3 and in AD-031's Trade-off. |
| Full `STATE.md` § Handoff refresh | Already deliberately deferred to `user-registry`. Only the branch-protection contradiction is repaired here (P2). |
| Splitting `STATE.md`'s 30 decisions into per-decision files | On-demand context, not always-loaded — the article's budget argument does not reach it. Revisit if the Decisions section starts being read in full every session. |

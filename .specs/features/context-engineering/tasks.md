# Context Engineering Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its
Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill
is the source of truth for the full flow (per-task cycle, adequacy review, Verifier,
discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Spec**: `.specs/features/context-engineering/spec.md`
**Context**: `.specs/features/context-engineering/context.md`
**Design**: none — skipped, see `context.md` § Why Design Is Skipped
**Status**: Done — 7 tasks committed, plus fix commits `fa05813` (Verifier iteration 1) and
`27960df` (iteration 2). Pre-squash references: they resolve only through the PR.
**Branch**: `docs/context-engineering`, off `origin/main` (`5c8d55d`)

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec — confirm before Execute.
> Guidelines found: `CLAUDE.md` § Tests, `docs/test-patterns.md`, `.specs/STATE.md` AD-024 / AD-026,
> `.github/workflows/ci.yml`.

**This feature ships no executable code.** It changes `CLAUDE.md`, four `docs/*.md` files and
`.specs/STATE.md` — no `.cs` file, no `.csproj`, no project reference. AD-024's layer-to-level
mapping has no layer to bind to, so the strong default ("cover every AC with a test") cannot be
applied literally: there is no unit, integration or E2E level at which a markdown line count is
observable.

The default is therefore satisfied a level down rather than waived: **every acceptance criterion
is discharged by a mechanical, binary check** run against the working tree, and each check's
output is the evidence cited in `validation.md`. "Documentation, verified by inspection" is not
an accepted outcome here — a criterion with no runnable check counts as uncovered, exactly as
AD-028's evidence-or-zero requires.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
|---|---|---|---|---|
| Always-loaded context (`CLAUDE.md`) | **doc-gate** (mechanical) | Every AC of CTX-01…CTX-08 has a binary check: line count, fence count and fence language, link resolution, first-20-lines content, inventory citation | `CLAUDE.md` | `bash $SCRATCH/doc-gate.sh` |
| Spoke docs (`docs/*.md`) | **doc-gate** (mechanical) | Every inventoried rule routed to a spoke resolves to a `file:line`; all four failure narratives present with their concrete artifact named | `docs/*.md` | `bash $SCRATCH/doc-gate.sh` |
| Decision log (`.specs/STATE.md`) | **doc-gate** (mechanical) | `AD-031` present with all six required fields; branch-protection claim agrees with live `gh api` output | `.specs/STATE.md` | `bash $SCRATCH/doc-gate.sh` + `gh api …/branches/main/protection` |
| C# solution (`src/**`) | **none — build gate only** | Untouched by this feature; the gate proves the claim rather than assuming it | `src/**` | Docker-free gate |

**Provenance of the doc-gate**: it is an author-side gate script written to the session scratchpad,
**not committed** — committing it would create the CI-enforced budget check that spec § Out of Scope
and assumption A-3 explicitly defer. Its full source and output are reproduced in `validation.md`
so the run is auditable. The Verifier does **not** run it: it re-derives every criterion
independently, per AD-028.

## Gate Check Commands

> Generated from codebase — confirm before Execute.

| Gate Level | When to Use | Command |
|---|---|---|
| **Quick** | After each spoke task (T1–T4) — link and narrative checks only | `bash $SCRATCH/doc-gate.sh --spokes` |
| **Full** | After the hub and decision-log tasks (T5–T7) — every CTX check | `bash $SCRATCH/doc-gate.sh` |
| **Build** | Once, after the final task — proves `src/**` is untouched and still green | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` |

`$SCRATCH` = `/tmp/claude-1000/-home-obogoni-projects-hikvision-replicator/15d7d012-3a7a-4b4b-9fb2-5a19cf844b80/scratchpad`

The Docker-free gate is used for **Build** rather than the full gate: this feature touches no code,
so Testcontainers integration tests would prove nothing that the compile does not, at the cost of a
PostgreSQL container. CI still runs the full gate on the PR (AD-027) — that remains the enforcement
boundary.

Expected counts, unchanged by this feature: **81 unit** tests. Any deviation is a defect, not a
result.

---

## Execution Plan

Phases run sequentially; tasks within a phase run in order.

### Phase 1: Spokes

Every spoke exists before the hub links it, so CTX-04 (no dangling link) can be checked the
moment the hub is written rather than repaired afterwards.

```
T1 → T2 → T3 → T4
```

### Phase 2: Hub

```
T5
```

### Phase 3: Decision log

```
T6 → T7
```

---

## Task Breakdown

### ✅ T1: Extract the Git workflow spoke

**What**: Create `docs/git-workflow.md` holding the full AD-025 reference material now inlined in
`CLAUDE.md` § Git Workflow — the commit-type table and scopes, squash-PR mechanics, the
`[remote rejected]` output, `enforce_admins`, the `--dry-run` trap, the required-check and
`strict=true` rules, the verify-`main` command, and the `gh repo edit` settings — with the stranded
PR #2/#4 narrative kept whole.
**Where**: `docs/git-workflow.md` (new)
**Depends on**: None
**Reuses**: `CLAUDE.md` § Git Workflow (source text); `.specs/STATE.md` AD-025 (linked, not copied)
**Requirement**: CTX-02, CTX-05 (R-06, R-08…R-14)

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] Every rule R-06, R-08, R-09, R-10, R-11, R-12, R-13, R-14 appears and is citable by line
- [ ] The PR #2/#4 stranding narrative names both PR numbers and the retarget/delete-on-merge fix
- [ ] The `--dry-run` trap states *why* it passes (no pack is sent, so protection never evaluates)
- [ ] AD-025 is linked for the protection payload, not transcribed (edge case: one authority per rule)
- [ ] Quick gate passes: `bash $SCRATCH/doc-gate.sh --spokes`

**Tests**: doc-gate
**Gate**: quick








**Committed**: `2d3fbab` (pre-squash reference)
**Commit**: `docs: extract the git workflow into its own reference`

---

### ✅ T2: Extract the code-style spoke

**What**: Create `docs/code-style.md` holding AD-027's enforcement model and the style hazards —
`.editorconfig` as the single source, `EnforceCodeStyleInBuild`, `IDE0055` as an error, the
`dotnet format whitespace` invocations, the `AnalysisMode`/`AnalysisLevel` settings with the 10 `CA`
findings and the ratchet rule, the `-warnaserror` prohibition with its 4 `NU1903` + 4 `CS0618`
reason, and file-scoped namespaces / primary constructors — with the `dotnet format` and L-007
narratives kept whole.
**Where**: `docs/code-style.md` (new)
**Depends on**: T1
**Reuses**: `CLAUDE.md` § Code Style (source text); `.specs/STATE.md` AD-027; `.specs/LESSONS.md` L-007
**Requirement**: CTX-02, CTX-05 (R-24…R-30)

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] Every rule R-24…R-30 appears and is citable by line
- [ ] The bare-`dotnet format` narrative names `PostgresFixture` **and** `UnreachableDatabaseFixture`
      and the `[Obsolete]` stamping, and says what was silenced (the deprecated `PostgreSqlBuilder` advisory)
- [ ] The L-007 narrative states the `--no-incremental` remedy and why silence is untrustworthy
- [ ] The 10 `CA` findings are pointed at `code-style-enforcement/spec.md`, not re-enumerated
- [ ] Quick gate passes: `bash $SCRATCH/doc-gate.sh --spokes`

**Tests**: doc-gate
**Gate**: quick

**Committed**: `313dace` (pre-squash reference)
**Commit**: `docs: extract the code style rules into their own reference`

---

### ✅ T3: Extract the slice anatomy spoke

**What**: Create `docs/slice-anatomy.md` holding everything about how a vertical slice is built —
the three-file layout, the `OneOf` result pattern and standalone error records, the per-layer
return-type rules and `.Match()` naming, the write-path request-flow diagram, the
database-is-the-authority rule (AD-022), the `CancellationToken` contract, the
`IRepository<T>`/`Specification<T>` rules, EF Core configuration discovery, per-slice DTOs,
`MapGroup` grouping, and the password-encryption rule.
**Where**: `docs/slice-anatomy.md` (new)
**Depends on**: T2
**Reuses**: `CLAUDE.md` §§ Result Pattern, Vertical Slice Structure, CancellationToken, Repository &
Specifications, EF Core (source text); AD-001…AD-009, AD-022 (linked)
**Requirement**: CTX-02 (R-31…R-41)

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] Every rule R-31…R-41 appears and is citable by line
- [ ] The write-path flow diagram survives intact, including the two bracketed annotations
      (ciphertext-only aggregate, `now` from `TimeProvider`)
- [ ] The AD-022 rule keeps its consequence: renaming the named index degrades a 409 into a 500
      unless a test covers it
- [ ] Code blocks are permitted here (assumption A-7) and each is anchored to a real path
- [ ] Quick gate passes: `bash $SCRATCH/doc-gate.sh --spokes`

**Tests**: doc-gate
**Gate**: quick

**Committed**: `5263da6` (pre-squash reference)
**Commit**: `docs: extract the vertical slice anatomy into its own reference`

---

### ✅ T4: Absorb the E2E setup notes into the test-patterns spoke

**What**: Add an E2E setup section to the existing `docs/test-patterns.md` — the `IAPIRequestContext`
driver needing neither a browser download nor `pwsh`, when browser installation *would* be needed,
and the `E2E_BASE_URL` override.
**Where**: `docs/test-patterns.md` (modify)
**Depends on**: T3
**Reuses**: `CLAUDE.md` § E2E setup (source text)
**Requirement**: CTX-02 (R-23)

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] R-23 appears and is citable by line
- [ ] The file is **not** renamed or moved — the `../docs/test-patterns.md` links in `.specs/STATE.md`
      (AD-011, AD-019, AD-024) still resolve
- [ ] The existing three sections are unchanged
- [ ] Quick gate passes: `bash $SCRATCH/doc-gate.sh --spokes`

**Tests**: doc-gate
**Gate**: quick

**Committed**: `ad82293` (pre-squash reference)
**Commit**: `docs: fold the e2e setup notes into the test patterns reference`

---

### ✅ T5: Rewrite CLAUDE.md as the hub

**What**: Replace `CLAUDE.md` with a ≤ 110-line hub: a purpose block stating the product and its two
quality attributes, the stack, the project structure, the commands and both gate commands, the 15
rules that keep their imperative in default context, and a one-line pointer for each of the 28
demoted rules.
**Where**: `CLAUDE.md` (rewrite)
**Depends on**: T1, T2, T3, T4
**Reuses**: `.specs/ROADMAP.md` § Product Goal and AD-014 (purpose block source); the four spokes
**Requirement**: CTX-01, CTX-03, CTX-04, CTX-06, CTX-07, CTX-08

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] `wc -l CLAUDE.md` ≤ 110
- [ ] ≤ 8 fenced blocks, every one a shell command block or the project-structure tree — no C#,
      no ASCII request-flow diagram (AC-2 as amended)
- [ ] All 15 hub-resident rules present; all 11 `H→` rules named in one line; all four spokes linked
- [ ] Every relative path referenced resolves to an existing file
- [ ] Purpose stated within the first 20 lines; latency named primary, fault tolerance second;
      ROADMAP linked and **no scale numbers restated**
- [ ] Full gate passes: `bash $SCRATCH/doc-gate.sh`

**Tests**: doc-gate
**Gate**: full

**Committed**: `2843660` (pre-squash reference)
**Commit**: `docs: restructure CLAUDE.md into a hub with topic spokes`

---

### ✅ T6: Record AD-031

**What**: Append `AD-031` to `.specs/STATE.md` § Decisions in the established six-field format,
stating the hub-versus-spoke routing rule for future content and recording the line budget as
documentary and unenforced.
**Where**: `.specs/STATE.md` (modify, § Decisions)
**Depends on**: T5
**Reuses**: AD-029's format and reasoning as precedent
**Requirement**: CTX-11, CTX-12, CTX-13

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] `AD-031` present with Decision · Reason · Trade-off · Scope · Date · Status
- [ ] It answers "hub or spoke?" as a rule a future author can apply without re-deriving it
- [ ] Its Trade-off records that the ≤ 110 ceiling has no mechanical enforcement (A-3), in the same
      terms AD-028 uses for its own documentary-only clause
- [ ] Its Scope names `CLAUDE.md` and all four spokes
- [ ] No existing AD is edited — supersession, if any, is stated inside AD-031
- [ ] Full gate passes: `bash $SCRATCH/doc-gate.sh`

**Tests**: doc-gate
**Gate**: full

**Committed**: `c000aab` (pre-squash reference)
**Commit**: `docs(specs): record AD-031 splitting CLAUDE.md into a hub and spokes`

---

### ✅ T7: Repair the branch-protection contradiction

**What**: Correct the `.specs/STATE.md` § Handoff claim that branch protection is **"Not
configured"**. Live state confirms it is active and matches `CLAUDE.md` exactly — `strict=true`,
`build-and-test` required, `enforce_admins=true`, `required_approving_review_count=0`. Change only
that claim and the "Next step" clause that depends on it.
**Where**: `.specs/STATE.md` (modify, § Handoff)
**Depends on**: T6
**Reuses**: live `gh api repos/obogoni/hikvision-replicator/branches/main/protection` output
**Requirement**: CTX-09, CTX-10

**Tools**:
- MCP: NONE
- Skill: `tlc-spec-driven`

**Done when**:
- [ ] The "Not configured" bullet and the dependent "Next step" clause agree with live protection
- [ ] `CLAUDE.md`, `.specs/STATE.md` and the live API all state the same thing
- [ ] The rest of the stale Handoff is untouched — the full refresh stays deferred to `user-registry`
- [ ] Full gate passes: `bash $SCRATCH/doc-gate.sh`
- [ ] Build gate passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` → **81 unit tests**, no change

**Tests**: doc-gate + build
**Gate**: build

**Committed**: `ddc9e75` (pre-squash reference)
**Commit**: `docs(specs): correct the handoff's stale branch-protection claim`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3

Phase 1:  T1 ──→ T2 ──→ T3 ──→ T4
Phase 2:  T5
Phase 3:  T6 ──→ T7
```

**7 tasks → one task-budgeted batch (≤ ~8).** Execution is inline in the main window; no batch
sub-agents are offered or dispatched. The **Verifier still runs** as a fresh sub-agent after T7 —
it is unconditional (AD-028), not a function of task count.

---

## Task Granularity Check

| Task | Scope | Status |
|---|---|---|
| T1: Git workflow spoke | 1 new file | ✅ Granular |
| T2: Code style spoke | 1 new file | ✅ Granular |
| T3: Slice anatomy spoke | 1 new file | ✅ Granular |
| T4: E2E notes into test-patterns | 1 file, 1 section | ✅ Granular |
| T5: Hub rewrite | 1 file | ✅ Granular |
| T6: AD-031 | 1 file, 1 appended entry | ✅ Granular |
| T7: Handoff repair | 1 file, 1 claim | ✅ Granular |

T3 is the widest (11 rules from five source sections) but is one file and one cohesive concept —
how a slice is built. Splitting it by source section would produce spokes too thin to justify a
hop, which the spec's edge cases forbid.

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T1 | None | (phase entry) | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T1, T2, T3, T4 | Phase 1 → Phase 2 | ✅ Match |
| T6 | T5 | Phase 2 → Phase 3 | ✅ Match |
| T7 | T6 | T6 → T7 | ✅ Match |

T5's dependency on all four spokes is carried by the phase boundary rather than four arrows: Phase 2
cannot start until Phase 1 completes, and Phase 1 is exactly T1–T4. No task depends on a later phase.

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
|---|---|---|---|---|
| T1 | Spoke docs | doc-gate | doc-gate | ✅ OK |
| T2 | Spoke docs | doc-gate | doc-gate | ✅ OK |
| T3 | Spoke docs | doc-gate | doc-gate | ✅ OK |
| T4 | Spoke docs | doc-gate | doc-gate | ✅ OK |
| T5 | Always-loaded context | doc-gate | doc-gate | ✅ OK |
| T6 | Decision log | doc-gate | doc-gate | ✅ OK |
| T7 | Decision log + `src/**` claim | doc-gate + build | doc-gate + build | ✅ OK |

No task declares `Tests: none`, and no check is deferred to a later task: each task's checks run
against the file that task produces. The doc-gate is extended in place as tasks land — T1 adds the
spoke checks, T5 adds the hub checks — never written once at the end, which would be exactly the
test-deferral anti-pattern.

# Context Engineering Specification

**Feature**: `context-engineering`
**Created**: 2026-08-20
**Source**: [Writing a good CLAUDE.md](https://www.humanlayer.dev/blog/writing-a-good-claude-md) (HumanLayer)

## Problem Statement

`CLAUDE.md` is loaded into **every** session by default, so every line it carries is
paid for on every task — relevant or not. It has grown to **285 lines / 22 fenced
blocks**, sitting on the 300-line ceiling the source article names, with a quarter of
it (72 lines) spent on Git workflow reference material and another 60 on restating
code that lives in `src/`. Meanwhile the file never states **what the product does** —
the WHY the article calls one of the three essential components — because that content
lives only in `.specs/ROADMAP.md`, which nothing loads by default.

The repository already proves the hub-and-spoke pattern works (`docs/test-patterns.md`
is referenced, not inlined). This feature applies it to the rest of the file.

## Goals

- [ ] `CLAUDE.md` drops from 285 lines to **≤ 110**, with every removed rule relocated
      to a named spoke — **zero rules lost**, verified by a rule-by-rule inventory.
- [ ] The product's purpose (WHY) reaches default context for the first time.
- [ ] Reference material moves to `docs/` files with self-descriptive names; the hub
      keeps the imperative rule and points at the spoke for the reasoning.
- [ ] Hard-won failure narratives survive relocation intact — none is summarised away.

## Out of Scope

| Item | Reason |
|---|---|
| Restructuring `.specs/STATE.md`, `ROADMAP.md`, `LESSONS.md` | These are **on-demand** context, loaded by the skill at Design/resume — not by every session. The article's budget argument does not apply to them. |
| A CI link-checker or line-count gate | Would make this the third enforced gate (AD-025, AD-027) and needs its own decision. Verification here is the one-time Verifier pass; recorded as assumption A-3. |
| Rewording rules, tightening policy, or changing any decision | This feature **relocates and compresses**; it does not legislate. A rule that reads differently after the move is a defect, not an improvement. |
| `.claude/settings.local.json`, hooks, slash commands | The article suggests hooks for formatting; AD-027 already resolved that question in favour of compiler enforcement, which is stronger. No change warranted. |
| Rewriting the `tlc-spec-driven` skill files | Not project-owned; they live in `~/.claude/skills/`. |
| A full `STATE.md` Handoff refresh | Deliberately deferred to `user-registry`. Only the direct contradiction with `CLAUDE.md` is repaired here — see P2. |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
|---|---|---|---|
| A-1 · Line budget for the hub | ≤ 110 lines (target ~100) | User chose hub-and-spoke over HumanLayer-strict ~60: at ~60 the universally-applicable git rules would leave default context, and the PR-base rule has already been broken twice. | **y** |
| A-2 · Failure narratives | One-line imperative in hub, full incident in spoke | Keeps the deterrent in default context at ~1 line instead of ~8, without losing the cost record. | **y** |
| A-3 · No mechanical enforcement of the budget | Verifier pass only; no CI check | Enforcement is a separate decision (see Out of Scope). Consequence accepted: the ≤ 110 ceiling is **documentary**, exactly as AD-028's clause is, and can drift. | n — logged |
| A-4 · Spoke naming | Topic-based: `docs/git-workflow.md`, `docs/code-style.md`, `docs/slice-anatomy.md` | Matches the article's "self-descriptive names" and the convention `docs/test-patterns.md` already started. | **y** |
| A-5 · Where the validation narrative lives | Not duplicated into a spoke — hub points at `.specs/STATE.md` AD-028 | AD-028 already carries the `code-style-enforcement` AC-1 story in full. A fourth spoke would be a third copy. | n — logged |
| A-6 · E2E setup notes | Absorbed into the existing `docs/test-patterns.md` | It is test-level knowledge, and the article prefers extending a well-named spoke over creating a thin one. | n — logged |
| A-7 · Code snippets in spokes | Spokes may keep code blocks; **the hub may not** (commands excepted) | The article's "use `file:line` instead of snippets" is a budget argument about always-loaded context. A spoke read on demand is where a worked example earns its cost. | n — logged |
| A-8 · Content preservation is verified per rule, not per line | Inventory table below is the contract | Verbatim-diff checking would block the compression the feature exists to do. | n — logged |
| A-9 · CTX-10 was exceeded by one item | Accepted, not reverted | The Handoff's "Next step" clause **contained** the protection claim, so it had to be touched; while there, the already-completed "merge the `code-style-enforcement` PR" step was also retired. That is one item beyond "confined to the contradicting claim". Reverting it would reinstate a statement that is false — the PR is merged — so the overreach is **declared** instead. Flagged by the Verifier, not by the author. | n — logged |

**Open questions:** none — all resolved or logged above.

---

## User Stories

### P1: The hub fits its budget without losing a rule ⭐ MVP

**User Story**: As Claude starting any session in this repo, I want `CLAUDE.md` to carry
only what applies to every task, so that the instruction budget is spent on rules I will
actually need rather than on reference material for work I am not doing.

**Why P1**: This is the feature. Everything else is a consequence of it.

**Acceptance Criteria**:

1. WHEN `CLAUDE.md` is measured THEN it SHALL be **≤ 110 lines**.
2. WHEN `CLAUDE.md` is measured THEN it SHALL contain **≤ 8 fenced code blocks**, and
   every one of them SHALL be either a shell command block or the project-structure tree
   — no C#, and no ASCII request-flow diagram.

   > **Amended 2026-08-20, before implementation.** As first written this criterion said
   > *"every one of them SHALL be a shell command block"*, which would have forbidden the
   > project-structure tree — a ` ```text ` block, and content the source article names as
   > essential codebase mapping. The intent was to bar **snippets that restate `src/`**, and
   > a directory tree is a map, not a snippet. Recorded here rather than silently corrected:
   > an author quietly rewriting their own acceptance criterion is the failure mode AD-028
   > was created to catch.
3. WHEN each rule in the inventory below is looked up THEN it SHALL resolve to a cited
   `file:line` in either `CLAUDE.md` or a named spoke — **evidence-or-zero**; a rule
   nobody can cite counts as lost regardless of confidence that it was moved.
4. WHEN a rule is marked **`H→`** in the inventory THEN the hub SHALL state it in one
   line and link its spoke. WHEN a rule is marked **`→`** THEN the hub SHALL NOT restate
   it, but the spoke holding it SHALL be reachable from the hub by a link — no spoke is
   orphaned, and no rule lands in a file the hub never names.

   > **Amended 2026-08-20, before implementation.** As first written this criterion required
   > *every* demoted rule to keep a one-line hub pointer. That contradicted the inventory in
   > this same spec, whose legend defines `→` as **spoke only** and applies it to 20 of the
   > 28 demoted rules — satisfying the original wording would have meant 28 pointer lines,
   > rebuilding the bulk the feature exists to remove. The two halves were written by the
   > same author and the shared intent hid the disagreement, which is precisely the defect
   > AD-028 records for `code-style-enforcement`'s `AC-1`. Corrected to the inventory's
   > actual three-way taxonomy; recorded rather than silently rewritten.
5. WHEN any relative path referenced by `CLAUDE.md` is resolved THEN the target file
   SHALL exist.
6. WHEN the spokes are read THEN each of the four failure narratives (stranded PRs
   #2/#4 · `dotnet format` stamping `[Obsolete]` on the fixtures · `git push --dry-run`
   giving a false pass · L-007's incremental-build silence) SHALL appear with its
   concrete detail intact — the specific artifact, the specific wrong outcome.

**Independent Test**: `wc -l CLAUDE.md` ≤ 110; count fence openers and their info strings;
walk the inventory table and cite each row; resolve every markdown link target.

---

### P1: The WHY reaches default context ⭐ MVP

**User Story**: As Claude reasoning about a trade-off, I want to know what this system is
for without loading `ROADMAP.md`, so that "is this fast enough?" and "does this survive an
offline device?" are questions I ask unprompted.

**Why P1**: The article names WHY as one of three essential components, and it is the one
the current file is missing entirely. AD-014 makes latency the primary quality attribute;
a session that never reads it optimises for the wrong thing.

**Acceptance Criteria**:

1. WHEN `CLAUDE.md` is read THEN it SHALL state, within the first 20 lines, that the
   system replicates users to Hikvision face-recognition devices.
2. WHEN `CLAUDE.md` is read THEN it SHALL name **latency** from user creation to
   enrolled-on-all-devices as the primary quality attribute, and **fault tolerance to
   offline devices** as the second (per AD-014).
3. WHEN the purpose block is read THEN it SHALL link `.specs/ROADMAP.md` for phases,
   scale targets, and open decisions — and SHALL NOT restate scale numbers, which would
   create a second place for them to go stale.

**Independent Test**: Read the first 20 lines of `CLAUDE.md` cold and answer "what does
this build and what makes it good?" without opening another file.

---

### P2: The hub stops contradicting the decision log

**User Story**: As Claude resuming work, I want `CLAUDE.md` and `STATE.md` § Handoff to
agree on whether branch protection is live, so that I do not have to guess which side is
stale.

**Why P2**: `CLAUDE.md` documents branch protection as **enforced**, quoting a real
`[remote rejected]` response; `STATE.md` § Handoff still lists it under **"Not
configured: nothing mechanically blocks a direct push."** AD-030 was written about
exactly this failure mode — *"a decision log whose entries disagree with the
always-loaded instructions is worse than no log."* Not P1 because it is a pre-existing
defect this feature surfaces rather than causes.

**Acceptance Criteria**:

1. WHEN the live protection state is queried via
   `gh api repos/obogoni/hikvision-replicator/branches/main/protection` THEN the result
   SHALL be recorded, and whichever of the two files disagrees with it SHALL be corrected.
2. WHEN the repair is made THEN it SHALL be confined to the contradicting claim — the
   rest of the stale Handoff stays deferred to `user-registry` per the existing decision.

**Independent Test**: Query live protection; grep both files for the protection claim;
confirm all three agree.

---

### P2: The change is recorded as a decision

**User Story**: As a future feature author, I want the hub-and-spoke split recorded in the
decision log, so that the next person adding a convention knows to put it in a spoke
rather than growing `CLAUDE.md` back to 285 lines.

**Why P2**: Without it, the file re-inflates. AD-029 is the precedent — retiring a
document without recording *why* invites its recreation.

**Acceptance Criteria**:

1. WHEN `.specs/STATE.md` is read THEN it SHALL contain a new `AD-031` in the established
   format (Decision · Reason · Trade-off · Scope · Date · Status).
2. WHEN `AD-031` is read THEN it SHALL state the routing rule for future content: what
   belongs in the hub versus a spoke.
3. WHEN `AD-031` is read THEN its Trade-off section SHALL record that the line budget is
   documentary and unenforced (A-3), consistent with how AD-028 states its own limits.

**Independent Test**: Read `AD-031` and answer "where does a new convention go?"

---

## Edge Cases

- WHEN a rule appears in both `CLAUDE.md` and `.specs/STATE.md` (e.g. AD-025's protection
  payload) THEN the spoke SHALL link the AD rather than copy it — one authority per rule.
- WHEN a spoke would contain only two or three lines THEN it SHALL be absorbed into an
  existing spoke instead (A-6), not created — a spoke too thin to justify a hop is worse
  than an inline line.
- WHEN a rule is genuinely universal but currently buried in a long section (e.g. "base
  every PR on `main`") THEN it SHALL be **promoted** into the hub even though its section
  is being demoted — the split is by relevance, not by section boundary.
- WHEN the `docs/test-patterns.md` link in `.specs/STATE.md` (written as `../docs/`) is
  resolved THEN it SHALL still work — the spoke is extended, never moved or renamed.

---

## Rule Inventory (the content-preservation contract)

Every imperative currently in `CLAUDE.md`, and where it must land. AC-3 verifies each row
resolves to a `file:line`. **H** = hub keeps the imperative; **H→** = hub keeps a one-line
pointer, spoke keeps the detail; **→** = spoke only.

| # | Rule | Destination |
|---|---|---|
| R-01 | Stack: .NET 10 · Minimal APIs · EF Core 10 + PostgreSQL · AES-256 | H |
| R-02 | Docker is required to run the app or integration tests (AD-018) | H |
| R-03 | No job runner is in the solution; decided Phase 2 (AD-030) | H |
| R-04 | Never commit directly to `main`; branch first | H |
| R-05 | Branch naming `<type>/<kebab-slug>` | H |
| R-06 | Conventional Commits; type table; scopes in use | H→ `git-workflow.md` |
| R-07 | One atomic commit per task; never batch | H |
| R-08 | Merge via squash PR; PR title must be a valid conventional subject | H→ `git-workflow.md` |
| R-09 | **Base every PR on `main`** + the PR #2/#4 stranding narrative | H→ `git-workflow.md` |
| R-10 | Verify `main` after any merge, with the verification command | → `git-workflow.md` |
| R-11 | `git push --dry-run` does not test branch protection | → `git-workflow.md` |
| R-12 | Branch protection is enforced; `enforce_admins=true`; rejection output | H→ `git-workflow.md` |
| R-13 | `build-and-test` required, `strict=true`; no approval required | → `git-workflow.md` |
| R-14 | Repo settings enforced via `gh repo edit`; how to query live protection | → `git-workflow.md` |
| R-15 | Verifier runs as a fresh sub-agent after the last task (AD-028) | H |
| R-16 | Author ≠ verifier; evidence-or-zero | H |
| R-17 | Standalone fallback is a deviation to declare | H |
| R-18 | The `code-style-enforcement` AC-1 contradiction narrative | → `STATE.md` AD-028 (A-5) |
| R-19 | Project structure tree | H |
| R-20 | Core commands (compose · restore · build · ef · run · test) | H |
| R-21 | Docker-free gate and full gate commands | H |
| R-22 | Migrations self-apply at startup | H |
| R-23 | E2E needs no browser download and no `pwsh`; `E2E_BASE_URL` override | → `test-patterns.md` |
| R-24 | `.editorconfig` is the single source of style; build is the gate; `IDE0055` is an error | H→ `code-style.md` |
| R-25 | **Never bare `dotnet format`** + the `[Obsolete]`-stamping incident | H→ `code-style.md` |
| R-26 | Use `dotnet format whitespace` (whole solution / one folder) | H→ `code-style.md` |
| R-27 | L-007: incremental builds re-report zero diagnostics; use `--no-incremental` | H→ `code-style.md` |
| R-28 | `AnalysisMode=Recommended` @ `AnalysisLevel 10.0`; 10 CA warnings; ratchet, never `All` | → `code-style.md` |
| R-29 | Severity in `.editorconfig`, never `-warnaserror` (4 NU1903 + 4 CS0618) | → `code-style.md` |
| R-30 | File-scoped namespaces; primary constructors where appropriate | → `code-style.md` |
| R-31 | Endpoints grouped via `MapGroup` + `MapXxxEndpoints()` | → `slice-anatomy.md` |
| R-32 | DTOs separate from entities; never shared between features (AD-004) | → `slice-anatomy.md` |
| R-33 | Passwords AES-256 on write; never returned in responses (AD-008) | → `slice-anatomy.md` |
| R-34 | `OneOf` for all fallible operations; standalone error records, no base class | → `slice-anatomy.md` |
| R-35 | Domain / service / endpoint layer return-type rules; `.Match()` naming | → `slice-anatomy.md` |
| R-36 | Three-file slice layout under `Features/{Resource}/{Operation}/` | H→ `slice-anatomy.md` |
| R-37 | The write-path request flow diagram | → `slice-anatomy.md` |
| R-38 | The database is the authority on uniqueness; pre-check is not (AD-022) | → `slice-anatomy.md` |
| R-39 | `ExecuteAsync` takes a required trailing `CancellationToken` | → `slice-anatomy.md` |
| R-40 | Inject `IRepository<T>`, never `AppDbContext`; always use `Specification<T>` | → `slice-anatomy.md` |
| R-41 | EF config via `IEntityTypeConfiguration<T>`, auto-applied | → `slice-anatomy.md` |
| R-42 | Read `docs/test-patterns.md` before writing any test | H |
| R-43 | The project a test lives in declares its level (AD-026) | H |

**Coverage**: 43 rules · **15** keep their imperative in the hub (`H`) · **9** keep a one-line
hub imperative plus a spoke link (`H→`) · **19** are spoke-only (`→`). 15 + 9 + 19 = 43.

**The naming rule** (this is the test AC-4's negative half is judged against):

> A hub line may **name** what a spoke covers. It may **state** a rule only when that rule is
> load-bearing for a rule the hub itself carries. Naming is the default; `H→` must be earned.

> **Amended 2026-08-20, after Verifier iteration 1, then corrected after iteration 2.**
>
> Iteration 1 caught the hub restating three rules the inventory marked `→`, and caught that
> AC-4's negative half was never checked by the gate. The author's response was to re-mark all
> three `→ → H→`. Iteration 2 rejected that: it found a **fourth** breach the re-mark had missed
> (R-38), and judged the handling *"convenient, not principled — in each case the author made the
> smaller edit"*, since R-11 had been fixed by rewording the hub while R-12/R-32/R-34 were fixed
> by rewriting the spec. That criticism is correct and is the reason the naming rule above now
> exists instead of a case-by-case judgement.
>
> Applying it: **R-32, R-34 and R-38 are reverted/left at `→` and the hub was reworded to name
> them** — "DTO boundaries, the result pattern … and where uniqueness is really enforced" names
> three spoke topics without stating any of them. **Only R-12 stays `H→`**, because it is the one
> that passes the load-bearing test: it is the teeth behind R-04, and "never commit directly to
> `main`" without "the server rejects the push" reads as a convention rather than a hard stop —
> a rule this repository has already broken. Two of the three promotions are therefore withdrawn,
> which is the larger edit, not the smaller one.

---

## Requirement Traceability

| ID | Story | Status |
|---|---|---|
| CTX-01 | P1: Hub fits budget — ≤ 110 lines, ≤ 8 fenced blocks, shell only | Implementing |
| CTX-02 | P1: Hub fits budget — all 43 inventory rules resolve to a `file:line` | Implementing |
| CTX-03 | P1: Hub fits budget — `H→` rules keep a one-line hub pointer; every spoke is linked | Implementing |
| CTX-04 | P1: Hub fits budget — every referenced relative path exists | Implementing |
| CTX-05 | P1: Hub fits budget — four failure narratives survive with concrete detail | Implementing |
| CTX-06 | P1: WHY in default context — purpose stated in first 20 lines | Implementing |
| CTX-07 | P1: WHY in default context — latency primary, fault tolerance second | Implementing |
| CTX-08 | P1: WHY in default context — links ROADMAP, does not restate scale numbers | Implementing |
| CTX-09 | P2: Contradiction repair — protection claim agrees with live state | Implementing |
| CTX-10 | P2: Contradiction repair — confined to the contradicting claim | **Breached — declared (A-9)** |
| CTX-11 | P2: AD-031 recorded in the established format | Implementing |
| CTX-12 | P2: AD-031 states the hub-vs-spoke routing rule | Implementing |
| CTX-13 | P2: AD-031 records the budget as documentary and unenforced | Implementing |

---

## Success Criteria

- [ ] `CLAUDE.md` ≤ 110 lines with 43/43 inventory rules citable.
- [ ] A reader of only `CLAUDE.md` can state what the product does and what makes it good.
- [ ] Every `H→` rule is still named in the hub, and every spoke is reachable from it — no silent removals.
- [ ] `AD-031` answers "hub or spoke?" for the next convention without re-deriving it.

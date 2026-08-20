# Context Engineering Validation

**Date**: 2026-08-20
**Spec**: `.specs/features/context-engineering/spec.md`
**Diff range**: `5c8d55d..fa05813` (branch `docs/context-engineering`, base `origin/main`)
**Verifier**: independent sub-agent, **iteration 2** (author ≠ verifier; no context from iteration 1's Verifier)

**Verdict**: ❌ **FAIL** — narrowly, and for different reasons than iteration 1.

---

## Iteration History

| Iter | Verdict | Headline |
|---|---|---|
| 1 | ❌ FAIL | 6/14 sensor mutants survived · Edge Case 1 breached (six AD statements transcribed into `docs/slice-anatomy.md`) · CTX-02 R-20 `dotnet restore` resolved nowhere · AC-4 negative half breached ×3 and never checked · CTX-10 overreach · stale AC-2 wording in `tasks.md` |
| 2 | ❌ FAIL | Five of the six reported gaps are genuinely closed. **Edge Case 1 is still breached at four new sites** the author's regression checks cannot see; **CTX-10 is declared (A-9) rather than met**; the hardened gate is **over-fitted to iteration 1's literal mutants** — 12 of my 25 independent must-kill mutants survive it, including inversions of AD-030, `enforce_admins`, and the never-batch rule |

Everything below was re-derived from `spec.md`, `git show origin/main:CLAUDE.md`, the live GitHub API,
and the files themselves. The author's gate script was **audited as an artifact, never used as evidence**.

---

## Task Completion

| Task | Status | Notes |
|---|---|---|
| T1 Git workflow spoke | ✅ Done | `2d3fbab` |
| T2 Code style spoke | ✅ Done | `313dace` |
| T3 Slice anatomy spoke | ✅ Done | `5263da6`, amended by `fa05813` |
| T4 E2E notes into test-patterns | ✅ Done | `ad82293` |
| T5 Hub rewrite | ✅ Done | `2843660`, amended by `fa05813` |
| T6 AD-031 | ✅ Done | `c000aab` |
| T7 Handoff repair | ⚠️ Partial | `ddc9e75` — CTX-10 breached, declared as A-9 rather than fixed |
| (fix) | ⚠️ Bookkeeping | `fa05813` is recorded in no task. **All seven `**Committed**:` SHAs are stapled to T1**; T2–T7 carry none (`tasks.md:130–142`) |

Build gate (`tasks.md` § Gate Check Commands, Build level), re-run by me:
`dotnet build HikvisionReplicator.slnx` → **0 errors, 14 warnings**; `dotnet test src/HikvisionReplicator.Tests` →
**81 passed, 0 failed, 0 skipped** — exactly the expected count. `git diff --name-only 5c8d55d..HEAD | grep ^src/` → **0 files**.

---

## Spec-Anchored Acceptance Criteria

### P1 — The hub fits its budget without losing a rule

| Criterion | Spec-defined outcome | `file:line` + check I ran | Result |
|---|---|---|---|
| AC-1a `CLAUDE.md` ≤ 110 lines | ≤ 110 | `CLAUDE.md` — `wc -l` = **110** | ✅ PASS (at the ceiling, 0 headroom) |
| AC-1b ≤ 8 fenced blocks, each shell **or** the project-structure tree; no C#, no ASCII flow diagram | ≤ 8; info string ∈ {shell, `text`-as-tree} | `CLAUDE.md:27` ```` ```text ```` (tree: `^src/` + `├──`), `:42` ```` ```bash ````, `:53` ```` ```bash ```` → **3 blocks**; no `csharp` fence; no `HTTP …/api/` or `→ …()` inside any fence | ✅ PASS |
| AC-2 / CTX-02 all 43 inventory rules resolve to a `file:line` | 43/43 citable | Full table below — **43/43** | ✅ PASS |
| AC-4a `H→` rules keep a one-line hub imperative + spoke link | 11 rules | R-06 `CLAUDE.md:71`, R-08 `:70`, R-09 `:69`, R-12 `:65`, R-24 `:79`, R-25 `:82`, R-26 `:80`, R-27 `:83`, R-32 `:91`, R-34 `:92`, R-36 `:91` — **11/11**, each with its spoke link at `:75`, `:87`, `:94` | ✅ PASS |
| AC-4b `→` rules NOT restated in the hub | 17 rules absent from `CLAUDE.md` | 16/17 clean. **R-38**: `CLAUDE.md:92–93` states the proposition — *"why the database rather than the pre-check is the authority on uniqueness"* | ⚠️ Borderline breach (see Gap 5) |
| AC-4c every spoke reachable from the hub | 4 links | `CLAUDE.md:75`, `:87`, `:94`, `:98` | ✅ PASS |
| AC-5 every relative path resolves | all exist | `.specs/ROADMAP.md`, `.specs/STATE.md`, `docs/git-workflow.md`, `docs/code-style.md`, `docs/slice-anatomy.md`, `docs/test-patterns.md` — **6/6 `os.path.exists`** | ✅ PASS |
| AC-6 four failure narratives keep concrete detail | artifact + wrong outcome named | PR #2/#4: `docs/git-workflow.md:58–60` (*"showed a 'Merged' badge … landed on the intermediate branch"*) · `[Obsolete]` stamping: `docs/code-style.md:28–32` (`PostgreSqlBuilder`, `PostgresFixture`, `UnreachableDatabaseFixture`) · dry-run: `docs/git-workflow.md:84–85` (*"sends no pack"*) · L-007: `docs/code-style.md:38–47` (`--no-incremental`) | ✅ PASS |

### P1 — The WHY reaches default context

| Criterion | Spec-defined outcome | `file:line` + check I ran | Result |
|---|---|---|---|
| CTX-06 purpose in first 20 lines | replicates users to Hikvision face-recognition devices | `CLAUDE.md:5` — *"Live replication of users to Hikvision facial-recognition access devices"* | ✅ PASS |
| CTX-07 latency **primary**, offline tolerance **second** | per AD-014 | `CLAUDE.md:9–10` — *"**Latency** … is the primary quality attribute; **surviving individual offline readers** is the second (AD-014)"*; matches `.specs/STATE.md:116` | ✅ PASS |
| CTX-08 links ROADMAP, restates no scale numbers | link present; no figures | `CLAUDE.md:13` links `.specs/ROADMAP.md`; regex for `\d{1,3}(,\d{3})+`, `\d+ ?(GB\|KB\|MB)`, `p\d\d < \d+s` over the hub → **0 hits** | ✅ PASS |

### P2 — The hub stops contradicting the decision log

| Criterion | Spec-defined outcome | `file:line` + check I ran | Result |
|---|---|---|---|
| CTX-09 live protection recorded; the disagreeing file corrected | all three agree | Live `gh api …/branches/main/protection`, queried by me: `required_status_checks.contexts == ["build-and-test"]`, `strict == True`, `enforce_admins.enabled == True`, `required_approving_review_count == 0`, `required_linear_history == True`. Matches `.specs/STATE.md:353` and `CLAUDE.md:65` / `docs/git-workflow.md:70–91` | ✅ PASS |
| CTX-10 repair confined to the contradicting claim | one claim touched | `.specs/STATE.md:337` — the **Next step** bullet also retired *"merge the `code-style-enforcement` PR"*, which is not a protection claim | ❌ **Breached; declared as A-9, not fixed** |

### P2 — The change is recorded as a decision

| Criterion | Spec-defined outcome | `file:line` + check I ran | Result |
|---|---|---|---|
| CTX-11 AD-031 in the six-field format | Decision · Reason · Trade-off · Scope · Date · Status | `.specs/STATE.md:316–327` — all six present, `Status: active` | ✅ PASS |
| CTX-12 states the hub-vs-spoke routing rule | actionable three-way rule | `.specs/STATE.md:318–320` — hub / hub-one-liner+spoke / spoke-only, each with a criterion and examples | ✅ PASS |
| CTX-13 **Trade-off** records the budget as documentary and unenforced | inside the Trade-off field | `.specs/STATE.md:324` — *"the ≤ 110-line ceiling is documentary only. No repository setting, build step or CI check measures it"* | ✅ PASS |
| — | — | Same line: *"it landed at 109"*. **It is 110** since `fa05813` | ⚠️ Stale figure (Gap 4) |

**Status**: ❌ 1 AC breached-and-declared (CTX-10), 1 borderline (AC-4b / R-38), 1 stale figure inside a passing AC.

---

## Rule Inventory — 43/43 re-derived independently

Re-derived against `git show origin/main:CLAUDE.md` (285 lines). Taxonomy arithmetic re-counted from the
table itself: **15 `H` + 11 `H→` + 17 `→` = 43**, no duplicate IDs, no gaps in `R-01…R-43`. ✅ Confirmed.

| # | Hub `file:line` | Spoke `file:line` |
|---|---|---|
| R-01 | `CLAUDE.md:18–19` | — |
| R-02 | `CLAUDE.md:21–22` | — |
| R-03 | `CLAUDE.md:22–23` | — |
| R-04 | `CLAUDE.md:65–66` | — |
| R-05 | `CLAUDE.md:67` | — |
| R-06 | `CLAUDE.md:71`, ptr `:74` | `docs/git-workflow.md:19–38` |
| R-07 | `CLAUDE.md:71–72` | — |
| R-08 | `CLAUDE.md:70–71` | `docs/git-workflow.md:40–48` |
| R-09 | `CLAUDE.md:69–70` | `docs/git-workflow.md:53–64` |
| R-10 | ptr `CLAUDE.md:74` | `docs/git-workflow.md:64–68` |
| R-11 | ptr `CLAUDE.md:74–75` | `docs/git-workflow.md:84–85` |
| R-12 | `CLAUDE.md:65–66` | `docs/git-workflow.md:70–82` |
| R-13 | — | `docs/git-workflow.md:50–51`, `:93–98` |
| R-14 | — | `docs/git-workflow.md:89–91`, `:100–114` |
| R-15 | `CLAUDE.md:107` | — |
| R-16 | `CLAUDE.md:108–109` | — |
| R-17 | `CLAUDE.md:109–110` | — |
| R-18 | — | `.specs/STATE.md:291` (AD-028, per A-5) |
| R-19 | `CLAUDE.md:27–38` | — |
| R-20 | `CLAUDE.md:43` compose · `:44` restore · `:55` build · `:49` ef · `:45` run · `:55`/`:60` test | — |
| R-21 | `CLAUDE.md:53–61` | — |
| R-22 | `CLAUDE.md:48–49` | — |
| R-23 | — | `docs/test-patterns.md:54–64` |
| R-24 | `CLAUDE.md:79–80` | `docs/code-style.md:6–14` |
| R-25 | `CLAUDE.md:82–83` | `docs/code-style.md:23–34` |
| R-26 | `CLAUDE.md:80` | `docs/code-style.md:18–21` |
| R-27 | `CLAUDE.md:83–85` | `docs/code-style.md:36–47` |
| R-28 | ptr `CLAUDE.md:87` | `docs/code-style.md:53–60` |
| R-29 | — | `docs/code-style.md:62–67` |
| R-30 | — | `docs/code-style.md:69–72` |
| R-31 | — | `docs/slice-anatomy.md:23` |
| R-32 | `CLAUDE.md:91` | `docs/slice-anatomy.md:24–26` |
| R-33 | — | `docs/slice-anatomy.md:85–89` |
| R-34 | `CLAUDE.md:92` | `docs/slice-anatomy.md:28–38` |
| R-35 | — | `docs/slice-anatomy.md:40–53` |
| R-36 | `CLAUDE.md:91` | `docs/slice-anatomy.md:11–21` |
| R-37 | — | `docs/slice-anatomy.md:58–73` |
| R-38 | ⚠️ `CLAUDE.md:92–93` (should be pointer only) | `docs/slice-anatomy.md:75–83` |
| R-39 | — | `docs/slice-anatomy.md:91–95` |
| R-40 | — | `docs/slice-anatomy.md:97–103` |
| R-41 | — | `docs/slice-anatomy.md:105–109` |
| R-42 | `CLAUDE.md:98` | — |
| R-43 | `CLAUDE.md:101–103` | — |

**Result: 43/43 resolve.** Iteration 1's `dotnet restore` gap is closed (`CLAUDE.md:44`), as is the
migrations location (`CLAUDE.md:48`).

**One fidelity loss found by re-reading the old file line by line:** the pre-change hub carried
`dotnet ef database update --project src/HikvisionReplicator.Api` (`origin/main:CLAUDE.md:125`). The
`--project` argument survives in **no file** — `CLAUDE.md:49` prints the bare command, which cannot run
from the repository root against a `.slnx`. R-20 is marked `H`, so the hub is where it should be.

---

## Discrimination Sensor

**Method.** Throwaway `git worktree` at `fa05813`; every mutation asserted to match its anchor
**exactly once** before being applied (the wrong-path false-pass guard from `.specs/STATE.md:342`);
each mutant scored by the *set difference* of failing checks against a recorded baseline, so a kill
by an unrelated check is not counted as a kill by the intended check. Two gates run per mutant:

- **A — the author's `doc_gate.py`** (128 checks; baseline 128/128 PASS), audited as an artifact.
- **B — my own gate**, written from `spec.md` alone before reading A (495 checks; baseline 494/495 —
  the one baseline failure is Gap 1 below). B asserts the **positive semantic shape** of each rule
  (subject + polarity + object) rather than blacklisting negative phrases, so an inversion fails by
  *not matching* instead of by matching a phrase someone predicted.

Real tree confirmed clean afterwards (`git status --short` shows only the pre-existing
`.specs/LESSONS.md` / `lessons.json` edits and this file); worktree removed and pruned.

| # | Mutation | Target | Gate A | Gate B |
|---|---|---|---|---|
| C1 | **must-kill control** — gut the never-commit-to-`main` rule | `CLAUDE.md:65` | ✅ Killed | ✅ Killed |
| C2 | **must-survive control** — cosmetic whitespace on a heading | `CLAUDE.md:63` | ✅ Survived (correct) | ✅ Survived (correct) |
| M01 | Throughput becomes primary, latency second | `CLAUDE.md:9` | ✅ Killed | ✅ Killed |
| M02 | Restate a scale number as **10 GB** (not the `50k`/`10k` literals) | `CLAUDE.md:13` | ❌ **Survived** | ✅ Killed |
| M03 | Invert AD-030 — *"**Hangfire is the job runner** … already in the solution"* | `CLAUDE.md:22` | ❌ **Survived** | ✅ Killed |
| M04 | Docker becomes optional | `CLAUDE.md:21` | ✅ Killed | ✅ Killed |
| M05 | Migrations self-apply *"only in Development"* (conditional weakening) | `CLAUDE.md:48` | ❌ **Survived** | ✅ Killed |
| M06 | Invert `enforce_admins` — *"being the repo owner **exempts** you"* | `CLAUDE.md:65` | ❌ **Survived** | ✅ Killed |
| M07 | *"batching a feature's tasks into a single commit is acceptable"* | `CLAUDE.md:71` | ❌ **Survived** | ✅ Killed |
| M08 | Protection becomes conditional — *"rejects the push **only until CI reports**"* | `CLAUDE.md:65` | ❌ **Survived** | ❌ **Survived** |
| M09 | Delete `dotnet restore` (iteration 1's R-20 gap) | `CLAUDE.md:44` | ✅ Killed | ✅ Killed |
| M10 | Strip R-42's *"before writing any test"* | `CLAUDE.md:98` | ✅ Killed | ✅ Killed |
| M11 | Strip R-36/R-32/R-34 hub imperatives to a bare link (iteration 1's M5b) | `CLAUDE.md:91` | ✅ Killed | ✅ Killed |
| M12 | AC-4 breach: password rule into the hub (+1 line) | `CLAUDE.md:94` | ⚠️ Killed **by the ≤110 budget check**, not the AC-4 check | ✅ Killed |
| M12b | Same breach, **line-neutral** | `CLAUDE.md:92` | ⚠️ Killed by collateral damage to R-34's pointer | ✅ Killed by `R-33 not restated in hub` |
| M13b | AC-4 breach: `IEntityTypeConfiguration<T>` auto-discovery into the hub, line-neutral | `CLAUDE.md:99` | ❌ **Survived** | ✅ Killed |
| M22 | AC-4 breach: analyzer ratchet rule into the hub, line-neutral | `CLAUDE.md:87` | ❌ **Survived** | ✅ Killed |
| M14 | AC-2 breach: retag the commands block ```` ```bash ```` → ```` ```text ```` (not the tree) | `CLAUDE.md:42` | ❌ **Survived** | ✅ Killed |
| M15 | AC-5 breach: rot the `code-style.md` link target | `CLAUDE.md:87` | ✅ Killed | ✅ Killed |
| M16 | AC-6: keep "PR #2 and PR #4", delete the concrete wrong outcome | `docs/git-workflow.md:58` | ❌ **Survived** | ✅ Killed |
| M17 | AC-6: delete *why* `--dry-run` gives a false pass | `docs/git-workflow.md:84` | ✅ Killed | ✅ Killed |
| M18 | Spoke endorses bare `dotnet format` while keeping the "Never use" heading | `docs/code-style.md:34` | ❌ **Survived** | ✅ Killed |
| M19 | Edge Case 1: transcribe **AD-006's Reason verbatim** into a spoke | `docs/slice-anatomy.md:102` | ❌ **Survived** | ✅ Killed |
| M20 | CTX-13: move the "documentary only" clause out of Trade-off into Reason | `.specs/STATE.md:324` | ✅ Killed | ✅ Killed |
| M21 | CTX-09: regress the Handoff to "Not configured" | `.specs/STATE.md:353` | ✅ Killed | ✅ Killed |

**Sensor depth**: P0-full (25 must-kill mutations + 1 must-survive control).

**Result — Gate A (the author's doc-gate, and the *only* mechanical evidence `tasks.md` designates
for every AC): 13/25 killed, 12 survived.** Gate B: **24/25 killed**, 1 survived.

### Why the survivors matter — the gate is over-fitted to iteration 1's literal mutants

Every survivor traces to a check written against the exact string a previously-reported mutant used:

| Check in `doc_gate.py` | Pattern | Defeated by |
|---|---|---|
| "hub does not deny migrations self-apply" | `migrations are not applied\|not applied at startup\|never applied at startup` | M05 — a *qualifier* instead of a negation |
| "hub does not deny that protection is enforced" | `…\|nothing on the server enforces\|…` (iteration 1's M14 verbatim) | M06, M08 |
| "hub does not demote latency" | `latency is (not\|NOT)` | M01 killed only because the positive check happened to match; the demotion check itself is a phrase blacklist |
| "code-style does not endorse bare `dotnet format`" | `bare \`dotnet format\` is (fine\|safe)` — **case-sensitive** | M18 — the sentence starts *"Bare …"* |
| "code-style does not endorse `AnalysisMode=All`" | `(jump\|go) (straight )?to \`?AnalysisMode=All` | any other endorsement |
| Edge Case 1 (6 checks) | the six literal phrases iteration 1 named | M19 — any *other* AD sentence |
| AC-4 negative (17 checks) | one token per rule, lifted from the spoke's current wording | M13b, M22 — the same rule, worded differently |
| AC-2 fence class | `allowed = {bash, sh, console, text}` — `text` is blanket-allowed | M14 |
| CTX-05 narratives | the artifact name only, never the wrong outcome | M16 |

The spec's own defence of the `fa05813` re-marking says *"the load-bearing part of this fix is … that the
gate now enforces the negative half for all 17 remaining `→` rules."* That claim does not hold: the 17
checks are single-token greps against the wording currently in the spokes, and two line-neutral AC-4
breaches (M13b, M22) pass all 17. The one AC-4 mutant Gate A did kill (M12b) died from collateral
damage to a different check.

A second structural problem: the hub now sits at **exactly** 110 lines, so the `≤ 110` check fires on
*any* addition. That masks weak content checks — M12 and M13 register as kills for a reason unrelated
to the fault injected. Mutants must be line-neutral for this gate to say anything.

---

## Edge Cases

- [ ] **Edge Case 1 — a spoke links an AD rather than copying it: still BREACHED.** The six phrases
      iteration 1 named are gone (verified: `grep` over `docs/` for all six returns nothing). The
      **class** is not. A generic 12-gram check of every line the feature *added* to `docs/`, against
      `.specs/STATE.md` minus `origin/main:CLAUDE.md`, finds:
      - `docs/code-style.md:49–51` — near-verbatim reproduction of `.specs/STATE.md:340`
        (*"never take a warning census from a build that did not succeed … first measured as 3
        findings; the true number is 10"*). **Not in the pre-change `CLAUDE.md` and not in the
        43-rule inventory.**
      - `docs/code-style.md:57` — *"seven of them in `IntegrationTests`"* ← `.specs/STATE.md:347`
      - `docs/code-style.md:64` — *"SSH.NET 2025.1.0, high severity, transitive via Testcontainers"* ← `.specs/STATE.md:346`
      - `docs/git-workflow.md:110–114` — the `gh repo view --json deleteBranchOnMerge,…` block ← `.specs/STATE.md:352`

      None of the four is inventoried, so this is also **new content shipped by a feature whose spec
      § Out of Scope says it "relocates and compresses; it does not legislate."**
- [x] **Edge Case 2 — a two-or-three-line spoke is absorbed instead of created.** The E2E notes went
      into the existing `docs/test-patterns.md:47–64` (A-6); no fifth spoke created.
- [x] **Edge Case 3 — a universal rule buried in a demoted section is promoted.** "Base every PR on
      `main`" is in the hub at `CLAUDE.md:69` while the rest of AD-025 sits in the spoke.
- [x] **Edge Case 4 — `../docs/test-patterns.md` from `.specs/STATE.md` still resolves.** File not
      renamed or moved; `.specs/STATE.md:92` and `:156` resolve to `docs/test-patterns.md`.

---

## Judgement on the three spec amendments

**Amendment 1 — AC-2, before implementation (`fb58165`).** *"every fenced block SHALL be a shell command
block"* → *"a shell command block **or the project-structure tree** — no C#, and no ASCII request-flow
diagram."* **Not a lowering.** The original contradicted the same spec's R-19, which routes the
project-structure tree to the hub as `H`; the amendment resolved an internal contradiction and *added*
two explicit prohibitions the original lacked. Recorded in place, before any code. This is the model.
Caveat: the amended wording is only as strong as its checker, and the author's implementation of it
(`allowed = {…, "text"}`) turned "or the project-structure tree" into "or any `text` block" — M14.

**Amendment 2 — AC-4, before implementation (`fb58165`).** *"every demoted rule keeps a one-line hub
pointer"* → the three-way taxonomy. This **is** a relaxation in one direction (28 pointer lines → 11) —
but it too resolved a contradiction with the inventory in the same document, and it **added** a negative
obligation (*"the hub SHALL NOT restate it"*) the original never had. The tightened half is precisely
what the author then failed. Net: **not a lowering.**

**Amendment 3 — R-12 / R-32 / R-34 re-marked `→` → `H→`, in `fa05813`, *after* the Verifier caught the
breach.** My verdict: **the bar was lowered — mildly, incompletely, and with unusually honest disclosure.**

- No AC *sentence* changed, but AC-3 and AC-4 are evaluated **against the inventory table**, so
  rewriting three of its rows after the fact is a goalpost move in substance. The blockquote at
  `spec.md:256–272` states the timing plainly and calls its own judgement suspect, which is the right
  form for it, and the substantive case (R-12 is the teeth behind R-04; R-32/R-34 are the hooks that
  make the pointer worth following) is genuine — all three do have the `H→` shape under AD-031's own
  routing rule.
- **The asymmetry with R-11 is convenient, not principled.** R-11 (`--dry-run` gives a false pass) is a
  *hazard producing a false signal* — the exact category AD-031's middle rung is defined for
  (`.specs/STATE.md:319`). Nothing in the routing rule explains why R-12 was promoted and R-11 demoted;
  in each case the author chose the smaller edit. The R-11 choice was the stricter one, so it lowers
  nothing on its own, but the pair reads as post-hoc rather than derived.
- **The "mis-filed at inventory time" defence is incomplete.** I found a fourth: `CLAUDE.md:92–93`
  states R-38's proposition while R-38 remains `→`. So the `H→`/`→` boundary was not drawn crisply
  three times — it was drawn loosely across the whole inventory, and one instance is still wrong. The
  author's gate cannot see it (its R-38 token is `23505|named .*index`).
- **The claim the amendment leans on is not supported.** *"The gate now enforces the negative half for
  all 17 remaining `→` rules"* is true in form only; M13b and M22 pass all 17.

**Amendment 4 (not asked about, but it is one) — A-9.** Declaring the CTX-10 overreach rather than
reverting it is the **right call on the merits** — the Handoff's "Next step" bullet genuinely *contained*
the protection claim (`origin/main:.specs/STATE.md` Next step: *"…until then CI reports but does not
block"*), so it had to be touched, and re-instating "merge the `code-style-enforcement` PR" would print
a falsehood. But the AC is still **not met**, and this report records it as breached-and-declared, not
as a pass. A cleaner repair existed (keep the bullet, drop only the protection clause).

---

## Code Quality

| Principle | Status |
|---|---|
| Minimum code | ✅ |
| Surgical changes | ⚠️ Four un-inventoried paragraphs imported from `.specs/STATE.md` into two spokes (Edge Case 1) |
| No scope creep | ⚠️ Same finding — spec § Out of Scope says this feature does not legislate |
| Matches patterns | ✅ Spokes follow `docs/test-patterns.md`'s shape; AD-031 follows AD-029's format |
| Spec-anchored outcome check | ✅ Re-derived independently; CTX-10 flagged, R-38 flagged |
| Per-layer Coverage Expectation met | ❌ `tasks.md:36–38` promises *"every acceptance criterion is discharged by a mechanical, binary check"*; the gate's checks are not discriminating for 12 of 25 behaviour-level faults |
| Every test maps to a spec requirement | ✅ Every gate check names a CTX/R id |
| Documented guidelines followed | ✅ `CLAUDE.md`, `docs/test-patterns.md`, `.specs/STATE.md` AD-024/026/028 |

---

## Gate Check

- **Build gate** (`tasks.md` Build level): `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- **Result**: 0 errors, 14 warnings; **81 passed, 0 failed, 0 skipped**
- **Test count before / after**: 81 / 81 — delta 0, as `tasks.md:70` requires. No test deleted, no assertion weakened.
- **Doc gate** (author's, audited not trusted): 128/128 — see the sensor section for what that number is worth.

---

## Ranked Gaps

1. **[Major] Edge Case 1 is still breached — four un-inventoried transcriptions from `.specs/STATE.md`
   into spokes.** `docs/code-style.md:49–51`, `:57`, `:64`; `docs/git-workflow.md:110–114`. Fix: cut them,
   or link `.specs/STATE.md` § Handoff / AD-027 instead. The largest (`:49–51`) reproduces
   `.specs/STATE.md:340` almost word for word.
2. **[Major] The doc-gate is not discriminating and is over-fitted to iteration 1's reported mutants.**
   12/25 independent must-kill mutants survive, including inversions of AD-030 (M03), `enforce_admins`
   (M06) and never-batch (M07), and two line-neutral AC-4 breaches (M13b, M22). Fix: replace phrase
   blacklists with positive assertions of each rule's imperative; make the AC-2 `text` allowance require
   the block actually be the structure tree; make the Edge-Case-1 check generic (n-gram overlap with
   `.specs/STATE.md` minus `origin/main:CLAUDE.md`) instead of six literals; add the case-insensitive flag.
3. **[Minor] CTX-10 is breached, declared (A-9) rather than met.** `.specs/STATE.md:337`. Accept the
   deviation or make the narrower repair; either way the PR must say the AC is not met.
4. **[Minor] `.specs/STATE.md:322` says the hub *"landed at 109"*; it is 110** since `fa05813` — the fix
   spent the last line of headroom and the decision log was not re-measured. Exactly the documentary
   drift A-3 and AD-031's own Trade-off warn about.
5. **[Minor] R-38 is inventoried `→` but its proposition is stated in the hub** (`CLAUDE.md:92–93`) —
   the same mis-filing class `fa05813` claimed to have closed, still open and still unchecked.
6. **[Minor] `--project src/HikvisionReplicator.Api` was lost** from the `dotnet ef database update`
   command; `CLAUDE.md:49` now prints a command that cannot run from the repo root. R-20 is `H`.
7. **[Cosmetic] `tasks.md` commit bookkeeping is wrong.** All seven SHAs are stapled under T1
   (`tasks.md:130–142`); T2–T7 record none, and `fa05813` is recorded nowhere.
8. **[Cosmetic] `.specs/LESSONS.md` and `.specs/lessons.json` carry uncommitted iteration-1 edits.**

---

## Requirement Traceability Update

| Requirement | Previous | New |
|---|---|---|
| CTX-01 | Implementing | ✅ Verified |
| CTX-02 | Implementing | ✅ Verified (43/43) |
| CTX-03 | Implementing | ✅ Verified (11/11 `H→`, 4/4 spokes linked) |
| CTX-04 | Implementing | ✅ Verified (6/6 paths) |
| CTX-05 | Implementing | ✅ Verified (4/4 narratives) |
| CTX-06 | Implementing | ✅ Verified |
| CTX-07 | Implementing | ✅ Verified |
| CTX-08 | Implementing | ✅ Verified |
| CTX-09 | Implementing | ✅ Verified against the live API |
| CTX-10 | Implementing | ❌ Needs Fix (breached; declared as A-9) |
| CTX-11 | Implementing | ✅ Verified |
| CTX-12 | Implementing | ✅ Verified |
| CTX-13 | Implementing | ✅ Verified (figure at `.specs/STATE.md:322` stale) |
| AC-4 negative half | (unchecked in iter. 1) | ⚠️ 16/17 clean; R-38 restated at `CLAUDE.md:92–93` |
| Edge Case 1 | ❌ (iter. 1) | ❌ Still Needs Fix — new sites |

---

## Summary

**Overall**: ⚠️ **Not ready** — the artifact is close, the evidence behind it is not.

**Spec-anchored check**: 12/13 CTX requirements verified with cited `file:line`; CTX-10 breached-and-declared.
**Inventory**: 43/43 rules resolve; taxonomy 15 + 11 + 17 = 43 confirmed.
**Sensor**: 25 must-kill mutations + 1 must-survive control — author's gate **13/25 killed**, my
independent gate 24/25. Both controls behaved correctly.
**Gate**: build 0 errors, 81/81 unit tests, `src/**` untouched.

**What works**: the hub-and-spoke split itself. 110 lines from 285, every one of the 43 imperatives
still citable, all four failure narratives intact with their concrete artifacts, the WHY in default
context and consistent with AD-014, AD-031 usable as a routing rule, and the branch-protection
contradiction genuinely resolved against the live API. Five of iteration 1's six gaps are closed.

**Issues found**: Edge Case 1 survives in a new set of transcriptions the author's six literal checks
cannot see; the hardened gate buys its 128/128 largely by memorising the last round's mutants; the
third spec amendment moved three inventory rows to match shipped work and missed a fourth.

**Next steps**: gaps 1 and 2 are the blockers. Gap 2 is the one that matters beyond this feature — a
gate written to pass the mutants you were shown is a regression test, not a sensor.

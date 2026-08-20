# Context Engineering Validation

**Date**: 2026-08-20
**Spec**: `.specs/features/context-engineering/spec.md`
**Diff range**: `5c8d55d..44a9420` (branch `docs/context-engineering`, base `origin/main`)
**Verifier**: independent sub-agent, **iteration 3 — final** (author ≠ verifier; no context carried from
the iteration-1 or iteration-2 Verifiers beyond their written reports)

**Verdict**: ✅ **PASS**, with one declared breach (CTX-10) and one Major finding that is about the
author's *evidence apparatus*, not about what ships.

---

## Iteration History

| Iter | Verdict | Headline |
|---|---|---|
| 1 | ❌ FAIL | 6/14 sensor mutants survived · Edge Case 1 breached (six AD statements transcribed into `docs/slice-anatomy.md`) · R-20 `dotnet restore` resolved nowhere · AC-4's negative half breached ×3 and never checked · CTX-10 overreach · stale AC-2 wording in `tasks.md` |
| 2 | ❌ FAIL | Five of six gaps closed, but Edge Case 1 breached at **four new sites**; the hardened gate was **over-fitted to iteration 1's literal mutants** (12 of 25 independent must-kill mutants survived); a **fourth** AC-4 breach (R-38) found; the R-12/R-32/R-34 re-marking judged *"convenient, not principled"*; CTX-10 declared not fixed; `AD-031` quoted 109 when the hub was 110; `--project` lost |
| 3 | ✅ PASS | **Every artifact-level gap from iterations 1 and 2 is closed and independently re-verified.** 43/43 rules cite; taxonomy 15/9/19 confirmed row by row; AC-4's negative half is clean for all 19 `→` rules including paraphrase; Edge Case 1's four sites are gone with nothing equivalent put back; the naming rule is a real test and it was applied **against** the author. The doc-gate is **still over-fitted** — now to iteration 2's list — but it ships nowhere and is not the evidence this report rests on |

Everything below was re-derived by me from `spec.md`, `git show origin/main:CLAUDE.md`, the live GitHub
API, and the files themselves. The author's gate script was **audited as an artifact and mutated as a
subject — never used as evidence.**

---

## Task Completion

| Task | Status | Notes |
|---|---|---|
| T1 Git workflow spoke | ✅ Done | `2d3fbab` |
| T2 Code style spoke | ✅ Done | `313dace`, amended by `44a9420` |
| T3 Slice anatomy spoke | ✅ Done | `5263da6`, amended by `fa05813` |
| T4 E2E notes into test-patterns | ✅ Done | `ad82293` (insertion-only, 19 lines) |
| T5 Hub rewrite | ✅ Done | `2843660`, amended by `fa05813`, `44a9420` |
| T6 AD-031 | ✅ Done | `c000aab` |
| T7 Handoff repair | ✅ Done | `ddc9e75` |
| (fix rounds) | ✅ Recorded | `fa05813` (iter. 1), `44a9420` (iter. 2). **`tasks.md:18` cites `27960df` for iteration 2 — that commit is not on this branch.** Per-task SHAs are correctly unstapled (iteration 2's Gap 7 closed) |

**Build gate**, re-run by me — `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`:
0 errors, 14 warnings; **81 passed, 0 failed, 0 skipped** — exactly the count `tasks.md:71` requires.
`git diff --name-only origin/main..HEAD | grep ^src/` → **0 files**. `git status --porcelain` → clean.

---

## Spec-Anchored Acceptance Criteria

### P1 — The hub fits its budget without losing a rule

| Criterion | Spec-defined outcome | `file:line` + the check I ran | Result |
|---|---|---|---|
| AC-1 `CLAUDE.md` ≤ 110 lines | ≤ 110 | `wc -l CLAUDE.md` = **109** (1 line of headroom; was 110 at iteration 2) | ✅ PASS |
| AC-2 ≤ 8 fenced blocks, each shell **or** the project-structure tree; no C#, no ASCII flow diagram | ≤ 8; info string ∈ {shell, `text`-as-tree} | Fence scan: `CLAUDE.md:27–38` ```` ```text ```` (contains `src/` + `├──` + `HikvisionReplicator.Api/` → is the tree), `:42–46` ```` ```bash ````, `:53–61` ```` ```bash ```` → **3 blocks**. No `csharp`/`cs` info string; `HTTP POST /api/devices` absent from the hub | ✅ PASS |
| AC-3 / CTX-02 all 43 inventory rules resolve to a `file:line` | 43/43 citable | Full table below — **43/43**, re-derived by me against `git show origin/main:CLAUDE.md` | ✅ PASS |
| AC-4a `H→` rules keep a one-line hub imperative + spoke link | 9 rules | R-06 `CLAUDE.md:70–71`, R-08 `:70`, R-09 `:69`, R-12 `:65–66`, R-24 `:79–80`, R-25 `:82–83`, R-26 `:80`, R-27 `:83–85`, R-36 `:91` — **9/9**, spoke links at `:75`, `:87`, `:93` | ✅ PASS |
| AC-4b `→` rules NOT restated in the hub | 19 rules absent from `CLAUDE.md` | **19/19 clean.** Re-checked semantically line by line, not by token grep — including the three sites iteration 2 flagged. `CLAUDE.md:91–92` now reads *"DTO boundaries, the result pattern, the write-path flow, and where uniqueness is really enforced"* — four spoke **topics named**, none stated. R-38's previous restatement (*"why the database rather than the pre-check is the authority"*) is gone | ✅ PASS |
| AC-4c every spoke reachable from the hub | 4 links | `CLAUDE.md:75`, `:87`, `:93`, `:97` | ✅ PASS |
| AC-5 every relative path resolves | all exist | `.specs/ROADMAP.md`, `.specs/STATE.md`, `docs/git-workflow.md`, `docs/code-style.md`, `docs/slice-anatomy.md`, `docs/test-patterns.md` — **6/6 `os.path.exists`** | ✅ PASS |
| AC-6 four failure narratives keep concrete detail | artifact **and** wrong outcome named | PR #2/#4 → `docs/git-workflow.md:58–60` (*"showed a 'Merged' badge … commits landed on the intermediate branch instead of `main`"*) · `[Obsolete]` stamping → `docs/code-style.md:28–32` (`PostgreSqlBuilder`, `PostgresFixture`, `UnreachableDatabaseFixture`, *"The warning disappeared; the underlying deprecated call did not"*) · `--dry-run` → `docs/git-workflow.md:84–85` (*"sends no pack, so the server never evaluates protection"*) · L-007 → `docs/code-style.md:38–47` (*"MSBuild skips the compile entirely and replays the previous result"*) | ✅ PASS |

### P1 — The WHY reaches default context

| Criterion | Spec-defined outcome | `file:line` + the check I ran | Result |
|---|---|---|---|
| CTX-06 purpose in the first 20 lines | replicates users to Hikvision face-recognition devices | `CLAUDE.md:5` — *"Live replication of users to Hikvision facial-recognition access devices"* | ✅ PASS |
| CTX-07 latency **primary**, offline tolerance **second** | per AD-014 | `CLAUDE.md:9–10` — *"**Latency** … is the primary quality attribute; **surviving individual offline readers** is the second (AD-014)"*. Matches `.specs/STATE.md:116` (AD-014) verbatim in substance | ✅ PASS |
| CTX-08 links ROADMAP, restates no scale numbers | link present; no figures | `CLAUDE.md:13` links `.specs/ROADMAP.md`. Numeric scan over the whole hub returns only AD ids, `AES-256`, port `5000`, `L-007` — **no `50k`/`10k`/`10 GB`/`p95`** | ✅ PASS |

### P2 — The hub stops contradicting the decision log

| Criterion | Spec-defined outcome | `file:line` + the check I ran | Result |
|---|---|---|---|
| CTX-09 live protection recorded; the disagreeing file corrected | all three agree | Live `gh api …/branches/main/protection`, queried by me: `contexts == ["build-and-test"]`, `strict == true`, `enforce_admins == true`, `required_approving_review_count == 0`, `required_linear_history == true`. Matches `.specs/STATE.md:353`, `CLAUDE.md:65`, `docs/git-workflow.md:70–98` | ✅ PASS |
| CTX-10 repair confined to the contradicting claim | one claim touched | `.specs/STATE.md:337` — the **Next step** bullet also retired *"merge the `code-style-enforcement` PR"* | ❌ **Breached — declared (A-9)**, and reported as such at `spec.md:294`. It is the **only** row in the traceability table carrying a breached status; I checked all thirteen |

### P2 — The change is recorded as a decision

| Criterion | Spec-defined outcome | `file:line` + the check I ran | Result |
|---|---|---|---|
| CTX-11 AD-031 in the six-field format | Decision · Reason · Trade-off · Scope · Date · Status | `.specs/STATE.md:316–327` — all six present, `Status: active` | ✅ PASS |
| CTX-12 states the hub-vs-spoke routing rule | actionable three-way rule | `.specs/STATE.md:318–320` — hub / hub-one-liner + spoke / spoke-only, each with a criterion and worked examples | ✅ PASS |
| CTX-13 **Trade-off** records the budget as documentary and unenforced | inside the Trade-off field | `.specs/STATE.md:324` — *"the ≤ 110-line ceiling is documentary only. No repository setting, build step or CI check measures it"* | ✅ PASS |
| — (iteration 2 Gap 4) | quoted figure must match | `.specs/STATE.md:322` says *"it landed at 109"*; `wc -l CLAUDE.md` = **109**. Now consistent, and the gate self-checks the equality | ✅ Closed |

**Status**: ✅ 12/13 CTX requirements verified with independently cited `file:line`; **CTX-10 breached and
honestly declared**, and it is the only one.

---

## Rule Inventory — 43/43 re-derived independently

Taxonomy re-counted from the table itself by parsing `spec.md`: **15 `H` + 9 `H→` + 19 `→` = 43**. No
duplicate ids, no gaps in `R-01…R-43`. The arithmetic the spec claims at `spec.md:253–254` is correct. ✅

| # | Class | Hub `file:line` | Spoke `file:line` |
|---|---|---|---|
| R-01 | H | `CLAUDE.md:18–19` | — |
| R-02 | H | `CLAUDE.md:21–22` | — |
| R-03 | H | `CLAUDE.md:22–23` | — |
| R-04 | H | `CLAUDE.md:65–66` | — |
| R-05 | H | `CLAUDE.md:66–67` | `docs/git-workflow.md:10–17` |
| R-06 | H→ | `CLAUDE.md:70–71`, named again `:74` | `docs/git-workflow.md:19–36` |
| R-07 | H | `CLAUDE.md:71–72` | `docs/git-workflow.md:38` |
| R-08 | H→ | `CLAUDE.md:70` | `docs/git-workflow.md:40–48` |
| R-09 | H→ | `CLAUDE.md:69–70` | `docs/git-workflow.md:53–64` |
| R-10 | → | named `CLAUDE.md:74` | `docs/git-workflow.md:63–68` |
| R-11 | → | named `CLAUDE.md:74–75` | `docs/git-workflow.md:84–85` |
| R-12 | H→ | `CLAUDE.md:65–66` | `docs/git-workflow.md:70–82` |
| R-13 | → | — | `docs/git-workflow.md:50–51`, `:93–98` |
| R-14 | → | named `CLAUDE.md:74` | `docs/git-workflow.md:87–91`, `:100–111` |
| R-15 | H | `CLAUDE.md:106` | — |
| R-16 | H | `CLAUDE.md:107–108` | — |
| R-17 | H | `CLAUDE.md:108–109` | — |
| R-18 | → | — | `.specs/STATE.md:291` (AD-028, per A-5) |
| R-19 | H | `CLAUDE.md:27–38` | — |
| R-20 | H | compose `:43` · restore `:44` · run `:45` · ef `:49` (**`--project` restored**) · build `:55`,`:58` · test `:55`,`:59`,`:60` | E2E run cmd `docs/test-patterns.md:51` |
| R-21 | H | `CLAUDE.md:53–61` | — |
| R-22 | H | `CLAUDE.md:48–49` | — |
| R-23 | → | — | `docs/test-patterns.md:47–64` |
| R-24 | H→ | `CLAUDE.md:79–80` | `docs/code-style.md:6–14` |
| R-25 | H→ | `CLAUDE.md:82–83` | `docs/code-style.md:23–34` |
| R-26 | H→ | `CLAUDE.md:80` | `docs/code-style.md:18–21` |
| R-27 | H→ | `CLAUDE.md:83–85` | `docs/code-style.md:36–47` |
| R-28 | → | named `CLAUDE.md:87` | `docs/code-style.md:49–56` |
| R-29 | → | — | `docs/code-style.md:58–62` |
| R-30 | → | — | `docs/code-style.md:64–67` |
| R-31 | → | — | `docs/slice-anatomy.md:23` |
| R-32 | → | named `CLAUDE.md:91` (*"DTO boundaries"*) | `docs/slice-anatomy.md:24–26` |
| R-33 | → | — | `docs/slice-anatomy.md:85–89` |
| R-34 | → | named `CLAUDE.md:91` (*"the result pattern"*) | `docs/slice-anatomy.md:28–38` |
| R-35 | → | — | `docs/slice-anatomy.md:40–56` |
| R-36 | H→ | `CLAUDE.md:91` | `docs/slice-anatomy.md:11–21` |
| R-37 | → | named `CLAUDE.md:92` (*"the write-path flow"*) | `docs/slice-anatomy.md:58–73` |
| R-38 | → | named `CLAUDE.md:92` (*"where uniqueness is really enforced"*) | `docs/slice-anatomy.md:75–83` |
| R-39 | → | — | `docs/slice-anatomy.md:91–95` |
| R-40 | → | — | `docs/slice-anatomy.md:97–103` |
| R-41 | → | — | `docs/slice-anatomy.md:105–113` |
| R-42 | H | `CLAUDE.md:97–98` | — |
| R-43 | H | `CLAUDE.md:100–102` | — |

**Result: 43/43 resolve.** Iteration 2's `--project` loss is closed at `CLAUDE.md:49`.

**Two independent lossless-relocation checks, both clean:**

1. Every backticked span in `git show origin/main:CLAUDE.md` (identifiers, flags, paths, commands) —
   **0 of them absent** from the union of `CLAUDE.md` + the four spokes + `.specs/STATE.md`.
2. Sentence-level: of every old-hub sentence, only 8 contain a content word missing from the new corpus,
   and all 8 are inflection or synonym differences from rewording (`strategy`, `simply`, `preserved`,
   `responses`, `enforces`, `further`, `skipped`, `guidelines`). **No imperative lives in no file.**

One clause worth naming because it is the easiest kind to lose: the old hub's *"The Verifier receives only
`spec.md`, the commit range, and the test files"* is not in the new hub — it survives at
`.specs/STATE.md:288` (AD-028), which is where R-16's authority sits. Not a loss.

---

## AC-4's negative half — re-derived semantically, not by token

This is how iteration 2 broke the previous round, so I checked it by reading rather than by grep. For each
of the 19 `→` rules I asked: *does a hub line let a reader who never opens the spoke act on this rule?*

| Hub line | What it does | Verdict |
|---|---|---|
| `:74–75` *"Protection payload, the type table, the verify-`main` command, and the `git push` dry-run trap:"* | **Names** R-14, R-06, R-10, R-11. A reader learns four topics exist and nothing about their content — they cannot verify `main`, cannot query protection, and do not know what the dry-run trap *is* | ✅ naming |
| `:87` *"Both incidents in full, plus the analyzer ratchet:"* | **Names** R-28. No `AnalysisMode`, no `Recommended`, no ratchet direction | ✅ naming |
| `:91–92` *"DTO boundaries, the result pattern, the write-path flow, and where uniqueness is really enforced:"* | **Names** R-32, R-34, R-37, R-38. *"where … is really enforced"* poses the question; the spoke answers it | ✅ naming |
| everything else | R-13, R-18, R-23, R-29, R-30, R-31, R-33, R-35, R-39, R-40, R-41 have no hub mention at all | ✅ absent |

The only near-miss is `CLAUDE.md:33`, where `IRepository<T>` appears — but that is inside the
project-structure tree describing `Shared/`, is R-19's content, and is verbatim from the pre-change hub.
R-40's imperative (*inject it, never `AppDbContext`; always go through `Specification<T>`*) is nowhere in
the hub.

**19/19 clean.** Iteration 2's R-38 finding is genuinely fixed, and fixed by weakening the hub text rather
than by re-classifying the row.

---

## Ruling 1 — the naming rule

> *"A hub line may **name** what a spoke covers. It may **state** a rule only when that rule is
> load-bearing for a rule the hub itself carries. Naming is the default; `H→` must be earned."*
> — `spec.md:258–259`

**Verdict: principled, and it was applied against the author's interest. Accepted.**

The decisive evidence is not the prose but the diff. Comparing the inventory as first written (`fb58165`,
before any Verifier ran) with the inventory now:

```
12c12
< | R-12 | Branch protection is enforced; `enforce_admins=true`; rejection output | → `git-workflow.md` |
---
> | R-12 | Branch protection is enforced; `enforce_admins=true`; rejection output | H→ `git-workflow.md` |
```

**One row changed across all three iterations.** R-32, R-34 and R-38 are back where the original spec put
them, and the hub was rewritten to comply. Iteration 2's charge — *"in each case the author made the
smaller edit"* — no longer holds: withdrawing two promotions and rewording the hub is the larger edit, and
it is the one that was made.

Applying the rule myself to the four contested rows:

- **R-12 passes.** `CLAUDE.md:65` carries R-04 (*never commit directly to `main`*). Without *"the server
  rejects the push"*, R-04 reads as a convention. It is literally load-bearing for a hub rule, and it is a
  rule this repository has already broken. ✅
- **R-32 fails.** Nothing the hub states depends on DTOs being per-slice — note the hub's R-36 line no
  longer says *"no shared DTOs"*, so the dependency was removed rather than asserted away. ✅ correctly `→`
- **R-34 fails.** No hub rule depends on `OneOf`. ✅ correctly `→`
- **R-38 fails.** No hub rule depends on where uniqueness is enforced. ✅ correctly `→`

It also resolves the R-11/R-12 asymmetry iteration 2 called *"convenient, not principled"*: R-11 is a
hazard about a *verification technique* the hub never tells you to use, so nothing in the hub leans on it;
R-12 backs R-04. That is a stated criterion doing the discriminating, not a judgement call.

**Residue, recorded not waived:**

1. R-12 remains a post-hoc reclassification of a row the author's own shipped text had already breached.
   One row, defended by a rule stated in advance of the re-derivation and cutting the other way on three
   other rows — acceptable, but it is not nothing.
2. **The rule does not cleanly justify two of the nine `H→` rows it now governs.** R-36 (three-file slice
   layout) is load-bearing for no hub rule; it is orientation, and under AD-031's own routing rule
   (*"matters only while doing one kind of work"*) it reads as spoke-only. R-09 is justified by Edge Case 3
   (*promote a genuinely universal rule*) rather than by the naming rule. Both were `H→` in the original
   inventory, so neither is a moved goalpost — but the rule is presented as covering the whole `H→` column
   and it does not. Cosmetic for this feature; a trap for the next author who applies it literally.

---

## Ruling 2 — the three-way Edge Case 1 check

> Text shared with `.specs/STATE.md` counts as a violation only if it was **absent** from
> `git show origin/main:CLAUDE.md`.

**Verdict: the reasoning is sound and I confirmed it empirically. The implementation is a one-shot
regression test, not the general class check the commit message claims.**

**Sound.** I ran the two-way check myself (12-gram token overlap, spokes vs `.specs/STATE.md`) and it
returns 25 hits in `docs/git-workflow.md` alone — the `[remote rejected]` block, the *"a PR whose base is
another branch merges into that branch"* sentence, the *"merge the base PR with its branch deleted"*
sentence. **Every one is a relocation AC-3 positively requires**, and deleting them to satisfy a two-way
check would fail CTX-02. The author's argument that a two-way check flags every legitimate relocation is
not a rationalisation; it is measurably true, and the exemption is the correct fix.

**Exploitable, in three ways I demonstrated:**

1. **Paraphrase defeats it.** Re-importing the removed `.specs/STATE.md:340` paragraph *verbatim* is
   caught (mutant M25, killed). Re-importing the same paragraph with a light reword that breaks each
   12-gram is **not** (M26, survived). The check catches *verbatim copying*, which is one instance of the
   class; the class is *second authority*, and a reworded copy is still a second authority.
2. **It skips the file the feature actually modified.** The file list is hard-coded to `code-style.md`,
   `git-workflow.md`, `slice-anatomy.md`, `CLAUDE.md`. `docs/test-patterns.md` — which T4 edited — is not
   in it. Transcribing AD-024 verbatim into `docs/test-patterns.md` survives (M27). (I checked the real
   file: its overlap with `.specs/STATE.md` is pre-existing `test-project-conventions` content, so this is
   a gate hole, not a live breach.)
3. **The exemption does not generalise past this base commit.** *"Absent from `origin/main:CLAUDE.md`"* is
   meaningful only while `origin/main`'s hub still contains the relocated text. For the next feature the
   anchor is empty and the check degenerates into the two-way check it was written to replace. The commit
   message presents *"the ADs restate CLAUDE.md by construction"* as a standing principle; it is a
   property of this one diff.

**State of the artifact**: at N=12 the three new spokes show **zero** three-way hits, and I inspected every
two-way hit by hand — all are inventoried relocations (R-09, R-12, R-25, R-38). The four sites iteration 2
named are gone and nothing equivalent replaced them. **Edge Case 1 is satisfied in fact.**

---

## Discrimination Sensor

**Method.** Throwaway `git worktree` at `44a9420`; the gate script copied and re-rooted at the worktree so
it could not silently read the real tree. Every mutation asserts its anchor matches **exactly once** before
being applied (the wrong-path false-pass guard, `.specs/STATE.md:342` / L-012). Scored by which check
label failed, so a kill by an unrelated check is recorded as collateral, not as a kill. Baseline in the
worktree: **139/139 PASS**, matching the author's claim. Worktree removed and pruned; real tree verified
clean.

**The battery is mine.** It reuses none of iteration 2's mutants and none of the author's. It targets
*classes* — inversion, conditioning, weakening, paraphrase restatement, silent deletion, narrative
hollowing, structural retag, second-authority import — with a must-kill and a must-survive control.

### Main battery — 27 must-kill + 2 controls

| # | Class | Mutation | Gate A (author's `doc_gate.py`) |
|---|---|---|---|
| C1 | control | **must-kill** — drop the `slice-anatomy.md` link from the hub | ✅ Killed (correct) |
| C2 | control | **must-survive** — cosmetic reword of a spoke lead-in | ✅ Survived (correct) |
| M1 | inversion | AD-030 inverted: *"Hangfire is already wired into the solution"* | ✅ Killed — but by the **presence** check for the phrase `job runner`, not by the inversion sensor (see M1b) |
| M2 | inversion | `enforce_admins` inverted: *"though repo owners bypass the check entirely"* | ❌ **Survived** |
| M3 | inversion | R-43 inverted: *"A `[Category]` trait on the test declares its level"* | ❌ **Survived** |
| M4 | inversion | R-11 inverted in the spoke: *"`git push --dry-run` is a reliable pre-flight for this"* | ✅ Killed |
| M5 | inversion | R-13 inverted in the spoke: *"requires one approval (`required_approving_review_count=1`)"* | ❌ **Survived** — and it now contradicts the live API |
| M6 | inversion | latency demoted without using the word "not" | ❌ **Survived** |
| M7 | weakening | `IDE0055` **error** → **warning**, *"does not fail the build"* | ❌ **Survived** |
| M8 | weakening | `CancellationToken` *"last parameter, no default"* → *"`= default` is fine"* | ❌ **Survived** |
| M9 | weakening | per-slice DTOs → *"may be shared between features"* | ✅ Killed |
| M10 | conditioning | *"Never commit directly to `main`* **unless the change is documentation-only**" | ✅ Killed |
| M11 | conditioning | *"never batch, **unless the tasks are trivially related**"* | ❌ **Survived** |
| M12 | conditioning | migrations *"applied at startup **once `RUN_MIGRATIONS=1` is set**"* | ⚠️ Killed **only** by the AD-031 line-count check — collateral, not a kill of the injected fault |
| M13 | conditioning | Verifier dispatched *"when the feature ships code"* | ❌ **Survived** |
| M14 | conditioning | *"never jump to `AnalysisMode=All` **without asking first**"* | ❌ **Survived** |
| M15 | paraphrase | AC-4 breach, line-neutral: hub states R-32 + R-34 in different words | ❌ **Survived** |
| M16 | paraphrase | AC-4 breach, line-neutral: hub states R-33 in different words | ❌ **Survived** |
| M17 | paraphrase | AC-4 breach, line-neutral: hub states R-13 in different words | ❌ **Survived** |
| M18 | paraphrase | AC-4 breach, line-neutral: hub states R-28 in different words | ❌ **Survived** |
| M19 | deletion | delete the `.specs/ROADMAP.md` link | ✅ Killed |
| M20 | deletion | delete `dotnet restore` from the commands block | ✅ Killed |
| M21 | narrative | keep `PostgresFixture`, `UnreachableDatabaseFixture`, `[Obsolete]`; delete the wrong outcome | ❌ **Survived** |
| M22 | narrative | keep *"PR #2 and PR #4"*; delete what actually went wrong | ❌ **Survived** |
| M23 | structural | retag the gate-commands fence ```` ```bash ```` → ```` ```text ```` | ✅ Killed |
| M24 | structural | grow the hub to 110 without updating AD-031's quoted count | ✅ Killed (this is the check the author added; it works) |
| M25 | edge1 | re-import the removed `.specs/STATE.md` paragraph **verbatim** | ✅ Killed |
| M26 | edge1 | same import, **lightly reworded** to break every 12-gram | ❌ **Survived** |
| M27 | edge1 | transcribe AD-024 verbatim into `docs/test-patterns.md` | ❌ **Survived** (file not in the check's list) |

### Targeted probes — is the gate fitted to iteration 2's *strings*?

| # | Probe | Result |
|---|---|---|
| M1b | AD-030 inverted **keeping the phrase `job runner`** | ❌ **Survived** — so M1's kill was incidental |
| M2b | `enforce_admins` inverted using **iteration 2's exact reported wording** (*"being the repo owner exempts you"*) | ✅ Killed |
| M11b | never-batch weakened using **iteration 2's exact wording** (*"batching is acceptable"*) | ✅ Killed |
| M12b | migrations conditioned using **iteration 2's exact wording** (*"only in Development"*) | ✅ Killed |
| M18b | bare `dotnet format` endorsed with a sentence starting *"Bare"* — **iteration 2's exact mutant** | ✅ Killed (the case-sensitivity fix works) |
| M18c | the analyzer fixers endorsed **without** the blacklisted phrase | ❌ **Survived** |
| M23b/c | `text` fence smuggled past the tree test by naming `HikvisionReplicator.Api/` inside the block | ❌ **Survived** (line-neutral variant) |
| M28 | hub restates R-39 in different words, line-neutral | ❌ **Survived** |

**Sensor depth**: P0-full — **36 must-kill mutations + 2 controls**.

**Result: 15/36 killed, of which 2 were collateral → 13/36 genuine. 21 survived.** Both controls behaved
correctly.

### The finding, stated plainly

**Every one of iteration 2's *named* survivors is now killed by its exact reported wording, and every
same-class variant of the same fault still survives.**

| Fault | Iteration 2's wording | A variant of the same fault |
|---|---|---|
| `enforce_admins` inverted | ✅ Killed (M2b) | ❌ Survived (M2) |
| never-batch weakened | ✅ Killed (M11b) | ❌ Survived (M11) |
| migrations conditioned | ✅ Killed (M12b) | ❌ Survived (M12, genuine) |
| bare `dotnet format` endorsed | ✅ Killed (M18b) | ❌ Survived (M18c) |
| AD-030 inverted | ✅ Killed (M1, incidentally) | ❌ Survived (M1b) |
| `text` fence retagged | ✅ Killed (M23) | ❌ Survived (M23c) |

The gate went from 128 checks to 139 by adding one regex per reported mutant. That is precisely the defect
iteration 2 diagnosed, reproduced one level up. The two structural remedies iteration 2 asked for were not
made: the AC-4 negative half is **still** 20 single-token greps lifted from the spokes' current wording
(M15–M18, M28 all pass all 20), and the CTX-05 narrative checks **still** assert only the artifact name and
never the wrong outcome (M21, M22). The one genuinely structural improvement is the Edge Case 1 n-gram
check, which is a real class check for verbatim copying — and the AD-031 line-count self-check, which
works exactly as designed.

**Why this does not fail the feature.** The gate ships nowhere: `tasks.md:48–52` states it is written to
the session scratchpad and **not committed**, and the spec's § Out of Scope plus assumption A-3 explicitly
defer any committed link-or-budget check as a separate decision. There is no durable artifact to harden.
Every surviving mutant describes a fault the artifact does not have — I checked each one against the real
files directly. The evidence AD-028 requires is the Verifier's independent re-derivation, which is what
this report is; it is not the author's script, and `tasks.md` should stop implying otherwise.

---

## Edge Cases

- [x] **Edge Case 1 — a spoke links an AD rather than copying it.** The four sites iteration 2 named are
      removed (`44a9420`): `docs/code-style.md` lost the warning-census paragraph, the *"seven of them in
      `IntegrationTests`"* clause and the SSH.NET provenance; `docs/git-workflow.md` lost the `gh repo
      view` block. All four are now replaced by links (`docs/code-style.md:62`, `docs/git-workflow.md:110–111`).
      My own 12-gram three-way scan returns **0 hits** across the three new spokes; every two-way hit I
      inspected by hand is an inventoried relocation. All three spokes carry a `Source: .specs/STATE.md —
      AD-NNN` header, and `docs/slice-anatomy.md:111–113` explicitly refuses to restate AD-005/009/023.
      **No rule was lost by the removals** — R-28 survives at `docs/code-style.md:51–56` and R-29 at `:58–62`,
      both with their `4 NU1903 + 4 CS0618` figures intact.
- [x] **Edge Case 2 — a thin spoke is absorbed, not created.** The E2E notes went into the existing
      `docs/test-patterns.md:47–64` (A-6). No fifth spoke.
- [x] **Edge Case 3 — a universal rule buried in a demoted section is promoted.** *"Base every PR on
      `main`"* is at `CLAUDE.md:69` while the rest of AD-025 sits in the spoke.
- [x] **Edge Case 4 — `../docs/test-patterns.md` from `.specs/STATE.md` still resolves.** File neither
      renamed nor moved (T4 was insertion-only, 19 lines added, 0 removed); `.specs/STATE.md:92` and `:156`
      resolve.

---

## Code Quality

| Principle | Status |
|---|---|
| Minimum code | ✅ |
| Surgical changes | ✅ Iteration 2's four imported paragraphs are gone; the diff is now relocation only |
| No scope creep | ✅ The § Out of Scope *"relocates and compresses; does not legislate"* clause now holds. `CLAUDE.md:23`'s added *"nothing may assume Hangfire"* is faithful to AD-030's own *"may not assume Hangfire"* |
| Matches patterns | ✅ Spokes follow `docs/test-patterns.md`'s shape; AD-031 follows AD-029's format |
| Spec-anchored outcome check | ✅ Re-derived independently; CTX-10 flagged as breached, not passed |
| Per-layer Coverage Expectation met | ⚠️ `tasks.md:36–38` claims *"every acceptance criterion is discharged by a mechanical, binary check"*. That claim is **not true** for the semantic criteria — 21 of my 36 behaviour-level faults survive the gate. The criteria are nonetheless discharged, by this report's independent derivation |
| Every test maps to a spec requirement | ✅ Every gate check names a CTX or R id |
| Documented guidelines followed | ✅ `CLAUDE.md`, `docs/test-patterns.md`, `.specs/STATE.md` AD-024 / AD-026 / AD-028 |

---

## Gate Check

- **Build gate** (`tasks.md` Build level): `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- **Result**: 0 errors, 14 warnings; **81 passed, 0 failed, 0 skipped**
- **Test count before / after**: 81 / 81 — delta 0, as `tasks.md:71` requires. No test deleted, no assertion weakened.
- **`src/**` untouched**: `git diff --name-only origin/main..HEAD | grep ^src/` → 0 files.
- **Doc gate** (author's, audited and mutated, **not trusted**): 139/139 — see the sensor section for what that number is worth.

---

## Ranked Gaps

None of the following blocks the merge. They are ranked by what they cost if left.

1. **[Major — process, not artifact] The doc-gate is still over-fitted, now to iteration 2's list.**
   21/36 independent must-kill mutants survive; every named survivor from iteration 2 dies on its exact
   reported string while a variant of the same fault lives. **Do not carry the 139/139 figure into the PR
   as evidence** — cite this report instead. The two structural fixes iteration 2 asked for (positive
   assertions for the AC-4 negative half; narrative checks that assert the *outcome*, not the artifact
   name) were not made. Because the script is scratch-only and A-3 accepts the absence of mechanical
   enforcement, this is recorded as a lesson rather than a fix task. If a CI doc-gate is ever built
   (spec § Out of Scope), start from these 36 mutants, not from the 139 checks.
2. **[Minor] `tasks.md:36–38` overstates its own coverage claim.** *"Every acceptance criterion is
   discharged by a mechanical, binary check"* is false, and it is the sentence that created the incentive
   to grow the check count. One-line fix: say the doc-gate is an author-side smoke check and the
   Verifier's independent re-derivation is the evidence. Worth doing before merge — this feature is about
   documents that tell the truth about themselves.
3. **[Minor] CTX-10 is breached and declared (A-9), not met.** `.specs/STATE.md:337`. The declaration is
   honest, correctly recorded at `spec.md:294`, and it is the only such row. **The PR body must say the AC
   is not met** — a reader of the merged history should not have to open `spec.md` to find that out.
4. **[Cosmetic] `tasks.md:18` cites `27960df` as the iteration-2 fix commit.** That commit is not on this
   branch; the real one is `44a9420`. A pre-squash reference that resolves through no PR.
5. **[Cosmetic] `tasks.md:259` still says *"all 11 `H→` rules"***. The inventory is 9. A stale done-when
   criterion contradicting the spec it verifies.
6. **[Cosmetic] The naming rule does not cover two of the nine `H→` rows it governs** (R-36, R-09 — see
   Ruling 1, residue 2). Neither is a moved goalpost; both predate every Verifier round. A future author
   applying the rule literally would demote R-36.
7. **[Cosmetic, out of scope] `.specs/STATE.md:324` (AD-030's Trade-off) says AD-014 makes *throughput*
   the primary quality attribute; AD-014 at `:116` says *latency*.** Pre-existing, not caused here, and
   `CLAUDE.md:9` follows AD-014 correctly. Flagged so it is not lost.

---

## Requirement Traceability Update

| Requirement | Previous | New |
|---|---|---|
| CTX-01 | Implementing | ✅ Verified (109 lines) |
| CTX-02 | Implementing | ✅ Verified (43/43, plus two independent lossless checks) |
| CTX-03 | Implementing | ✅ Verified (9/9 `H→`, 4/4 spokes linked) |
| CTX-04 | Implementing | ✅ Verified (6/6 paths) |
| CTX-05 | Implementing | ✅ Verified (4/4 narratives, artifact **and** outcome) |
| CTX-06 | Implementing | ✅ Verified |
| CTX-07 | Implementing | ✅ Verified against AD-014 |
| CTX-08 | Implementing | ✅ Verified |
| CTX-09 | Implementing | ✅ Verified against the live API |
| CTX-10 | Breached — declared | ❌ **Breached — declared (A-9)**; accepted as a deviation, must appear in the PR |
| CTX-11 | Implementing | ✅ Verified |
| CTX-12 | Implementing | ✅ Verified |
| CTX-13 | Implementing | ✅ Verified (quoted figure now matches, and self-checks) |
| AC-4 negative half | ⚠️ 16/17 (iter. 2) | ✅ **19/19 clean**, re-derived semantically including paraphrase |
| Edge Case 1 | ❌ (iter. 1, iter. 2) | ✅ **Satisfied** — four sites removed, 0 three-way n-gram hits |

---

## Summary

**Overall**: ✅ **Ready to merge**, with CTX-10's declared breach carried into the PR.

**Spec-anchored check**: 12/13 CTX requirements verified with independently cited `file:line`; CTX-10
breached and honestly declared, and the only one.
**Inventory**: 43/43 rules resolve; taxonomy **15 `H` + 9 `H→` + 19 `→` = 43** confirmed row by row; net
inventory change across three iterations is **one row** (R-12).
**Sensor**: 36 must-kill mutations + 2 controls — author's gate **13/36 genuine kills**, 21 survived. Both
controls correct.
**Gate**: build 0 errors, 81/81 unit tests, `src/**` untouched, working tree clean.

**What works**: the split itself, and now the discipline behind it. 285 lines down to 109 with every one of
43 imperatives still citable and two independent checks confirming nothing was dropped. The WHY reaches
default context and agrees with AD-014. All four failure narratives keep their concrete artifact *and*
their concrete wrong outcome. The branch-protection contradiction is resolved against the live API. AD-031
is usable as a routing rule and its quoted figure now self-checks. Most of all: the naming rule is a real
test, and the author applied it against themselves — two promotions withdrawn, three rows returned to
their original classification, the hub reworded instead of the spec.

**What is still weak**: the author's gate, which buys its 139/139 the same way it bought its 128/128 —
by memorising the last Verifier's mutants. That matters for the lesson, not for the merge: the script is
scratch, the durable enforcement it stands in for is explicitly deferred by A-3, and the artifact it
guards is correct on independent inspection.

**Next steps**: fix gaps 2, 4 and 5 (three one-line edits), state CTX-10's breach in the PR body, and do
not quote 139/139 as evidence anywhere.

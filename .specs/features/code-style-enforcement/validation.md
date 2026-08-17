# code-style-enforcement Validation

**Date**: 2026-08-17
**Spec**: `.specs/features/code-style-enforcement/spec.md`
**Diff surface**: `65125e5` on `main` (squash-merge of PR #7). Pre-squash commits `f9c3bc9`…`e86a896`
were used for per-commit granularity — they resolve only locally, never on `main`.
**Verifier**: **independent sub-agent under AD-028 — author ≠ verifier.** This agent did not write
the spec, the config, or the commits. It derived every result below from `spec.md`, the diff, and
observed build behaviour, and formed a complete verdict on all 8 acceptance criteria **before**
opening the superseded author self-check. This file replaces that self-check; its prior version
survives in git history at `65125e5`.

**Verdict: PASS** — 8/8 acceptance criteria met, 12/12 sensor probes behaved as predicted.
Four **documentation-accuracy / spec-precision defects** are flagged below. None is functional;
none blocks the feature. Two of them are wrong numbers recorded in files that exist specifically
to stop a future regression, so they are worth a follow-up `docs` change.

---

## Method — what counts as evidence when a feature ships no tests

This feature ships **no test code, by design**: the enforcement mechanism *is* the build. The
`file:line` + assertion-expression form of evidence therefore does not apply, and its absence is
**not** reported as a coverage gap. The substituted evidence standard is:

> a **cited command**, its **observed exit code and diagnostics**, and — for every criterion whose
> expected outcome is *"nothing happens"* — a **must-fail control** proving the silence is caused
> by the config under test and not by a mis-aimed probe.

The control requirement is not decoration. Two of this spec's criteria (AC-5, AC-6) are satisfied
by an *absence* of diagnostics, which is exactly what a probe aimed at the wrong path also
produces. Every such criterion below carries its own inverted control.

**All mutations ran in a throwaway `git worktree`** at
`scratchpad/verify-wt`, never in the real tree. Final `git status --porcelain` on the repository:
empty (see § Tree State).

---

## Task Completion

**There is no `tasks.md` for this feature.** Step 1 of `validate.md` ("go through tasks.md") could
not be performed as written; task completion was re-derived from the six pre-squash commits, each
mapped to the requirement it discharges. Every commit builds clean (§ CSE-09).

| Commit | Subject | Discharges | Status |
| ------ | ------- | ---------- | ------ |
| `f9c3bc9` | specify build-time code style enforcement | spec | ✅ Done |
| `e462129` | add editorconfig ruleset | CSE-01, CSE-02, CSE-04, CSE-05, CSE-06 | ✅ Done |
| `973e64d` | conform existing code to the ruleset | CSE-09 | ✅ Done |
| `5525763` | enforce code style on every build | CSE-03, CSE-08 | ✅ Done |
| `fb02548` | gate pull requests on build and tests | CSE-07 | ✅ Done |
| `e86a896` | record AD-027 | CSE-08, CSE-10 | ✅ Done |

Absence of `tasks.md` is a process gap, not an implementation gap: the commit sequence is atomic,
one concern per commit, and ordered so that enforcement is switched on only after the code already
complies.

---

## Spec-Anchored Acceptance Criteria

Paths are relative to the repository root. All builds were run in the scratch worktree.

| Criterion | Spec-defined outcome | Command + observed outcome | Result |
| --------- | -------------------- | -------------------------- | ------ |
| **AC-1** Clean build succeeds with zero `IDE`/`CA` **errors**; the 10 accepted `CA` warnings are expected | exit 0; no `error IDE`/`error CA` line; exactly the 10 enumerated `CA` findings | `dotnet build HikvisionReplicator.slnx --no-incremental` → **exit 0**, `0 Error(s)`, `14 Warning(s)`. Zero lines matching `: error `. The `CA` warnings are exactly 10 distinct findings, and **every one matches the `spec.md` table at the exact `file:line`**: `CA1001` ×3 (`CredentialLeakageTests.cs:62`, `ErrorHandlingTests.cs:16`, `PostgresFixture.cs:16`), `CA1848` `GlobalExceptionHandler.cs:32`, `CA1725` `DeviceConfiguration.cs:19`, `CA1716` `Errors.cs:1`, `CA1711` `PostgresFixture.cs:96`, `CA1710` `TracingTests.cs:16`, `CA1305` `ErrorHandlingTests.cs:88`, `CA1310` `HarnessTests.cs:49` | ✅ PASS |
| **AC-2** Injected formatting violation **fails** the build with `error IDE0055` | exit 1 + `error IDE0055` | Probe P1 — mis-indent + trailing whitespace on `Api/Domain/Port.cs:12` → `dotnet build HikvisionReplicator.slnx` → **exit 1**, `Port.cs(12,5): error IDE0055` and `Port.cs(12,38): error IDE0055` | ✅ PASS |
| **AC-3** The failure requires no extra flag; AC-2's command carries no `-p:` or `-warnaserror` | plain `dotnet build` suffices | The AC-2 command is literally `dotnet build HikvisionReplicator.slnx` — **no flags at all**, not even `--no-incremental`. Control P10 proves the flag-free behaviour comes from `Directory.Build.props:23`: with `<EnforceCodeStyleInBuild>` deleted and the same violation present, the build returns **exit 0, `0 Error(s)`** and `IDE0055` is not even emitted as a warning | ✅ PASS |
| **AC-4** `dotnet format` fixes an injected violation and the build goes green | inject → format → build exit 0 | Probe P1 present → `dotnet format whitespace` → **exit 0**, `git diff` empty (file restored byte-for-byte) → `dotnet build … --no-incremental` → **exit 0, `0 Error(s)`**. `dotnet format whitespace --verify-no-changes` on the clean tree → **exit 0** | ✅ PASS (⚠️ see D-1: `spec.md` AC-4 names the *forbidden* bare command) |
| **AC-5** Files under `Infrastructure/Migrations/` produce no style diagnostics | injected violation in a migration → build exit 0 | Probe P3 — mis-indent + trailing whitespace injected into `Migrations/20260812151024_InitialCreate.cs:10` **and** `Migrations/AppDbContextModelSnapshot.cs:13` → `dotnet build … --no-incremental` → **exit 0**, `IDE0055` occurrences: **0**. **Must-fail control P4**: identical injections, `.editorconfig:43-45` exemption block deleted → **exit 1**, `InitialCreate.cs(10,5)` and `(10,67): error IDE0055`. The silence is the exemption, not a mis-aimed probe | ✅ PASS |
| **AC-6** Test method names with underscores produce no `CA1707` | build at the configured `AnalysisMode` → zero `CA1707` | Clean baseline build: `grep -c CA1707` → **0**. **Must-fail control P5**: `.editorconfig:54` (`dotnet_diagnostic.CA1707.severity = none`) removed → **152 distinct `CA1707` sites** appear (304 raw log lines; MSBuild summary `166 Warning(s)` = 152 + the 14 baseline). The zero is the exemption doing work | ✅ PASS |
| **AC-7** The CI workflow is valid and triggers on pull requests to `main` | workflow parses; `on.pull_request.branches` includes `main` | `yaml.safe_load('.github/workflows/ci.yml')` parses; `on.pull_request.branches == ['main']` (`ci.yml:12-13`), `on.push.branches == ['main']` (`ci.yml:14-15`); job `build-and-test`, 6 steps. **Stronger evidence the author did not use**: the workflow has already executed for real — `gh run list` shows 3 successful `pull_request` runs and 3 successful `push`-to-`main` runs. Run `32070510289` (this feature's own PR) logs `13 Warning(s) / 0 Error(s)`, `Passed: 81`, `Passed: 88`. The action refs (`actions/checkout@v7`, `actions/setup-dotnet@v6`) therefore resolve | ✅ PASS (⚠️ see D-9: the AC's own verification method cannot establish CSE-07) |
| **AC-8** Baseline warnings unchanged; none newly promoted to errors | `NU1903` and `CS0618` remain warnings; build exits 0 | **Pre-feature** build at `f9c3bc9` (docs-only, so equivalent to `main` at `bba7908`), forced-clean checkout, `--no-incremental` → `4 Warning(s), 0 Error(s)`: `CS0618` at `ErrorHandlingTests.cs:18` and `PostgresFixture.cs:20`, `NU1903` (SSH.NET 2025.1.0). **Post-feature** → `14 Warning(s), 0 Error(s)`: the **same** `CS0618` and `NU1903` diagnostics, unchanged, still warnings; delta is **exactly +10, all `CA`**. No pre-existing warning became an error | ✅ PASS (⚠️ see D-2: the spec's "×4" is a raw-log-line count) |

**Status**: ✅ All 8 ACs covered by cited commands with observed outcomes; every "expected silence"
criterion carries a must-fail control. ⚠️ 2 spec-precision defects flagged (D-1, D-2).

---

## Requirement Traceability

| Req | Verdict | Evidence |
| --- | ------- | -------- |
| CSE-01 single root `.editorconfig` is the only place rules are declared | ✅ Verified | `find` over the repo: exactly one `.editorconfig`, zero `.globalconfig`, zero `.ruleset`. `grep -niE 'warnaserror\|TreatWarningsAsErrors\|NoWarn'` across all `*.csproj`/`*.props`/`*.targets`/`*.yml`: **no hits in any build file** (only prose in `CLAUDE.md`/`.specs`). `Directory.Build.props` declares analysis *properties*, never severities |
| CSE-02 `IDE0055.severity = error` fails the build | ✅ Verified | `.editorconfig:26`. AC-2. Control P11: same line changed to `= warning` with the violation present → **exit 0**, `Port.cs(12,5): warning IDE0055`. The `error` token is load-bearing |
| CSE-03 no command-line flag needed | ✅ Verified | `Directory.Build.props:23`. AC-3 + control P10. Also confirmed to reach **all four projects**: probe P12 injected one violation each into `Tests/Domain/DeviceCreateTests.cs:7`, `IntegrationTests/HarnessTests.cs:8`, `E2E/DeviceEndpointsTests.cs:10` → **exit 1, 5 `error IDE0055`** across all three test projects |
| CSE-04 severity per rule, never `-warnaserror` | ✅ Verified | Grep above: no `-warnaserror`, no `TreatWarningsAsErrors`, no `NoWarn` in any build file or in `ci.yml` |
| CSE-05 generated migrations exempt | ✅ Verified — with a defect | AC-5 + control P4. **But see D-3**: only `generated_code = true` (`.editorconfig:44`) does the work; `dotnet_analyzer_diagnostic.category-Style.severity = none` (`.editorconfig:45`) is **inert** |
| CSE-06 `CA1707` disabled in test projects | ✅ Verified | AC-6 + control P5. Applies to all three test projects (`.editorconfig:53`) |
| CSE-07 CI runs build + unit + integration on every PR to `main` | ✅ Verified — strongest evidence in the feature | `ci.yml:12-13, 33-48`. Six real runs succeeded. **Additionally**: the check is now *mandatory* — `gh api repos/:owner/:repo/branches/main/protection` returns `required_status_checks.contexts = ["build-and-test"]`, `strict: true`, `enforce_admins: true`. The gate is mechanical, not documentary |
| CSE-08 `AnalysisMode` starts at a rung already satisfied; path to a stricter rung recorded with measured costs | ⚠️ Partially verified | First clause ✅: `Recommended` (`Directory.Build.props:30`) yields `0 Error(s)`. Second clause **only partly** — `STATE.md` AD-027 records `Minimum → 0`, `Recommended → 10`, `Recommended before the CA1707 exemption → 326`: the current rung and a *looser* one. The cost of the next rung **upward** is asserted ("never jump to `All`") but never measured. **Measured here**: `dotnet build … --no-incremental -p:AnalysisMode=All` → **249 warnings across 17 distinct `CA` rules, 0 errors** (adds `CA1034`, `CA1062`, `CA1307`, `CA1515`, `CA1812`, `CA2000`, `CA2007`, `CA2234`, `CA5394`). Recording this in AD-027 would give "never jump to `All`" a number |
| CSE-09 the 5 pre-existing violations fixed before enforcement; no commit on the branch has a failing build | ✅ Verified | Every pre-squash commit rebuilt from a **forced-clean** checkout (`git checkout -qf && git clean -qfd`), `--no-incremental`: `f9c3bc9` `4W/0E` · `e462129` `4W/0E` (rules dormant — no `Directory.Build.props`, so the ruleset commit genuinely does not change the build outcome) · `973e64d` `4W/0E` · `5525763` `14W/0E` (enforcement on) · `fb02548` `14W/0E` · `e86a896` `14W/0E`. **Zero errors at every commit.** The count "five" is exact: recreating the enforcement-on-with-violations state produced precisely 5 sites — `DeviceRepository.cs:47,48,49,50` + `IRepository.cs:6` |
| CSE-10 `CLAUDE.md` documents the fix command and the incremental-build caveat | ✅ Verified | Fix command `CLAUDE.md:175-176` (`dotnet format whitespace`, plus `--folder`); bare-`dotnet format` prohibition `CLAUDE.md:179-182`; incremental caveat `CLAUDE.md:184-186` |

---

## Discrimination Sensor

**Depth**: extended-lightweight — 12 probes (spec calls for 1–3). Justified because 2 of the 8 ACs
assert an *absence* of diagnostics, which needs an inverted control each, and because 3 documented
claims (`generated_code`, the glob magnitude, the L-007 corroboration) were themselves testable.
All probes ran in a throwaway worktree and were reverted with `git checkout -qf .`.

| # | Mutation | File / target | Predicted | Observed | Verdict |
| - | -------- | ------------- | --------- | -------- | ------- |
| P1 | Mis-indent + trailing whitespace | `Api/Domain/Port.cs:12` | build fails | **exit 1**, `IDE0055` at `(12,5)` and `(12,38)` | ✅ Killed |
| P2 | P1 left in place, build run a **second** consecutive time | same | (per spec edge case) "reports nothing" | **exit 1 again**, 4 `IDE0055` lines — identical output. Also identical with `--no-incremental` | ⚠️ Killed, but **contradicts the spec's edge-case text** (see D-4) |
| P3 | Mis-indent + trailing whitespace in two migration files | `Migrations/20260812151024_InitialCreate.cs:10`, `Migrations/AppDbContextModelSnapshot.cs:13` | build succeeds (exempt) | **exit 0**, `IDE0055` count 0 | ✅ As specified |
| P4 | **Must-fail control for P3** — `.editorconfig:43-45` exemption block deleted, P3 injections intact | `.editorconfig` | build fails | **exit 1**, `InitialCreate.cs(10,5)` + `(10,67): error IDE0055` | ✅ Killed — P3's silence is the exemption |
| P5 | **Must-fail control for AC-6** — `dotnet_diagnostic.CA1707.severity = none` removed | `.editorconfig:54` | `CA1707` floods | **152 distinct sites / 304 raw lines**, `166 Warning(s)` | ✅ Killed — matches the `.editorconfig:49` claim "fires 304 times" |
| P6 | Test-project glob `**.cs` → `**/*.cs` (the documented trap) | `.editorconfig:53` | per the comment, "18 `CA1707`" survive | **92 distinct sites / 184 raw lines** survive — all files sitting in a project root (`IntegrationTests/*`, `E2E/*`); the `.Tests` files, which live in `Domain/` and `Infrastructure/`, stay suppressed | ⚠️ Killed; **magnitude in the comment is wrong** (see D-5) |
| P7 | Migrations exemption reduced to `generated_code = true` only | `.editorconfig` | ? | **exit 0** — still exempt | ✅ `generated_code` is sufficient |
| P8 | Migrations exemption reduced to `category-Style.severity = none` only | `.editorconfig` | ? | **exit 1, 2 errors** — **not** exempt | ⚠️ Survived the exemption → line 45 is **inert** (D-3) |
| P9 | After a green `--no-incremental` build, rebuild with **no change** | — | (L-007) diagnostics vanish | `14 Warning(s)` → **`2 Warning(s)`**: all 12 analyzer/compiler diagnostics disappear, only the 2 restore-level `NU1903` survive | ✅ L-007 independently corroborated |
| P10 | **Control for CSE-03** — `<EnforceCodeStyleInBuild>` deleted, P1 violation intact | `Directory.Build.props:23` | violation not caught | **exit 0**, `0 Error(s)`; `IDE0055` not emitted at all | ✅ Mechanism confirmed |
| P11 | **Control for CSE-02** — `IDE0055.severity` `error` → `warning`, P1 violation intact | `.editorconfig:26` | build passes with a warning | **exit 0**, `Port.cs(12,5): warning IDE0055` | ✅ Mechanism confirmed |
| P12 | One violation injected per test project | `Tests/Domain/DeviceCreateTests.cs:7`, `IntegrationTests/HarnessTests.cs:8`, `E2E/DeviceEndpointsTests.cs:10` | all three fail | **exit 1, 5 `error IDE0055`** across all three | ✅ Killed — enforcement reaches every project, `E2E` included |

**Result**: 12/12 probes behaved as predicted by the *implementation*. No surviving mutant in the
functional sense: every mechanism the spec relies on was shown to be load-bearing by deleting it and
watching the guarantee disappear. P2, P6 and P8 diverge from what the **documentation** predicts,
not from what the code does — recorded as D-3, D-4, D-5.

**Methodology note for future verifiers.** An early pass of the CSE-09 per-commit rebuild produced
`5 Error(s)` at `f9c3bc9` and `e462129`, which would have been reported as "two commits on the
branch have a failing build". It was contamination: a `git checkout <commit>` after a
`git checkout <other> -- Directory.Build.props` left the props file in the tree, so those commits
were built with enforcement they never had. Re-running with `git checkout -qf` + `git clean -qfd`
and asserting `find . -maxdepth 1 -name Directory.Build.props | wc -l` per commit gave the correct
`0 Error(s)` everywhere. **When a probe's result depends on a file being absent, assert the absence
rather than assuming the checkout produced it.**

---

## Code Quality

| Principle | Status | Note |
| --------- | ------ | ---- |
| No features beyond what was asked | ✅ | Two new config files, one workflow, one docs section. No hook, no lint script, no `--verify-no-changes` CI step |
| No abstractions for single-use code | ✅ | — |
| No unnecessary "flexibility" added | ⚠️ | One inert line: `.editorconfig:45` (D-3). Harmless today, misleading tomorrow |
| Only touched files required for the task | ✅ | Production-code diff is 5 whitespace lines in 2 files; zero behavioural change |
| Didn't "improve" unrelated code | ✅ | The 8 pre-existing warnings and the 10 new `CA` findings were left alone and enumerated, not opportunistically fixed |
| Matches existing patterns/style | ✅ | Follows AD-025's precedent of moving rules out of prose into mechanism; every config choice carries an inline rationale comment, consistent with the repo's `.specs` habit |
| Would a senior engineer approve? | ✅ | Yes. Enforcement is unbypassable by construction, the `AnalysisMode` ratchet starts at a rung the code already satisfies, and the accepted debt is enumerated by `file:line` rather than left as a warning cloud. The commit ordering (rules dormant → conform → enforce) is exactly right |
| Tests map to acceptance criteria and are non-shallow | N/A | Feature ships no test code by design; substituted evidence standard stated in § Method. Not counted as a gap |
| Spec-anchored outcome check | ✅ with 2 ⚠️ | Every AC's observed outcome matches the spec's stated outcome. D-1 and D-2 are imprecision in the spec text, not mismatched behaviour |
| Per-layer Coverage Expectation met | N/A | No code layers added |
| Every test in scope maps to a requirement — no unclaimed tests | ✅ | Test delta is 0 |
| Documented project guidelines followed | ✅ | `CLAUDE.md` (§ Git Workflow, § Gate commands, § Code Style), `.specs/STATE.md` AD-025/AD-027, `docs/test-patterns.md` (cited as the reason for the `CA1707` exemption) |

---

## Edge Cases

- [x] **A generated migration regenerated by `dotnet ef` produces no style diagnostics.** Verified,
      with a must-fail control (P3/P4). Note that only the migration *body*
      (`20260812151024_InitialCreate.cs`) needs the `.editorconfig` rule at all — the `.Designer.cs`
      and `AppDbContextModelSnapshot.cs` files carry `// <auto-generated />` and Roslyn suppresses
      them regardless. The exemption is still load-bearing for the body.
- [ ] **"Incremental build after an edit that adds a violation … a *second*, up-to-date build
      reports nothing."** First half ✅ (P1). **Second half is false as written** for an
      error-severity diagnostic: a build that failed never becomes up-to-date, so the second
      consecutive build reproduced the same exit 1 and the same 4 `IDE0055` lines (P2). See D-4.
- [ ] **"Agent runs `dotnet build` twice and sees green — known false-green."** Cannot happen for an
      `IDE0055` **error**. It happens readily for **warnings**: after a green `--no-incremental`
      build, a no-change rebuild collapsed `14 Warning(s)` → `2` (P9). The hazard is real; the
      edge-case table describes it in terms of the wrong severity. See D-4.

---

## Gate Check

- **Gate command** (`CLAUDE.md` § Gate commands, Full):
  `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests && dotnet test src/HikvisionReplicator.IntegrationTests`
- **Result**: chain **exit 0**. `81` unit passed / `88` integration passed = **169 passed, 0 failed,
  0 skipped**.
- **Test count before feature**: 169 (81 + 88). **After**: 169. **Delta**: 0 — correct and expected;
  the diff surface contains no test file, and none was deleted or weakened.
- **Skipped tests**: none.
- **Failures**: none.
- **Also run**: `dotnet build HikvisionReplicator.slnx --no-incremental` → exit 0, `0 Error(s)`,
  `14 Warning(s)`; `dotnet format whitespace --verify-no-changes` → exit 0.
- **CI cross-check**: run `32070510289` (PR #7) reports `13 Warning(s) / 0 Error(s)`, `Passed: 81`,
  `Passed: 88`. The 13-vs-14 difference is an artefact of CI's separate `Restore` step, which
  absorbs one of the two `NU1903` emissions — not a discrepancy in the code.

---

## Disagreements with the superseded author self-check

The self-check's **verdict (PASS) and all 8 AC results are correct** — I reached the same verdict
independently. Its findings 1 and 2 are sound, and I reproduced finding 2 exactly. The following
are the differences.

### D-1 — `spec.md` AC-4 still names the command the implementation forbids · spec-precision gap

`spec.md` AC-4 reads "`dotnet format` fixes an injected violation". The implementation explicitly
forbids that command (`CLAUDE.md:179`, `Directory.Build.props:12-16`, AD-027). The self-check
silently re-scoped AC-4 to `dotnet format whitespace` in its own table without flagging that the
spec text was left pointing at the forbidden command.

I verified **both**: `dotnet format whitespace` fixes the violation and leaves nothing else
modified (`git diff` empty). Bare `dotnet format` also fixes it **and independently reproduced the
documented damage** — it stamped `[Obsolete]` onto `UnreachableDatabaseFixture`
(`ErrorHandlingTests.cs`) and `PostgresFixture`, exactly as AD-027 records. Finding 2 is confirmed
on independent evidence. But `spec.md` AC-4 still instructs a future reader to run the unsafe
command.

### D-2 — the "×4" warning counts mix two counting conventions · spec-precision gap

`spec.md` AC-8 and Out of Scope, `STATE.md` AD-027, and the self-check all say "`NU1903` ×4 and
`CS0618` ×4" (8 pre-existing warnings). That is a **raw-log-line** count. MSBuild's own summary
deduplicates to **`4 Warning(s)` pre-feature and `14 Warning(s)` post-feature**: 2 `CS0618` sites
(`ErrorHandlingTests.cs:18`, `PostgresFixture.cs:20`) and 1 `NU1903` advisory. The same documents
count the `CA` findings *deduplicated* (10, not the 20 raw lines). Reading them together implies a
post-feature total of 18 warnings; the build says 14.

The substantive claim of AC-8 is verified and unaffected: identical diagnostics pre and post, still
warnings, exit 0, delta exactly +10 `CA`. Only the arithmetic convention is inconsistent.

### D-3 — `.editorconfig:45` is inert config, and the self-check did not notice · missed

`.editorconfig:43-45` presents the migrations exemption as two mechanisms. I isolated them:

| Configuration | Migration violation present | Result |
| ------------- | --------------------------- | ------ |
| `generated_code = true` **only** (P7) | yes | **exit 0** — exempt |
| `category-Style.severity = none` **only** (P8) | yes | **exit 1**, 2 × `error IDE0055` — **not** exempt |

The whole exemption rests on `generated_code = true`. The category-severity line cannot exempt
`IDE0055` because a rule-specific `dotnet_diagnostic.IDE0055.severity = error` (`.editorconfig:26`)
outranks a category-level severity regardless of which section appears later. It is not harmful
today, but it reads as a second belt and is not one: a future edit that drops `generated_code` while
keeping line 45 would silently lose the migrations exemption and start failing `dotnet ef` output.
Either delete line 45 or comment it as decorative.

### D-4 — the Edge Cases table is wrong for error-severity diagnostics · missed

`spec.md` Edge Cases claims "a *second*, up-to-date build reports nothing" and calls the double
green a "known false-green". Neither can happen for `IDE0055`, which is an **error**: a failed build
is never up-to-date, so the second consecutive build reproduced the same exit 1 and the same 4
`IDE0055` lines, and so did a third with `--no-incremental` (P2). The false-green is real for
**warnings** — P9 collapsed `14 Warning(s)` → `2` on a no-change rebuild. The `CLAUDE.md:184-186`
wording ("re-reports zero **diagnostics**") inherits the same imprecision. The guidance to add
`--no-incremental` when silence is evidence is nonetheless correct and worth keeping.

> **ORCHESTRATOR CORRECTION (2026-08-17) — D-4 is partly wrong.** "A failed build is never
> up-to-date" is true, but it does not follow that an error-severity diagnostic cannot go missing.
> A false green *does* occur for `IDE0055`-as-error when the **previous build succeeded** and the
> severity is raised afterwards. Reproduced on the merged tree:
>
> | Step | Result |
> |---|---|
> | `IDE0055 = warning`, `--no-incremental` | `Build succeeded`, **2 `warning IDE0055`** |
> | same code, add `-warnaserror`, **incremental** | **`Build succeeded`, 0 diagnostics** ← false green at error severity |
> | same, `--no-incremental` | `Build FAILED`, **2 `error IDE0055`** |
>
> The up-to-date check hinges on whether the previous build **succeeded** and whether MSBuild
> **inputs** changed — not on the diagnostic's eventual severity. A command-line severity flag does
> not invalidate the cache. The Edge Cases table and `CLAUDE.md` are therefore correct as written;
> only their reasoning was under-specified. Lesson L-017 has been rewritten accordingly.

### D-5 — the L-007 corroboration cited in the self-check is not reproducible · could not reproduce

Self-check finding 4 and `STATE.md` AD-027 both record: *"An up-to-date incremental build reported
`Build succeeded` with zero diagnostics on code that `--no-incremental` failed with 5
`error IDE0055`."* That observation is what raised L-007's recurrence to 2 and promoted it to
`confirmed`.

I recreated that exact scenario: worktree at `e462129` (ruleset present, enforcement off, the 5 real
violations present) → green incremental build → `Directory.Build.props` from `5525763` dropped in →
plain incremental `dotnet build`. The build **reported all 5 `error IDE0055` and failed**. Adding
`Directory.Build.props` invalidates the up-to-date check. I could not produce a false green from an
error-severity diagnostic in any attempt.

**L-007 itself still stands** — I corroborated it independently and more cleanly with P9 (no-change
rebuild, `14 Warning(s)` → `2`). The lesson's `confirmed` status is defensible; the specific
evidence recorded for it is not.

> **ORCHESTRATOR CORRECTION (2026-08-17) — D-5 is refuted; AD-027's evidence does reproduce.**
> The recreation above changed the wrong variable. Dropping in `Directory.Build.props` adds an
> MSBuild **import**, which legitimately invalidates the up-to-date check — so of course the
> analyzers re-ran. The original observation raised severity via a **command-line flag** on an
> already-built tree, which does not invalidate anything. Replaying that exact sequence (see the
> table in D-4) produced the false green on the first attempt: `Build succeeded`, zero diagnostics,
> on code that `--no-incremental` immediately failed with 2 `error IDE0055`.
>
> AD-027's cited evidence and L-007's promotion to `confirmed` both stand as recorded. No amendment
> to `STATE.md` is warranted on this point. The one thing worth sharpening is the *precondition* —
> the preceding build must have succeeded — which is what made this finding look non-reproducible.
>
> Kept rather than deleted because the disagreement is the useful artifact: between the two attempts
> it is now established that a severity flag does not bust the cache and an added MSBuild import
> does, which neither pass established alone.

### D-6 — the `**/*.cs` glob magnitude is wrong in three places · missed

Self-check finding 3, `.editorconfig:52`, and `STATE.md` AD-027 all state that `**/*.cs` "left 18
`CA1707` behind" out of 304, and finding 3 leans on that ratio: *"304 → 18 looks like the rule
worked."*

Measured (P6): `**/*.cs` leaves **92 distinct sites / 184 raw log lines** — every file sitting
directly in a project root, i.e. all of `IntegrationTests/` and `E2E/`. `18` is the raw line count
for the single `E2E/DeviceEndpointsTests.cs` file alone. The *direction* of the finding is correct
and confirmed; the magnitude is off by roughly 5×, and it inverts the argument: `304 → 92` does not
look like the rule worked, which makes the trap less deceptive than recorded — but `.editorconfig:52`
exists precisely to stop someone re-introducing the bad glob, so it should carry the real number.

### D-7 — AC-5 and AC-6 were passed without must-fail controls · methodology

The self-check identified the exact hazard — "a sensor that reports a pass because its target path
was garbage is indistinguishable from a real pass unless the expected outcome is a failure" — and
then accepted its two *expected-silence* criteria on uncontrolled evidence:

- **M3 → AC-5**: violation in a migration → `Build succeeded`. No probe showed the exemption was
  what caused the success.
- **M5 → AC-6**: an underscored test method name → 0 `CA1707`. Since `CA1707` is `none` throughout
  the test projects, adding another underscored name cannot distinguish "exemption works" from
  "probe aimed at nothing".

Both conclusions turn out to be **correct** — my controls P4 (2 errors when the exemption is
removed) and P5 (152 sites reappear) supply the missing proof. The gap was in the evidence, not the
implementation.

### D-8 — the self-check's Deferred list is now stale, and AC-7 had stronger evidence available · missed

The last Deferred bullet says branch protection requiring `CI` is not configured, "so the gate is
still documentary". It **is** configured (landed in PR #8 after that report):
`required_status_checks.contexts = ["build-and-test"]`, `strict: true`, `enforce_admins: true`.
CSE-07's gate is mechanical, not documentary.

Separately, the self-check verified AC-7 only by parsing the YAML. Six real CI runs had already
succeeded by then, three of them `pull_request` events including this feature's own PR. A workflow
that parses is not a workflow that runs — `actions/checkout@v7` could have been a non-existent ref
and the parse would still have passed.

### D-9 — AC-7's stated verification is weaker than the requirement it verifies · spec-precision gap

CSE-07 requires that *"a CI workflow **runs** build + unit + integration tests on every pull request
targeting `main`."* AC-7's verification column asks only that *"the workflow parses; `on.pull_request.branches`
includes `main`."* Parsing establishes neither that the steps execute, nor that the action refs
resolve, nor that Testcontainers can reach a Docker daemon on the runner — all three are exactly
what would break first. The self-check verified AC-7 at the criterion's own weak standard and
stopped there.

The criterion is nonetheless satisfied on stronger evidence I gathered instead: six completed runs,
three of them `pull_request` events, with run `32070510289` logging `0 Error(s)`, `Passed: 81`,
`Passed: 88`. AC-7's *text* should name a real run as its verification, not a parse.

### Where I agree

Verdict PASS · all 8 AC results · "no tests, by design" and the substituted evidence standard ·
gate counts 81/88 with zero delta · finding 1 (never census warnings from a build that did not
succeed) · finding 2 (bare `dotnet format` stamps `[Obsolete]`, reproduced verbatim) · the
`ls`-alias methodology warning, which I hit in a different form myself (see the methodology note
under § Discrimination Sensor).

**Independence disclosure.** One leak, disclosed for completeness: a repo-wide
`grep -rniE 'warnaserror|...'` run to check CSE-04 had a path-exclusion pattern that failed to
match, so 3 lines of the old `validation.md` appeared in its output — its AC-3 row and two
finding headlines. Both topics were already settled by my own experiments run *before* that grep
(AC-3 by probe P1, the bare-`dotnet format` behaviour by direct reproduction), so no conclusion
above derives from it. Every other result was formed before the file was opened.

---

## Requirement Traceability Update

| Requirement | Previous Status | New Status |
| ----------- | --------------- | ---------- |
| CSE-01 | Implementing | ✅ Verified |
| CSE-02 | Implementing | ✅ Verified |
| CSE-03 | Implementing | ✅ Verified |
| CSE-04 | Implementing | ✅ Verified |
| CSE-05 | Implementing | ✅ Verified (inert second clause — D-3) |
| CSE-06 | Implementing | ✅ Verified |
| CSE-07 | Implementing | ✅ Verified (now a required status check) |
| CSE-08 | Implementing | ⚠️ Verified with gap — stricter rung never measured; measured here at 249 warnings |
| CSE-09 | Implementing | ✅ Verified (all 6 commits, forced-clean rebuild) |
| CSE-10 | Implementing | ✅ Verified |

---

## Fix Plans

None is a blocker. All four are `docs`-type corrections to text that exists to prevent a future
regression, which is why they are worth a small follow-up rather than a shrug.

### Fix 1 — correct the three wrong `**/*.cs` numbers (Minor)

- **Root cause**: the measurement was taken over a narrower scope than the claim describes.
- **Task**: change "18" to "92 distinct sites (184 raw lines)" in `.editorconfig:52` and
  `STATE.md` AD-027 § Measured during execution. Drop the "304 → 18 looks like the rule worked"
  framing.
- **Verify**: re-run P6.

### Fix 2 — resolve the inert `.editorconfig:45` (Minor)

- **Root cause**: rule-specific severity outranks category severity, so the line can never exempt
  `IDE0055`.
- **Task**: delete line 45, or annotate it as decorative and state that `generated_code = true` is
  the sole load-bearing clause, so a future edit cannot delete the wrong one.
- **Verify**: re-run P7/P8.

### Fix 3 — correct the Edge Cases severity confusion (Minor)

- **Root cause**: the false-green applies to warnings; `IDE0055` is an error.
- **Task**: reword `spec.md` § Edge Cases rows 1–2 and `CLAUDE.md:184-186` to say *warnings* vanish
  on an up-to-date rebuild while errors keep failing. Replace the unreproducible AD-027 L-007
  evidence with P9's (`14 Warning(s)` → `2`).
- **Verify**: re-run P2 and P9.

### Fix 4 — reconcile the warning arithmetic, and record the `All` cost (Minor)

- **Root cause**: raw-log-line counts mixed with MSBuild's deduplicated counts.
- **Task**: state the counting basis once — "a clean `--no-incremental` build reports
  `14 Warning(s)`: 10 `CA` + 2 `CS0618` sites + `NU1903`" — in `spec.md` AC-8/Out of Scope and
  `STATE.md`. Add the measured cost of the next rung to AD-027: `AnalysisMode=All` → 249 warnings,
  17 distinct `CA` rules, 0 errors, which satisfies CSE-08's second clause.
- **Verify**: `dotnet build … --no-incremental` and `… -p:AnalysisMode=All`.

---

## Tree State

`git status --porcelain` on `/home/obogoni/projects/hikvision-replicator`: **empty** after
verification. Every mutation ran in the scratch worktree
`scratchpad/verify-wt` and was reverted; the worktree was removed. No source file in the real tree
was modified at any point. No commit, push, or PR was created by the Verifier.

---

## Summary

**Overall**: ✅ Ready

**Spec-anchored check**: 8/8 ACs matched the spec-defined outcome · 4 spec-precision gaps flagged
(D-1 AC-4 names the forbidden command · D-2 mixed warning-count conventions · D-4 Edge Cases wrong
for error severity · D-9 AC-7's verification method cannot establish CSE-07) · 1 requirement gap
(CSE-08's stricter rung never measured)
**Sensor**: 12/12 probes behaved as predicted; every mechanism the spec depends on was shown
load-bearing by removal
**Gate**: 169 passed (81 unit + 88 integration), 0 failed, 0 skipped, 0 test delta

**What works**

- `dotnet build` with **zero flags** fails on bad formatting, in all four projects, and passes clean
  on `main`. Both halves of that guarantee were proved by removal, not assumed.
- The two exemptions (generated migrations, `CA1707` in test projects) are real and necessary —
  152 `CA1707` sites and 2 migration errors reappear the moment either is removed.
- CI is not documentary: six real runs, and `build-and-test` is a required status check on `main`
  with `strict` and `enforce_admins` on. This is the first mechanical gate the repo has had.
- No commit on the feature branch has a failing build — verified by rebuilding all six from forced-clean
  checkouts, after an initial contaminated run had suggested otherwise.
- Production-code footprint is 5 whitespace lines; the 18 accepted warnings are enumerated by
  `file:line` rather than left anonymous.

**Issues found** — 4 documentation defects, 0 functional:

1. D-6/Fix 1 — `**/*.cs` leaves 92 sites, not 18; wrong in `.editorconfig:52`, `STATE.md`, and the
   old report.
2. D-3/Fix 2 — `.editorconfig:45` is inert; the migrations exemption rests entirely on
   `generated_code = true`.
3. D-4+D-5/Fix 3 — the false-green applies to warnings, not errors, and the specific L-007 evidence
   recorded in AD-027 does not reproduce (the lesson itself does, via P9).
4. D-2+CSE-08/Fix 4 — mixed warning-count conventions, and the cost of the next `AnalysisMode` rung
   was never measured. Measured here: `All` → 249 warnings, 17 rules, 0 errors.
5. D-1/Fix 1b and D-9 — `spec.md` AC-4 still names bare `dotnet format`, and AC-7's verification
   method ("the workflow parses") cannot establish CSE-07. Both are one-line AC text edits.

**Next steps**: accept the feature. Route Fixes 1–4 as a single `docs(specs)` follow-up. Note that
AD-028's own stated motivation is discharged: an independent verifier found six things the author's
self-check did not, and two of them were wrong numbers in files written to prevent regressions.

# Validation — code-style-enforcement

**Verdict: PASS** · 8/8 acceptance criteria met · 5/5 discrimination-sensor mutations behaved as specified.

**Diff range**: `f9c3bc9`…`e86a896` (6 commits) on `build/code-style-enforcement`, branched off
`main` at `bba7908`. These are **pre-squash references** — the PR is squash-merged (AD-025), so
they resolve only via the PR, never on `main`.

## Independence caveat — read this first

**Author ≠ verifier was NOT satisfied.** The skill mandates a fresh Verifier sub-agent; the
session's operating instructions forbid spawning agents unless the user asks. This is therefore
the skill's documented *standalone fallback*: the same author ran the checks. Treat the coverage
mapping below as self-assessment. The **discrimination sensor is the load-bearing evidence**,
because a mutation either fails the build or it does not — that outcome does not depend on the
author's mental model. This is the second consecutive feature with this gap (see
`test-project-conventions/validation.md`).

## There are no tests, by design

This feature ships no test code, so the usual "assertion at `file:line`" evidence does not apply.
The enforcement mechanism **is** the build, so every criterion is verified by observing build
exit codes and diagnostics under controlled mutations. Coverage evidence is therefore a command
plus its observed output.

## Acceptance criteria

| AC | Criterion | Evidence | Result |
|---|---|---|---|
| AC-1 | Clean build succeeds, zero `IDE`/`CA` **errors** | `dotnet build … --no-incremental` → `Build succeeded`, exit 0; `error` count 0; 10 `CA` warnings, all enumerated in `spec.md` | ✅ |
| AC-2 | Injected formatting violation **fails** the build | M1: `error IDE0055` ×2, `Build FAILED` | ✅ |
| AC-3 | Failure needs no extra flag | M1's command carries no `-p:` and no `-warnaserror` | ✅ |
| AC-4 | `dotnet format whitespace` fixes it, build green again | injected probe → `dotnet format whitespace` → `Build succeeded`, exit 0 | ✅ |
| AC-5 | Migrations produce no style diagnostics | M3: violation in `20260812151024_InitialCreate.cs` → `Build succeeded`, 0 `IDE0055` | ✅ |
| AC-6 | Underscored test names produce no `CA1707` | M5: `A_method_named_with_underscores()` → `CA1707` count 0 | ✅ |
| AC-7 | CI workflow valid, triggers on PRs to `main` | `yaml.safe_load` parses; `on.pull_request.branches == ['main']`; 6 steps resolved | ✅ |
| AC-8 | Baseline warnings unchanged, none promoted to errors | `NU1903` ×4 and `CS0618` ×4 still warnings; build exit 0 | ✅ |

## Discrimination sensor

Mutations were applied to the committed tree, built with `--no-incremental`, then reverted with
`git checkout -- src/`. Final `git status --porcelain` was empty.

| # | Mutation | Expected | Observed | |
|---|---|---|---|---|
| M1 | Bad whitespace in `Api/Shared/Errors.cs` | build fails | `error IDE0055` ×2, `Build FAILED` | killed ✅ |
| M2 | Bad whitespace in `Tests/Infrastructure/EncryptionServiceTests.cs` | build fails | `error IDE0055` ×2, `Build FAILED` | killed ✅ |
| M3 | Bad whitespace in a generated migration | build succeeds (exempt) | 0 `IDE0055`, `Build succeeded` | as specified ✅ |
| M4 | `Directory.Build.props` removed, M1's violation reapplied | violation **not** caught | 0 `IDE0055`, `Build succeeded` | mechanism confirmed ✅ |
| M5 | Test method named with underscores | no `CA1707` | 0 `CA1707`, `Build succeeded` | as specified ✅ |

M2 matters because it proves enforcement reaches the test projects, not only the Api. M4 is the
control: it demonstrates that `Directory.Build.props` is what does the work, so the gate cannot be
passing for some unrelated reason.

**Two mutations were initially invalid and were re-run.** `ls` is aliased to a table-formatting
tool in this environment, so `$(ls … | head -1)` resolved to a column header instead of a path;
M2 and M5 appended to a junk file and reported false passes (`Build succeeded` where M2 must
fail). Caught because M2's expected result was `FAILED` and the run reported `succeeded`. Re-run
with `find`, both then behaved correctly. **A sensor that reports a pass because its target path
was garbage is indistinguishable from a real pass unless the expected outcome is a failure** —
which is the argument for including at least one must-fail mutation in every sensor pass.

## Spec-precision gap found and corrected

**AC-1 was wrong as originally written.** It required "zero `IDE`/`CA` diagnostics", which
contradicted this same spec's Out of Scope section accepting 10 `CA` warnings. Verifying it
literally would have failed the feature; verifying it loosely would have passed a vague
assertion. Corrected to "zero `IDE`/`CA` **errors**", with the 10 warnings enumerated by rule and
`file:line` so they remain a tracked debt list rather than an anonymous warning cloud.

Recorded rather than quietly fixed: the author wrote both the criterion and the implementation,
which is exactly the failure mode an independent verifier exists to catch.

## Gate results

| Gate | Result |
|---|---|
| `dotnet build HikvisionReplicator.slnx --no-incremental` | `Build succeeded`, exit 0 |
| `dotnet test src/HikvisionReplicator.Tests` | **81 passed**, 0 failed, 0 skipped |
| `dotnet test src/HikvisionReplicator.IntegrationTests` | **88 passed**, 0 failed, 0 skipped |
| `dotnet format whitespace --verify-no-changes` | exit 0 |
| E2E | not run — needs a live API; excluded from CI by CSE-07 |

Counts match those recorded for `test-project-conventions` (81 / 88), so no test was lost,
skipped, or weakened.

## Findings worth carrying forward

1. **A failing build hides warnings in dependent projects.** `AnalysisMode=Recommended` first
   measured as 3 findings; the real number is 10. The Api's `IDE0055` errors aborted the build
   before the two test projects compiled. **Never take a warning census from a build that did not
   succeed** — and this is why enforcement was switched on only after existing violations were
   fixed.
2. **Bare `dotnet format` is unsafe in this repo.** With `AnalysisMode=Recommended` it runs the
   analyzer fixers and "fixed" the deprecated Testcontainers `PostgreSqlBuilder` call by stamping
   `[Obsolete]` onto `PostgresFixture` and `UnreachableDatabaseFixture` — silencing a real
   advisory by marking the consumer obsolete, which would cascade to every user of those
   fixtures. An auto-fixer optimising for "no warning" is not optimising for correctness.
   `dotnet format whitespace` makes no semantic edits and still clears `IDE0055`.
3. **EditorConfig `**/*.cs` requires at least one directory level.** It silently missed test files
   sitting directly in a project root, leaving 18 of 304 `CA1707` behind. `**.cs` is correct. The
   partial success is the hazard — 304 → 18 looks like the rule worked.
4. **Lesson L-007 independently corroborated.** An up-to-date incremental build reported
   `Build succeeded` with zero diagnostics on code that `--no-incremental` failed with 5
   `error IDE0055`. Recurrence raised to 2 and promoted to `confirmed`.

## Deferred

- The 10 `CA` findings (7 in `IntegrationTests`) — enumerated in `spec.md`.
- The 8 pre-existing warnings: 4 × `NU1903` (transitive SSH.NET advisory via Testcontainers) and
  4 × `CS0618`. Own `build(deps)` change.
- Consolidating `TargetFramework` / `Nullable` / `ImplicitUsings` out of the four `.csproj` files
  into `Directory.Build.props`.
- `.git-blame-ignore-revs` — worthwhile at the first wide reformat sweep, not for 5 lines.
- Branch protection requiring the `CI` check. Until it is configured, CI reports but does not
  block, so the gate is still documentary in the same way AD-025's no-direct-commits rule is.

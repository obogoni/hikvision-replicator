# Feature: Build-Time Code Style Enforcement

## Problem

The repository has no `.editorconfig`, so no style or formatting rule is enforced anywhere. A
full `dotnet format` run reports only `WHITESPACE` diagnostics and zero style findings, because
without `.editorconfig` the Roslyn code-style (`IDE####`) rules sit below `warning` severity and
`EnforceCodeStyleInBuild` is unset. Development happens entirely through AI agents with no IDE,
and the pull request is the only gate — but **there is no CI workflow at all** (`.github/` holds
only `pull_request_template.md`), so nothing mechanical stands between an agent's edit and `main`.

The rejected alternative was a `PostToolUse` hook running `dotnet format` after every file edit.
It was measured at 6.4–8.2 s per invocation and would have been advisory, per-machine, and
bypassable — the same class of failure AD-025 already fixed by moving merge rules from
documentation into repository settings.

## Approach

Style enforcement rides on the compiler, not on a file watcher. `.editorconfig` holds the rules,
`Directory.Build.props` makes every `dotnet build` honour them unconditionally, and CI runs the
existing gate commands on every pull request. `dotnet format` is retained as the *fix* path only.

## Requirements

| ID | Requirement |
|---|---|
| CSE-01 | A single `.editorconfig` at the repo root is the only place style and formatting rules are declared. |
| CSE-02 | Formatting violations fail the build: `dotnet_diagnostic.IDE0055.severity = error`. |
| CSE-03 | Enforcement requires **no command-line flag** — `EnforceCodeStyleInBuild` lives in `Directory.Build.props` so every project and every agent inherits it. |
| CSE-04 | Severity is set per rule in `.editorconfig`, **never** via `-warnaserror`. |
| CSE-05 | Generated EF Core migrations are exempt from style diagnostics. |
| CSE-06 | `CA1707` is disabled in the test projects, where it contradicts the documented naming convention. |
| CSE-07 | A CI workflow runs build + unit + integration tests on every pull request targeting `main`. |
| CSE-08 | `AnalysisMode` starts at a rung the codebase already satisfies; the path to a stricter rung is recorded with measured costs. |
| CSE-09 | The five pre-existing formatting violations are fixed before enforcement is switched on, so no commit on the branch has a failing build. |
| CSE-10 | `CLAUDE.md` documents the fix command and the incremental-build caveat. |

## Acceptance Criteria

| AC | Criterion | Verification |
|---|---|---|
| AC-1 | A clean solution build succeeds with zero `IDE`/`CA` **errors**. The 10 `CA` **warnings** CSE-08 accepts are expected and enumerated below. | `dotnet build HikvisionReplicator.slnx --no-incremental` → exit 0, no `error IDE`/`error CA` lines |
| AC-2 | An injected formatting violation **fails** the build with `error IDE0055`. | inject bad whitespace → clean build → exit 1 and `error IDE0055`; revert |
| AC-3 | The failure requires no extra flag — plain `dotnet build` is sufficient. | AC-2's command carries no `-p:` or `-warnaserror` |
| AC-4 | `dotnet format` fixes an injected violation and the build goes green again. | inject → `dotnet format` → clean build exit 0 |
| AC-5 | Files under `Infrastructure/Migrations/` produce no style diagnostics. | injected violation in a migration file → build exit 0 |
| AC-6 | Test method names with underscores produce no `CA1707`. | build at the configured `AnalysisMode` → zero `CA1707` |
| AC-7 | The CI workflow is valid and triggers on pull requests to `main`. | workflow parses; `on.pull_request.branches` includes `main` |
| AC-8 | Baseline warnings are unchanged by this feature (no regression, none newly promoted to errors). | `NU1903` ×4 and `CS0618` ×4 remain warnings, build still exits 0 |

## Out of Scope

- Fixing the 8 pre-existing warnings (4 × `NU1903` transitive SSH.NET advisory via Testcontainers, 4 × `CS0618`). Pre-existing; belongs to a `build(deps)` change.
- Fixing the 10 code-quality findings that `AnalysisMode=Recommended` surfaces. Real refactoring work, triaged separately — see CSE-08. Enumerated so they stay a tracked list rather than an anonymous warning cloud:

  | Rule | Location | What |
  |---|---|---|
  | `CA1001` | `IntegrationTests/CredentialLeakageTests.cs:62`, `ErrorHandlingTests.cs:16`, `PostgresFixture.cs:16` | Type owns a disposable field but is not `IDisposable` |
  | `CA1848` | `Api/Infrastructure/GlobalExceptionHandler.cs:32` | Use `LoggerMessage` delegates |
  | `CA1725` | `Api/Infrastructure/DeviceConfiguration.cs:19` | Parameter name should match the base declaration |
  | `CA1716` | `Api/Shared/Errors.cs:1` | Namespace segment matches a reserved language keyword |
  | `CA1711` | `IntegrationTests/PostgresFixture.cs:96` | Reserved type-name suffix |
  | `CA1710` | `IntegrationTests/TracingTests.cs:16` | Collection type should end in `Collection` |
  | `CA1305` | `IntegrationTests/ErrorHandlingTests.cs:88` | Specify `IFormatProvider` |
  | `CA1310` | `IntegrationTests/HarnessTests.cs:49` | Specify `StringComparison` for correctness |

  Seven of the ten are in `IntegrationTests`, so clearing them touches test code, not the Api.
- Consolidating `TargetFramework` / `Nullable` / `ImplicitUsings` out of the four `.csproj` files into `Directory.Build.props`. Deferred: this feature adds only analysis properties.
- `.git-blame-ignore-revs`. Only 5 lines are reformatted, so blame damage is negligible; the convention becomes worthwhile at the first wide sweep.
- Any git hook, `PostToolUse` hook, or pre-commit tooling.
- E2E tests in CI — they need a live API.

## Edge Cases

| Case | Expected |
|---|---|
| Incremental build after an edit that adds a violation | Analyzers re-run and the violation is reported. A *second*, up-to-date build reports nothing — documented, not fixed (corroborates lesson L-007). |
| Agent runs `dotnet build` twice and sees green | Known false-green. `CLAUDE.md` states the first build after a change is the trustworthy signal. |
| A generated migration is regenerated by `dotnet ef` | No style diagnostics, per CSE-05, so scaffolding never breaks the build. |

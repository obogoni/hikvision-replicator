# Code Style

Source: `.specs/STATE.md` — **AD-027** (active, 2026-08-17). Rule severities live in
`.editorconfig`; this document explains the enforcement model and the hazards around it.

## Formatting is enforced by the compiler, not by discipline

`.editorconfig` at the repository root is the single source of style rules, and
`Directory.Build.props` sets `EnforceCodeStyleInBuild`. **Every `dotnet build` is therefore the
style gate** — there is no flag to remember and no separate lint command. `IDE0055` is an
**error**, so bad formatting fails the build.

`.github/workflows/ci.yml` runs the full gate on every PR to `main` and is the enforcement
boundary; local runs are advisory.

## Fixing violations

```bash
dotnet format whitespace                                               # the whole solution
dotnet format whitespace --folder src/HikvisionReplicator.Api/Shared   # faster, one folder
```

### Never use bare `dotnet format`

Bare `dotnet format` also runs the **analyzer fixers**, which make semantic edits rather than
whitespace ones.

On this repository it "fixed" the deprecated Testcontainers `PostgreSqlBuilder` call by stamping
`[Obsolete]` onto `PostgresFixture` **and** `UnreachableDatabaseFixture` — silencing a real
deprecation advisory by marking the *consumer* obsolete. The warning disappeared; the underlying
deprecated call did not. Nothing about that edit is visible in a diff summary that says
"formatting".

`whitespace` is all `IDE0055` needs. Use it.

## Do not trust a quiet build

**An up-to-date incremental build re-reports zero diagnostics even when the code still violates
them** (lesson `L-007`, confirmed, recurrence 2). MSBuild skips the compile entirely and replays
the previous result, so silence means "nothing was rebuilt", not "nothing is wrong".

The trustworthy signal is the **first** build after a change. When a build's silence is being
used as evidence — "this introduced no new warnings" — add `--no-incremental`:

```bash
dotnet build HikvisionReplicator.slnx --no-incremental
```

A related trap from the same feature: **never take a warning census from a build that did not
succeed.** `AnalysisMode=Recommended` first measured as 3 findings; the true number is 10. The
Api project's `IDE0055` errors aborted the build before the two test projects ever compiled.

## Analyzer rules

`AnalysisMode` is `Recommended`, pinned to `AnalysisLevel` `10.0`. It currently surfaces **10
`CA` findings, all warnings**, enumerated by rule and `file:line` in
`.specs/features/code-style-enforcement/spec.md` — seven of them in `IntegrationTests`. That
file is the enumeration; this one does not duplicate it.

**Ratchet rules upward as findings are cleared — never jump to `AnalysisMode=All`.**

### Severity belongs in `.editorconfig`, never in `-warnaserror`

A clean `--no-incremental` build already emits **4 `NU1903`** (SSH.NET 2025.1.0, high severity,
transitive via Testcontainers) and **4 `CS0618`**. `-warnaserror` would turn all eight into build
failures, so severity is set per rule in `.editorconfig` instead. The `NU1903` advisories are
real and worth their own `build(deps)` change — they are simply not this gate's job.

## Conventions the rules do not mechanically cover

- File-scoped namespaces
- Primary constructors where appropriate

Everything else about how a slice is written — endpoint grouping, DTO boundaries, the result
pattern, password handling — is in [slice-anatomy.md](slice-anatomy.md), because those are
structural rules rather than style ones.

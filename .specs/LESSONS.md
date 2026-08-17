# LESSONS — auto-maintained by scripts/lessons.py

> Machine-owned. Do NOT hand-edit. Changes are overwritten on the next `lessons.py` write.
> Canonical state lives in `.specs/lessons.json`. Edit lessons only via the script.
> promote_threshold=2 distinct features · window_days=45 · quarantine_threshold=2

## Confirmed (load these at Specify/Design)

Corroborated across multiple features. Safe to apply as guidance.

### L-007 — Compare build warnings with --no-incremental before claiming none were introduced; an up-to-date incremental build re-reports zero warnings even when the code still emits them.
- signal: `gate_fail` · recurrence: 2 feature(s) · scope: `build` · harmful: 0
- features: test-project-conventions, code-style-enforcement
- evidence: src/HikvisionReplicator.Tests/HikvisionReplicator.Tests.csproj (build) (+1 more)
- last seen: 2026-08-17T21:10:08Z

## Candidates (under observation — do NOT load as guidance yet)

Seen once or not yet corroborated. Tracked, not trusted.

### L-001 — Assert the observable side effect itself, not the DI registration that is supposed to produce it.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `observability` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 1 mutation 7 — Program.cs:56-57 (.AddAspNetCoreInstrumentation / .AddEntityFrameworkCoreInstrumentation removed, suite green) (observability)
- last seen: 2026-08-12T16:24:04Z

### L-002 — When an acceptance criterion names several channels, assert the outcome in every named channel, not only the convenient ones.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `security` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 1 — DEV-07 trace-attribute clause had no file:line (security)
- last seen: 2026-08-12T16:24:04Z

### L-003 — Assert both the omitted case and the blank case for every required field, on every route that accepts it.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `validation` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 2 mutation M11 — RegisterDeviceService.cs:23 (blank password accepted at registration, suite green) (validation)
- last seen: 2026-08-12T16:24:04Z

### L-004 — Assert, for every independently-updatable field, that changing only that field advances the update timestamp — a test on one representative field does not cover the others.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `domain` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 3 mutations M3/M4 — Domain/Device.cs:171-175 (FaceCapacity) and :153-157 (HttpPort) changed-guard's changed=true removed independently, suite green on both (domain)
- last seen: 2026-08-12T16:33:45Z

### L-005 — Assert CancellationToken is honored on write paths with a pre-cancelled token expecting the operation to abort, not merely that the parameter is threaded through method signatures.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `repository` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 3 mutation M2 — Infrastructure/DeviceRepository.cs:35 (SaveChangesAsync(cancellationToken) -> SaveChangesAsync()), suite green (repository)
- last seen: 2026-08-12T16:33:45Z

### L-006 — A test reading a process-wide sink (span exporter, static logger, ActivitySource listener) must filter to traffic it provoked itself; otherwise a parallel test class's requests decide whether it passes.
- signal: `gate_fail` · recurrence: 1 feature(s) · scope: `observability` · harmful: 0
- features: test-project-conventions
- evidence: src/HikvisionReplicator.IntegrationTests/TracingTests.cs:187 (observability)
- last seen: 2026-08-17T13:50:36Z

### L-008 — Never take a warning census from a build that did not succeed; a failing project aborts the build before dependent projects compile, so their warnings are invisible.
- signal: `gate_fail` · recurrence: 1 feature(s) · scope: `build` · harmful: 0
- features: code-style-enforcement
- evidence: AnalysisMode measurement (build)
- last seen: 2026-08-17T21:10:08Z

### L-009 — Use 'dotnet format whitespace', never bare 'dotnet format': bare also runs analyzer fixers, which stamped [Obsolete] onto test fixtures to silence a deprecated-API advisory.
- signal: `gate_fail` · recurrence: 1 feature(s) · scope: `build` · harmful: 0
- features: code-style-enforcement
- evidence: src/HikvisionReplicator.IntegrationTests/PostgresFixture.cs:16 (build)
- last seen: 2026-08-17T21:10:09Z

### L-010 — In .editorconfig globs use '**.cs' not '**/*.cs'; the latter requires at least one directory level and silently skips files sitting in a project root.
- signal: `gate_fail` · recurrence: 1 feature(s) · scope: `config` · harmful: 0
- features: code-style-enforcement
- evidence: .editorconfig (config)
- last seen: 2026-08-17T21:10:09Z

### L-011 — An acceptance criterion demanding 'zero diagnostics' contradicts a design that accepts warnings; say 'zero errors' and enumerate the accepted warnings.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: code-style-enforcement
- evidence: AC-1 (specs)
- last seen: 2026-08-17T21:10:09Z

### L-012 — Every discrimination-sensor pass needs at least one must-fail mutation; a mutation applied to a wrong path reports a pass indistinguishable from a real one.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `tests` · harmful: 0
- features: code-style-enforcement
- evidence: sensor M2 (tests)
- last seen: 2026-08-17T21:10:09Z

### L-013 — Require a status check only after its workflow is on the default branch; requiring it earlier deadlocks every in-flight PR, which triggers no run and so reports no check.
- signal: `gate_fail` · recurrence: 1 feature(s) · scope: `ci` · harmful: 0
- features: branch-protection
- evidence: .github/workflows/ci.yml / PR #8 (ci)
- last seen: 2026-08-17T21:30:56Z

## Quarantined (failed when applied — ignore)

A confirmed lesson that recurred alongside failure. Kept for the maintainer to review.

_none_

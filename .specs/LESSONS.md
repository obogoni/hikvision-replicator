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

### L-014 — A rule-specific dotnet_diagnostic.<ID>.severity outranks any dotnet_analyzer_diagnostic.category-*.severity regardless of section order, so a category 'none' cannot exempt a rule set to error; use generated_code = true.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `config` · harmful: 0
- features: code-style-enforcement
- evidence: validation.md P8 (.editorconfig:45) (config)
- last seen: 2026-08-17T22:19:57Z

### L-015 — When execution changes the command a criterion names, amend the criterion text too; a stale acceptance criterion instructs future readers to run the command the implementation forbids.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: code-style-enforcement
- evidence: AC-4 (specs)
- last seen: 2026-08-17T22:19:57Z

### L-016 — Record the exact command and the dedup basis next to any measured diagnostic count; raw log-line counts, MSBuild's 'N Warning(s)' summary, and distinct sites all give different numbers.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `build` · harmful: 0
- features: code-style-enforcement
- evidence: AC-8 (build)
- last seen: 2026-08-17T22:19:57Z

### L-017 — An up-to-date incremental build re-reports nothing even when a config change since the last build would now make the diagnostic an error; the cache hinges on whether the previous build succeeded, not on the diagnostic's severity.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `build` · harmful: 0
- features: code-style-enforcement
- evidence: AD-027 L-007 sequence: warning-severity build succeeds, then -warnaserror incremental build reports 0 (build)
- last seen: 2026-08-17T22:19:57Z

### L-018 — When a requirement promises the path to a stricter setting is recorded with measured costs, measure the stricter setting itself; measuring only the current and looser rungs leaves the ratchet unpriced.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: code-style-enforcement
- evidence: CSE-08 (specs)
- last seen: 2026-08-17T22:19:57Z

### L-019 — Verify a CI workflow by citing a real completed run, not by parsing its YAML; a parse validates neither the action refs nor that any step executes.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `ci` · harmful: 0
- features: code-style-enforcement
- evidence: AC-7 (ci)
- last seen: 2026-08-17T22:19:57Z

### L-020 — When a spec inventories a compound rule as a list of items, give every item its own check; a gate that checks five of six sub-items silently blesses the missing one.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: context-engineering
- evidence: spec.md R-20 / CTX-02 — 'dotnet restore' resolves to no file:line (specs)
- last seen: 2026-08-20T15:02:45Z

### L-021 — Scope a documentation check to the prose that states the rule, excluding fenced blocks and file trees, or an incidental mention of the topic will satisfy it.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md sensor M2b, M5b, M6 — regexes matched the project-structure tree in CLAUDE.md (docs)
- last seen: 2026-08-20T15:02:45Z

### L-022 — Assert the rule's imperative, not the presence of its topic word; a check that a term appears passes unchanged when the rule is inverted.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md sensor M7, M10, M14, M2b — inverted rules passed a keyword-presence gate (docs)
- last seen: 2026-08-20T15:02:45Z

### L-023 — Give every negative acceptance criterion its own check; a SHALL NOT with no check is an unverified claim, not a satisfied one.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: context-engineering
- evidence: validation.md — AC-4 amended clause 'the hub SHALL NOT restate it' has no check and is breached at CLAUDE.md:64, :90, :91 (specs)
- last seen: 2026-08-20T15:02:45Z

### L-024 — When extracting a reference doc, source it only from the file being split; pulling extra text from the decision log creates a second authority that drifts.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md Edge Cases — docs/slice-anatomy.md:21,41,44-46,48-50,115-116,118-120 transcribe .specs/STATE.md AD-001/002/004/008/009/023 (docs) (+1 more)
- last seen: 2026-08-20T15:25:57Z

### L-025 — Write a regression check for the class of defect, not for the wording of the instance that was reported; a check matching the caught mutant's literal string passes on the next variant.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `tests` · harmful: 0
- features: context-engineering
- evidence: validation.md sensor M13b, M19, M22 — checks written against the exact strings iteration 1's mutants used (tests) (+1 more)
- last seen: 2026-08-20T15:45:58Z

### L-026 — Assert that a rule is stated unconditionally; a check that the rule appears still passes when a qualifier is appended that guts it.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md sensor M05, M08 — 'applied at startup only in Development' and 'rejects the push only until CI reports' (docs)
- last seen: 2026-08-20T15:25:57Z

### L-027 — Score a mutant by which check failed, not by the gate's exit code, and keep mutations size-neutral when the artifact sits exactly on a size limit.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `tests` · harmful: 0
- features: context-engineering
- evidence: validation.md sensor M12, M13 — killed by the <=110 line-count check, not by the AC-4 check under test (tests)
- last seen: 2026-08-20T15:25:57Z

### L-028 — When an amendment reclassifies inventory rows to match work already shipped, re-derive the whole classification; the loose boundary that mis-filed one row mis-filed others.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: context-engineering
- evidence: spec.md:256-272 re-marked R-12/R-32/R-34 after the fact; R-38 is still marked → while stated at CLAUDE.md:92-93 (specs)
- last seen: 2026-08-20T15:25:57Z

### L-029 — Re-measure every figure a decision entry quotes about an artifact at the last commit that touches that artifact, not at the commit that first wrote the entry.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: context-engineering
- evidence: .specs/STATE.md:322 says the hub landed at 109 lines; fa05813 made it 110 (specs)
- last seen: 2026-08-20T15:25:57Z

### L-030 — A check that a rule is absent must match the rule's proposition, not one token from its current wording; a restatement in different words leaves the token unmatched and passes.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md § Sensor, M15/M16/M17/M18/M28 (docs)
- last seen: 2026-08-20T15:45:58Z

### L-031 — When a criterion requires a narrative to keep its concrete detail, assert the wrong outcome it records, not only the artifact it names.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md § Sensor, M21/M22 (docs)
- last seen: 2026-08-20T15:45:58Z

### L-032 — Apply a duplication or corpus-overlap check to every file the change touched, not only to the files it created.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `docs` · harmful: 0
- features: context-engineering
- evidence: validation.md § Sensor, M27 (docs)
- last seen: 2026-08-20T15:45:58Z

### L-033 — An author-written gate's passing count measures the checks written, not the faults they catch, so never cite it as the evidence for an acceptance criterion.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: context-engineering
- evidence: validation.md § Code Quality; tasks.md:36-38 (specs)
- last seen: 2026-08-20T15:45:58Z

### L-034 — When a repair must touch a clause that merely contains the defect, narrow the edit to the defective clause; retiring the surrounding statement breaks a confinement criterion even when the retirement is correct.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `specs` · harmful: 0
- features: context-engineering
- evidence: CTX-10; spec.md:294; .specs/STATE.md:337 (specs)
- last seen: 2026-08-20T15:45:58Z

### L-035 — Before documenting that a guard distinguishes two forms of an input, mutate it to the other form and confirm a test fails; an expression that is invariant across them distinguishes nothing.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `normalization` · harmful: 0
- features: user-registry
- evidence: validation.md § Discrimination Sensor, mutation 3a — SkiaFaceImageNormalizer.cs:125-126 (normalization)
- last seen: 2026-08-26T12:21:05Z

### L-036 — When an edge case lists several kinds of input, commit one fixture per named kind; a kind with no fixture is uncovered however well its siblings are asserted.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `fixtures` · harmful: 0
- features: user-registry
- evidence: validation.md § Edge Cases, Note 2 — no CMYK fixture for the grayscale/CMYK/ICC edge case (fixtures)
- last seen: 2026-08-26T12:21:05Z

### L-037 — Register a new Meter on the metrics provider as deliberately as a new ActivitySource on the tracer; an instrument nobody reads records into nothing in production while its test still passes.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `observability` · harmful: 0
- features: user-registry
- evidence: validation.md § Precision Notes, Note 4 — USR-41 metrics recorded with no WithMetrics pipeline in Program.cs (observability)
- last seen: 2026-08-26T12:21:06Z

### L-038 — An atomicity claim needs a test that forces the second write to fail, not an argument that both writes share one SaveChanges.
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `persistence` · harmful: 0
- features: user-registry
- evidence: validation.md § Precision Notes, Note 6 — USR-30 same-transaction claim has no fault-injection test unlike USR-10 (persistence)
- last seen: 2026-08-26T12:21:06Z

### L-039 — Before deleting a component-level test as redundant, mutate the component and confirm a surviving test fails; a caller that trims or compensates hides the component's wrong bound behind an identical response.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `tests` · harmful: 0
- features: user-registry
- evidence: validation-ad036.md M8 — src/HikvisionReplicator.Api/Domain/Specs/ActiveUsersPagedSpec.cs:22 (tests)
- last seen: 2026-08-26T21:22:47Z

### L-040 — When a rule requires proving a race-reachable behaviour deterministically, add the deterministic guard for every component that behaviour spans, not only the one that prompted the rule.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `tests` · harmful: 0
- features: user-registry
- evidence: validation-ad036.md D2 — src/HikvisionReplicator.Api/Infrastructure/DeviceRepository.cs:49 (tests)
- last seen: 2026-08-26T21:22:47Z

### L-041 — Pin a user-visible message against a literal in at least one test; comparing it only to the constant the production code emits moves both together and asserts nothing.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `tests` · harmful: 0
- features: user-registry
- evidence: validation-ad036.md M6 — src/HikvisionReplicator.Api/Shared/IUserRepository.cs:22-26 (tests)
- last seen: 2026-08-26T21:22:47Z

### L-042 — After a mutation run, rebuild before re-testing: restoring the source leaves the last build's mutated binary in place, and dotnet test --no-build then reports a failure against code that is no longer on disk.
- signal: `gate_fail` · recurrence: 1 feature(s) · scope: `testing` · harmful: 0
- features: user-registry
- evidence: src/HikvisionReplicator.IntegrationTests/UserPersistenceContractTests.cs:338 (testing)
- last seen: 2026-08-26T21:32:05Z

### L-043 — Before deleting a test as duplicated, read the requirement it cites and what its assertion actually protects; a string equality that looks like framework-format pinning may be the only guard on a distinct property.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `testing` · harmful: 0
- features: user-registry
- evidence: src/HikvisionReplicator.IntegrationTests/UserObservabilityTests.cs:190 (testing)
- last seen: 2026-08-26T21:36:10Z

## Quarantined (failed when applied — ignore)

A confirmed lesson that recurred alongside failure. Kept for the maintainer to review.

_none_

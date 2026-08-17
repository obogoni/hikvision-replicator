# STATE

## Decisions

Project-level decisions every future feature must follow or explicitly supersede.
AD-001…AD-012 were **reverse-engineered** from the existing codebase on 2026-08-02
when spec-driven development was adopted — they document conventions already in
force, not new choices. See [ARCHITECTURE.md](ARCHITECTURE.md) for the full map.

### AD-001
- **Decision**: Features are organised as vertical slices under `Features/{Resource}/{Operation}/`, three files each — `{Op}Service.Interface.cs`, `{Op}Service.cs`, `{Op}Service.Endpoint.cs`.
- **Reason**: Keeps everything one operation needs in one folder; adding an operation never edits an existing slice.
- **Trade-off**: Boilerplate per operation and duplicated response shapes across slices.
- **Scope**: `src/HikvisionReplicator.Api/Features/**`
- **Date**: 2026-08-02
- **Status**: active

### AD-002
- **Decision**: All fallible operations return `OneOf<TSuccess, …Error>`; errors are standalone records in `Shared/Errors.cs` with no abstract base class. Exceptions are reserved for genuinely exceptional failures.
- **Reason**: Makes every failure mode visible in the signature and exhaustively handled at the endpoint's `.Match()`.
- **Trade-off**: Adding an error type to a service ripples through every caller's `Match`.
- **Scope**: Domain factories, all services, all endpoints.
- **Date**: 2026-08-02
- **Status**: active

### AD-003
- **Decision**: Services return `Task<OneOf<Response, …>>`, never `Task<IResult>`. HTTP translation happens only in the endpoint layer via `ToMinimalApiResult()` in `Infrastructure/DomainErrorExtensions.cs`.
- **Reason**: Services stay transport-agnostic and directly unit-testable.
- **Trade-off**: One extra mapping hop per endpoint.
- **Scope**: `Features/**`, `Infrastructure/DomainErrorExtensions.cs`
- **Date**: 2026-08-02
- **Status**: active

### AD-004
- **Decision**: DTOs are per-slice and never shared between features, even when identical (e.g. `UpsertUser.UserResponse` and `GetUser.UserResponse`).
- **Reason**: A slice can change its contract without breaking a neighbouring slice.
- **Trade-off**: Deliberate duplication that looks like a DRY violation.
- **Scope**: `Features/**`
- **Date**: 2026-08-02
- **Status**: active

### AD-005
- **Decision**: Aggregate roots (`Device`, `User`, `Replication`) implement `IAggregateRoot`, keep private setters and a private EF constructor, and are only constructed through static `Create(...)` factories returning `OneOf<T, ValidationError>`. Validation lives in the domain, not in DTO attributes.
- **Reason**: An invalid aggregate cannot be represented; validation cannot be bypassed by a new caller.
- **Trade-off**: More ceremony than anaemic entities + FluentValidation.
- **Scope**: `Domain/**`
- **Date**: 2026-08-02
- **Status**: active

### AD-006
- **Decision**: Services inject `IRepository<T>` (never `AppDbContext`) and query exclusively through `Specification<T>` subclasses in `Domain/Specs/`. Inline LINQ predicates in services are not allowed.
- **Reason**: Query rules are named, reusable, and testable; the persistence type never leaks into a slice.
- **Trade-off**: A new spec class for every query shape.
- **Scope**: `Features/**`, `Domain/Specs/**`, `Infrastructure/*Repository.cs`
- **Date**: 2026-08-02
- **Status**: active

### AD-007
- **Decision**: `ExecuteAsync` takes `CancellationToken cancellationToken` as a required last parameter (no default) and forwards it to every async call; endpoints declare `CancellationToken ct` for ASP.NET Core to inject.
- **Reason**: Cancellation must be end-to-end; a default value lets it be silently dropped.
- **Scope**: All services and endpoints.
- **Trade-off**: None material.
- **Date**: 2026-08-02
- **Status**: active

### AD-008
- **Decision**: Device passwords are AES-256 encrypted (reversible) on write via `IEncryptionService`, and the encrypted value is never returned in any response.
- **Reason**: The service must recover the plaintext to authenticate against devices, so hashing is not an option.
- **Trade-off**: Compromise of `Encryption:Key` exposes every device credential; current mode (CBC) gives confidentiality without integrity.
- **Scope**: `Infrastructure/EncryptionService.cs`, device slices.
- **Date**: 2026-08-02
- **Status**: active

### AD-009
- **Decision**: EF Core mapping lives in `IEntityTypeConfiguration<T>` classes under `Infrastructure/`, picked up automatically by `ApplyConfigurationsFromAssembly`. Value objects map through `ValueConverter` + `internal static FromPersistence(...)`; enums persist as strings.
- **Reason**: Zero-touch registration; readable database values.
- **Trade-off**: `FromPersistence` is a validation-bypassing back door, limited to `internal`.
- **Scope**: `Infrastructure/**`, `Domain/**` value objects.
- **Date**: 2026-08-02
- **Status**: active

### AD-010
- **Decision**: Deferred work runs as Hangfire jobs (SQLite storage) enqueued from the service that triggers it — e.g. `UpsertUserService` enqueues `UserSyncJob`.
- **Reason**: Replication to devices is slow and failure-prone; it must not block the HTTP request.
- **Trade-off**: SQLite-backed Hangfire limits throughput and multi-instance deployment.
- **Scope**: `Features/**/*Job.cs`, `Program.cs`
- **Date**: 2026-08-02
- **Status**: active

### AD-011
- **Decision**: Default test level is in-process integration tests (xUnit + `TestWebApplicationFactory` + in-memory SQLite) exercising the HTTP surface; Playwright/NUnit E2E tests cover the same routes against a live API. Test names describe behaviour in plain English per [`docs/test-patterns.md`](../docs/test-patterns.md) — no HTTP verbs, no status codes, no method names.
- **Reason**: Slices are thin; testing through the endpoint covers routing, serialisation, domain rules, and persistence in one pass.
- **Trade-off**: Slower than unit tests, and domain edge cases are harder to reach.
- **Scope**: `src/HikvisionReplicator.Tests/**`, `src/HikvisionReplicator.E2ETests/**`
- **Status**: superseded by AD-019
- **Date**: 2026-08-02

### AD-012
- **Decision**: Adopt spec-driven development from 2026-08-02 onward. New work starts with `.specs/features/[feature]/spec.md`; existing slices are documented in `ARCHITECTURE.md` and backfilled with specs only when they are next changed.
- **Reason**: Backfilling specs for already-working code has low return; specs pay off on change.
- **Trade-off**: Requirement traceability is incomplete for pre-2026-08-02 code.
- **Scope**: Whole repository.
- **Date**: 2026-08-02
- **Status**: superseded by AD-013

### AD-013
- **Decision**: Rewrite the application from scratch, spec-first. The existing `src/` is treated as a **reference implementation**, not a base to extend: every feature is specified before it is built. Conventions AD-001…AD-011 and the stack (.NET 10 · ASP.NET Core Minimal APIs · EF Core 10 + SQLite · Ardalis.Specification · OneOf · Hangfire · OpenTelemetry) carry over unchanged.
- **Reason**: The current code has no requirement traceability and its core capability (pushing replications to devices) was never built; backfilling specs onto a half-finished pipeline costs more than rebuilding against specs.
- **Trade-off**: Discards working, tested code for the seven shipped slices and repeats that effort.
- **Scope**: Whole repository — supersedes AD-012.
- **Date**: 2026-08-02
- **Status**: active

### AD-014
- **Decision**: The product goal is **live synchronization of users to devices, performant and fault-tolerant**, driven by the stadium scenario: a spectator who buys a ticket minutes before an event must be able to enter by facial recognition. Latency from `POST /api/users` to enrolled-on-all-devices is the primary quality attribute; resilience to individual offline readers is the second.
- **Reason**: Stated by the product owner as the reason the project exists.
- **Trade-off**: Prioritises propagation latency and fault tolerance over feature breadth — operational features (health, reconciliation, visibility, auth) land after the engine works.
- **Scope**: Whole product; sizes every Phase 2 decision.
- **Date**: 2026-08-02
- **Status**: active

### AD-015
- **Decision**: Every user is replicated to **every** registered device. No access scoping, groups, or per-device permissions in the MVP.
- **Reason**: Confirmed as sufficient for the MVP; keeps the fan-out rule trivial.
- **Trade-off**: Fan-out is `users × devices` with no way to reduce it, and the rule is physically invalid if a device's face-library capacity is below the user count (see ROADMAP OD-1). Zoned access (VIP/sector) will require a new aggregate.
- **Scope**: `replication-queue` fan-out rules, `User` aggregate.
- **Date**: 2026-08-02
- **Status**: active

### AD-016
- **Decision**: An external integrator is the system of record for users and drives both additions and removals through this API. This service owns propagation to devices only. Delete/removal is a first-class Phase 1 capability, not a later addition.
- **Reason**: Confirmed integration model; the Remove replication path only ever fires from an integrator-initiated delete, so it cannot be deferred.
- **Trade-off**: No independent verification that our user set matches the ticketing system's — `reconciliation` (Phase 4) covers device drift, not upstream drift.
- **Scope**: `user-registry`, `replication-queue`.
- **Date**: 2026-08-02
- **Status**: active

### AD-017
- **Decision**: The replication engine is built against an `IDeviceClient` port with a fake in-memory adapter. The real Hikvision ISAPI adapter is a separate later feature (`isapi-device-client`, Phase 3) swapped in behind the same port.
- **Reason**: Lets the entire pipeline — fan-out, priority, retries, status transitions, failure handling — be specified and tested with no hardware, and keeps device-protocol concerns out of the engine.
- **Trade-off**: Engine acceptance criteria are verified against a fake; real-device behaviour (timeouts, partial uploads, vendor error codes) is not exercised until Phase 3.
- **Scope**: `replication-queue`, `replication-worker`, `isapi-device-client`.
- **Date**: 2026-08-02
- **Status**: active

### AD-018
- **Decision**: **PostgreSQL is the database from the first commit** of the rewrite — not SQLite. This resolves ROADMAP OD-2 and reopens Hangfire-on-PostgreSQL as a viable job runner (OD-3).
- **Reason**: SQLite is single-writer; a parallel replication worker draining millions of rows would serialize on it, directly defeating the performance goal in AD-014. Choosing at the start avoids a provider migration once real data exists.
- **Trade-off**: Local development and CI now require a running PostgreSQL instance (Docker), where SQLite needed nothing. The existing `docker-compose.yml` gains a database service alongside Tempo/Grafana.
- **Scope**: `Infrastructure/AppDbContext.cs`, migrations, `Program.cs`, `docker-compose.yml`, all test projects. Amends the stack recorded in AD-013.
- **Date**: 2026-08-02
- **Status**: active

### AD-019
- **Decision**: Integration tests run against a **real PostgreSQL instance provisioned by Testcontainers**, shared per test collection and reset between tests. Everything else in AD-011 carries over unchanged: integration-first through the HTTP surface, Playwright/NUnit E2E against a live API, and behaviour-based test naming per [`docs/test-patterns.md`](../docs/test-patterns.md).
- **Reason**: AD-018 makes in-memory SQLite a different engine from production — provider-specific behaviour (concurrency, locking, `SELECT … FOR UPDATE`, JSON/array columns) is exactly what the replication worker depends on, so testing against SQLite would verify the wrong database.
- **Trade-off**: Tests need Docker and run slower than in-memory SQLite; CI must provide a Docker daemon.
- **Scope**: `src/HikvisionReplicator.Tests/**`, `src/HikvisionReplicator.E2ETests/**`. Supersedes AD-011.
- **Date**: 2026-08-02
- **Status**: active — the "integration is the default level" clause is amended by AD-024; the Testcontainers requirement and behaviour-based naming stand unchanged

### AD-020
- **Decision**: Device face-library capacity is a **hard domain constraint modelled on the `Device` aggregate** (`FaceCapacity`), not an implementation detail. The bench/development unit holds 10,000 faces; production readers must hold at least the full user count (see AD-021).
- **Reason**: Capacity varies by model, so it cannot be a constant. Modelling it lets the system enforce it.
- **Trade-off**: Every device registration must supply a capacity figure the operator has to look up.
- **Scope**: `device-registry`, `replication-queue` capacity guards.
- **Date**: 2026-08-02
- **Status**: active

### AD-021
- **Decision**: Resolve the 50,000-users vs. 10,000-faces-per-device conflict by **standardising on higher-capacity Hikvision models** rather than scoping provisioning. AD-015 (every user on every device) therefore stands unchanged, and gate/sector scoping stays out of scope.
- **Reason**: Product owner's decision — keeps the fan-out rule trivial and avoids introducing an access-scope aggregate.
- **Trade-off**: Capital expense, and the system runs near 100% of each device's face library with little headroom for growth beyond 50,000 users — a later increase in the user target requires new hardware or a retrofit to scoped provisioning. The existing 10,000-face bench unit cannot be used to validate full-scale enrolment in Phase 3. Concern was raised and the decision reaffirmed.
- **Mitigation (required, not optional)**: `Device.FaceCapacity` is enforced — a replication that would exceed a device's capacity fails loudly with a distinct error and surfaces in `replication-visibility`. Silent enrolment failure at a turnstile is the system's worst failure mode.
- **Scope**: `device-registry`, `replication-queue`, hardware procurement.
- **Date**: 2026-08-02
- **Status**: active

### AD-022
- **Decision**: Uniqueness and similar invariants are enforced by a **database constraint**, and the provider's constraint violation is translated into a domain error (`ConflictError`) **inside the repository**. A pre-check specification may run first, but only to produce a friendlier message — it is never the authority. Services never catch provider exceptions; they see only `OneOf` values.
- **Reason**: A read-then-write check alone is racy: two concurrent writers both pass the pre-check and one gets an unhandled `DbUpdateException` → `500`. The reference implementation had exactly this bug in `CreateDeviceService`. Translating in the repository keeps `PostgresException` out of the slices.
- **Trade-off**: Each invariant needs both a constraint and a translation path, and the translation must key off a named index — a renamed index silently degrades a 409 into a 500 unless a test covers it.
- **Scope**: `Infrastructure/*Repository.cs`, `Shared/I*Repository.cs`, all slices with uniqueness or idempotency rules. Phase 2 `replication-queue` idempotency depends on this.
- **Date**: 2026-08-12
- **Status**: active

### AD-023
- **Decision**: Time comes from an injected `TimeProvider`; services read `provider.GetUtcNow().UtcDateTime` and pass `now` **into** domain factories and mutators. Aggregates never call `DateTime.UtcNow` themselves. Timestamp fields advance only when a value actually changed.
- **Reason**: `UpdatedAt` assertions (DEV-23) and Phase 2's retry backoff and latency SLO all need a controllable clock. The reference `Device.Update` read the wall clock internally and advanced `UpdatedAt` unconditionally, making a no-op update indistinguishable from a real one.
- **Trade-off**: Every factory and mutator signature carries a `now` parameter, and every service takes one more constructor dependency.
- **Scope**: `Domain/**`, `Features/**`, `Program.cs` (registers `TimeProvider.System`).
- **Date**: 2026-08-12
- **Status**: active

### AD-024
- **Decision**: **Test level is chosen by layer, not uniformly.**
  - **Unit tests** cover pure logic with no I/O — domain aggregates, value objects, and self-contained infrastructure logic such as `EncryptionService` and options validation. Depth: all branches, 1:1 with spec acceptance criteria, every listed edge case. They live under `src/HikvisionReplicator.Tests/Domain/` and carry `[Trait("Category", "Unit")]` so they run without Docker.
  - **Integration tests** cover everything that touches I/O or wiring — feature slices and their routes, repositories and specifications, startup behaviour, and cross-cutting handlers — in-process through the HTTP surface against Testcontainers PostgreSQL (AD-019).
  - **E2E tests** (Playwright/NUnit) stay a thin out-of-process confirmation of each route, not a coverage layer.
- **Reason**: Branch-level domain behaviour is observable only indirectly through HTTP. The two defects the rewrite fixes in `Device` — IP normalization and the `UpdatedAt` change-guard — are exactly this shape, and DEV-23's "no change means no touch" is not cleanly assertable through a round-trip. Splitting by layer also keeps a fast, Docker-free feedback loop for the pure logic.
- **Trade-off**: Two levels mean some rules are asserted twice, so a domain change can require edits in both a unit and an integration test. It also invites drift where a branch is unit-tested but never proven reachable through the API — mitigated by keeping AC-level coverage at the integration layer for **every** endpoint, so unit tests add depth rather than replacing endpoint coverage.
- **Scope**: `src/HikvisionReplicator.Tests/**`, `src/HikvisionReplicator.E2ETests/**`, `docs/test-patterns.md`. Amends AD-019's default-level clause.
- **Date**: 2026-08-12
- **Status**: active — the layer definitions stand; **where each level lives is amended by AD-026**, which gives each its own project and retires the `[Trait("Category", "Unit")]` marker. Paths named above are pre-AD-026.

### AD-025
- **Decision**: **Git workflow is branch-per-change, Conventional Commits, merged into `main` only through a squash-merged pull request.**
  - **No direct commits to `main`** — the sole exception is an explicit, in-the-moment instruction from the user for that specific commit. A general approval of a task is not such an instruction.
  - **Branch naming** is `<type>/<kebab-slug>`, reusing the commit type vocabulary (`feat/device-registry`, `fix/ip-normalization`, `chore/repo-conventions`).
  - **Commit messages** follow Conventional Commits — `type(scope): subject` with the standard type set (`feat` `fix` `docs` `test` `refactor` `perf` `build` `ci` `chore`); **scope is optional but encouraged**, drawn from `domain` · `devices` · `infra` · `tests` · `e2e` · `specs` · `deps`. Subject is imperative, lower case, no trailing period.
  - **Merging** happens via `gh pr create` against `.github/pull_request_template.md`, reviewed and merged by the user, using **squash merge**. The PR title therefore becomes the `main` commit and must itself be a valid conventional-commit subject.
  - **A PR's base is `main`.** Stack only when genuinely required, and when stacking, either merge the base PR *with its branch deleted* or retarget the child to `main` before merging. **A PR whose base is another branch merges into that branch, not into `main`** — GitHub retargets a child only when the base branch is deleted on merge. **After every merge, verify `main` itself** (`git ls-tree -d --name-only origin/main src/`, `git log origin/main`); a "Merged" badge says only that something merged somewhere.
  - **Enforcement is mechanical where the repo can enforce it, documentary elsewhere** (amended 2026-08-17):
    - *Mechanical*, via repository settings — `squashMergeAllowed=true`, `mergeCommitAllowed=false`, `rebaseMergeAllowed=false` make squash the only available merge; `deleteBranchOnMerge=true` deletes each merged head branch, which also auto-retargets stacked children to `main`. Set with `gh repo edit --enable-squash-merge=true --enable-merge-commit=false --enable-rebase-merge=false --delete-branch-on-merge`.
    - *Documentary*, via CLAUDE.md, this entry, and the PR template — commit-message format, branch naming, one-atomic-commit-per-task, and the no-direct-commits-to-`main` rule. Still no commit-msg or pre-commit hooks.
- **Reason**: The spec-driven workflow already produces one atomic commit per task, which makes per-branch history a genuine audit trail of requirement → task → commit; a PR is where that trail gets reviewed. Squash keeps `main` readable at one commit per shipped change while the granular history survives on the PR. Hooks are still rejected — they need per-clone installation (`core.hooksPath`) and would block the agent mid-task with no reviewer present — but repository settings need no per-clone install and cost nothing, so the clauses a setting *can* enforce are no longer left to discipline.
- **Reason for the 2026-08-17 amendment**: the base-is-`main` rule and the mechanical settings were both written in blood. Twice, a PR was merged and the work did not reach `main`:
  - **PR #2** (`chore/repo-conventions` → base `feat/device-registry`) — the base had already been squash-merged by PR #1, so AD-025 itself, the PR template, and the CLAUDE.md Git Workflow section were stranded off `main` for four days. Recovered by PR #3.
  - **PR #4** (`refactor/test-project-layout` → base `docs/conventional-commits`) — the whole `test-project-conventions` feature merged into that branch instead of `main`. Recovered by PR #5.
  Both had `deleteBranchOnMerge=false` as the root cause: the base branches survived, so the children were never retargeted. Both were caught only by reading `main` directly, never by the PR UI, which showed "Merged" in each case.
- **Trade-off**: Squashing means the per-task SHAs recorded in `validation.md` files are **pre-squash references** that do not resolve on `main` after merge — they remain reachable only via the PR, so a deleted branch's PR page becomes the sole home of that history. Auto-deleting head branches makes that deletion automatic, which is the point but also means a branch cannot be re-pushed after merge without recreating it. Direct commits to `main` remain only documentarily prevented — no branch-protection rule is configured, so the rule still holds only as long as CLAUDE.md is honoured.
- **Scope**: Whole repository — every branch, commit, and merge, by any contributor or agent. Adds `.github/pull_request_template.md` and a Git Workflow section to `CLAUDE.md`. The 2026-08-17 amendment also covers GitHub repository merge settings.
- **Date**: 2026-08-13 · **amended** 2026-08-17 (base-is-`main` rule; mechanical enforcement via repo settings)
- **Status**: active

### AD-026
- **Decision**: **Each test level gets its own project, and the project name declares the level.**
  - `HikvisionReplicator.Tests` — **unit**. Pure logic, no I/O. References neither Testcontainers nor a web host, so it cannot compile a test that needs Docker. Folders mirror the Api tree (`Domain/`, `Infrastructure/`).
  - `HikvisionReplicator.IntegrationTests` — **integration**. In-process through the HTTP surface against Testcontainers PostgreSQL (AD-019), and the home of `PostgresFixture` and `TestWebApplicationFactory`.
  - `HikvisionReplicator.E2E` — **e2e**. NUnit + Playwright against a live API. Directory, csproj filename and root namespace all match the assembly name.
  - **`[Trait("Category", "Unit")]` is retired.** The Docker-free gate is `dotnet test src/HikvisionReplicator.Tests` — a whole project, no `--filter`.
  - **Class names carry no level suffix**; the assembly disambiguates. `DeviceEndpointsTests` therefore exists in both the integration and e2e projects.
- **Reason**: AD-024 defines three levels but only two projects existed to hold them, so "which level is this test?" was answered by a folder plus an attribute that every new unit test had to remember. An omitted attribute silently dropped a test from the fast gate — a failure mode with no signal. A project boundary cannot be forgotten: the unit project's package list makes an I/O test a compile error rather than a convention violation.
- **Trade-off**: The full gate is now two commands instead of one, and package pins that matter to both suites (EF Core, kept in step with the Api) are duplicated across two csproj files and can drift. The unit project keeps EF Core pins it does not directly use, purely to suppress MSB3277 assembly conflicts from the Api reference — a non-obvious dependency that a future cleanup could remove and reintroduce 44 warnings.
- **Consequence found during execution**: splitting the assemblies removed the scheduling cover that was hiding a real test-isolation defect. `TracingTests` asserted `Assert.Single` over a span sink that receives spans process-wide, and a parallel class's `GET /api/devices` made it fail deterministically once the 81 unit tests no longer occupied the parallel worker slots. Fixed in `267ab4a` by correlating on a `traceparent` the class alone provokes. **A gate that passes because of thread scheduling is not evidence** — see `docs/test-patterns.md` § Test isolation.
- **Scope**: `src/HikvisionReplicator.Tests/**`, `src/HikvisionReplicator.IntegrationTests/**`, `src/HikvisionReplicator.E2E/**`, `HikvisionReplicator.slnx`, `CLAUDE.md`, `README.md`, `docs/test-patterns.md`, `.github/pull_request_template.md`, `.specs/ARCHITECTURE.md`. Amends AD-024's "where each level lives" clause; AD-024's layer definitions are unchanged.
- **Date**: 2026-08-17
- **Status**: active

### AD-027
- **Decision**: **Code style is enforced by the compiler on every build; the pull request is the gate. No hooks.**
  - **`.editorconfig`** at the repo root is the single source of style and formatting rules, read by the Roslyn analyzers, `dotnet format`, and CI alike.
  - **`Directory.Build.props`** sets `EnforceCodeStyleInBuild=true`, `AnalysisLevel=10.0`, `AnalysisMode=Recommended`. Every project inherits it, so **no command-line flag is ever required** — CI, local, and agent builds cannot disagree about what the rules are.
  - **`dotnet_diagnostic.IDE0055.severity = error`.** Formatting violations fail the build. `IDE0055` is the entire formatting layer as one diagnostic, which makes `dotnet build` the formatting gate and removes the need for a `dotnet format --verify-no-changes` step in CI.
  - **Severity is set per rule in `.editorconfig`, never with `-warnaserror`.** A clean build already emits 4 `NU1903` + 4 `CS0618`; `-warnaserror` would fail on those pre-existing, unrelated warnings.
  - **The fix command is `dotnet format whitespace`, never bare `dotnet format`.**
  - **Exemptions**: EF Core migrations are generated code (`generated_code = true`, Style category `none`) so scaffolding can never break the build; `CA1707` is `none` in the three test projects.
  - **`.github/workflows/ci.yml`** runs restore → build → unit → integration on every PR to `main` and on push to `main`. E2E is excluded (needs a live API).
  - **No git hooks and no `PostToolUse` editor hooks**, consistent with AD-025's rejection of hooks.
- **Reason**: The repo had no `.editorconfig`, so nothing was enforced anywhere — a full `dotnet format` run reported only `WHITESPACE` diagnostics and zero style findings, because `IDE####` rules sit below `warning` by default and `EnforceCodeStyleInBuild` was unset. Development happens entirely through AI agents with no IDE, so the "as you type" layer that normally catches style does not exist here; and there was **no CI workflow at all**, so nothing mechanical stood between an agent's edit and `main`. The compiler is the one tool an agent already runs and already reads the output of, which makes it the natural enforcement point: a style violation arrives in the same channel as a compiler error and cannot be scrolled past.
- **Reason the hook approach was rejected**: the starting proposal was a `PostToolUse` hook running `dotnet format` on each edited file. Measured at **6.4–8.2 s per invocation** (full MSBuild workspace load every time) on every `Write`/`Edit`; `dotnet format whitespace --folder` is 1.35 s but still per-edit. Worse, the sketch was silently inert — `$FILE_PATH` does not exist (hook input arrives as JSON on stdin at `.tool_input.file_path`), and `--include ""` scopes to **nothing**, so it would have exited 0 having formatted no files while appearing configured. Hooks are also per-machine, absent from a fresh clone, and bypassable. Same class of failure AD-025 fixed by moving merge rules out of documentation and into repository settings.
- **Reason for the exemptions**: `CA1707` ("identifiers should not contain underscores") fired **304 times**, entirely in the test projects, against the deliberate behaviour-based naming convention in `docs/test-patterns.md` § Naming Tests ("Words separated by underscores"). The rule is wrong here, not the names. Migrations are exempted because `dotnet ef` regenerates them and a scaffolded file must never fail a build.
- **Trade-off**: `AnalysisMode=Recommended` surfaces **10 `CA` findings** that are warnings only, so they do not gate — real risk of warning blindness, mitigated by enumerating them in `.specs/features/code-style-enforcement/spec.md` rather than leaving an anonymous warning cloud. Pinning `AnalysisLevel=10.0` means an SDK upgrade will *not* bring new rules automatically; that is deliberate (reproducibility) but must be revisited on purpose. `IDE0055` as an error also means the formatter's canonical output wins over local taste — the reformat of `DeviceRepository.IsAddressConflict` is arguably less readable, and that is the accepted price of not arguing with a formatter.
- **Measured during execution** (all on a clean `--no-incremental` solution build):
  - `AnalysisMode` cost, *after* `CA1707` was exempted: `Minimum` → 0 findings · `Recommended` → 10 · (`Recommended` before the exemption → 326).
  - **A failing build hides warnings in dependent projects.** The first `Recommended` measurement read as 3 findings because the Api's `IDE0055` errors aborted the build before the two test projects compiled. Enforcement was therefore switched on *after* the existing violations were fixed, so no commit on the branch has a failing build — and no measurement was taken from a build that did not complete.
  - **Bare `dotnet format` is not safe here.** With `AnalysisMode=Recommended` it runs the analyzer fixers, and it "fixed" the deprecated Testcontainers `PostgreSqlBuilder` call by stamping `[Obsolete]` onto `PostgresFixture` and `UnreachableDatabaseFixture` — silencing a real advisory by marking the *consumer* obsolete. `dotnet format whitespace` makes no semantic edits and still clears `IDE0055`.
  - **EditorConfig glob gotcha**: `**/*.cs` requires at least one directory level, so it missed test files sitting directly in a project root and left 18 `CA1707` behind. `**.cs` is correct.
  - **Lesson L-007 corroborated independently.** An up-to-date incremental build reported "Build succeeded" with zero diagnostics on code that a `--no-incremental` build failed with 5 `error IDE0055`. This is a false-green an agent can easily trust; the first build after a change is the signal.
- **Scope**: `.editorconfig`, `Directory.Build.props`, `.github/workflows/ci.yml` (both new), `CLAUDE.md` (§ Code Style, § Gate commands), and 5 whitespace lines in `Api/Infrastructure/DeviceRepository.cs` and `Api/Shared/IRepository.cs`. Does **not** move `TargetFramework`/`Nullable`/`ImplicitUsings` out of the four `.csproj` files — deferred. No `.git-blame-ignore-revs`: only 5 lines are reformatted, so the convention becomes worthwhile at the first wide sweep.
- **Date**: 2026-08-17
- **Status**: active

---

## Handoff

- **Feature**: `code-style-enforcement` — **complete** (AD-027), awaiting PR review. `test-project-conventions` (AD-026) and the AD-025 amendment are merged to `main` (PR #5, PR #6).
- **Phase / Task**: Execute finished, 6 atomic commits `f9c3bc9`…`e86a896` on `build/code-style-enforcement` (off `main` at `bba7908`). **Pre-squash references** — they resolve only via the PR. Validation ran as a standalone self-check again, **not** an independent sub-agent: the session forbids spawning agents unless the user asks, so **author ≠ verifier was not satisfied for the second consecutive feature**. The discrimination sensor is the load-bearing evidence, since a mutation's build outcome does not depend on the author's mental model.
- **Completed**: spec.md · validation.md (PASS, 8/8 ACs, 5/5 sensor mutations as specified). `.editorconfig` is the ruleset; `Directory.Build.props` sets `EnforceCodeStyleInBuild` + `AnalysisLevel 10.0` + `AnalysisMode Recommended`; `IDE0055` is an **error**, so `dotnet build` is the formatting gate with no flags. `.github/workflows/ci.yml` runs build + unit + integration on PRs to `main` — **the repo's first CI**. Gates: 81 unit · 88 integration, unchanged.
- **In-progress** (file:line): none
- **Next step**: merge the `code-style-enforcement` PR, then **configure branch protection on `main` requiring the `CI` check** — until then CI reports but does not block, so the gate is documentary in exactly the way AD-025's no-direct-commits clause is. Then specify Phase 1 item 2, `user-registry`; resolve OD-4 (face-image storage — 10 GB of BLOBs in the transactional database) during that spec.
- **Blockers**: none.
- **Verification findings worth carrying forward**:
  - **Never take a warning census from a build that did not succeed.** `AnalysisMode=Recommended` first measured as 3 findings; the true number is 10. The Api's `IDE0055` errors aborted the build before the two test projects compiled. Enforcement was therefore switched on only *after* existing violations were fixed.
  - **Bare `dotnet format` is unsafe here.** It runs the analyzer fixers and "fixed" the deprecated Testcontainers `PostgreSqlBuilder` call by stamping `[Obsolete]` onto `PostgresFixture` and `UnreachableDatabaseFixture` — silencing a real advisory by marking the consumer obsolete. Use `dotnet format whitespace`.
  - **A sensor mutation applied to a wrong path reports a false pass.** `ls` is aliased to a table formatter in this environment, so `$(ls … | head -1)` yielded a column header; two mutations silently targeted a junk file. Caught only because one of them was a *must-fail* mutation that reported success. Always include a must-fail mutation.
  - **`AC-1` was self-contradictory** — it demanded "zero diagnostics" while the same spec accepted 10 `CA` warnings. Corrected to "zero errors" with the warnings enumerated. Written by the same author who implemented it, which is what an independent verifier is for.
  - The `device-registry` and `test-project-conventions` findings still stand: gaps were missing assertions over correct production code, found by mutation and never by the passing gate; and a gate that passes because of thread scheduling is not evidence.
  - Lessons: **L-007 is now `confirmed`** (recurrence 2 — incremental builds re-report zero warnings; corroborated independently by this feature). L-008…L-012 added as candidates from the findings above. L-001…L-006 remain candidates; `user-registry` is where they get tested.
- **Pre-existing warnings, unchanged by this feature**: a clean `--no-incremental` build emits **4 NU1903** (SSH.NET 2025.1.0, high severity, transitive via Testcontainers) plus **4 CS0618**. This is why severity is set per rule in `.editorconfig` and **never** via `-warnaserror` — the flag would fail the build on these. Still worth its own `build(deps)` change.
- **Known non-gating debt**: 10 `CA` warnings from `AnalysisMode=Recommended`, enumerated by rule and `file:line` in `.specs/features/code-style-enforcement/spec.md`. Seven are in `IntegrationTests`. Ratchet rules to `error` as they are cleared; never jump to `AnalysisMode=All`.
- **Open decisions**: Phase 2 still needs three numbers — device/reader count, live-sync latency SLO (proposed p95 < 30s), bulk-load window. OD-3 (job runner under load) open.
- **Uncommitted files**: none
- **Branch**: `build/code-style-enforcement`, based on `main` (`bba7908`). Everything else is merged and its branch deleted; `main` carries all four projects (`Api`, `Tests`, `IntegrationTests`, `E2E`), verified by reading `main` directly.
- **Stacked-PR hazard is now recorded in AD-025, not here** — it was hit twice (PR #2, PR #4) and both times the work merged into a branch instead of `main`. The rule and the root cause live in the decision log; the repository settings that prevent it (`squashMergeAllowed` only, `deleteBranchOnMerge=true`) are applied. Do not restate it in future handoffs — read AD-025.
- **Repo settings now differ from a fresh clone's defaults**: merge commits and rebase merges are disabled, head branches auto-delete. Anyone reasoning about merge behaviour should check `gh repo view --json deleteBranchOnMerge,squashMergeAllowed,mergeCommitAllowed,rebaseMergeAllowed` rather than assume GitHub defaults.
- **Not configured**: branch protection on `main`. Nothing mechanically blocks a direct push; that clause of AD-025 is still documentary only.
- **Surviving pre-rewrite branches**, untouched and unreviewed: `001-hikvision-device-api`, `002-adr-conformance` (local-only, no upstream).

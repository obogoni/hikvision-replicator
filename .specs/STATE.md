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
  - **Enforcement is documentary, not mechanical** — CLAUDE.md plus this entry plus the PR template. No commit-msg or pre-commit hooks.
- **Reason**: The spec-driven workflow already produces one atomic commit per task, which makes per-branch history a genuine audit trail of requirement → task → commit; a PR is where that trail gets reviewed. Squash keeps `main` readable at one commit per shipped change while the granular history survives on the PR. Documentary enforcement was chosen over hooks because hooks need per-clone installation (`core.hooksPath`) and would block the agent mid-task with no reviewer present.
- **Trade-off**: Squashing means the per-task SHAs recorded in `validation.md` files are **pre-squash references** that do not resolve on `main` after merge — they remain reachable only via the PR. Documentary enforcement also means nothing mechanically prevents a stray commit on `main`; the rule holds only as long as CLAUDE.md is honoured. Stacked branches (work based on an unmerged branch) require a manual rebase once the base squash-merges.
- **Scope**: Whole repository — every branch, commit, and merge, by any contributor or agent. Adds `.github/pull_request_template.md` and a Git Workflow section to `CLAUDE.md`.
- **Date**: 2026-08-13
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
- **Consequence found during execution**: splitting the assemblies removed the scheduling cover that was hiding a real test-isolation defect. `TracingTests` asserted `Assert.Single` over a span sink that receives spans process-wide, and a parallel class's `GET /api/devices` made it fail deterministically once the 81 unit tests no longer occupied the parallel worker slots. Fixed in `98765c4` by correlating on a `traceparent` the class alone provokes. **A gate that passes because of thread scheduling is not evidence** — see `docs/test-patterns.md` § Test isolation.
- **Scope**: `src/HikvisionReplicator.Tests/**`, `src/HikvisionReplicator.IntegrationTests/**`, `src/HikvisionReplicator.E2E/**`, `HikvisionReplicator.slnx`, `CLAUDE.md`, `README.md`, `docs/test-patterns.md`, `.github/pull_request_template.md`, `.specs/ARCHITECTURE.md`. Amends AD-024's "where each level lives" clause; AD-024's layer definitions are unchanged.
- **Date**: 2026-08-17
- **Status**: active

---

## Handoff

- **Feature**: `device-registry` (Phase 1, item 1) — **complete**
- **Phase / Task**: Execute finished. 24 tasks, 28 commits on `feat/device-registry`. Three Verifier iterations; every gap closed and mutation-verified.
- **Completed**: spec.md (confirmed) · design.md (approved) · tasks.md (24 ✅) · validation.md (iteration 3) · 25 of 26 requirements verified, DEV-26 deferred by decision. **169 in-process tests** (81 unit / 88 integration, Testcontainers PostgreSQL) + 9 e2e. Build reports zero NuGet advisory warnings.
- **In-progress** (file:line): none
- **Next step**: **Review and squash-merge the `feat/device-registry` PR into `main`** (branch is pushed and in sync with `origin`; merging now goes through a PR per AD-025), then rebase `chore/repo-conventions` onto the new `main` and specify Phase 1 item 2, `user-registry`. Resolve OD-4 (face-image storage — 10 GB of BLOBs in the transactional database) during that spec.
- **Blockers**: none.
- **Verification findings worth carrying forward**: all three gaps were *missing assertions over correct production code*, not bugs. Mutation testing found every one; the passing gate found none. Lessons L-001…L-005 in `lessons.json` are all `candidate` — promotion needs corroboration from a second feature, so `user-registry` is where they get tested.
- **Open decisions**: Phase 2 still needs three numbers — device/reader count, live-sync latency SLO (proposed p95 < 30s), bulk-load window. OD-3 (job runner under load) open.
- **Uncommitted files**: none
- **Branch**: `chore/repo-conventions` (stacked on `feat/device-registry`; needs `git rebase --onto main feat/device-registry chore/repo-conventions` once that PR squash-merges)

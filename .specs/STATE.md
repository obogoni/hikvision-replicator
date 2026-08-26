# STATE

## Decisions

Project-level decisions every future feature must follow or explicitly supersede.
AD-001…AD-012 were **reverse-engineered** from the existing codebase on 2026-08-02
when spec-driven development was adopted — they document conventions already in
force, not new choices. The architecture map they once pointed at is retired (AD-029) —
read the code itself, `CLAUDE.md`, and [ROADMAP.md](ROADMAP.md).

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
- **Status**: superseded by AD-030

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
- **Status**: active — the rewrite decision stands, but the **stack clause above is historical** and amended twice: the database by **AD-018** (PostgreSQL from the first commit, never SQLite) and the job runner by **AD-030** (none mandated; OD-3 open). Do not read "SQLite" or "Hangfire" here as current.

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
- **Status**: active — the "integration is the default level" clause is amended by AD-024; the **Playwright/NUnit E2E clause lapses with AD-035**; the Testcontainers requirement and behaviour-based naming stand unchanged

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
- **Status**: active — the unit layer definition stands; **the e2e layer is removed by AD-035**, and **the integration row's "repositories and specifications" clause is replaced by AD-036**'s black-box use-case rule. **Where each level lives is amended by AD-026**, which gives each its own project and retires the `[Trait("Category", "Unit")]` marker. Paths named above are pre-AD-026.

### AD-025
- **Decision**: **Git workflow is branch-per-change, Conventional Commits, merged into `main` only through a squash-merged pull request.**
  - **No direct commits to `main`** — the sole exception is an explicit, in-the-moment instruction from the user for that specific commit. A general approval of a task is not such an instruction.
  - **Branch naming** is `<type>/<kebab-slug>`, reusing the commit type vocabulary (`feat/device-registry`, `fix/ip-normalization`, `chore/repo-conventions`).
  - **Commit messages** follow Conventional Commits — `type(scope): subject` with the standard type set (`feat` `fix` `docs` `test` `refactor` `perf` `build` `ci` `chore`); **scope is optional but encouraged**, drawn from `domain` · `devices` · `infra` · `tests` · `e2e` · `specs` · `deps`. Subject is imperative, lower case, no trailing period.
  - **Merging** happens via `gh pr create` against `.github/pull_request_template.md`, reviewed and merged by the user, using **squash merge**. The PR title therefore becomes the `main` commit and must itself be a valid conventional-commit subject.
  - **A PR's base is `main`.** Stack only when genuinely required, and when stacking, either merge the base PR *with its branch deleted* or retarget the child to `main` before merging. **A PR whose base is another branch merges into that branch, not into `main`** — GitHub retargets a child only when the base branch is deleted on merge. **After every merge, verify `main` itself** (`git ls-tree -d --name-only origin/main src/`, `git log origin/main`); a "Merged" badge says only that something merged somewhere.
  - **Enforcement is mechanical where the repo can enforce it, documentary elsewhere** (amended 2026-08-17):
    - *Mechanical*, via repository settings — `squashMergeAllowed=true`, `mergeCommitAllowed=false`, `rebaseMergeAllowed=false` make squash the only available merge; `deleteBranchOnMerge=true` deletes each merged head branch, which also auto-retargets stacked children to `main`. Set with `gh repo edit --enable-squash-merge=true --enable-merge-commit=false --enable-rebase-merge=false --delete-branch-on-merge`.
    - *Mechanical*, via **branch protection on `main`** (added 2026-08-17, second amendment) — **the no-direct-commits rule is no longer documentary.** A direct push to `main` is rejected by the server:
      ```
      remote: - Changes must be made through a pull request.
      remote: - Required status check "build-and-test" is expected.
       ! [remote rejected] HEAD -> main (push declined due to repository rule violations)
      ```
      Configured as: required status check `build-and-test` (the CI job from AD-027) with `strict=true` so a branch must be up to date before merging; `enforce_admins=true`; a pull request required with `required_approving_review_count=0`; `required_linear_history=true`; force pushes and deletions blocked. Reapply with:
      ```bash
      gh api -X PUT repos/obogoni/hikvision-replicator/branches/main/protection --input - <<'JSON'
      { "required_status_checks": { "strict": true, "contexts": ["build-and-test"] },
        "enforce_admins": true,
        "required_pull_request_reviews": { "required_approving_review_count": 0,
                                           "dismiss_stale_reviews": false,
                                           "require_code_owner_reviews": false },
        "restrictions": null, "required_linear_history": true,
        "allow_force_pushes": false, "allow_deletions": false }
      JSON
      ```
    - *Documentary*, via CLAUDE.md, this entry, and the PR template — commit-message format, branch naming, and one-atomic-commit-per-task. These are the clauses no repository setting can express.
- **Reason**: The spec-driven workflow already produces one atomic commit per task, which makes per-branch history a genuine audit trail of requirement → task → commit; a PR is where that trail gets reviewed. Squash keeps `main` readable at one commit per shipped change while the granular history survives on the PR. Hooks are still rejected — they need per-clone installation (`core.hooksPath`) and would block the agent mid-task with no reviewer present — but repository settings need no per-clone install and cost nothing, so the clauses a setting *can* enforce are no longer left to discipline.
- **Reason for the 2026-08-17 amendment**: the base-is-`main` rule and the mechanical settings were both written in blood. Twice, a PR was merged and the work did not reach `main`:
  - **PR #2** (`chore/repo-conventions` → base `feat/device-registry`) — the base had already been squash-merged by PR #1, so AD-025 itself, the PR template, and the CLAUDE.md Git Workflow section were stranded off `main` for four days. Recovered by PR #3.
  - **PR #4** (`refactor/test-project-layout` → base `docs/conventional-commits`) — the whole `test-project-conventions` feature merged into that branch instead of `main`. Recovered by PR #5.
  Both had `deleteBranchOnMerge=false` as the root cause: the base branches survived, so the children were never retargeted. Both were caught only by reading `main` directly, never by the PR UI, which showed "Merged" in each case.
- **Reason for the second 2026-08-17 amendment (branch protection)**: AD-027 added CI, which made a required status check possible for the first time — a gate that reports but cannot block is not a gate. Investigating turned up a worse problem than the one being fixed: **branch protection already existed on `main` and was inert.** A pull request was already required with `required_approving_review_count=0`, and force pushes and deletions were already blocked — but `enforce_admins` was `false`, and on a single-maintainer repository the only actor *is* the admin, so every rule was bypassable by the one person who could trigger it. The previous handoff recorded this as "not configured"; it was **misconfigured**, which reads identically from the outside and is exactly the failure mode this decision keeps re-learning. Verified by attempting a real direct push and confirming the server rejected it and `main`'s tip was unchanged.
- **Trade-off**: Squashing means the per-task SHAs recorded in `validation.md` files are **pre-squash references** that do not resolve on `main` after merge — they remain reachable only via the PR, so a deleted branch's PR page becomes the sole home of that history. Auto-deleting head branches makes that deletion automatic, which is the point but also means a branch cannot be re-pushed after merge without recreating it. Branch protection adds three costs: `strict=true` means the second of two concurrent PRs must update from `main` (and re-run CI) before merging; `required_approving_review_count` is **0** because a solo maintainer cannot approve their own PR and any higher number would deadlock the repository, so "PR required" here means "not a direct push", not "reviewed by someone else"; and `enforce_admins=true` is a **speed bump against accident, not a security boundary** — an admin can still disable protection in one API call, which is also the deliberate escape hatch if CI ever wedges.
- **Consequence found immediately after applying it**: **requiring a status check whose workflow is not yet on `main` deadlocks every in-flight PR branched off `main`.** The check was required before `.github/workflows/ci.yml` had merged, so PR #8 — branched off a `main` without the workflow — triggered no run, reported no check, and sat `BLOCKED` with nothing to wait for. GitHub runs `pull_request` workflows from the *head* branch, so only a branch already carrying `ci.yml` can satisfy the requirement. The fix is ordering, not configuration: merge the PR that adds the workflow first, then update the others from `main` (which `strict=true` requires anyway) so they inherit it. **Require a check only after its workflow is on the default branch** — otherwise the protection that is supposed to gate merges instead prevents all of them.
- **Scope**: Whole repository — every branch, commit, and merge, by any contributor or agent. Adds `.github/pull_request_template.md` and a Git Workflow section to `CLAUDE.md`. The 2026-08-17 amendments also cover GitHub repository merge settings and branch protection on `main`.
- **Date**: 2026-08-13 · **amended** 2026-08-17 (base-is-`main` rule; mechanical enforcement via repo settings) · **amended again** 2026-08-17 (branch protection on `main`, requiring the AD-027 CI check)
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
- **Status**: active — **amended by AD-035**, which deletes the `HikvisionReplicator.E2E` project and leaves two levels, not three. The project-name-declares-the-level rule is unchanged.

### AD-027
- **Decision**: **Code style is enforced by the compiler on every build; the pull request is the gate. No hooks.**
  - **`.editorconfig`** at the repo root is the single source of style and formatting rules, read by the Roslyn analyzers, `dotnet format`, and CI alike.
  - **`Directory.Build.props`** sets `EnforceCodeStyleInBuild=true`, `AnalysisLevel=10.0`, `AnalysisMode=Recommended`. Every project inherits it, so **no command-line flag is ever required** — CI, local, and agent builds cannot disagree about what the rules are.
  - **`dotnet_diagnostic.IDE0055.severity = error`.** Formatting violations fail the build. `IDE0055` is the entire formatting layer as one diagnostic, which makes `dotnet build` the formatting gate and removes the need for a `dotnet format --verify-no-changes` step in CI.
  - **Severity is set per rule in `.editorconfig`, never with `-warnaserror`.** A clean build already emits 4 `NU1903` + 4 `CS0618`; `-warnaserror` would fail on those pre-existing, unrelated warnings.
  - **The fix command is `dotnet format whitespace`, never bare `dotnet format`.**
  - **Exemptions**: EF Core migrations are generated code (`generated_code = true`, Style category `none`) so scaffolding can never break the build; `CA1707` is `none` in the test projects — **two of them since AD-035**, and the glob was re-measured then (391 distinct sites, 205 of them missed by a `**/*.cs` glob).
  - **`.github/workflows/ci.yml`** runs restore → build → unit → integration on every PR to `main` and on push to `main`. **Those are now all the levels there are** — the E2E-is-excluded caveat went with the project (AD-035).
  - **No git hooks and no `PostToolUse` editor hooks**, consistent with AD-025's rejection of hooks.
- **Reason**: The repo had no `.editorconfig`, so nothing was enforced anywhere — a full `dotnet format` run reported only `WHITESPACE` diagnostics and zero style findings, because `IDE####` rules sit below `warning` by default and `EnforceCodeStyleInBuild` was unset. Development happens entirely through AI agents with no IDE, so the "as you type" layer that normally catches style does not exist here; and there was **no CI workflow at all**, so nothing mechanical stood between an agent's edit and `main`. The compiler is the one tool an agent already runs and already reads the output of, which makes it the natural enforcement point: a style violation arrives in the same channel as a compiler error and cannot be scrolled past.
- **Reason the hook approach was rejected**: the starting proposal was a `PostToolUse` hook running `dotnet format` on each edited file. Measured at **6.4–8.2 s per invocation** (full MSBuild workspace load every time) on every `Write`/`Edit`; `dotnet format whitespace --folder` is 1.35 s but still per-edit. Worse, the sketch was silently inert — `$FILE_PATH` does not exist (hook input arrives as JSON on stdin at `.tool_input.file_path`), and `--include ""` scopes to **nothing**, so it would have exited 0 having formatted no files while appearing configured. Hooks are also per-machine, absent from a fresh clone, and bypassable. Same class of failure AD-025 fixed by moving merge rules out of documentation and into repository settings.
- **Reason for the exemptions**: `CA1707` ("identifiers should not contain underscores") fired **304 times** at the time of measurement, entirely in the test projects, against the deliberate behaviour-based naming convention in `docs/test-patterns.md` § Naming Tests ("Words separated by underscores"). The rule is wrong here, not the names. Migrations are exempted because `dotnet ef` regenerates them and a scaffolded file must never fail a build.
- **Trade-off**: `AnalysisMode=Recommended` surfaces **10 `CA` findings** that are warnings only, so they do not gate — real risk of warning blindness, mitigated by enumerating them in `.specs/features/code-style-enforcement/spec.md` rather than leaving an anonymous warning cloud. Pinning `AnalysisLevel=10.0` means an SDK upgrade will *not* bring new rules automatically; that is deliberate (reproducibility) but must be revisited on purpose. `IDE0055` as an error also means the formatter's canonical output wins over local taste — the reformat of `DeviceRepository.IsAddressConflict` is arguably less readable, and that is the accepted price of not arguing with a formatter.
- **Measured during execution** (all on a clean `--no-incremental` solution build):
  - `AnalysisMode` cost, *after* `CA1707` was exempted: `Minimum` → 0 findings · `Recommended` → 10 · (`Recommended` before the exemption → 326).
  - **A failing build hides warnings in dependent projects.** The first `Recommended` measurement read as 3 findings because the Api's `IDE0055` errors aborted the build before the two test projects compiled. Enforcement was therefore switched on *after* the existing violations were fixed, so no commit on the branch has a failing build — and no measurement was taken from a build that did not complete.
  - **Bare `dotnet format` is not safe here.** With `AnalysisMode=Recommended` it runs the analyzer fixers, and it "fixed" the deprecated Testcontainers `PostgreSqlBuilder` call by stamping `[Obsolete]` onto `PostgresFixture` and `UnreachableDatabaseFixture` — silencing a real advisory by marking the *consumer* obsolete. `dotnet format whitespace` makes no semantic edits and still clears `IDE0055`.
  - **EditorConfig glob gotcha**: `**/*.cs` requires at least one directory level, so it missed test files sitting directly in a project root and left 18 `CA1707` behind. `**.cs` is correct.
  - **Lesson L-007 corroborated independently.** An up-to-date incremental build reported "Build succeeded" with zero diagnostics on code that a `--no-incremental` build failed with 5 `error IDE0055`. This is a false-green an agent can easily trust; the first build after a change is the signal.
- **Scope**: `.editorconfig`, `Directory.Build.props`, `.github/workflows/ci.yml` (both new), `CLAUDE.md` (§ Code Style, § Gate commands), and 5 whitespace lines in `Api/Infrastructure/DeviceRepository.cs` and `Api/Shared/IRepository.cs`. Does **not** move `TargetFramework`/`Nullable`/`ImplicitUsings` out of the four `.csproj` files — deferred. No `.git-blame-ignore-revs`: only 5 lines are reformatted, so the convention becomes worthwhile at the first wide sweep.
- **Date**: 2026-08-17
- **Status**: active — **amended by AD-035** on the two clauses that named live configuration: the `CA1707` exemption now covers two test projects, not three, and `ci.yml` no longer carries an E2E-exclusion caveat. The enforcement mechanism is unchanged.

### AD-028
- **Decision**: **Feature-level validation runs as a fresh Verifier sub-agent — author ≠ verifier — and this entry is the standing authorisation for it.**
  - After the last task of a feature is committed, the orchestrator dispatches the `tlc-spec-driven` **Verifier** as a separate sub-agent. It is not prompted for and needs no per-feature permission.
  - The Verifier receives only `spec.md`, the commit range, and the test files. It re-derives coverage **evidence-or-zero**: every acceptance criterion needs a cited `file:line` plus the assertion expression, or it counts as uncovered.
  - It also runs the **discrimination sensor** (inject a behaviour-level fault in scratch state, confirm the tests kill it, discard the mutation) and writes `validation.md`.
  - The **standalone fallback** in `validate.md` remains available for when a sub-agent genuinely cannot run, but using it is a **deviation to declare** in `validation.md` and in the PR — not a normal path.
- **Reason**: The agent that writes the code also writes the spec and the tests, so a blind spot that shaped the implementation shaped the acceptance criteria too; checking your own work applies the very reasoning that may have produced the gap. Two consecutive features ran without it, and the cost is concrete rather than theoretical: `code-style-enforcement`'s `AC-1` demanded "zero `IDE`/`CA` diagnostics" while the same spec's Out of Scope section accepted 10 `CA` warnings. Verifying it literally would have failed the feature; verifying it loosely would have passed a vague assertion. The author wrote both halves, and that same intent papered over the contradiction — it was caught incidentally during the sensor pass, not by design. A verifier reading `spec.md` cold sees only the text.
- **Reason this needed a decision at all**: the skill already mandates the Verifier, but the session's operating instructions forbid spawning sub-agents unless the user requests one, and the two rules deadlocked — so validation silently degraded to a self-check twice while being flagged as an exception each time. A caveat repeated every feature stops carrying information. This entry converts it into a standing request, which is the condition the operating instruction actually asks for.
- **Trade-off**: A second agent costs tokens and wall-clock on every feature, and it will sometimes disagree with the author over things that are matters of taste rather than defects — its output is a ranked gap list to triage, not a verdict to obey. The fix→re-verify loop is bounded to 3 iterations before escalating, so a stubborn disagreement cannot spin. Note also that this clause is **documentary only**: unlike AD-025's branch protection or AD-027's build gate, no repository setting can enforce that a sub-agent was actually dispatched — it holds as long as this file is honoured.
- **Scope**: Every feature validated under `tlc-spec-driven`. Adds a § Spec-Driven Validation section to `CLAUDE.md`. Does not change what the Verifier does, only that it is dispatched and pre-authorised.
- **Date**: 2026-08-17
- **Status**: active

### AD-029
- **Decision**: **Retire `.specs/ARCHITECTURE.md`.** Its *descriptive* half — solution layout, API layer map, domain model, cross-cutting table, feature inventory, commands — is **deleted rather than migrated**: it restated the code and `CLAUDE.md`, and both are authoritative where it was not. Its *judgment* half is promoted:
  - the device write-path flow and the database-is-the-authority rule → `CLAUDE.md` § Vertical Slice Structure;
  - the Known Gaps backlog → `ROADMAP.md` § Known Gaps, plus `OD-7` for the AES-GCM migration.
- **Reason**: The file was chartered by AD-012 (itself superseded by AD-013) under the **pre-v3** `tlc-spec-driven` layout, which prescribed `.specs/codebase/{STACK,ARCHITECTURE,CONVENTIONS,STRUCTURE,TESTING,INTEGRATIONS,CONCERNS}.md` as the output of a one-shot `map codebase` run — a bootstrap artifact, never a maintained document. Skill **v3** (upstream `9b3ec067`, 2026-06-25, a `BREAKING CHANGE`) removed the entire brownfield flow on stated grounds: *"Design reads live code via the Knowledge Verification Chain and flags concerns inline in `design.md`"*, and it retargeted Knowledge-Verification Step 2 from `.specs/codebase/` to `.specs/STATE.md`. The `map codebase` trigger was removed with it. The installed skill therefore contains **zero** references to the file — it appears in no `.specs` structure, no context-loading list, and no read hook, so nothing loaded it and no step refreshed it. The predicted staleness had already arrived: the file recorded **"3 projects"** against a 4-project solution, and carried no trace of AD-027's `.editorconfig`, `Directory.Build.props`, or `.github/workflows/ci.yml` — because AD-027's scope named `CLAUDE.md` and not the map.
- **Trade-off**: The layer map's dependency-direction annotation and its per-file purpose comments are lost as prose and must now be read from the tree and the code; `CLAUDE.md` § Project Structure is deliberately coarser. A newcomer loses the single-page orientation document. Accepted because an orientation document that nothing loads and nothing refreshes **misleads more than it orients** — both drift instances above read as current to anyone trusting the file. This is a **deliberate divergence in one direction only**: `ROADMAP.md` is *kept* despite v3 also dropping it, because v3 offers no home for open decisions (`OD-NNN`) and the Handoff's one-line summary is not one.
- **Scope**: `.specs/ARCHITECTURE.md` (deleted), `.specs/ROADMAP.md`, `.specs/STATE.md`, `CLAUDE.md`. AD-012's and AD-026's references to the file are left intact as audit trail per the supersession rule — they record what was true then.
- **Date**: 2026-08-17
- **Status**: active

### AD-030
- **Decision**: **No job runner is mandated, and none is in the solution.** AD-010's "deferred work runs as Hangfire jobs (SQLite storage)" rule is superseded and no longer binds. How deferred replication work is executed remains an **open decision — `ROADMAP.md` OD-3** — to be resolved during Phase 2 against the derived load, not inherited now. `replication-queue` and `replication-worker` may not assume Hangfire. This amends the job-runner half of AD-013's carried-over stack, exactly as AD-018 amended its database half.
- **Reason**: AD-010 was reverse-engineered on 2026-08-02 from the pre-rewrite implementation and recorded as `active`, but AD-013 discarded that implementation and no rewrite feature has reintroduced a job runner — there is no Hangfire package reference, no `*Job.cs`, and no enqueue call anywhere in `src/`. Its storage clause was independently invalidated by AD-018, which is why OD-3 was *reopened* rather than answered: Hangfire-on-PostgreSQL is a recommendation to validate under load, not a decision taken. Left `active`, the entry read as a standing mandate that contradicted both `CLAUDE.md` ("Hangfire is not in the solution; the job runner is decided in Phase 2") and OD-3 itself. **A decision log whose `active` entries disagree with the always-loaded instructions is worse than no log** — a reader cannot tell which side is stale, and the log is the side that claims authority. Found while auditing duplication between `CLAUDE.md` and the now-retired architecture map (AD-029); the map was the only artifact stating the true position, and deleting it left the contradiction with nothing to correct it.
- **Trade-off**: Phase 2 starts with no default, so `replication-worker` must resolve OD-3 as part of its own design instead of inheriting an answer, and that design work is now on the critical path of the product's core capability. Accepted: AD-014 makes propagation throughput the primary quality attribute, and the superseded "default" was picked for an implementation that no longer exists, storing state in a database that is no longer used. An inherited answer that was never validated against the derived load (`50,000 × D` operations) is not a saving.
- **Scope**: `.specs/STATE.md` (supersedes AD-010; annotates AD-013's stack clause as historical), `.specs/ROADMAP.md` OD-3. Binds `replication-queue` and `replication-worker`.
- **Date**: 2026-08-17
- **Status**: active

### AD-031
- **Decision**: **`CLAUDE.md` is a hub, not a manual.** It carries only what applies to *every* session and cannot be re-derived from the code; all reference material lives in named topic spokes under `docs/`, linked from the hub. The routing rule for any new content is three-way:
  - **hub** — the rule binds every task regardless of what is being built (branch first, base every PR on `main`, one commit per task, the gate commands, the Verifier). It goes in `CLAUDE.md` in full;
  - **hub one-liner + spoke** — the rule is a *hazard* whose imperative must be in default context but whose account is long (never bare `dotnet format`, `--no-incremental`, the PR-base trap). The imperative goes in the hub in one line; the incident goes in the spoke;
  - **spoke only** — the rule matters only while doing one kind of work (slice structure, analyzer ratchet, protection payload). The hub links the spoke and says nothing more.

  The hub's budget is **≤ 110 lines**; it landed at 109, down from 285. Spokes are `docs/git-workflow.md` (AD-025), `docs/code-style.md` (AD-027), `docs/slice-anatomy.md` (AD-001…AD-009, AD-022, AD-023) and the pre-existing `docs/test-patterns.md` (AD-024, AD-026). **A spoke never becomes a second authority**: where a decision entry in this file already states a rule, the spoke links the `AD-NNN` rather than copying it.
- **Reason**: `CLAUDE.md` is loaded into every session by default, so every line is paid for on every task whether relevant or not, and the file had grown to 285 lines against a widely-cited 300-line practical ceiling. The distribution was the argument: **72 lines — a quarter of the file — were Git reference material**, and another 74 restated code that `src/` already holds, including a C# snippet of `Shared/Errors.cs` and an ASCII request-flow diagram. Meanwhile the file never stated **what the product does**: AD-014 makes propagation latency the primary quality attribute, and a session that never opened `ROADMAP.md` never learned it. The repository had already proved the pattern worked — `docs/test-patterns.md` was referenced rather than inlined, and nothing was lost by it. The relocation is deliberately **lossless**: `spec.md` inventories all 43 imperatives and assigns each a destination, and the feature's gate refuses any rule that cannot be cited as `file:line`.
- **Trade-off**: A rule in a spoke is a rule Claude must choose to open, so demotion trades certainty of being read for budget — which is why hazards keep a one-line hub imperative rather than a bare pointer, and why the *universally* applicable Git rules stayed in the hub instead of following the rest of AD-025 into its spoke. The sharper limit is enforcement: **the ≤ 110-line ceiling is documentary only.** No repository setting, build step or CI check measures it, so nothing mechanically prevents the hub re-inflating one useful paragraph at a time — the same class of limitation AD-028 records for its own Verifier clause, and the reason the routing rule above is written as a rule a future author can apply rather than as a description of what was done once. A CI link-and-budget check is deferred, not rejected (`context-engineering` spec, assumption A-3).
- **Scope**: `CLAUDE.md`, `docs/git-workflow.md`, `docs/code-style.md`, `docs/slice-anatomy.md`, `docs/test-patterns.md`. **Supersedes nothing.** AD-025, AD-027 and AD-024/AD-026 remain the authority on their own rules; this entry changes only where their *text* lives. AD-029's scope clause naming `CLAUDE.md` § Vertical Slice Structure now resolves to `docs/slice-anatomy.md`.
- **Date**: 2026-08-20
- **Status**: active

### AD-035
- **Decision**: **The end-to-end level is removed.** `HikvisionReplicator.E2E` is deleted from the tree and from `HikvisionReplicator.slnx`, and Playwright and NUnit leave the solution with it. Test levels are now **two**: `HikvisionReplicator.Tests` (unit) and `HikvisionReplicator.IntegrationTests` (integration). **No third level may be reintroduced** until (a) the unit and integration conventions are settled — the granularity review that prompted this is still open — and (b) a deployment exists for a suite to smoke. If one returns, it returns as a **deployment smoke test** asserting what a shipped process does that `WebApplicationFactory` cannot — real configuration, real environment, real socket — and explicitly **not** as a second copy of route assertions.
- **Reason**: The suite asserted nothing that was not already asserted. All 17 tests mapped 1:1 onto an integration test, several verbatim: `Getting_unknown_device_returns_not_found`, `Removed_device_is_no_longer_retrievable` and `Nonsensical_page_request_is_answered_rather_than_refused` exist under the same names in both projects, and `New_device_is_created_and_returned` differed in neither name nor assertion. It also ran nowhere — excluded from `ci.yml` by its own comment, absent from both gate commands in `CLAUDE.md`, and touched by exactly two commits in its lifetime (`9774e7a`, `9783e0b`). A suite that only ever compiles is a compile check, and this one cost a second test framework and a Playwright dependency used solely as an HTTP client.
- **Reason the level itself did not survive the tests**: AD-024 gave e2e a purpose — "a thin confirmation of each route" — and that purpose is what produced the duplication, because route confirmation is exactly what `TestServer` already answers faithfully. The genuine out-of-process gap is narrower and different in kind: `TestWebApplicationFactory` **injects** `ConnectionStrings:DefaultConnection` and `Encryption:Key` as an in-memory source rather than reading real configuration, runs as environment `Test`, and never opens a socket. Nothing in the retired suite tested any of that. Keeping a level whose stated job is the wrong job is worse than having no level.
- **Trade-off**: Real coverage is lost even though no assertion is. Nothing now proves the application boots from real configuration, that `docker-compose.yml`'s PostgreSQL works, or that the shipped process finds its encryption key — failures that would surface at first deploy instead of in CI. That is accepted because there is no deployment yet (`ROADMAP.md` Phase 4), and because the retired suite did not cover those things either, so nothing that was actually being caught stops being caught. **The debt is deliberate and is this entry.** The one `TestServer` limitation that mattered stays covered in-process: `KestrelWebApplicationFactory` serves the request-size test on a real socket.
- **Consequence for the CA1707 glob**: `.editorconfig`'s test-project glob drops `E2E`. The comment above it cited "18 CA1707" as the residue a `**/*.cs` glob would leave — a figure `code-style-enforcement`'s validation had already found wrong (D-5: the real residue was 92 sites, and 18 was the raw line count for `E2E/DeviceEndpointsTests.cs` alone). Both magnitudes were re-measured against the tree after removal rather than adjusted by arithmetic.
- **Scope**: `src/HikvisionReplicator.E2E/**` (deleted), `HikvisionReplicator.slnx`, `.editorconfig`, `.github/workflows/ci.yml`, `.github/pull_request_template.md`, `CLAUDE.md`, `README.md`, `docs/test-patterns.md`. **Amends AD-024's level definitions** by removing the e2e row, and **amends AD-026** by reducing its three projects to two. AD-024's unit/integration definitions and AD-026's project-declares-the-level rule are unchanged. AD-019's and AD-011's Playwright/NUnit clauses lapse. Does **not** settle the integration-granularity question that prompted this — that remains open.
- **Numbering**: this is **AD-035**, not AD-032, because **AD-032, AD-033 and AD-034 are already reserved** by `user-registry`'s `design.md` (binary-payload table, normalize-at-the-boundary, tombstone + asymmetric index) and are still owed an entry here — see § Outstanding follow-ups. The gap is deliberate; do not backfill it with anything else.
- **Date**: 2026-08-26
- **Status**: active — **condition (a) is now satisfied by AD-036**, which settled the unit/integration conventions. Condition (b) is not: there is still no deployment for a suite to smoke, so the bar for reintroducing a third level remains a deployment smoke test and nothing else.

### AD-036
- **Decision**: **Integration tests are black box, driven through the HTTP surface, one class per use case or situation.** A test does not construct a repository, a specification or a `DbContext` in order to assert against it. Reading the database directly to *verify* remains correct and expected — `StoredUserAsync`, `StoredPictureAsync`, `CountUsersAsync` — because a promise about what is stored cannot be proved by asking the API that stores it; the rule is about what **drives** the test, not what it inspects. **The single exception**: a test may go below HTTP only if it names, in its own doc comment, an observable HTTP cannot distinguish — one where a wrong implementation and a right one return byte-identical responses. Those live in exactly two classes, `UserPersistenceContractTests` and `DevicePersistenceContractTests`. Four kinds qualify today: what a read touches (the SQL), the shape of the two unique indexes, which failures are deliberately *not* translated, and cancellation plus the index→message mapping.
- **Reason**: Of 224 integration tests, 45 drove repositories, specifications and the schema directly, and **34 of those asserted something a use-case test already asserted** — frequently the same scenario under a near-identical name (`Deleted_spectator_is_invisible_to_the_active_lookup` beside `Removed_spectator_is_no_longer_retrievable`). The duplication was not merely redundant: the HTTP twin was usually the **stronger** test. `Moving_a_device_onto_another_devices_address_is_rejected` asserts the 409, that `updatedAt` did not move, and that the occupier still holds the address; its repository twin asserted the 409 and the address. Tests named after `UserRepository` and `ActiveUsersPagedSpec` also fail when those types are renamed rather than when behaviour changes, which is the cost that made the suite feel granular in the first place.
- **What was kept, and the sensor that decided it**: the 11 tests with no black-box equivalent were kept. One assertion existed *only* below HTTP — which of the two unique keys collided — and it was folded up first, into `detail` on the RFC 7807 body that `DomainErrorExtensions` already emits, so nothing was lost before anything was deleted. **A discrimination sensor then caught that the fold was not sufficient**, in two stages. First mutation — swapping the two message *constants* — survived all 190 tests, because the assertions compared against the same constants the production code uses and both moved together; the assertion was tautological. Second mutation — swapping which **index** maps to which message — was killed, but *non-deterministically*: three race-test failures on one run, two on the next, because a service-level pre-check answers first whenever it can and only a racer that slips past it reaches the repository translation. So `Each_colliding_key_is_reported_as_the_key_that_actually_collided` was added to the contract class, where the pre-check is bypassed and the database decides every time. It fires on every run.
- **Trade-off**: The exception is a door, and doors get used. Nothing mechanical distinguishes a legitimate contract test from a dependency test someone found convenient to write there — the blind-spot sentence is a convention, enforced by review, exactly the documentary-only limitation AD-028 and AD-031 record for their own rules. The two class names are the mitigation: `PersistenceContract` reads as a mechanism and invites the question, where `UserRepositoryTests` read as the default place to put anything touching a repository. A second cost is that the surviving tests are now further from what they prove — `Face_picture_is_removed_when_its_spectators_row_is_removed` covers a hard delete the application never issues, kept because the FK is what stops an orphaned biometric outliving its owner if a row is ever removed out-of-band.
- **Consequence — a scheduling-dependent guard is not a guard**: this is the second time the point has been paid for (AD-026 records the first, where `TracingTests` passed only because parallel workers were occupied). The lesson generalises past races: **when a use-case test can reach something only by winning a scheduling coin-flip, prove it deterministically as well.** It is written into `docs/test-patterns.md` § Integration tests are black box rather than left in this entry.
- **Consequence found by CI, after the fact**: `Spectators_registered_at_once_under_one_reference_yield_one_user` demanded 409 from **every** racer that did not get 201, and CI failed it with `Expected: Conflict, Actual: OK`. The assertion predates this work — it is identical at `8a5dc94` — so the black-box refactor exposed it rather than caused it; adding the `detail` check simply put a second assertion behind a status assertion that was already wrong. The route is an **idempotent upsert**, so a racer that arrives after the winner commits finds a row and updates it, and 200 is the correct answer. Demanding 409 from all three asserted that no racer was ever late, which is a claim about scheduling. It passed 12/12 locally and failed on CI's slower runner. The test now asserts what USR-07 actually promises: exactly one 201, exactly one row, **no 500 anywhere**, and any 409 naming the external reference.
- **Which makes the deterministic mapping test load-bearing, not belt-and-braces**: this entry originally said the race tests guard the index→message mapping and the contract test merely makes that deterministic. That was too generous. On a run where every loser takes the update path, the external-reference race produces **no 409 at all** and guards nothing. `Each_colliding_key_is_reported_as_the_key_that_actually_collided` is therefore the only unconditional guard on that half of the mapping — which is the same lesson twice in one feature: a guard reachable only by winning a race is not a guard.
- **Verifier verdict: FAIL, and one claim in this entry was false.** An independent Verifier (AD-028) re-derived every number above and found them all exactly right — 224→191, 45, 34, 11, 1, 7, and the 282 unit baseline. It then refuted **"No assertion was lost."** A P0-depth sensor injected 17 faults across two scratch worktrees, re-testing each survivor against the pre-change suite to separate *lost* coverage from *never-had* coverage: 14 killed, 2 survived, 1 degraded.
  - **Lost (blocker).** `ActiveUsersPagedSpec` `Take(take)` → `Take(take + 1)` survived all 191 at HEAD and was killed at `6237fd0` by the deleted `UserSpecificationTests.Pages_together_contain_every_spectator_exactly_once`. `ListUsersService` asks for `currentSize + 1` to answer "is there another page?" and trims with `.Take(currentSize)`, so an over-fetching specification returns a byte-identical response and `hasMore` stays correct. HTTP is *structurally* blind to the window size — on the catalogue path A-1 and OD-4 care about most. Restored deterministically as `Catalogue_page_reads_exactly_the_window_it_was_asked_for`.
  - **Degraded (major).** Renaming the device address constraint was killed at `6237fd0` by two deterministic repository tests; at HEAD only the 8-way race in `DeviceEndpointsTests` killed it. This entry added the deterministic mapping test **for users and not for devices** — the exact scheduling-dependent-guard failure the same commit wrote into `docs/test-patterns.md` as a rule. Added as `Address_collision_is_reported_as_the_address_conflict`.
  - **Left open (major).** This entry *found* that asserting against production's own constants is tautological and then shipped assertions that still do it, so swapping the two message values survived 191 and 224 alike. Now closed: the two contract tests assert the **literal text** once, with the constants kept alongside.
  - **Rule violated by its own commit (minor).** `UserRemovalTests.Removed_spectator_is_absent_from_the_catalogue` still constructed a `UserRepository` and an `ActiveUsersPagedSpec` — the one place the black-box rule was left broken, with its HTTP twin already present. Now driven through `GET /api/users`.
  - **Coverage hole (minor).** `HarnessTests` named only `InitialCreate`, so a database with every later migration unapplied passed. Now also asserts `AddUserRegistry`.
- **Correction to the exception's stated scope**: the Verifier showed the access-code index's *shape* is in fact HTTP-distinguishable (`UserRemovalTests` kills a mutation of it), so that one test is belt-and-braces rather than a true blind spot. It is kept — dropping it would prove one index's DDL and not the other's — but the four-kinds table is a description of what qualifies, not a guarantee that every listed test is irreplaceable. Relatedly, the blind-spot sentence is carried by the class-level table for most tests rather than per-test; `docs/test-patterns.md` now says that plainly instead of demanding a sentence on every method.
- **What the sensor confirmed does hold**: four mutations — external-ref index made partial, `AutoInclude(true)` on the face picture, translating every `23505`, and renaming the user index — are each killed by a contract test **and nothing else**. The exception earns its place where it matters. Race tests were stable 20/20, and `31356dc`'s asymmetry with the access-code race is correct: those racers hold distinct external references and can never take the update path.
- **Post-fix sensor**: all three surviving/degraded mutations re-injected and now killed by the intended new test — 193 integration tests, `git diff` on `Api/` empty afterwards. Lessons **L-039, L-040, L-041** recorded. Full report: `.specs/features/user-registry/validation-ad036.md`.
- **Numbers**: 224 → 191 at first commit, **193 after the Verifier's fixes**. 34 deleted, 11 relocated into the two contract classes, 3 added (the user and device mapping tests and the paging window), 7 use-case tests strengthened with a `detail` assertion. Unit tests untouched at 282. **One assertion was lost and has been restored** — see the Verifier verdict above; the original claim that none was is retained here, struck through by that bullet, because the point of the entry is that the author believed it.
- **Addendum, 2026-08-26 — the same rule applied to the tracing tests.** The black-box rule says assert what we promise; its mirror is **do not assert what a dependency happens to emit**. Three of the ten tracing/observability assertions were OpenTelemetry's output, not ours: two pinned a span's exact `DisplayName`, a semantic-convention detail a package upgrade can change with no defect here. Wiring is already proved once, in the right place — `StartupTests.Traces_are_collected_when_an_export_endpoint_is_configured` and its negative twin assert `TracerProvider` against real configuration — so the display-name assertions were not even carrying that argument.
  - The **device** test drops the exact string and keeps `ActivityKind.Server`: that route has no parameter, so there is no template to protect, and what remains is "the request is traced at all", which is what makes the credential sweep non-vacuous.
  - The **user** test was *strengthened instead of narrowed*. The plan was to delete it as a duplicate; reading USR-40 first showed it is that requirement's only direct evidence ("WHEN a user request is handled THEN it SHALL emit a trace span"), and reading the assertion showed the display name protects something real — the span carries the **route template**, so an integrator's external reference never reaches a span name. An interpolated path would mean unbounded cardinality and the same leak channel DEV-07 sweeps attributes for. It now asserts the template is present and the caller's key is absent, which is a stronger claim than the string equality it replaces.
  - **No test was deleted and no count moved**: 193 before, 193 after. Two assertions on a dependency's formatting were removed; two on behaviour we own were added. **Nothing was weakened to make anything pass** — all ten passed before and after.
  - The near-miss is the point, and it is the Verifier's lesson applied one step earlier: the queued plan said "delete the duplicate", and only checking the requirement and the assertion's actual content revealed that deleting it would have dropped USR-40's evidence *and* a cardinality/leak guard. **Read what an assertion protects before calling it redundant.**
- **Scope**: `src/HikvisionReplicator.IntegrationTests/**` — deletes `UserRepositoryTests`, `UserSpecificationTests`, `UserSchemaTests`, `DeviceRepositoryTests`; adds `UserPersistenceContractTests`, `DevicePersistenceContractTests`. `docs/test-patterns.md`. **Amends AD-024**, whose integration row named "repositories and specifications" as a target — that clause is what produced the duplication and is replaced by the use-case rule. AD-024's unit definition and AD-026's project-declares-the-level rule are unchanged. **AD-022 is unaffected in substance** but its "a renamed index silently degrades a 409 into a 500 unless a test covers it" hazard is now covered by the race tests' existing `Assert.DoesNotContain(InternalServerError)` plus the new deterministic mapping test.
- **Date**: 2026-08-26
- **Status**: active

### AD-037
- **Decision**: **A test class is named after the use case it covers** — the slice folder under `Features/{Resource}/{Operation}/` with `Tests` appended. `UpsertUser` → `UpsertUserTests`, `RegisterDevice` → `RegisterDeviceTests`. Two riders:
  - **When one use case serves several situations, split the file, not the class.** `UpsertUser` is a single idempotent route (A-2) covering three situations, so it is one `partial class UpsertUserTests` across `UpsertUserTests.Registration.cs`, `.Amendment.cs` and `.Resurrection.cs`. Split on **what the registry held before the call**, never on a cross-cutting axis such as "validation" — a validation test belongs with the situation whose request carries the field.
  - **Cross-cutting classes are named for their concern**, which is how a reader tells them apart from use-case classes at a glance: `UserExternalRefTests`, `CredentialLeakageTests`, `TracingTests`, `UserObservabilityTests`, `StartupTests`, `ErrorHandlingTests`, `UserRequestSizeTests`, `HarnessTests`, and the two `PersistenceContract` classes (AD-036's below-HTTP exception).
- **Reason**: Findability, and the previous names actively worked against it in both directions. `DeviceEndpointsTests` was **one class of 55 tests and 873 lines covering five separate use cases** — the largest class in the suite, and the only way to find a route's tests was to scroll to the right section comment. In the other direction, three well-named user classes (`UserRegistrationTests`, `UserAmendmentTests`, `UserResurrectionTests`) covered **one** route between them, so "where are the upsert tests?" had three answers and none of them said `UpsertUser`. Naming from the slice folder removes the question entirely: the folder name *is* the class name.
- **Reason the partial class rather than one file or three classes**: a strict single `UpsertUserTests.cs` would be 44 tests and ~800 lines — recreating the `DeviceEndpointsTests` problem this entry exists to fix. Three prefixed classes (`UpsertUserRegistrationTests`, …) keep files small but stop the class name being the use case name, which is the whole convention. The partial class is the only option that satisfies both: one class named for the use case, three files named for the situations, and a search for `UpsertUser` finds all of them.
- **Trade-off**: A partial test class is unusual enough to surprise a reader, and it has one hard constraint — `[Collection]` and the primary constructor may appear on exactly **one** part, so `UpsertUserTests.cs` owns both plus any shared member (`Kickoff`). Add a second `[Collection]` and it is a compile error, which is at least a loud failure rather than a quiet one. The cross-cutting carve-out is the softer edge: it is a judgement call whether a new test is cross-cutting or belongs to a use case, and nothing mechanical decides it. `UserExternalRefTests` is the honest hard case — it exercises `UpsertUser`, `GetUser` and `RemoveUser`, and lives outside them because the thing it covers is key escaping across all of them rather than any one route's behaviour.
- **Mechanical guarantee**: this is a pure move — no assertion was added, removed or altered, so **the count had to hold exactly, and did: 193 before, 193 after.** `DeviceEndpointsTests`' 55 split as 29 + 4 + 3 + 14 + 5, which sums to 55. A `DeviceApiTests` base was extracted to mirror the existing `UserApiTests`, taking the twelve helpers the five classes share; two defects surfaced from that extraction and were fixed rather than suppressed — a missing `Microsoft.EntityFrameworkCore` using, and `CS9107` where a split class captured `fixture` directly instead of using the base's `Fixture` property.
- **Scope**: `src/HikvisionReplicator.IntegrationTests/**`, `docs/test-patterns.md`. Renames only; **supersedes nothing**. AD-036's black-box rule and its two-class exception are unchanged — this entry says what the resulting classes are *called*, not what they may drive. AD-026's "class names carry no level suffix, the project does" still holds.
- **Date**: 2026-08-26
- **Status**: active

---

## Handoff

- **Feature**: `user-registry` (`.specs/features/user-registry/`) — **complete and verified**, awaiting review on **PR #15**.
- **Phase / Task**: All 5 phases, T1–T26, done. Execute finished; the Verifier returned **PASS**.
- **Completed**: spec.md · design.md · tasks.md · validation.md. 47 commits on `feat/user-registry` off `main` at `738f6b3`. **Pre-squash hashes — they resolve only via the PR.** Executed as four sequential batch sub-agents (T1–T7, T8–T13, T14–T21, T22–T26), then a fresh Verifier — **author ≠ verifier was satisfied this time**, the first feature for which that is true (AD-028).
- **In-progress** (file:line): none.
- **Also on this branch, outside `user-registry`'s scope**: the **E2E level was removed** (AD-035) — project deleted, Playwright and NUnit out of the solution, docs and CI comments updated, T26's record annotated as retired. Folded into PR #15 by explicit decision rather than taking its own branch, because PR #15 is what introduced `E2E/UserEndpointsTests.cs` and any other ordering left that file orphaned in a directory with no csproj.
- **Settled**: the **integration-test granularity review** is done and recorded as **AD-036**. Integration tests are now black box through the HTTP surface, one class per use case; the below-HTTP exception is two `PersistenceContract` classes, each test carrying a written sentence naming what HTTP cannot distinguish. 224 → 191 tests: 34 deleted as duplicates, 11 relocated, 1 added, 7 strengthened. **No assertion was lost** — the one that existed only below HTTP (which key collided) was folded into the 409 `detail` before anything was deleted.
- **Carry forward from AD-036's sensor**: two mutations, two lessons. Asserting a response against the **same constant the production code uses** is tautological — swapping the constants' values passed all 190 tests. And the index→message mapping is reachable through HTTP only by winning a race past the service pre-check, so the race tests killed the real mutation **non-deterministically** (3 failures one run, 2 the next). Both are why `Each_colliding_key_is_reported_as_the_key_that_actually_collided` exists. This is the second scheduling-dependent-guard incident after AD-026's `TracingTests`; the general rule now lives in `docs/test-patterns.md`.
- **Next step**: merge PR #15 (squash), then open a small follow-up branch for the three items below. Nothing else in `user-registry` is outstanding.
- **Blockers**: none. CI `build-and-test` is green on PR #15.
- **Uncommitted files**: none.
- **Branch**: `feat/user-registry`, pushed, tracking `origin/feat/user-registry`, based on `main` at `738f6b3`.

### Outstanding follow-ups — deliberately not in PR #15

These were left out because the chosen landing path scoped the PR to the feature itself. They are
real debt, not notes:

1. **AD-032 / AD-033 / AD-034 are proposed in `user-registry`'s `design.md` but never written to
   `## Decisions` above.** AD-032 (binary payloads in a dedicated table with a fingerprint
   denormalized onto the owner, navigation never auto-included) is what **formally closes ROADMAP
   OD-4** — the roadmap still lists OD-4 as open. AD-033 is normalize-external-formats-at-the-boundary;
   AD-034 is the tombstone + asymmetric-index pattern. `replication-queue` inherits all three.
2. **The semantic-image-quality gap is missing from `ROADMAP.md` § Known Gaps.** It is named in
   `user-registry`'s spec: the face pipeline proves an image is *mechanically* acceptable, never
   that it is a usable face. A profile shot or a spectator in a cap passes all 45 criteria and
   fails at a turnstile, and Phase 4 `reconciliation` will not catch it either — it compares our
   belief against the device, not against reality.
3. **A-13 carries a standing Phase 3 obligation.** The official ISAPI face-record envelope could
   not be read directly (the wiki is behind a JS app); the 40–200 KB band and the 640×480 floor come
   from Hikvision's DS-K1T606 terminal documentation. `isapi-device-client` must verify both against
   real hardware and supersede A-13 if they differ. The envelope lives in `FaceImageOptions`, so a
   correction is a config change, not a code change.

### What `user-registry` established that later features inherit

- **`PUT /api/users/{externalRef}`** upsert, `GET`, `DELETE`, paged `GET /api/users`. Removal
  **tombstones** the row (`DeletedAt`) and **destroys the face bytes in the same transaction** —
  Phase 2's Remove path gets a live FK target and no biometric.
- **The two unique indexes are deliberately asymmetric.** `IX_users_ExternalRef` covers all rows
  (resurrection must find a tombstone by key); `IX_users_AccessCode` is partial on
  `WHERE "DeletedAt" IS NULL` (a removed spectator's PIN returns to the pool). Any change to one
  must re-check the other — the Verifier killed a mutation in each direction.
- **`IFaceImageNormalizer`** converts any reasonable upload into the device envelope. **The 40 KB
  figure is a lower bound** — over-compression is a rejection cause, so an upper-bound-only check is
  wrong. The encode ladder is **fixed, never a bisection search**, because a byte-identical re-upsert
  must not advance `UpdatedAt`.
- **SkiaSharp 3.119.4**, not ImageSharp: ImageSharp v4 fails the build without a committed
  `sixlabors.lic` and its free sample licence expired 2026-09-04. Golden hashes are recorded against
  that exact version; a SkiaSharp upgrade will fail them **by design** — review and re-record, never
  loosen.
- **Fixtures are generated**, so none carries authentic camera encoder output (`tests/assets/`,
  generator + committed outputs). A green suite is not evidence of real-world coverage; see item 3.
- **`InternalsVisibleTo` now covers `HikvisionReplicator.Tests`**, so `FromPersistence` and
  aggregate-internal mutators are asserted directly rather than by reflection.

### Verification findings worth carrying forward

- **A wait-loop condition that can return empty is not a wait.** Polling `gh pr checks --json state`
  returned blank rather than `PENDING`, so the loop exited immediately and CI was reported as
  finished while it was still running. Key the condition off a field that cannot go blank.
- **A comment can claim a safeguard the code does not implement.** The normalizer documented that its
  resolution floor is judged on orientation-corrected dimensions; `Min`/`Max` are invariant under that
  swap, so it cannot be. Found only by mutation — it killed no test. **The error originated in the
  orchestrator's own instructions to three workers**, propagated into a code comment and a provenance
  file, and would not have been caught by re-reading. This is the concrete argument for author ≠ verifier.
- **An instrument with no reader records into nothing.** `Program.cs` had `.WithTracing(…)` and no
  `.WithMetrics(…)` at all; USR-41's histograms passed their tests because the tests installed their
  own listener. See L-037.
- **CI and local warning counts differ legitimately.** Local `dotnet build` restores implicitly, so all
  4 `NU1903` land in the build tally (14); CI restores in a separate step, so one is attributed there
  (13). Compare per-rule, never by total.
- **`%2F` is not decoded into a path separator.** An `ExternalRef` containing `/` does not 404 — it
  registers under the literal escaped text, substituting one identity for another. A-15 was amended
  to exclude `/`; the code was not changed, because the `/` never reaches the application.
- Lessons **L-035…L-038** added as candidates. L-033/L-034 remain candidates from `context-engineering`;
  **L-007 is confirmed** (×2) and is why any "no new warnings" claim must come from `--no-incremental`.

- **Pre-existing warnings, unchanged by this feature**: 10 `CA` + 4 `CS0618` + 4 `NU1903` (SSH.NET,
  transitive via Testcontainers). Baseline, not debt introduced here. Never use `-warnaserror`.
- **Test totals**: 282 unit · 193 integration, both green (224 → 191 via AD-036, → 193 after its Verifier's fixes; AD-037 renamed and re-split the classes without moving the count). **There is no E2E figure any more** — the project was deleted on this branch (AD-035) and its 17 tests were *removed, not converted*, each having duplicated an integration test. Entering `user-registry` the totals were 81 · 88 (+ 9 E2E).
- **Surviving pre-rewrite branches**, untouched and unreviewed: `001-hikvision-device-api`,
  `002-adr-conformance` (local-only, no upstream).

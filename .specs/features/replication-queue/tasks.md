# replication-queue Tasks

## Execution Protocol (MANDATORY — do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its
Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is
the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review,
Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Design**: `.specs/features/replication-queue/design.md`
**Status**: In Progress — Phase 1 complete (T1–T6, 348 unit tests, was 282); Phase 2 complete
(T6b, T7–T12, 358 unit · 209 integration)

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec — confirm before Execute. Guidelines found:
> `CLAUDE.md`, `docs/test-patterns.md`, `.specs/STATE.md` (AD-024, AD-026, AD-036, AD-037, AD-040),
> `.github/workflows/build-and-test.yml`.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| --- | --- | --- | --- | --- |
| Domain aggregates, value objects, events (`Replication`, `BackfillIntent`, `User`/`Device` raises) | unit | All branches; 1:1 with spec ACs; every listed edge case | `src/HikvisionReplicator.Tests/Domain/**` | `dotnet test src/HikvisionReplicator.Tests` |
| Self-contained infrastructure logic with no I/O (`DomainEventDispatcher`) | unit | All branches, including the no-handler and no-events paths | `src/HikvisionReplicator.Tests/Infrastructure/**` | `dotnet test src/HikvisionReplicator.Tests` |
| Feature slices and their routes (device guard, every write path that fans out) | integration — **black box through HTTP** (AD-036) | Every use case: happy path, every listed edge case, every documented error path. Verifying by reading the database is expected; *driving* by repository is not | `src/HikvisionReplicator.IntegrationTests/{UseCase}Tests.cs` (AD-037) | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Startup / wiring / cross-cutting (`AppDbContext` override, DI registration, meters) | integration | Ordering and failure behaviour; every meter registered on the provider (L-037) | `src/HikvisionReplicator.IntegrationTests/StartupTests.cs`, `…/ReplicationObservabilityTests.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Observables HTTP cannot distinguish — index shape, the expansion operation | integration — **contract class** (AD-040) | Each test names, in its own doc comment, the observable HTTP cannot distinguish | `src/HikvisionReplicator.IntegrationTests/ReplicationQueueContractTests.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| EF configurations, migrations, DI registration lines | none — build gate only (index *shape* is covered by the contract class above) | — | — | build gate only |

**Baseline (do not regress):** 282 unit · 193 integration, both green. Every task's count is a
**minimum**; no existing test may be deleted or weakened to make a gate pass.

## Gate Check Commands

> Generated from codebase (`CLAUDE.md` § Gate commands, `.github/workflows/build-and-test.yml`) —
> confirm before Execute.

| Gate Level | When to Use | Command |
| --- | --- | --- |
| Quick | After tasks with unit tests only — Docker-free (AD-024, AD-026) | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` |
| Full | After any task with integration tests — needs a Docker daemon (AD-019) | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests && dotnet test src/HikvisionReplicator.IntegrationTests` |
| Build | After phase completion, or for config/migration-only work | `dotnet build HikvisionReplicator.slnx --no-incremental` |

**L-007 (confirmed ×2):** any "no new warnings" claim must come from a `--no-incremental` build — an
up-to-date incremental build re-reports zero diagnostics even when the code still violates them.
**Never use bare `dotnet format`**; fix whitespace with `dotnet format whitespace`.

---

## Execution Plan

Phases are ordered and run sequentially — each phase completes before the next begins, and tasks
within a phase execute in order.

### Phase 1: Domain foundation (Docker-free) — ✅ COMPLETE

Pure logic. Nothing here touches EF, so the whole phase runs on the quick gate.

```
T1 ✅ → T2 ✅ → T3 ✅ → T4 ✅ → T5 ✅ → T6 ✅
```

Commits: `3f44aec`, `b39532a`, `862c3ce`, `cb4228f`, `b1dd1c7`, `3fc2ddc`. 282 → **348 unit tests**,
0 failed, no existing test touched. Three deviations, all accepted:
**(1)** `IAggregateRoot` gaining abstract members made T1's "User and Device compile unchanged"
unsatisfiable, so an `abstract class AggregateRoot` holds the event list and the protected `Raise`;
default interface implementations were rejected as exactly the silent-no-op shape the design warns
about. **(2)** Aggregate-contract tests live in `Tests/Domain/`, not a `Tests/Shared/` namespace that
would add a CA1716 warning. **(3)** `BackfillStatus` ships with T4 rather than T2, so T2 carries no
unused type.

### Phase 2: Persistence — ✅ COMPLETE

Schema, mapping and the constraint translation the invariant depends on. **T6b is a Phase 1
correction found by the Phase 1 worker** and must land before anything is mapped.

```
T6b ✅ → T7 ✅ → T8 ✅ → T9 ✅ → T10 ✅ → T11 ✅ → T12 ✅
```

Commits: `148cfeb`, `90fb5ec`, `2e7103a`, `79f5284`, `5da4900`, `f6b00ee`, `4454e21`.
348 → **358 unit tests**, 193 → **209 integration tests**, 0 failed, no existing test touched.
One deviation, accepted: **EF Core 10 refuses `Migrate()` while the model carries changes no
migration covers**, so mapping an aggregate without its migration turns every integration test
red. T9's single migration was therefore split and moved forward — `AddReplicationQueue` ships
with T7 and `AddBackfillIntents` with T8, each with the configuration that needs it — leaving
T9 to register both DbSets and prove the schema (both tables, all four indexes, both foreign-key
behaviours, no pending model diff, and the upgrade onto a database already holding spectators
and readers). T9's commit subject was adjusted to match what it actually does.

### Phase 3: Dispatch plumbing

The hook that makes Phase 1's events reach Phase 4's handler.

```
T13 → T14 → T15
```

### Phase 4: Fan-out rules

```
T16 → T17 → T18 → T19 → T20 → T21
```

### Phase 5: Capacity guard and observability

```
T22 → T23 → T24 → T25
```

---

## Task Breakdown

### T1: Add domain-event storage to the aggregate contract

**What**: `IDomainEvent` marker and `DomainEvents` / `ClearDomainEvents()` on `IAggregateRoot`, with the protected raise helper aggregates use.
**Where**: `src/HikvisionReplicator.Api/Shared/IDomainEvent.cs`, `Shared/IAggregateRoot.cs`
**Depends on**: None
**Reuses**: `Shared/IAggregateRoot.cs` (extends it — AD-042 amends AD-005)
**Requirement**: enabler for REP-01…REP-07; AD-042

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `DomainEvents` is read-only to callers; events are added only through the protected helper
- [ ] `ClearDomainEvents()` empties the collection
- [ ] `User` and `Device` still compile unchanged
- [ ] Gate passes: quick
- [ ] ≥ 3 unit tests pass; 282 unit baseline not reduced

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): give aggregates a domain-event collection`

---

### T2: Create the Replication aggregate

**What**: The aggregate with its FKs, operation, lane, status, attempt count and bounded last error — creation and invariants only, no transitions.
**Where**: `src/HikvisionReplicator.Api/Domain/Replication.cs` (+ the four enums)
**Depends on**: T1
**Reuses**: `Domain/User.cs` factory shape (AD-005), `Shared/IAggregateRoot.cs`
**Requirement**: REP-26, REP-33

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `Create(...)` yields `Pending`, attempt count `0`, no last error (REP-26)
- [ ] Private setters, private EF constructor, static factory only (AD-005)
- [ ] `now` is a parameter; no ambient clock read (AD-023)
- [ ] `MaxLastErrorLength = 1000` defined and enforced where the error is stored (REP-33)
- [ ] Gate passes: quick
- [ ] ≥ 6 unit tests pass

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): add the Replication aggregate`

---

### T3: Enforce the Replication transition table

**What**: `Begin`, `Succeed`, `Fail`, `Retry`, `Supersede` with every illegal transition refused and the aggregate left untouched.
**Where**: `src/HikvisionReplicator.Api/Domain/Replication.cs` (modify)
**Depends on**: T2
**Reuses**: the `OneOf<Success, ValidationError>` convention (AD-002)
**Requirement**: REP-27, REP-28, REP-29, REP-30, REP-31, REP-32

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Exactly the five legal transitions are permitted (REP-27)
- [ ] Every illegal transition returns a validation error **and changes no field** (REP-28)
- [ ] `Succeeded` and `Superseded` refuse everything after them (REP-29)
- [ ] `Fail` increments the attempt count by one and stores the error (REP-30)
- [ ] `Retry` preserves the attempt count (REP-31)
- [ ] Every transition timestamps from the passed-in clock (REP-32)
- [ ] An error longer than 1,000 characters is truncated, and the transition still succeeds (REP-33)
- [ ] Gate passes: quick
- [ ] ≥ 18 unit tests pass — every legal transition and a refusal for each illegal one

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): enforce the replication transition table`

---

### T4: Create the BackfillIntent aggregate

**What**: The one-per-device intent with its terminal `Expanded` transition.
**Where**: `src/HikvisionReplicator.Api/Domain/BackfillIntent.cs`
**Depends on**: T1
**Reuses**: `Domain/Replication.cs` factory shape from T2
**Requirement**: REP-20 (domain half)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `Create(deviceId, now)` yields `Pending` with no `ExpandedAt`
- [ ] `MarkExpanded(now)` sets the status and the timestamp
- [ ] A second `MarkExpanded` is refused with a validation error (REP-20)
- [ ] Gate passes: quick
- [ ] ≥ 5 unit tests pass

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): add the BackfillIntent aggregate`

---

### T5: Raise user events from the branches that already decide

**What**: `UserRegistered`, `UserChanged`, `UserRestored`, `UserRemoved` raised from `Create`, the **changed branch** of `Update`, `Restore` and `MarkDeleted`.
**Where**: `src/HikvisionReplicator.Api/Domain/User.cs` (modify), `Domain/Events/`
**Depends on**: T1
**Reuses**: the existing `changed` guard inside `User.Update` (USR-26) — the event goes in that branch, nowhere else
**Requirement**: REP-04, REP-06, REP-42 (domain halves)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `Create` raises `UserRegistered`; `Restore` raises `UserRestored`, **not** `UserChanged` (REP-06)
- [ ] `Update` raises `UserChanged` **only** when a field actually differs (REP-04)
- [ ] An update that changes nothing raises **no event at all** and still does not advance `UpdatedAt` (REP-04, USR-26 unbroken)
- [ ] `MarkDeleted` raises `UserRemoved`
- [ ] All 282 existing unit tests still pass unchanged
- [ ] Gate passes: quick
- [ ] ≥ 8 unit tests pass, asserting **which** events each call raises and that a no-op raises none

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): raise user lifecycle events from the aggregate`

---

### T6: Raise DeviceRegistered from Device.Create

**What**: The single device-side event.
**Where**: `src/HikvisionReplicator.Api/Domain/Device.cs` (modify), `Domain/Events/DeviceRegistered.cs`
**Depends on**: T1
**Reuses**: T5's event shape
**Requirement**: REP-14 (domain half)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `Device.Create` raises exactly one `DeviceRegistered`
- [ ] A device **update** raises nothing — a backfill is owed on registration only
- [ ] Gate passes: quick
- [ ] ≥ 3 unit tests pass

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): raise DeviceRegistered on device creation`

---

### T6b: Carry the aggregate in domain events, not its id

**What**: Change all five events to carry the aggregate instance, and give `Replication` a `User` navigation and `BackfillIntent` a `Device` navigation with matching factories.
**Where**: `src/HikvisionReplicator.Api/Domain/Events/*.cs`, `Domain/Replication.cs`, `Domain/BackfillIntent.cs`, `Domain/User.cs`, `Domain/Device.cs` (modify)
**Depends on**: T6
**Reuses**: T2/T4's factory shapes
**Requirement**: corrects the design for REP-01, REP-14 — see design.md § Risks, first row

**Why this exists**: `UserRegistered` and `DeviceRegistered` are raised from inside the static factories (`Domain/User.cs:71`, `Domain/Device.cs:77`), where the database-generated key is still `0`. A handler staging a row from that id would write a foreign key to a row that does not exist. Carrying the aggregate lets EF fix the key up at save, and spares the handler a re-read.

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] All five events carry the aggregate instance and `OccurredAt`; no event carries a bare id
- [ ] `Replication` has a `User` navigation **and keeps** its int-based factory for the already-persisted paths (bulk expansion, live fan-out over registered devices)
- [ ] `BackfillIntent` has a `Device` navigation and an instance factory, keeping the int-based one
- [ ] `Replication` gains **no** `Device` navigation — a replication is only ever staged against a device already in the catalogue
- [ ] A unit test asserts a registration event exposes the aggregate whose `Id` is still `0`, so the defect cannot silently return
- [ ] All 348 existing unit tests still pass
- [ ] Gate passes: quick
- [ ] ≥ 6 unit tests pass

**Tests**: unit · **Gate**: quick
**Commit**: `fix(domain): carry the aggregate in domain events instead of its id`

---

### T7: Map Replication, with the partial unique index

**What**: `IEntityTypeConfiguration<Replication>` — enums as strings, FK behaviours, and the three indexes, with index names as constants.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationConfiguration.cs`
**Depends on**: T3
**Reuses**: `Infrastructure/UserConfiguration.cs` — the `HasFilter("\"DeletedAt\" IS NULL")` partial-index pattern and its named-constant convention (AD-009)
**Requirement**: REP-09 (the invariant), REP-16 (cascade)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `IX_replications_pending` is `UNIQUE (UserId, DeviceId) WHERE Status = 'Pending'`
- [ ] `Replication → Device` cascades; `Replication → User` restricts (users are tombstoned, AD-034)
- [ ] Enums are stored as strings, not ordinals
- [ ] Index names are `const` on the configuration class, so the repository can key off them
- [ ] The `User` navigation from T6b is mapped, so EF fixes up the foreign key for a replication staged against an unsaved spectator
- [ ] A contract test asserts the index **shape**, naming in its doc comment what HTTP cannot distinguish (AD-040)
- [ ] Gate passes: full
- [ ] ≥ 3 integration tests pass in `ReplicationQueueContractTests`

**Tests**: integration (contract) · **Gate**: full
**Commit**: `feat(infra): map the Replication aggregate and its pending index`

---

### T8: Map BackfillIntent

**What**: `IEntityTypeConfiguration<BackfillIntent>` — unique `DeviceId`, cascade on device delete.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/BackfillIntentConfiguration.cs`
**Depends on**: T4
**Reuses**: T7's configuration shape
**Requirement**: REP-14, REP-16

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `UNIQUE (DeviceId)` — one intent per device
- [ ] Cascade on device delete
- [ ] The `Device` navigation from T6b is mapped, so an intent staged against an unsaved device gets its key
- [ ] Gate passes: build
- [ ] Build emits no new warnings, verified with `--no-incremental` (L-007)

**Tests**: none (build gate; the index shape is asserted in T7's contract class) · **Gate**: build
**Commit**: `feat(infra): map the BackfillIntent aggregate`

---

### T9: Add the migration and register both DbSets

**What**: One EF migration creating both tables with their indexes, plus the `AppDbContext` sets.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/Migrations/`, `Infrastructure/AppDbContext.cs` (modify)
**Depends on**: T7, T8
**Reuses**: the existing migration chain; migrations apply at startup (`Migrate()`, never `EnsureCreated()`)
**Requirement**: REP-09, REP-14, REP-16

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Migration creates both tables, all four indexes and both FK behaviours
- [ ] It applies cleanly on an empty database **and** on one already holding users and devices
- [ ] The model snapshot has no pending-changes diff
- [ ] Gate passes: full
- [ ] 193 integration baseline still green

**Tests**: integration (the existing startup/migration coverage exercises it) · **Gate**: full
**Commit**: `feat(infra): add the replication queue migration`

---

### T10: Translate the pending-index violation into a conflict

**What**: `IReplicationRepository` + implementation, turning `23505` on the pending index into a `ConflictError` and letting every other violation propagate.
**Where**: `src/HikvisionReplicator.Api/Shared/IReplicationRepository.cs`, `Infrastructure/ReplicationRepository.cs`
**Depends on**: T9
**Reuses**: `Infrastructure/UserRepository.cs:58-73` — the `PostgresException` / `UniqueViolation` / named-index switch, shape for shape (AD-022)
**Requirement**: REP-13

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] A violation on `IX_replications_pending` becomes the `DuplicatePendingWork` conflict
- [ ] A `23505` on **any other** index is deliberately not matched and propagates
- [ ] The message is asserted against its **literal text** at least once, not only against the constant the production code emits (AD-036's tautology trap)
- [ ] Gate passes: full
- [ ] ≥ 3 integration tests pass in `ReplicationQueueContractTests`

**Tests**: integration (contract) · **Gate**: full
**Commit**: `feat(infra): translate duplicate pending work into a conflict`

---

### T11: Add the user and device specifications

**What**: `RegisteredDeviceIdsSpec`, `ActiveUserIdsSpec`, `ActiveUserCountSpec` — id/count projections, never full aggregates.
**Where**: `src/HikvisionReplicator.Api/Domain/Specs/`
**Depends on**: T9
**Reuses**: `Domain/Specs/ActiveUsersPagedSpec.cs` — including its lesson that a specification must read **exactly** the window it is asked for
**Requirement**: REP-01, REP-17, REP-21

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Each projects ids or a count — no face pictures, no navigations (A-1)
- [ ] `ActiveUserIdsSpec` and `ActiveUserCountSpec` exclude tombstoned users (REP-18)
- [ ] Gate passes: build
- [ ] Build clean with `--no-incremental`

**Tests**: none (build gate; behaviour is asserted through the use cases that consume them in Phase 4) · **Gate**: build
**Commit**: `feat(domain): add specifications for fan-out reads`

---

### T12: Add the queue specifications

**What**: `PendingReplicationsForUserSpec`, `PendingReplicationPairsForDeviceSpec`, `PendingBackfillIntentForDeviceSpec`.
**Where**: `src/HikvisionReplicator.Api/Domain/Specs/`
**Depends on**: T9
**Reuses**: T11's projection shape
**Requirement**: REP-08, REP-19, REP-20

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Each filters on `Pending` only, so terminal rows are never returned
- [ ] Gate passes: build
- [ ] Build clean with `--no-incremental`

**Tests**: none (build gate; asserted through Phase 4's use cases) · **Gate**: build
**Commit**: `feat(domain): add specifications for queue reads`

---

### T13: Build the domain-event dispatcher

**What**: `IDomainEventDispatcher` and a hand-rolled implementation resolving `IDomainEventHandler<T>` from the service provider. No MediatR.
**Where**: `src/HikvisionReplicator.Api/Shared/IDomainEventDispatcher.cs`, `Shared/IDomainEventHandler.cs`, `Infrastructure/DomainEventDispatcher.cs`
**Depends on**: T1
**Reuses**: the DI conventions already in `Program.cs`
**Requirement**: AD-042 (enabler for REP-01…REP-07)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Every handler registered for an event type is invoked, once each
- [ ] An empty event list short-circuits without resolving anything
- [ ] The cancellation token reaches every handler (AD-007)
- [ ] Gate passes: quick
- [ ] ≥ 6 unit tests pass, using fake handlers

**Tests**: unit · **Gate**: quick
**Commit**: `feat(infra): add a domain-event dispatcher`

---

### T14: Dispatch from SaveChangesAsync, before base

**What**: The `AppDbContext` override — collect events from tracked aggregates, dispatch, call `base`, and clear events **only after it returns**.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/AppDbContext.cs` (modify)
**Depends on**: T13
**Reuses**: nothing — this is the one new hook
**Requirement**: REP-07

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Dispatch happens **before** `base.SaveChangesAsync`, so staged rows join the same save (REP-07)
- [ ] A handler that throws leaves **no row committed** — asserted by forcing a throw and reading the table
- [ ] A failed save leaves the aggregates' events **intact**, asserted by provoking a real failure and inspecting the collection
- [ ] A save with no event-carrying aggregate resolves no handler
- [ ] Gate passes: full
- [ ] ≥ 5 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(infra): dispatch domain events before saving`

---

### T15: Register the handlers and assert the feature runs nothing

**What**: DI registration for the dispatcher and handlers, a startup assertion that every `IDomainEvent` has at least one handler, and the AD-039 assembly check.
**Where**: `src/HikvisionReplicator.Api/Program.cs` (modify), `src/HikvisionReplicator.IntegrationTests/StartupTests.cs` (modify)
**Depends on**: T14
**Reuses**: `StartupTests.cs` patterns
**Requirement**: REP-34

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Every `IDomainEvent` type in the assembly resolves at least one handler — an unregistered event fails startup rather than fanning out nothing (the L-037 shape)
- [ ] A test asserts the API assembly contains no `IHostedService`, no `BackgroundService`, no scheduler and no job-runner package reference (REP-34, AD-039)
- [ ] Gate passes: full
- [ ] ≥ 4 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(api): register domain-event handlers and assert no runner ships`

---

### T16: Fan out user writes to every registered device

**What**: `ReplicationFanOut` handling `UserRegistered`, `UserRestored`, `UserChanged`, `UserRemoved` — one `Pending` row per registered device, staged, never saved.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationFanOut.cs`
**Depends on**: T10, T11, T12, T15
**Reuses**: AD-041's stage-never-save contract; T11/T12 specifications
**Requirement**: REP-01, REP-02, REP-03, REP-04, REP-05, REP-06, REP-07, REP-42

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] A create with N devices registered yields N `Add` rows, lane `Live`, status `Pending` (REP-01)
- [ ] An update that changed something yields N `Update` rows (REP-02); a removal yields N `Remove` rows (REP-03)
- [ ] A no-op upsert yields **nothing** (REP-04); a write with no devices registered succeeds and yields nothing (REP-05)
- [ ] A resurrection yields `Add`, not `Update` (REP-06)
- [ ] A **second** `DELETE` — which returns success, not 404 (USR-32) — yields no further rows (REP-42)
- [ ] The handler never calls `SaveChanges` (AD-041)
- [ ] Gate passes: full
- [ ] ≥ 12 integration tests pass, driven through real HTTP and verified by reading the table

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): queue work for every registered device on a user write`

---

### T17: Supersede pending work instead of duplicating it

**What**: Before staging, move any `Pending` row for the pair to `Superseded`; leave `InProgress` rows alone.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationFanOut.cs` (modify)
**Depends on**: T16
**Reuses**: `PendingReplicationsForUserSpec` from T12
**Requirement**: REP-08, REP-09, REP-10, REP-12, REP-13

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Three successive upserts leave one `Pending` row and two `Superseded` ones, the `Pending` carrying the newest intent (REP-08)
- [ ] A superseded row keeps its operation, lane, attempt count and last error (REP-10)
- [ ] An `InProgress` row is **not** superseded; the new intent is inserted `Pending` beside it (REP-12)
- [ ] Concurrent upserts leave exactly one `Pending` row per pair, with **no `500`** anywhere (REP-13)
- [ ] The duplicate-pending conflict is proven **deterministically**, not only by racing — a guard reachable only by winning a coin-flip is not a guard (AD-036)
- [ ] Gate passes: full
- [ ] ≥ 8 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): supersede pending work rather than duplicating it`

---

### T18: Make a removal supersede a pending add and still execute

**What**: The `UserRemoved` path supersedes any pending `Add`/`Update` for the pair and still stages its own `Remove`.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationFanOut.cs` (modify)
**Depends on**: T17
**Reuses**: T17's supersession helper
**Requirement**: REP-11, REP-41

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Removing a user with a pending `Add` leaves the `Add` `Superseded` and a `Remove` `Pending` (REP-11)
- [ ] Removing a user who was never replicated anywhere still queues `Remove` rows — the queue cannot know what a device holds (REP-41)
- [ ] Gate passes: full
- [ ] ≥ 4 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): let a removal supersede pending work and still run`

---

### T19: Record a backfill intent when a device is registered

**What**: The `DeviceRegistered` handler — exactly one intent row, no per-user rows.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationFanOut.cs` (modify)
**Depends on**: T16
**Reuses**: T8's mapping, T4's aggregate
**Requirement**: REP-14, REP-15, REP-43

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Registering a device with 50 users present creates **one** row, not 50 (REP-14)
- [ ] Registering with no users still creates the intent (REP-15)
- [ ] A registration rejected for a duplicate `ip:port` leaves **no** intent behind (REP-43)
- [ ] Gate passes: full
- [ ] ≥ 5 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): record a backfill intent on device registration`

---

### T20: Implement the backfill expansion

**What**: `ExpandBackfillAsync` — stage one `Bulk` `Add` per active user, skip pairs already holding a `Pending` row, mark the intent `Expanded`, return the count.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationFanOut.cs` (modify)
**Depends on**: T19
**Reuses**: `ActiveUserIdsSpec`, `PendingReplicationPairsForDeviceSpec`
**Requirement**: REP-17, REP-18, REP-19, REP-20, REP-44, REP-45, REP-46

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] One `Bulk` `Add` per **active** user (REP-17); tombstoned users skipped (REP-18)
- [ ] A pair already holding a `Pending` row is left untouched — a live intent outranks a backfill one (REP-19, REP-46)
- [ ] The intent reaches `Expanded`, and a second invocation stages nothing (REP-20)
- [ ] Expansion for a deleted device stages nothing and does not throw (REP-44)
- [ ] Concurrent invocation produces no duplicate `Pending` row (REP-45)
- [ ] Every test lives in `ReplicationQueueContractTests` and **names, in its own doc comment, what HTTP cannot distinguish** (AD-040)
- [ ] Gate passes: full
- [ ] ≥ 9 integration tests pass

**Tests**: integration (contract) · **Gate**: full
**Commit**: `feat(replication): expand a device backfill into bulk-lane work`

---

### T21: Let a device delete take its queued work with it

**What**: Prove the cascade — a device with pending rows and an intent still deletes, and DEV-25 keeps returning `204`.
**Where**: `src/HikvisionReplicator.IntegrationTests/RemoveDeviceTests.cs` (modify); fix `ReplicationConfiguration`/`BackfillIntentConfiguration` if it does not hold
**Depends on**: T20
**Reuses**: existing `RemoveDeviceTests` patterns
**Requirement**: REP-16, REP-40

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Deleting a device holding `Pending` replications succeeds with `204` (REP-40) — not a constraint violation
- [ ] Its replications and its backfill intent are gone (REP-16)
- [ ] Replications for **other** devices are untouched
- [ ] Gate passes: full
- [ ] ≥ 3 integration tests pass; all existing `RemoveDeviceTests` still green

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): cascade queued work when a device is deleted`

---

### T22: Refuse a device that cannot hold the fleet

**What**: The fleet-admission guard in `RegisterDeviceService` — reject when `FaceCapacity` is below the active user count.
**Where**: `src/HikvisionReplicator.Api/Features/Devices/RegisterDevice/RegisterDeviceService.cs` (modify)
**Depends on**: T11, T21
**Reuses**: `ActiveUserCountSpec`; the slice's existing `ConflictError` path, so no endpoint change (AD-003)
**Requirement**: REP-21, REP-22, REP-47

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Capacity below the active count is refused with a `409` naming **both** numbers (REP-21)
- [ ] Capacity equal to or above the count registers normally (REP-22)
- [ ] With zero active users, any capacity registers (REP-47)
- [ ] Tombstoned users do not count toward the ceiling
- [ ] Gate passes: full
- [ ] ≥ 6 integration tests pass; all existing `RegisterDeviceTests` still green

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): refuse a device that cannot hold the active roster`

---

### T23: Apply the guard when capacity is lowered

**What**: The same check on a `FaceCapacity` change in `UpdateDeviceService`.
**Where**: `src/HikvisionReplicator.Api/Features/Devices/UpdateDevice/UpdateDeviceService.cs` (modify)
**Depends on**: T22
**Reuses**: T22's guard
**Requirement**: REP-23

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Lowering capacity below the active count is refused on the same terms as REP-21
- [ ] A capacity change that stays at or above the count succeeds
- [ ] An update that does not touch capacity is unaffected, and `updatedAt` still does not move on a no-op (DEV-23 unbroken)
- [ ] Gate passes: full
- [ ] ≥ 4 integration tests pass; all existing `UpdateDeviceTests` still green

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): refuse lowering capacity below the active roster`

---

### T24: Signal when the roster outgrows a registered device

**What**: On a user write that takes the active count above a device's ceiling, accept the user and emit the warning log plus the counter.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationFanOut.cs` (modify)
**Depends on**: T23
**Reuses**: T22's count read
**Requirement**: REP-24, REP-25

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] The user is still accepted with a `2xx` (REP-24)
- [ ] A warning log carries the device identifier, its capacity and the new count — the count is unbounded cardinality and cannot be a tag
- [ ] No signal fires while every device is at or above the count (REP-25)
- [ ] Gate passes: full
- [ ] ≥ 4 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): signal when the roster outgrows a device`

---

### T25: Instrument the queue

**What**: `ReplicationMetrics` with the four instruments, **registered on the metrics provider**.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/ReplicationMetrics.cs`, `Program.cs` (modify), `src/HikvisionReplicator.IntegrationTests/ReplicationObservabilityTests.cs`
**Depends on**: T24
**Reuses**: `SkiaFaceImageNormalizer`'s meter pattern and `Program.cs:93-96`'s `.WithMetrics(...).AddMeter(...)`; `UserObservabilityTests.cs` for test shape
**Requirement**: REP-35, REP-36, REP-37, REP-38, REP-39

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Enqueue counter tagged by operation and lane (REP-35); supersession counter (REP-36); capacity-exceeded counter tagged by device (REP-37); fan-out size recorded (REP-38)
- [ ] The meter name appears in `Program.cs`'s `AddMeter` — asserted against the **provider registration**, not only a listener the test installs (REP-39, L-037)
- [ ] Gate passes: full
- [ ] ≥ 6 integration tests pass

**Tests**: integration · **Gate**: full
**Commit**: `feat(replication): instrument enqueue, supersession and capacity`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5

Phase 1:  T1 ──→ T2 ──→ T3 ──→ T4 ──→ T5 ──→ T6
            │      │            │      │      │
            │      ↓            │      │      │
Phase 2:    │     T7 ──→ T8 ──→ T9 ──→ T10 ──→ T11 ──→ T12
            │     (T7←T3)  (T8←T4)  (T9←T7,T8)
            ↓
Phase 3:  T13 ──→ T14 ──→ T15
          (T13←T1)
Phase 4:  T16 ──→ T17 ──→ T18 ──→ T19 ──→ T20 ──→ T21
          (T16←T10,T11,T12,T15)        (T19←T16)
Phase 5:  T22 ──→ T23 ──→ T24 ──→ T25
          (T22←T11,T21)
```

Execution is strictly sequential — there is no intra-phase parallelism.

**Batch packing (~7 tasks per worker, whole phases only):**

| Batch | Phases | Tasks | Count |
| --- | --- | --- | --- |
| 1 ✅ | Phase 1 | T1–T6 | 6 |
| 2 | Phase 2 | T6b, T7–T12 | 7 |
| 3 | Phase 3 + Phase 4 | T13–T21 | 9 |
| 4 | Phase 5 | T22–T25 | 4 |

26 tasks (T6b added mid-flight) → **4 sequential batches**. More than one batch, so the sub-agent offer applies.

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T1 | 1 interface + 1 contract extension | ✅ Granular |
| T2, T3 | 1 aggregate, split at creation vs. transitions | ✅ Granular |
| T4, T5, T6 | 1 aggregate / 1 aggregate's events each | ✅ Granular |
| T7, T8 | 1 EF configuration each | ✅ Granular |
| T9 | 1 migration | ✅ Granular |
| T10 | 1 repository + its interface | ✅ Granular |
| T11, T12 | 3 specifications each, cohesive by what they read | ⚠️ Multi-file but cohesive — accepted; each is a few lines and they share one purpose |
| T13 | 1 dispatcher + 2 interfaces | ✅ Granular |
| T14, T15 | 1 override / 1 registration + assertion | ✅ Granular |
| T16–T20 | 1 rule each on one handler file | ✅ Granular |
| T21 | 1 behaviour (cascade) + its tests | ✅ Granular |
| T22–T25 | 1 slice change / 1 signal / 1 metrics class | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T1 | None | root | ✅ |
| T2 | T1 | T1→T2 | ✅ |
| T3 | T2 | T2→T3 | ✅ |
| T4 | T1 | T1→T4 | ✅ |
| T5 | T1 | T1→T5 | ✅ |
| T6 | T1 | T1→T6 | ✅ |
| T6b | T6 | T6→T6b | ✅ |
| T7 | T3, T6b | T3,T6b→T7 | ✅ |
| T8 | T4 | T4→T8 | ✅ |
| T9 | T7, T8 | T7,T8→T9 | ✅ |
| T10 | T9 | T9→T10 | ✅ |
| T11 | T9 | T9→T11 | ✅ |
| T12 | T9 | T9→T12 | ✅ |
| T13 | T1 | T1→T13 | ✅ |
| T14 | T13 | T13→T14 | ✅ |
| T15 | T14 | T14→T15 | ✅ |
| T16 | T10, T11, T12, T15 | all four → T16 | ✅ |
| T17 | T16 | T16→T17 | ✅ |
| T18 | T17 | T17→T18 | ✅ |
| T19 | T16 | T16→T19 | ✅ |
| T20 | T19 | T19→T20 | ✅ |
| T21 | T20 | T20→T21 | ✅ |
| T22 | T11, T21 | T11,T21→T22 | ✅ |
| T23 | T22 | T22→T23 | ✅ |
| T24 | T23 | T23→T24 | ✅ |
| T25 | T24 | T24→T25 | ✅ |

No task depends on a later phase. T7←T3 and T13←T1 point backward across phases; T16←T15 and T22←T21 point backward across phases. ✅

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T1 | Aggregate contract (domain) | unit | unit | ✅ |
| T2, T3 | Domain aggregate | unit | unit | ✅ |
| T4 | Domain aggregate | unit | unit | ✅ |
| T5, T6 | Domain aggregate | unit | unit | ✅ |
| T7 | EF configuration **+ index shape** | none (config) / integration-contract (shape) | integration (contract) | ✅ highest wins |
| T8 | EF configuration | none — build gate | none | ✅ |
| T9 | Migration + DbContext sets | none (config) — exercised by existing startup coverage | integration | ✅ exceeds matrix, allowed |
| T10 | Repository + constraint translation | integration (contract) | integration (contract) | ✅ |
| T11, T12 | Specifications (config-like, no behaviour of their own) | none — build gate | none | ✅ — asserted through Phase 4 use cases that consume them, which is coverage **in the same feature**, not deferral of this task's own layer |
| T13 | Infrastructure logic, no I/O | unit | unit | ✅ |
| T14 | Cross-cutting wiring | integration | integration | ✅ |
| T15 | Startup / DI | integration | integration | ✅ |
| T16–T20 | Fan-out rules (I/O) | integration | integration | ✅ |
| T21 | Schema behaviour through a route | integration | integration | ✅ |
| T22, T23 | Feature slices | integration | integration | ✅ |
| T24 | Fan-out rule + log/metric | integration | integration | ✅ |
| T25 | Metrics + provider registration | integration | integration | ✅ |

No ❌. T11/T12 are the only `Tests: none` entries and both are build-gate layers per the matrix.

---

## Requirement Coverage

All 47 criteria map to a task:

| Requirements | Tasks |
| --- | --- |
| REP-01…REP-07 | T5, T16 |
| REP-08…REP-13 | T10, T12, T17 |
| REP-11, REP-41 | T18 |
| REP-14, REP-15, REP-43 | T6, T8, T19 |
| REP-16, REP-40 | T7, T8, T9, T21 |
| REP-17…REP-20, REP-44…REP-46 | T4, T20 |
| REP-21, REP-22, REP-47 | T11, T22 |
| REP-23 | T23 |
| REP-24, REP-25 | T24 |
| REP-26…REP-33 | T2, T3 |
| REP-34 | T15 |
| REP-35…REP-39 | T25 |

**Coverage:** 47 total, 47 mapped to tasks, 0 unmapped.

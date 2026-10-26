# replication-worker Tasks

## Execution Protocol (MANDATORY — do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its
Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is
the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review,
Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Design**: `.specs/features/replication-worker/design.md`
**Status**: Draft

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec — confirm before Execute. Guidelines found:
> `CLAUDE.md`, `docs/test-patterns.md`, `.specs/STATE.md` (AD-024, AD-026, AD-036, AD-037, AD-040,
> AD-044), `.github/workflows/ci.yml`.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| --- | --- | --- | --- | --- |
| Domain aggregate (`Replication` transitions) | unit | All branches; 1:1 with spec ACs; every refused transition leaves every field unchanged | `src/HikvisionReplicator.Tests/Domain/**` | `dotnet test src/HikvisionReplicator.Tests` |
| Pure worker logic with no I/O (`DevicePause`, `RetrySchedule`, `OutcomeDecision`, options validator, `FakeDeviceClient` scripting) | unit | All branches; every boundary (cap, threshold, max attempts) on both sides | `src/HikvisionReplicator.Tests/Infrastructure/**` | `dotnet test src/HikvisionReplicator.Tests` |
| Worker behaviour (supervisor, device loop, outcome recorder, backfill, lease sweep) | integration — **black box**: driven through HTTP, observed on the fake adapter and by reading the database (AD-036). Arranging state by hand (a stale lease, a mid-call delete) is allowed | Every WRK criterion: happy path, every listed edge case, every failure path. Poll-until with a deadline, never fixed sleeps | `src/HikvisionReplicator.IntegrationTests/{Concern}Tests.cs` on `WorkerPostgresFixture` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Startup / wiring (options, adapter gating, hosted-service registration, meters) | integration | Every failing configuration fails startup naming the setting; every meter registered (L-037) | `StartupTests.cs`, `WorkerObservabilityTests.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Observables HTTP cannot distinguish — index shape, concurrency translation | integration — **contract class** (AD-040) | Each test names, in its doc comment, the observable HTTP cannot distinguish | `ReplicationQueueContractTests.cs`, `UserPersistenceContractTests.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| EF configuration, migration, DI lines, docs | none — build gate only (index shape covered above) | — | — | build gate only |

**Baseline (do not regress):** 364 unit · 286 integration, both green. Every task's count is a
**minimum**; no existing test may be deleted or weakened. The **only** sanctioned changes to existing
tests are the AD-044 amendments to REP-27/REP-28 (T2, T3) and the REP-34 replacement (T11), each
named in its task.

## Gate Check Commands

> Generated from codebase (`CLAUDE.md` § Gate commands, `.github/workflows/ci.yml`) — confirm before Execute.

| Gate Level | When to Use | Command |
| --- | --- | --- |
| Quick | After tasks with unit tests only — Docker-free | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` |
| Full | After any task with integration tests — needs Docker | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests && dotnet test src/HikvisionReplicator.IntegrationTests` |
| Build | After each phase, and for docs/config-only tasks | `dotnet build HikvisionReplicator.slnx --no-incremental` |

**L-007 (confirmed ×2):** a "no new warnings" claim comes from a `--no-incremental` build only. The
baseline is **17 warnings, 0 errors**. **Never bare `dotnet format`** — `dotnet format whitespace`.
**Lessons store:** `scripts/lessons.py list` mutates `.specs/LESSONS.md`/`lessons.json`; revert both
after any read, never commit them as a side effect.

---

## Execution Plan

### Phase 1: Domain and pure logic (Docker-free)

```
T1 → T2 → T3 → T4 → T5 → T6 → T7
```

### Phase 2: Persistence and the device port

```
T8 → T9 → T10
```

### Phase 3: Delivery

```
T11 → T12 → T13 → T14
```

### Phase 4: Failure handling

```
T15 → T16 → T17 → T18 → T19
```

### Phase 5: Observability, SLO, docs

```
T20 → T21 → T22
```

---

## Task Breakdown

### T1: `Replication` — rejection backoff (`NextAttemptAt`, `Retry(now, notBefore)`) ✅

**What**: Add `NextAttemptAt`; add `Retry(DateTime now, DateTime notBefore)` (`Failed → Pending`, sets `NextAttemptAt`); `Begin` clears it; the existing `Retry(now)` keeps REP-31 and leaves `NextAttemptAt` null.
**Where**: `Domain/Replication.cs`, `Tests/Domain/ReplicationTransitionTests.cs`
**Depends on**: None
**Reuses**: `MoveTo` guard
**Requirement**: WRK-16, WRK-19 (domain half)
**Tools**: MCP: NONE · Skill: NONE
**Done when**:
- [x] Retry with not-before → `Pending`, `NextAttemptAt == notBefore`, attempt count preserved
- [x] `Begin` clears `NextAttemptAt`; a new replication has `NextAttemptAt == null`
- [x] Retry from any non-`Failed` status refused, every field unchanged
- [x] Quick gate passes; ≥ 368 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(replication): schedule a rejected attempt's retry`

---

### T2: `Replication.Release` — back to the queue with no attempt counted ✅

**What**: `Release(string reason, DateTime now)`: `InProgress → Pending`, attempt count unchanged, `LastError = reason` (truncated, REP-33), `NextAttemptAt = null`. **Amend** the REP-27/REP-28 cases that assert `InProgress → Pending` is refused (AD-044), citing AD-044 in the test.
**Where**: `Domain/Replication.cs`, `Tests/Domain/ReplicationTransitionTests.cs`
**Depends on**: T1
**Requirement**: WRK-11, WRK-22, WRK-23 (domain half)
**Done when**:
- [x] Release from `InProgress` → `Pending`, attempts unchanged, reason stored and truncated at 1,000
- [x] Release from every other status refused, every field unchanged
- [x] Only the AD-044-named cases changed; diff shows no other test edited
- [x] Quick gate passes; ≥ 372 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(replication): release an attempt that never reached the device`

---

### T3: `Replication.SupersedeAfterAttempt`

**What**: `InProgress | Failed → Superseded`, attempt count and last error kept. `Supersede` stays `Pending`-only — the REP-12 refusal test for `Supersede` from `InProgress` is **kept unchanged**. Amend only REP-28 cases asserting `InProgress/Failed → Superseded` is impossible by any method (AD-044).
**Where**: `Domain/Replication.cs`, `Tests/Domain/ReplicationTransitionTests.cs`
**Depends on**: T2
**Requirement**: WRK-20, WRK-21 (domain half)
**Done when**:
- [ ] From `InProgress` and from `Failed` → `Superseded`, attempts and error kept
- [ ] From `Pending`, `Succeeded`, `Superseded` refused, fields unchanged
- [ ] `Supersede` from `InProgress` still refused (REP-12)
- [ ] Quick gate passes; ≥ 377 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(replication): supersede an attempt a newer intent overtook`

---

### T4: `RetrySchedule`

**What**: `TimeSpan DelayAfter(int attempts)` = `RetryBaseDelay × 2^(attempts − 1)` capped at `RetryMaxDelay`; `bool IsExhausted(int attempts)` = `attempts >= MaxAttempts`.
**Where**: `Infrastructure/Worker/RetrySchedule.cs`, `Tests/Infrastructure/Worker/RetryScheduleTests.cs`
**Depends on**: T3
**Requirement**: WRK-16, WRK-17
**Done when**:
- [ ] Defaults give 2, 4, 8, 16 s for attempts 1–4; the cap holds at a large attempt count without overflow
- [ ] `IsExhausted` false at 4, true at 5 with `MaxAttempts = 5`; respects a configured value
- [ ] Quick gate passes; ≥ 383 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(worker): compute the rejection backoff and the dead-letter threshold`

---

### T5: `DevicePause`

**What**: Consecutive-failure breaker per design: trips at `PauseAfterConsecutiveUnreachable`, probe at `ProbeInitialDelay × 2^failedProbes` capped at `ProbeMaxDelay`, any answer resets and reports `Resumed`.
**Where**: `Infrastructure/Worker/DevicePause.cs`, `Tests/Infrastructure/Worker/DevicePauseTests.cs`
**Depends on**: T4
**Requirement**: WRK-12, WRK-13, WRK-15
**Done when**:
- [ ] One `Unreachable` does not pause; two consecutive do, returning `Paused`; an answer between them resets the count
- [ ] While paused, `CanAttempt` false before `ProbeDueAt`, true at it; failed probes give 5, 10, 20, 40, 60, 60 s
- [ ] `Rejected` and `Delivered` both resume and return `Resumed` exactly once
- [ ] Quick gate passes; ≥ 391 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(worker): pause a reader after consecutive unreachable calls`

---

### T6: `ReplicationWorkerOptions` + validator + startup binding

**What**: Options class with the design's defaults, `IValidateOptions` validator, bound with `ValidateOnStart` in `Program.cs`.
**Where**: `Infrastructure/Worker/ReplicationWorkerOptions.cs`, `Program.cs`, `Tests/Infrastructure/Worker/ReplicationWorkerOptionsValidatorTests.cs`, `IntegrationTests/StartupTests.cs`
**Depends on**: T5
**Reuses**: `FaceImageOptions` + validator pattern
**Requirement**: WRK-30
**Done when**:
- [ ] Unit: each non-positive span, `MaxAttempts < 1`, `PauseAfterConsecutiveUnreachable < 1`, `Lease <= DeviceCallTimeout`, max-below-base for both backoffs fails with the setting's name; defaults pass
- [ ] Integration: one invalid setting aborts host startup with that name in the message
- [ ] Full gate passes; ≥ 401 unit · ≥ 287 integration

**Tests**: unit + integration · **Gate**: full · **Commit**: `feat(worker): validate the worker's settings at startup`

---

### T7: `OutcomeDecision`

**What**: Pure function: (outcome, attempt count, `newerPendingExists`, `userTombstoned`, operation, schedule, now) → one action: `Succeed` · `Release` · `RetryAt(t)` · `DeadLetter` · `SupersedeAfterAttempt` · `SupersedeWithoutCall`.
**Where**: `Infrastructure/Worker/OutcomeDecision.cs`, `Tests/Infrastructure/Worker/OutcomeDecisionTests.cs`
**Depends on**: T6
**Requirement**: WRK-11, WRK-16, WRK-17, WRK-20, WRK-21
**Done when**:
- [ ] Every row of the design's recorder rules has a test, including newer-pending overriding `Release` and `RetryAt` but **not** `Succeed`
- [ ] Tombstoned user with `Add`/`Update` → `SupersedeWithoutCall`; with `Remove` → proceeds
- [ ] Rejected at attempt 4 → `RetryAt`; at 5 → `DeadLetter`
- [ ] Quick gate passes; ≥ 412 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(worker): decide what an attempt's outcome does to its replication`

---

### T8: Schema — `NextAttemptAt`, row version, two partial indexes

**What**: Map `NextAttemptAt`, `Version` (`xmin`, `IsRowVersion()`), `IX_replications_device_in_progress` (`UNIQUE (DeviceId) WHERE Status='InProgress'`), `IX_replications_device_pending` (`(DeviceId, Lane, CreatedAt) WHERE Status='Pending'`); migration `AddReplicationWorker` shipped in the same task.
**Where**: `Infrastructure/ReplicationConfiguration.cs`, `Infrastructure/Migrations/*_AddReplicationWorker.cs`, `IntegrationTests/ReplicationQueueContractTests.cs`
**Depends on**: T7
**Requirement**: WRK-06, WRK-16 (persistence half)
**Done when**:
- [ ] Contract tests (doc comment names the blind spot): both indexes' definitions read from `pg_indexes`; a second `InProgress` row for one device is refused with `23505` on the named index; two `InProgress` rows on different devices are allowed
- [ ] Full gate passes; ≥ 412 unit · ≥ 290 integration

**Tests**: integration (contract) · **Gate**: full · **Commit**: `feat(replication): enforce one in-flight operation per device`

---

### T9: Translate a lost row-version race on the user and device write paths

**What**: `UserRepository`/`DeviceRepository` translate `DbUpdateConcurrencyException` whose entries are `Replication` to `ConflictError(DuplicatePendingWork)`; any other concurrency exception propagates.
**Where**: `Infrastructure/UserRepository.cs`, `Infrastructure/DeviceRepository.cs`, `Shared/IReplicationRepository.cs`, `IntegrationTests/UserPersistenceContractTests.cs`
**Depends on**: T8
**Requirement**: WRK-06 (race closure, AD-044)
**Done when**:
- [ ] Contract test: a pending row changed behind a tracked context makes the user save return the conflict, deterministically, with the literal message pinned once
- [ ] A concurrency exception on a non-replication entity is **not** translated
- [ ] Full gate passes; ≥ 293 integration

**Tests**: integration (contract) · **Gate**: full · **Commit**: `fix(replication): turn a claim racing a user write into a conflict`

---

### T10: `IDeviceClient` port + `FakeDeviceClient` + adapter gating

**What**: Port, outcome records, `DeviceEndpoint`/`DeviceEnrolment`; fake with the design's scripting surface and a per-device concurrency high-water mark; registered only when `DeviceClient:Adapter=Fake`, else startup fails. `TestWebApplicationFactory` and `appsettings.Development.json` set `Fake`.
**Where**: `Shared/IDeviceClient.cs`, `Shared/DeviceCallOutcomes.cs`, `Infrastructure/FakeDeviceClient.cs`, `Program.cs`, `appsettings.Development.json`, `IntegrationTests/TestWebApplicationFactory.cs`, `Tests/Infrastructure/FakeDeviceClientTests.cs`, `IntegrationTests/StartupTests.cs`
**Depends on**: T9
**Requirement**: WRK-01, WRK-02 (port half)
**Done when**:
- [ ] Unit: latency honoured and cancellable; `SetUnreachable` and `RejectWhen` produce their outcomes; call log ordered; high-water mark counts overlapping calls
- [ ] Integration: missing or unknown `DeviceClient:Adapter` fails startup naming the setting
- [ ] Full gate passes; ≥ 418 unit · ≥ 295 integration

**Tests**: unit + integration · **Gate**: full · **Commit**: `feat(worker): add the device port and a fake reader fleet`

---

### T11: Walking skeleton — supervisor, device loop, first delivery

**What**: `ReplicationWorker` (loop set synced to `RegisteredDeviceIdsSpec`), `DeviceLoop` (Live-then-Bulk claim via `NextEligibleReplicationSpec`, `Begin`, save, call, `Succeed`), recorder `Delivered` branch, hosted registration behind `ReplicationWorker:Enabled`. `TestWebApplicationFactory` sets `Enabled=false`; new `WorkerPostgresFixture` + `WorkerWebApplicationFactory` (own collection, worker on, ms intervals). **Replace REP-34's no-hosted-service assertion** with WRK-35's no-job-runner assertion (AD-043).
**Where**: `Infrastructure/Worker/{ReplicationWorker,DeviceLoop,ReplicationOutcomeRecorder}.cs`, `Domain/Specs/NextEligibleReplicationSpec.cs`, `Program.cs`, `IntegrationTests/{WorkerPostgresFixture,WorkerWebApplicationFactory,DeliveryTests}.cs`, `IntegrationTests/StartupTests.cs` (REP-34 test)
**Depends on**: T10
**Requirement**: WRK-01, WRK-02, WRK-03, WRK-05, WRK-31, WRK-32, WRK-35
**Done when**:
- [ ] Upsert with 3 devices → fake receives one `Add` per device carrying external ref, access code and face bytes; 3 rows `Succeeded` with injected-clock timestamps
- [ ] `Update` and `Remove` delivered with the right payloads (`Remove` = external ref only)
- [ ] With Live and Bulk both pending on a device, Live is called first; within a lane, oldest first
- [ ] Empty queue → no calls; no devices → worker runs, no calls
- [ ] All 286 pre-existing integration tests still pass with the worker disabled in their factory
- [ ] Full gate passes; ≥ 302 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): drain the queue to every device`

---

### T12: One in flight per device; devices independent

**What**: Device-index `23505` on claim → back off one poll interval; a slow device never delays another.
**Where**: `Infrastructure/Worker/DeviceLoop.cs`, `IntegrationTests/DeliveryTests.cs`
**Depends on**: T11
**Requirement**: WRK-06, WRK-07, WRK-08
**Done when**:
- [ ] Two worker hosts on one database, one device, queued work → fake high-water mark for the device is 1 and every row is delivered exactly once
- [ ] One device at 2 s latency, another at 10 ms → the fast device drains its backlog before the slow one finishes one call
- [ ] An `InProgress` row and a newer `Pending` for one pair → newer delivered only after the older is terminal
- [ ] Full gate passes; ≥ 305 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): never run two operations against one reader`

---

### T13: Devices joining, leaving and changing while the worker runs

**What**: Loop start/stop within a poll interval; device reloaded right before each call (no call starts after a delete is observed); new address/credentials used on the next call.
**Where**: `Infrastructure/Worker/{ReplicationWorker,DeviceLoop}.cs`, `IntegrationTests/DeviceChurnTests.cs`
**Depends on**: T12
**Requirement**: WRK-10, WRK-25, WRK-33
**Done when**:
- [ ] Device registered after start → its work delivered with no restart
- [ ] Device deleted → no further call to it after the delete is observed (design's WRK-25 interpretation)
- [ ] Device's address updated → next call's `DeviceEndpoint` carries the new address and decrypted password
- [ ] Full gate passes; ≥ 308 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): follow the device catalogue without a restart`

---

### T14: Backfill expansion

**What**: Supervisor expands each pending backfill intent (`PendingBackfillDeviceIdsSpec`, `ExpandBackfillAsync`, one save per device) every tick.
**Where**: `Infrastructure/Worker/ReplicationWorker.cs`, `Domain/Specs/PendingBackfillDeviceIdsSpec.cs`, `IntegrationTests/BackfillDeliveryTests.cs`
**Depends on**: T13
**Requirement**: WRK-04, WRK-34
**Done when**:
- [ ] Users exist, device registered → every active user delivered as `Bulk` `Add`; tombstoned users skipped; intent `Expanded`
- [ ] Two worker hosts → the intent is expanded once, each spectator delivered once
- [ ] User created during registration → delivered exactly once
- [ ] Full gate passes; ≥ 311 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): expand a new reader's backfill`

---

### T15: Offline reader — release, pause, probe, resume

**What**: `Unreachable` and timeout → `Release`; `DevicePause` wired into the loop; probe with the head item.
**Where**: `Infrastructure/Worker/{DeviceLoop,ReplicationOutcomeRecorder}.cs`, `IntegrationTests/OfflineReaderTests.cs`
**Depends on**: T14
**Requirement**: WRK-11, WRK-12, WRK-13, WRK-14, WRK-15
**Done when**:
- [ ] A call exceeding `DeviceCallTimeout` → row `Pending`, attempt count 0
- [ ] Unreachable through several probe cycles → zero `Failed` rows; restored → every row `Succeeded`, attempt 0, in WRK-05 order
- [ ] While paused, calls to that device are spaced by the probe schedule (fake call log), never hot-looped
- [ ] A `Rejected` answer does not pause
- [ ] Full gate passes; ≥ 315 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): ride out an offline reader without losing work`

---

### T16: Rejected items — backoff and dead letter

**What**: Recorder `RetryAt`/`DeadLetter` branches; unexpected adapter exception → treated as `Rejected`.
**Where**: `Infrastructure/Worker/{DeviceLoop,ReplicationOutcomeRecorder}.cs`, `IntegrationTests/RejectionTests.cs`
**Depends on**: T15
**Requirement**: WRK-16, WRK-17, WRK-18, WRK-19
**Done when**:
- [ ] One rejection → `Pending`, attempt 1, error stored, not called again before the delay
- [ ] Always rejected → after `MaxAttempts` the row stays `Failed` and is never called again; other users on that device still delivered
- [ ] Newer intent after a dead letter → delivered; dead letter unchanged
- [ ] Superseded during backoff → newer delivered without waiting
- [ ] Adapter throws → counted as a rejection, ends as a dead letter, never pauses the device
- [ ] Full gate passes; ≥ 320 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): retry a rejected record, then set it aside`

---

### T17: Never replay stale intent

**What**: Recorder checks `PendingReplicationForPairSpec` before re-queueing; `23505` on the pending index during the outcome save → reload and `SupersedeAfterAttempt`; tombstoned user → no call.
**Where**: `Infrastructure/Worker/{DeviceLoop,ReplicationOutcomeRecorder}.cs`, `Domain/Specs/PendingReplicationForPairSpec.cs`, `IntegrationTests/StaleIntentTests.cs`
**Depends on**: T16
**Requirement**: WRK-20, WRK-21
**Done when**:
- [ ] Upsert during an unreachable attempt → old row `Superseded` with attempt count kept, new row delivered, never two `Pending`
- [ ] Same for a rejected attempt
- [ ] Remove during an in-flight `Add` whose face is gone at load → no `Add` call, row `Superseded`, `Remove` delivered
- [ ] Full gate passes; ≥ 323 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): drop an attempt that a newer intent overtook`

---

### T18: Crash recovery and shutdown

**What**: Lease sweep (`ExpiredLeasesSpec`) in the supervisor; shutdown cancels calls and releases.
**Where**: `Infrastructure/Worker/{ReplicationWorker,DeviceLoop}.cs`, `Domain/Specs/ExpiredLeasesSpec.cs`, `IntegrationTests/WorkerRecoveryTests.cs`
**Depends on**: T17
**Requirement**: WRK-22, WRK-23
**Done when**:
- [ ] An `InProgress` row older than the lease (arranged by hand) → `Pending`, attempts unchanged, then delivered; one younger than the lease untouched
- [ ] Expired row with a newer pending intent → `Superseded`
- [ ] Host stopped mid-call → row `Pending`, attempt 0
- [ ] Full gate passes; ≥ 327 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): recover work a crashed worker left in flight`

---

### T19: Device deleted mid-call

**What**: A save that affects no row → discard, log, continue; other loops unaffected.
**Where**: `Infrastructure/Worker/ReplicationOutcomeRecorder.cs`, `IntegrationTests/DeviceChurnTests.cs`
**Depends on**: T18
**Requirement**: WRK-24
**Done when**:
- [ ] Device deleted while its call is in flight → no exception escapes, the worker keeps delivering to other devices
- [ ] Full gate passes; ≥ 328 integration

**Tests**: integration · **Gate**: full · **Commit**: `fix(worker): discard the outcome of a call to a deleted reader`

---

### T20: Worker instruments

**What**: `replication.delivery.duration`, `replication.attempts`, `replication.dead_lettered`, `replication.device.pause` on the existing meter; dead-letter and pause logs.
**Where**: `Infrastructure/ReplicationMetrics.cs`, `Infrastructure/Worker/*`, `IntegrationTests/WorkerObservabilityTests.cs`
**Depends on**: T19
**Requirement**: WRK-26, WRK-27, WRK-28, WRK-29
**Done when**:
- [ ] Each instrument recorded with the right tags in its scenario, observed through the registered provider (L-037), filtered to this test's own traffic
- [ ] Delivery duration equals success time − creation time (injected clock)
- [ ] Dead-letter log carries replication id, user id, device id, attempt count, last error
- [ ] Full gate passes; ≥ 333 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): instrument delivery, rejection and pauses`

---

### T21: Live-sync SLO under a running backfill

**What**: WRK-09's test.
**Where**: `IntegrationTests/LiveSyncSloTests.cs`
**Depends on**: T20
**Requirement**: WRK-09
**Done when**:
- [ ] 20 fake devices at 50 ms, ≥ 100 `Bulk` pending each, then an upsert → `Succeeded` on all 20 within 5 s, **and** `Bulk` rows still `Pending` on every device at that moment
- [ ] Passes 5 consecutive local runs
- [ ] Full gate passes; ≥ 334 integration

**Tests**: integration · **Gate**: full · **Commit**: `test(worker): prove a live upsert overtakes a running backfill`

---

### T22: Docs

**What**: `docs/test-patterns.md` class table (new worker classes, fixture rule); `replication-queue/spec.md` amendment notes on REP-27, REP-28, REP-34; `ROADMAP.md` § Known Gaps — core-capability row narrowed to "no real adapter".
**Where**: `docs/test-patterns.md`, `.specs/features/replication-queue/spec.md`, `.specs/ROADMAP.md`
**Depends on**: T21
**Requirement**: — (AD-044, AD-043 bookkeeping)
**Done when**:
- [ ] Build gate passes (`--no-incremental`), warnings ≤ 17
**Tests**: none · **Gate**: build · **Commit**: `docs: record the worker's test classes and the amended queue criteria`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5

Phase 1:  T1 → T2 → T3 → T4 → T5 → T6 → T7
Phase 2:  T8 → T9 → T10
Phase 3:  T11 → T12 → T13 → T14
Phase 4:  T15 → T16 → T17 → T18 → T19
Phase 5:  T20 → T21 → T22
```

Batches (~7 tasks, whole phases): **A** = Phase 1 (7) · **B** = Phases 2–3 (7) · **C** = Phases 4–5 (8).

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T1–T3 | one transition each on one aggregate | ✅ |
| T4, T5, T7 | one pure component each | ✅ |
| T6 | options + validator + binding | ⚠️ cohesive (one setting group) |
| T8 | one mapping + its migration (migration must ship with mapping — replication-queue finding) | ⚠️ cohesive |
| T9 | one translation in two repositories | ⚠️ cohesive |
| T10 | port + its only adapter + gating | ⚠️ cohesive — the port is untestable without the adapter |
| T11 | walking skeleton: supervisor + loop + recorder `Delivered` + harness | ⚠️ the smallest slice that delivers anything; split would leave untestable code (merge-backward rule) |
| T12–T21 | one behaviour each | ✅ |
| T22 | docs | ✅ |

## Diagram-Definition Cross-Check

| Task | Depends On (body) | Diagram | Status |
| --- | --- | --- | --- |
| T1 | None | start of Phase 1 | ✅ |
| T2–T7 | previous task | chain | ✅ |
| T8 | T7 | Phase 1 → Phase 2 | ✅ |
| T9, T10 | previous task | chain | ✅ |
| T11 | T10 | Phase 2 → Phase 3 | ✅ |
| T12–T14 | previous task | chain | ✅ |
| T15 | T14 | Phase 3 → Phase 4 | ✅ |
| T16–T19 | previous task | chain | ✅ |
| T20 | T19 | Phase 4 → Phase 5 | ✅ |
| T21, T22 | previous task | chain | ✅ |

## Test Co-location Validation

| Task | Layer | Matrix requires | Task says | Status |
| --- | --- | --- | --- | --- |
| T1–T3 | Domain aggregate | unit | unit | ✅ |
| T4, T5, T7 | Pure worker logic | unit | unit | ✅ |
| T6 | Pure logic + startup | unit + integration | unit + integration | ✅ |
| T8 | Mapping/migration + index shape | contract | integration (contract) | ✅ |
| T9 | Translation below HTTP | contract | integration (contract) | ✅ |
| T10 | Pure fake + startup gating | unit + integration | unit + integration | ✅ |
| T11–T19 | Worker behaviour | integration | integration | ✅ |
| T20 | Meters | integration | integration | ✅ |
| T21 | Worker behaviour | integration | integration | ✅ |
| T22 | Docs | none | none | ✅ |

**Requirement coverage:** WRK-01…WRK-35 all mapped (WRK-01/02 T10–T11 · 03 T11 · 04 T14 · 05 T11 · 06 T8, T9, T12 · 07–08 T12 · 09 T21 · 10 T13 · 11 T2, T7, T15 · 12–15 T5, T15 · 16 T1, T4, T7, T16 · 17 T4, T7, T16 · 18–19 T16 · 20–21 T3, T7, T17 · 22–23 T2, T18 · 24 T19 · 25 T13 · 26–29 T20 · 30 T6 · 31–32 T11 · 33 T13 · 34 T14 · 35 T11). 0 unmapped.

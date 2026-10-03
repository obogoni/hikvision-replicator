# replication-queue Validation

**Date**: 2026-10-03
**Spec**: `.specs/features/replication-queue/spec.md` (47 criteria, REP-01…REP-47)
**Diff range**: `main..HEAD` on `feat/replication-queue` — 35 commits, `f99f9e0`…`d993bf1`
(26 code commits, one per task: T1–T25 + T6b; 9 spec/docs commits)
**Verifier**: independent sub-agent (author ≠ verifier) — four batch workers wrote this code;
coverage below was re-derived from `spec.md` and the diff, evidence-or-zero.

**Verdict: PASS ✅**

---

## Task Completion

| Phase | Tasks | Status | Commits |
| --- | --- | --- | --- |
| 1 — Domain foundation | T1–T6 | ✅ Done | `3f44aec` `b39532a` `862c3ce` `cb4228f` `b1dd1c7` `3fc2ddc` |
| 2 — Persistence | T6b, T7–T12 | ✅ Done | `148cfeb` `90fb5ec` `2e7103a` `79f5284` `5da4900` `f6b00ee` `4454e21` |
| 3 — Dispatch plumbing | T13–T15 | ✅ Done | `a506c3e` `6b5cce2` `0cc9314` |
| 4 — Fan-out rules | T16–T21 | ✅ Done | `21ca2d3` `72da358` `9c04c12` `3a65ba6` `3d6920b` `a3bdde2` |
| 5 — Capacity + observability | T22–T25 | ✅ Done | `2aa8d5d` `eb79bef` `7b9f815` `eb633d0` |

26 code commits for 26 tasks — no task was batched into another's commit.

### Deviations re-checked against the tree

Every deviation `tasks.md` records was verified to have shipped what it claims, and none left a
criterion unimplemented.

| Deviation (from `tasks.md`) | Verified | Evidence |
| --- | --- | --- |
| T1: `abstract class AggregateRoot` instead of interface default members | ✅ | `Shared/IAggregateRoot.cs:19-28`; `User`/`Device` both `: AggregateRoot, IAggregateRoot` |
| T1: aggregate-contract tests in `Tests/Domain/`, not `Tests/Shared/` | ✅ | `Tests/Domain/AggregateRootTests.cs` (5 tests) |
| T1: `BackfillStatus` ships with T4, not T2 | ✅ | `cb4228f` adds `Domain/BackfillStatus.cs` alongside `BackfillIntent.cs` |
| Phase 2: migration split, `AddReplicationQueue`→T7, `AddBackfillIntents`→T8; T9 registers both DbSets + proves schema | ✅ | two migrations exist; `79f5284` touches only `AppDbContext.cs` (+4) and two test files |
| T15 creates `ReplicationFanOut` (empty bodies, registered + startup-guarded) | ✅ | `0cc9314` adds `ReplicationFanOut.cs` (+41) and the `Program.cs` unhandled-event guard |
| T15 isolates T14's `HostWith` harness — harness only, no assertion changed | ✅ | `DomainEventDispatchTests.cs` assertions unchanged across `0cc9314` |
| T17 injects `IReplicationRepository` into `UserRepository` | ✅ | `Infrastructure/UserRepository.cs:28` ctor; `ConflictMessage` delegates to `TranslateIfDuplicatePending` |
| T17: 8 construction sites → `RepositoryOver(context)` factory, signature only | ✅ | `UserPersistenceContractTests.cs` −8/+82, all deletions are `new UserRepository(context)` |
| T18 ships no production code — proof only | ✅ | `9c04c12` touches `RemoveUserTests.cs` only (+145) |
| T20 reads intents via `WithSpecification` on the context, no `IRepository<BackfillIntent>` | ✅ | `ReplicationFanOut.cs:140-142` with `PendingBackfillIntentForDeviceSpec` (AD-006 honoured) |
| T22 adds `Features/Devices/FleetAdmission.cs` beyond its `Where` | ✅ | `2aa8d5d`; shared verbatim by `RegisterDeviceService` and `UpdateDeviceService`, so REP-23's "same terms" is one sentence, not two |
| T24 adds `Domain/Specs/ReaderCeilingsSpec.cs` beyond its `Where` | ✅ | `7b9f815`; justified — `FaceCapacity` is a value-converted type, so `capacity < roster` cannot translate to SQL |
| T25 also instruments the Bulk lane in `ExpandBackfillAsync` | ✅ | `ReplicationFanOut.cs:171`; proved by `ReplicationQueueContractTests.cs:836` with its own blind-spot sentence |
| New build warning `CA1711` on `IDomainEventHandler`, kept unsuppressed | ✅ | reproduced on a `--no-incremental` build (L-007); 17 warnings, 0 errors |

No `// SPEC_DEVIATION` marker exists anywhere in `src/`, `.specs/` or `docs/`.

---

## Spec-Anchored Acceptance Criteria

All paths relative to `src/`. 47/47 traced to a `file:line` and an assertion whose value matches
the spec-defined outcome.

### P1 — Queue work whenever a spectator changes

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-01 | N rows, `Add`/`Live`/`Pending`, FK to the user and to one distinct device | `…IntegrationTests/UpsertUserTests.Registration.cs:345` — `Assert.Equal(3, queued.Count)` + `Assert.Equal(Add/Live/Pending, …)`; `:375` — `Assert.All(queued, w => Assert.Equal(spectator.Id, w.UserId))`, `:376` — `Assert.Equal(readers.Order(), queued.Select(w => w.DeviceId).Order())` | ✅ PASS |
| REP-02 | N rows, `Update`/`Live`/`Pending` | `…/UpsertUserTests.Amendment.cs:285-294` — `Assert.Equal(2, queued.Count)`; `Assert.Equal(ReplicationOperation.Update, work.Operation)` + `Live` + `Pending` | ✅ PASS |
| REP-03 | N rows, `Remove`/`Live`/`Pending` | `…/RemoveUserTests.cs:201-210` — `Assert.Equal(2, queued.Count)`; `Assert.Equal(ReplicationOperation.Remove, work.Operation)` + `Live` + `Pending` | ✅ PASS |
| REP-04 | no replication at all | `…/UpsertUserTests.Amendment.cs:306` — `Assert.Empty(await QueuedWorkAsync())`; `Tests/Domain/UserEventTests.cs:86` — `Assert.Empty(user.DomainEvents)` after a no-op upsert | ✅ PASS |
| REP-05 | user write succeeds, no replication | `…/UpsertUserTests.Registration.cs:384-385` — `Assert.Equal(Created, …)` + `Assert.Empty(…)`; also `Amendment.cs:316-317`, `RemoveUserTests.cs:241-242` | ✅ PASS |
| REP-06 | resurrection queues `Add`, not `Update` | `…/UpsertUserTests.Resurrection.cs:219-221` — `Assert.Equal(ReplicationOperation.Add, queued.Operation)` + `Live` + `Pending` | ✅ PASS |
| REP-07 | user write not committed, caller gets no success | `…/DomainEventDispatchTests.cs:251-252` — `Assert.Equal(InternalServerError, …)` + `Assert.Equal(0, await CountUsersAsync())`; `:273-275` — failed save commits nothing a handler staged; `…/UpsertUserTests.Registration.cs:418-423` — the converse direction | ✅ PASS (see Observation 1) |

### P1 — Never accumulate duplicate pending work

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-08 | existing row → `Superseded`, new intent inserted `Pending` | `…/UpsertUserTests.Amendment.cs:365-373` — `Assert.Equal(3, queued.Count)`, `Assert.Equal(2, queued.Count(w => w.Status == Superseded))`, single `Pending` whose `Operation == Update` and whose `CreatedAt` is the newest | ✅ PASS |
| REP-09 | at most one `Pending` per pair, enforced by a **partial unique index** | `…/ReplicationQueueContractTests.cs:128-137` — `Assert.Contains("UNIQUE", definition)`, `Assert.Contains("\"UserId\", \"DeviceId\"", …)`, filter half-asserted on `"Status"` and `'Pending'` read from `pg_indexes` | ✅ PASS |
| REP-10 | operation, lane, attempt count, last error unchanged | `…/UpsertUserTests.Amendment.cs:417-421` — all four: `Superseded` status, `Operation == Add`, `Lane == Live`, `AttemptCount == 1`, `LastError == "the reader refused the enrolment"` | ✅ PASS (full conjunction) |
| REP-11 | `Remove` supersedes the pending `Add` **and** is itself `Pending` | `…/RemoveUserTests.cs:314-322` — `Assert.Equal(Superseded, enrolment.Status)` + `Assert.Equal(Remove, removal.Operation)` + `Assert.Equal(Pending, removal.Status)` | ✅ PASS |
| REP-12 | `InProgress` not superseded; new intent inserted beside it | `…/UpsertUserTests.Amendment.cs:449-455` — `Assert.Equal(InProgress, inFlight.Status)`, `Assert.Equal(Pending, alongside.Status)`, `Assert.Equal(Update, alongside.Operation)`; `…/RemoveUserTests.cs:381-387` | ✅ PASS |
| REP-13 | one `Pending` per pair, one intent in full, no `500` | `…/UpsertUserTests.Amendment.cs:500-514` — `Assert.DoesNotContain(responses, r => r.StatusCode == InternalServerError)`, `Assert.Single(queued, Pending && DeviceId)`, `Operation == Update`, `Lane == Live`; message pinned deterministically at `…/ReplicationQueueContractTests.cs:337-341` — `Assert.Equal("Replication work for this user and device is already queued.", translated.AsT1.Message)` | ✅ PASS (see Observation 4) |

### P1 — Record a new device's backfill in one write

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-14 | exactly one `Pending` intent, same transaction, no per-user row | `…/RegisterDeviceTests.cs:357-358` — `Assert.Single(await BackfillDebtsAsync())` + `Assert.Equal(0, await CountQueuedWorkAsync())` with 50 spectators; `:384-385` — `Assert.NotEqual(0, debt.DeviceId)` + `Assert.Equal(registered, debt.DeviceId)` (the FK could only be fixed up inside the device's own save) | ✅ PASS |
| REP-15 | intent persisted even with no users | `…/RegisterDeviceTests.cs:370-371` — `Assert.Equal(BackfillStatus.Pending, debt.Status)` + `Assert.Null(debt.ExpandedAt)` | ✅ PASS |
| REP-16 | intent and every replication removed with the device | `…/RemoveDeviceTests.cs:138` — `Assert.DoesNotContain(…, w => w.DeviceId == removed)`; `:148-149` — single surviving debt belongs to the kept reader; schema rule at `…/ReplicationQueueContractTests.cs:225-232`, `:246-252` | ✅ PASS |
| REP-17 | one row per **active** user, `Add`/`Bulk`/`Pending` | `…/ReplicationQueueContractTests.cs:582-599` — `Assert.Equal(5, staged)`, `Assert.Equal(5, queued.Count)`, `Add` + `Bulk` + `Pending` + `deviceId` on every row | ✅ PASS |
| REP-18 | tombstoned users skipped | `…/ReplicationQueueContractTests.cs:621-625` — `Assert.Equal(5, staged)` with 5 active + 2 tombstoned, `Assert.DoesNotContain(queued, w => w.UserId == refunded)` (both) | ✅ PASS |
| REP-19 | existing `Pending` left untouched, no `Bulk` duplicate | `…/ReplicationQueueContractTests.cs:652-657` — `Assert.Equal(2, staged)` of 3, `Assert.Equal(3, queued.Count)`, `Assert.Single(queued, w => w.Lane == Live)`, 3 distinct `UserId` | ✅ PASS |
| REP-20 | intent reaches terminal `Expanded`; second invocation persists nothing | `…/ReplicationQueueContractTests.cs:679-680` — `Assert.Equal(BackfillStatus.Expanded, debt.Status)` + `Assert.Equal(Now, debt.ExpandedAt)`; `:703-704` — `Assert.Equal(0, staged)` + row ids identical before and after | ✅ PASS |

### P1 — Refuse a reader that can never hold the fleet

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-21 | conflict naming both capacity and active count | `…/RegisterDeviceTests.cs:439-444` — `Assert.Equal(Conflict, …)` + `Assert.Equal("This device holds 5 faces, but 10 users are active.", problem.detail)` (literal, not the production constant) | ✅ PASS |
| REP-22 | capacity ≥ active count registers unchanged | `…/RegisterDeviceTests.cs:473-474` (capacity == roster == 10 → `Created`), `:484-485` (11 > 10 → `Created`) | ✅ PASS |
| REP-23 | capacity update below the roster refused on the same terms | `…/UpdateDeviceTests.cs:264-269` — `Assert.Equal(Conflict, …)` + `Assert.Equal("This device holds 3 faces, but 7 users are active.", …)` (different numbers, so one format cannot satisfy both by coincidence); `:283-294` — nothing persisted | ✅ PASS |
| REP-24 | user accepted (`2xx`) **and** warning log with device/capacity/count **and** REP-37 counter | `…/ReplicationObservabilityTests.cs:140` — `Assert.Equal(Created, response.StatusCode)`; `:157-161` — `Assert.Contains($"Device {reader} holds 2 faces, but 3 users are now active.", warning)`; `:328-335` — `Assert.Single(capacity measurements)` + `Assert.Equal(1, exceeded.Value)` + `Assert.Equal(reader, exceeded.Tags[DeviceTag])` | ✅ PASS (full conjunction) |
| REP-25 | no capacity-exceeded signal | `…/ReplicationObservabilityTests.cs:205` — `Assert.Empty(CapacityWarnings())` with roster == capacity == 5; `:175` — silent at 2/2 | ✅ PASS (see Observation 2) |

### P1 — A queue that models execution without performing it

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-26 | status `Pending`, attempts `0`, last error unset | `Tests/Domain/ReplicationCreateTests.cs:20` `Assert.Equal(Pending, …Status)`; `:26` `Assert.Equal(0, …AttemptCount)`; `:32` `Assert.Null(…LastError)` | ✅ PASS |
| REP-27 | exactly the five legal transitions | `Tests/Domain/ReplicationTransitionTests.cs:57-115` — five facts, each `Assert.True(result.IsT0)` + the exact target status + `UpdatedAt` from the supplied clock | ✅ PASS |
| REP-28 | refuse with a validation error, every field unchanged | `…/ReplicationTransitionTests.cs:132-135` (and 150-153, 168-171, 186-189, 204-207) — `Assert.True(result.IsT1)`, `Field == "status"`, exact refusal message, `Assert.Equal(before, Capture(work))` over status/attempts/error/updatedAt | ✅ PASS |
| REP-29 | `Succeeded` and `Superseded` are terminal | same five theories, each carrying `[InlineData(Succeeded)]` and `[InlineData(Superseded)]` — `…/ReplicationTransitionTests.cs:120-208` | ✅ PASS |
| REP-30 | attempts +1 exactly, last error stored | `…/ReplicationTransitionTests.cs:219-220` — `Assert.Equal(1, work.AttemptCount)` + `Assert.Equal("device said no", work.LastError)`; `:232-233` — second failure gives exactly `2` | ✅ PASS |
| REP-31 | attempt count preserved on retry | `…/ReplicationTransitionTests.cs:245` — `Assert.Equal(1, work.AttemptCount)` after `Retry` | ✅ PASS |
| REP-32 | timestamp from a passed-in clock | `…/ReplicationCreateTests.cs:69-70` — `Assert.Equal(QueuedOn, CreatedAt/UpdatedAt)`; every transition fact asserts `UpdatedAt` equals its own distinct constant (`StartedOn`/`SettledOn`/`RefusedOn`) | ✅ PASS |
| REP-33 | truncated to 1,000 chars, transition still succeeds | `…/ReplicationTransitionTests.cs:258-262` — `Assert.True(result.IsT0)`, `Assert.Equal(Failed, Status)`, `Assert.Equal(1000, LastError.Length)`, `Assert.Equal(complaint[..1000], LastError)`; `:273` — a 1,000-char string is stored whole | ✅ PASS |
| REP-34 | no `IHostedService`/`BackgroundService`/scheduler/job-runner reference; all ACs directly invocable | `…/StartupTests.cs:225-240` — `Assert.DoesNotContain` over assembly types for both base types **and** over `GetServices<IHostedService>()` filtered to this assembly; `:250-263` — both `GetReferencedAssemblies()` and the shipped `*.dll` set swept against the job-runner list | ✅ PASS |

### P2 — See the queue filling

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-35 | counter tagged by operation and lane | `…/ReplicationObservabilityTests.cs:269-271` — `Assert.Equal(1, enqueued.Value)`, `Tags[operation] == "Add"`, `Tags[lane] == "Live"`; `:287-291` — `["Add","Update"]`; Bulk lane at `…/ReplicationQueueContractTests.cs:849-850` — `"Add"`/`"Bulk"` | ✅ PASS |
| REP-36 | supersession counter increments | `…/ReplicationObservabilityTests.cs:302-303` — `Assert.Single(…)` + `Assert.Equal(1, superseded.Value)`; `:313` — `Assert.Empty(…)` when nothing was outstanding | ✅ PASS |
| REP-37 | counter tagged by device | `…/ReplicationObservabilityTests.cs:331-336` — `Assert.Equal(1, exceeded.Value)`, `Assert.Equal(reader, (int)exceeded.Tags[DeviceTag])`, no count-shaped tag | ✅ PASS |
| REP-38 | fan-out size recorded | `…/ReplicationObservabilityTests.cs:351-352` — `Assert.Single(…)` + `Assert.Equal(2, fanOut.Value)` with two readers | ✅ PASS |
| REP-39 | every meter registered via `AddMeter` on the provider | `…/ReplicationObservabilityTests.cs:412-417` — boots with an OTLP endpoint configured + an in-memory exporter on the real `MeterProvider`, `ForceFlush()`, `Assert.Contains(exported, m => m.Name == "replication.enqueued")` | ✅ PASS |

### Edge cases

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| REP-40 | pending rows removed with the device; delete not refused | `…/RemoveDeviceTests.cs:128` — `Assert.Equal(NoContent, …)` with 4 queued rows + 2 debts arranged; `:160-169` — exactly the other reader's 2 `Pending` rows survive | ✅ PASS |
| REP-41 | `Remove` queued even with nothing ever replicated | `…/RemoveUserTests.cs:360-368` — `Assert.DoesNotContain(…Succeeded)`, failed row keeps `Failed` + its `LastError`, removal is `Remove`/`Pending` | ✅ PASS |
| REP-42 | second `DELETE` enqueues nothing | `…/RemoveUserTests.cs:226-231` — `Assert.Equal(NoContent, second.StatusCode)` + row ids identical before and after; `Tests/Domain/UserEventTests.cs:148` | ✅ PASS |
| REP-43 | second registration at the address rejected, no intent left behind | `…/RegisterDeviceTests.cs:413-421` — `Assert.Equal(Conflict, …)`, `Assert.Equal(1, await CountDevicesAsync())`, `Assert.Single(debts)` whose `DeviceId` is the accepted reader | ✅ PASS |
| REP-44 | deleted device: persist nothing, do not throw | `…/ReplicationQueueContractTests.cs:729-730` — `Assert.Equal(0, staged)` + `Assert.Empty(await QueuedWorkAsync())`, no `Assert.Throws` wrapper (a throw fails the test) | ✅ PASS |
| REP-45 | concurrent expansion: at most one persists, no duplicate `Pending` | `…/ReplicationQueueContractTests.cs:773-775` — `Assert.Equal(3, queued.Count)`, 3 distinct `UserId`, all `Pending` | ✅ PASS |
| REP-46 | queued by exactly one path, never twice | `…/ReplicationQueueContractTests.cs:655-657` — 3 rows for 3 spectators, exactly one on the `Live` lane, 3 distinct `UserId` | ✅ PASS (see Observation 3) |
| REP-47 | zero active users: any capacity registers | `…/RegisterDeviceTests.cs:494-495` — capacity 1 on an empty registry → `Created`, 1 device | ✅ PASS |

**Status**: ✅ 47/47 covered, every asserted value matches the spec-defined outcome. No criterion
with a precise spec outcome is backed by a vague assertion; no criterion lacks a `file:line`.

### Observations (evidence-strength notes, not gaps)

1. **REP-07, the "replications fail" direction.** The committed proof makes the *handler* throw
   (`DomainEventDispatchTests.cs:242`) or the *user* half fail (`UpsertUserTests.Registration.cs:404`).
   No test makes the replication INSERT itself fail and then asserts the user row is absent. The
   property is structural — one `SaveChangesAsync`, one transaction — and mutation M17 (fan-out
   committing in a transaction of its own) was killed, but by the capacity-signal tests, not by a
   REP-07-named assertion. Recorded, not scored as a gap.
2. **REP-25 conjunction.** Silence is asserted on the warning log only; the counter's silence is
   covered transitively (mutation M16, which fires the counter for every reader regardless of
   ceiling, was killed by `Reader_the_roster_outgrew_is_counted_against_that_reader`).
3. **REP-46 arrangement.** Proved by arranging a spectator who already holds live work when the
   expansion runs, rather than by genuine simultaneity — which is the right call under AD-036's
   "a scheduling-dependent guard is not a guard".
4. **REP-13 "one intent in full".** All four racers send `Update`, so the "not a mixture" clause is
   carried structurally (a row is one INSERT) rather than discriminated by the assertion.

---

## Discrimination Sensor

**Depth: P0-full** (17 behaviour-level mutations — this feature is the product's core capability).
Each mutation was applied to a backed-up copy of the file, the suite run, and the file restored
byte-for-byte; `git status` was re-checked clean after every one. The real tree was never left
mutated.

| # | File:line | Mutation | Killed? | Killed by |
| --- | --- | --- | --- | --- |
| M1 | `Domain/User.cs:122` | Raise `UserChanged` outside the `changed` branch | ✅ Killed | 1 unit (`An_upsert_that_changes_nothing_announces_nothing`) + 2 integration (`Re_sending_an_identical_representation_queues_nothing`, `A_write_that_changes_nothing_reaches_no_handler`) |
| M2 | `Infrastructure/AppDbContext.cs:70-78` | Dispatch **after** `base.SaveChangesAsync` | ✅ Killed | 35 integration tests |
| M3 | `Infrastructure/AppDbContext.cs:78-81` | Clear domain events **before** the save | ✅ Killed | `A_failed_write_leaves_its_aggregate_still_carrying_the_events_it_raised` |
| M4 | `Migrations/20261003154546_AddReplicationQueue.cs:39,45` | Invert the two FK delete rules (Device↔User) | ✅ Killed | `Queued_work_follows_the_reader_it_was_owed_to_and_never_the_spectator` + 4 `RemoveDeviceTests` |
| M5 | `Migrations/20261003154546_AddReplicationQueue.cs:63` | Drop `WHERE "Status" = 'Pending'` — index unconditionally unique | ✅ Killed | 12 integration tests incl. `Outstanding_work_is_unique_per_spectator_and_reader_only_while_it_is_pending` |
| M6 | `Domain/Specs/ActiveUserIdsSpec.cs:17` | Expansion includes tombstoned users (REP-18) | ✅ Killed | `Expanding_a_debt_skips_tombstoned_spectators` |
| M7 | `Infrastructure/ReplicationFanOut.cs:158` | Expansion stops skipping pairs that hold a `Pending` row (REP-19) | ✅ Killed | `A_spectator_already_owed_work_by_the_reader_keeps_their_live_intent` |
| M8 | `Features/Devices/FleetAdmission.cs:37` | Admission boundary `<` → `<=` (REP-22 / REP-47) | ✅ Killed | `Reader_sized_exactly_to_the_active_roster_is_registered`, `Lowering_capacity_to_exactly_the_active_roster_is_accepted`, `Tombstoned_spectators_do_not_count_against_a_reader_capacity` |
| M9 | `Domain/Specs/PendingReplicationsForUserSpec.cs:22` + `Domain/Replication.cs:127` | Supersede `InProgress` rows as well as `Pending` (REP-12) | ✅ Killed | 1 unit (`Only_pending_work_can_be_replaced_by_a_newer_intent(InProgress)`) + 2 integration |
| M10 | `Infrastructure/ReplicationFanOut.cs:262` | Capacity-signal boundary `<` → `<=` (REP-25) | ✅ Killed | 5 `ReplicationObservabilityTests` incl. `No_reader_is_named_while_every_one_of_them_can_hold_the_roster` |
| M11 | `Program.cs:124` | Remove `.AddMeter(ReplicationMetrics.MeterName)` (REP-39, L-037) | ✅ Killed | `Configured_deployment_collects_the_queue_metrics` — and **only** that test, exactly the L-037 shape |
| M12 | `Infrastructure/ReplicationFanOut.cs:233` | `metrics.FanOut(readers.Count)` → `FanOut(1)` (REP-38 value) | ✅ Killed | `A_spectator_write_records_how_many_readers_it_owed_work_to` |
| M13 | `Domain/User.cs:138` | A second `MarkDeleted` raises `UserRemoved` again (REP-42, domain guard) | ✅ Killed | 1 unit (`Removing_an_already_removed_spectator_announces_nothing`). Integration stayed green — the route short-circuits in `RemoveUserService.cs:40`, so this guard is unreachable over HTTP; see M14 |
| M14 | `RemoveUserService.cs:40` + `Domain/User.cs:138` | Remove **both** second-removal guards — the "enqueue hung off a successful response" shape REP-42 names | ✅ Killed | 2 unit + 2 integration incl. `Removing_a_spectator_a_second_time_queues_nothing_further` |
| M15 | `Infrastructure/ReplicationFanOut.cs:61` | Resurrection queues `Update` instead of `Add` (REP-06) | ✅ Killed | `Resurrecting_a_spectator_queues_an_add_rather_than_an_update` |
| M16 | `Infrastructure/ReplicationFanOut.cs:262-266` | Capacity counter fires for every reader, not just outgrown ones (REP-25/REP-37 conjunction probe) | ✅ Killed | `Reader_the_roster_outgrew_is_counted_against_that_reader` |
| M17 | `Infrastructure/ReplicationFanOut.cs:233` | Fan-out commits its own rows in a second transaction (AD-041 / REP-07 probe) | ✅ Killed | 5 `ReplicationObservabilityTests` — killed, but incidentally; see Observation 1 |

**Result: 17/17 killed, 0 survived — PASS ✅**

Tree state re-verified after every mutation and at the end: `git status --porcelain` shows only the
untracked `.agents/` and `.claude/`, `git diff` is empty, HEAD is `d993bf1`, 35 commits ahead of
`main`.

---

## Gate Check

- **Build gate**: `dotnet build HikvisionReplicator.slnx --no-incremental` → **Build succeeded**,
  0 errors, 17 warnings (all pre-existing except the one `CA1711` on `IDomainEventHandler`, which
  `tasks.md` flags deliberately; `.editorconfig` was not edited to hide it).
- **Quick gate**: `dotnet test src/HikvisionReplicator.Tests` → **364 passed, 0 failed, 0 skipped**.
- **Full gate**: `dotnet test src/HikvisionReplicator.IntegrationTests` → **286 passed, 0 failed,
  0 skipped**.
- **Counts match the task record** (364 unit · 286 integration).
- **Test count before the feature**: 282 unit · 193 integration (`tasks.md` baseline).
  **Delta: +82 unit, +93 integration.**
- **Skipped tests**: none, in either project.
- **Test integrity**: `git diff --numstat main...HEAD` over both test projects shows deletions in
  exactly two files — `StartupTests.cs` (−1: one doc-comment line) and
  `UserPersistenceContractTests.cs` (−8: eight `new UserRepository(context)` call sites moved to a
  `RepositoryOver(context)` factory, forced by T17's constructor change). Every other test file is
  additive. **No test was deleted, skipped, renamed away or weakened.**

---

## Code Quality

| Check | Status |
| --- | --- |
| No features beyond what was asked | ✅ — nothing drains the queue; the expansion exists but is called by nothing (AD-039) |
| No abstractions for single-use code | ✅ — hand-rolled dispatcher instead of MediatR; no `IRepository<BackfillIntent>` for a single consumer |
| No unnecessary flexibility | ✅ — two fixed lanes, closed enum sets, no runner seam |
| Only touched files required for the task | ⚠️→✅ — two files landed beyond their task's `Where` (`FleetAdmission.cs`, `ReaderCeilingsSpec.cs`); both are declared in `tasks.md` and both are load-bearing (REP-23's "same terms"; a value-converted `FaceCapacity` cannot be compared in SQL) |
| Didn't "improve" unrelated code | ✅ — the only change to existing files is the `UserRepository` constructor and the two device services' guards |
| Matches existing patterns/style | ✅ — `OneOf` results, private EF ctor + static factory (AD-005), Ardalis specifications (AD-006), clock passed in (AD-023) |
| Would a senior engineer approve? | ✅ |
| Tests map to ACs and are non-shallow | ✅ — spot-checked REP-10 (four fields asserted) and REP-28 (field + message + full before/after snapshot) |
| Spec-anchored outcome check | ✅ — 47/47; literal sentences pinned for REP-21, REP-23, REP-13 and the startup refusal rather than compared against the production constant (AD-036's tautology trap) |
| Per-layer Coverage Expectation met | ✅ — domain 1:1 with REP-26…REP-33; every route that fans out covered happy + edge + error through HTTP (AD-036) |
| Every test maps to a spec AC / edge case / Done-when | ✅ — no unclaimed tests found in the diff |
| Below-HTTP tests each name their own blind spot | ✅ — every `ReplicationQueueContractTests` member carries the AD-040 sentence in its doc comment |
| Documented guidelines followed | ✅ — `CLAUDE.md`, `docs/test-patterns.md`, `.specs/STATE.md` AD-021/022/026/036/037/038/039/040/041/042 |

---

## Edge Cases

- [x] REP-40 — device deleted with pending work: rows cascade, delete returns `204`
- [x] REP-41 — removal queued for a never-replicated spectator
- [x] REP-42 — second `DELETE` enqueues nothing (two independent guards, both sensor-probed)
- [x] REP-43 — duplicate address rejected, no intent left behind
- [x] REP-44 — expansion for a deleted device: nothing persisted, nothing thrown
- [x] REP-45 — concurrent expansion leaves one row per spectator
- [x] REP-46 — queued by exactly one path
- [x] REP-47 — empty registry admits any capacity

---

## Requirement Traceability Update

| Requirement | Previous Status | New Status |
| --- | --- | --- |
| REP-01…REP-07 | Implementing | ✅ Verified |
| REP-08…REP-13 | Implementing | ✅ Verified |
| REP-14…REP-20 | Implementing | ✅ Verified |
| REP-21…REP-25 | Implementing | ✅ Verified |
| REP-26…REP-34 | Implementing | ✅ Verified |
| REP-35…REP-39 | Implementing | ✅ Verified |
| REP-40…REP-47 | Implementing | ✅ Verified |

---

## Lessons Distilled

None. `validation.md` records no failed AC, no uncovered criterion, no surviving mutant, no
spec-precision gap and no `// SPEC_DEVIATION` marker, so per `references/lessons.md` a clean PASS
records nothing. The four observations above are evidence-strength notes on assertions that already
pass, not grounded failures — recording them would put opinions into the playbook.

---

## Summary

**Overall: ✅ Ready**

**Spec-anchored check**: 47/47 ACs matched the spec-defined outcome; 0 spec-precision gaps.
**Sensor**: 17/17 mutations killed (P0-full depth).
**Gate**: 364 unit + 286 integration passed, 0 failed, 0 skipped; build 0 errors.

**What works**: every user write fans out to every registered reader inside the write's own
transaction; supersession keeps exactly one piece of outstanding work per pair, arbitrated by a
partial unique index rather than a read-then-write check; registration records an O(1) backfill
debt that expands on demand, skipping tombstones and deferring to live intents; a reader too small
for the fleet cannot enter the catalogue or survive a capacity reduction; the whole status model
and transition table exist with nothing running — no hosted service, no scheduler, no job-runner
package anywhere in the assembly.

**Issues found**: none blocking. Four evidence-strength observations are recorded above for the
feature-4 author, the most useful being Observation 1 — REP-07's "the replication insert itself
fails" direction has no dedicated test, and feature 4, which will write to this table from a
drainer, is the natural place to add one.

**Next steps**: merge. `replication-worker` inherits the status model, the `Bulk`/`Live` lanes and
`ExpandBackfillAsync` unchanged.

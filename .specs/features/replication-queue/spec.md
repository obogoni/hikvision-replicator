# replication-queue Specification

## Problem Statement

The product's core capability does not exist: nothing records that a spectator must reach a
turnstile. `user-registry` and `device-registry` ship two catalogues that know nothing about each
other, so a ticket bought minutes before kickoff produces a database row and no propagation.
This feature builds the durable record of intent — what must be enrolled where — with the
idempotency and ordering rules that keep it honest under churn, and nothing that executes it.

## Goals

- [ ] Every user change produces exactly one pending replication per registered device, committed
      in the same transaction as the user write — no spectator can exist with no queued work.
- [ ] At most one `Pending` replication exists for any (user, device) pair, under concurrent
      upserts, enforced by the database rather than by a read-then-write check.
- [ ] Registering a device records its backfill in O(1) writes, not 50,000.
- [ ] A device that can never hold the fleet is refused at registration (AD-021's mandatory guard).
- [ ] The assembly contains no job runner, no hosted service and no scheduler (AD-039).

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
| --- | --- |
| Draining the queue, retry scheduling, backoff, dead-lettering | Feature 4 `replication-worker`, which also resolves OD-3. This feature ships runner-agnostic (AD-039). |
| The `IDeviceClient` port and its fake adapter | No execution path here means nothing to call it. Introduced by feature 4. |
| Running the backfill expansion | The expansion *rule* is specified and directly invocable here (REP-17…REP-20); invoking it on a schedule is feature 4's. |
| Enrolled-state tracking — what a device actually holds | Feature 7 `reconciliation`. Its absence is why a removal still executes (REP-11). |
| Any query/read API over the queue | Feature 8 `replication-visibility`. |
| Retention or purge of terminal rows | Deliberately unspecified — see Assumptions. Bound for ROADMAP § Known Gaps. |
| Per-replication capacity blocking | The guard is fleet-admission only (REP-21…REP-25); a drain that would overfill fails in feature 4. |
| Auth on any touched endpoint | Every endpoint is anonymous until feature 9 `api-auth` (A-6 of `device-registry`). Unchanged here. |

---

## Assumptions & Open Questions

Every ambiguity is resolved or recorded here — nothing is left silently unclear.

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --- | --- | --- | --- |
| Superseded rows are kept, not deleted | Terminal `Superseded` status; the queue is an intent log | Feature 8 must be able to show what was queued and what replaced it | y |
| A removal supersedes a pending Add and still executes | `Remove` row inserted `Pending`; the Add moves to `Superseded` | The queue does not track what a device holds, so a pending Add is not evidence the face never arrived | y |
| Device registration fans out lazily | One backfill intent row, expanded later by feature 4 | Keeps registration fast and atomic; 50k inserts do not belong in an HTTP request | y |
| Two fixed lanes | `Live` and `Bulk` | AD-038 requires exactly one preemption relationship; an integer knob would weaken feature 4's ordering guarantee | y |
| Capacity basis | Active (non-tombstoned) user count | AD-015 sends every user to every device, so eventual load *is* the active user count; queue depth reads clear on a full device | y |
| Capacity guard is fleet-admission, not per-enqueue | Refuse registration below the ceiling; signal when the count crosses it; never block a replication | The basis does not depend on which spectator is enqueued, so per-row blocking adds nothing. Satisfies AD-021 without superseding it | y |
| Fan-out shares the user's transaction | Same `SaveChanges`, same transaction | AD-034's argument: a crash between two writes leaves a spectator queued nowhere, undetectable until a turnstile | y |
| No retention rule in this feature | Terminal rows accumulate | A purge written before feature 8 exists would guess at what operators need; churn at 20 devices is modest. Recorded as debt | y |
| **A deleted device takes its queued work with it** | Cascade: pending replications and the backfill intent go with the device row | `RemoveDevice` is a hard delete by design (DEV-25) to free the `ip:port` index entry. Refusing the delete while work is pending would break DEV-25, and no Remove can reach a decommissioned reader | n — agent default |
| **`LastError` is bounded** | Stored truncated to 1,000 characters | An unbounded device error string is an unbounded column; the worker that writes it does not control its length | n — agent default |

**Open questions:** none — all resolved or logged above.

---

## Implicit-Requirement Dimensions Sweep

Large scope — every dimension resolves to a requirement or an explicit `N/A because`.

| Dimension | Resolution |
| --- | --- |
| Input validation & bounds | REP-28, REP-33 — operation/lane/status are closed sets; `LastError` is truncated |
| Failure / partial-failure states | REP-07 — the user write and its fan-out commit or fail together |
| Idempotency / retry / duplicate handling | REP-04, REP-08…REP-13, REP-19 |
| Auth boundaries & rate limits | **N/A because** no endpoint is added and every existing one is anonymous until feature 9 (A-6); this feature changes no auth boundary |
| Concurrency / ordering | REP-09, REP-12, REP-13 — one `Pending` per pair enforced by a partial unique index; in-flight rows are never superseded |
| Data lifecycle / expiry | REP-14, REP-23, REP-40 — cascade on device delete; retention explicitly deferred (Assumptions) |
| Observability | REP-35…REP-39 |
| External-dependency failure | **N/A because** this feature makes no outbound call — there is no device client, no execution path (AD-039). Feature 4 owns it |
| State-transition integrity | REP-26…REP-32 — the transition table and its guards |

---

## User Stories

### P1: Queue work whenever a spectator changes ⭐ MVP

**User Story**: As the propagation service, I want every user change to produce pending work for
every registered device in the same transaction, so that no spectator can exist without being
queued somewhere.

**Why P1**: This is the capability the product does not have. Without it the two catalogues never
meet.

**Acceptance Criteria**:

1. **REP-01** — WHEN a user is created through `PUT /api/users/{externalRef}` and N devices are
   registered THEN the system SHALL persist exactly N replications, each with operation `Add`,
   lane `Live`, status `Pending`, and a foreign key to that user and to one distinct device.
2. **REP-02** — WHEN an existing user is updated and at least one field changed THEN the system
   SHALL persist exactly N replications with operation `Update`, lane `Live`, status `Pending`.
3. **REP-03** — WHEN a user is removed through `DELETE /api/users/{externalRef}` THEN the system
   SHALL persist exactly N replications with operation `Remove`, lane `Live`, status `Pending`.
4. **REP-04** — WHEN an upsert leaves every field unchanged — the no-op USR-26 already defines by
   not advancing `UpdatedAt` — THEN the system SHALL persist no replication at all.
5. **REP-05** — WHEN a user is written and no devices are registered THEN the user write SHALL
   succeed and SHALL persist no replication.
6. **REP-06** — WHEN a user is resurrected (an upsert against a tombstoned `ExternalRef`) THEN the
   system SHALL queue it as `Add`, not `Update`, because the device no longer holds that face.
7. **REP-07** — WHEN persisting the replications fails for any reason THEN the user write SHALL
   NOT be committed, and the caller SHALL NOT receive a success response.

**Independent Test**: `PUT` a user with 3 devices registered, then read the queue directly — three
`Pending` `Add` rows, one per device, and the user row, all present after one request.

---

### P1: Never accumulate duplicate pending work

**User Story**: As an operator, I want a spectator edited five times before kickoff to leave one
piece of pending work per device, not five, so that the queue reflects intent rather than history
of keystrokes.

**Why P1**: Without it, AD-038's live lane fills with stale intent and the p95 is spent replaying
superseded states.

**Acceptance Criteria**:

1. **REP-08** — WHEN an intent is enqueued for a (user, device) pair that already has a `Pending`
   replication THEN the system SHALL move the existing row to `Superseded` and insert the new
   intent as `Pending`.
2. **REP-09** — WHEN any (user, device) pair is considered THEN at most one replication with status
   `Pending` SHALL exist for it, enforced by a **partial unique index** on the pair where status is
   `Pending` — never by a read-then-write check (the defect AD-022 removed from the pre-rewrite
   code).
3. **REP-10** — WHEN a row is superseded THEN its operation, lane, attempt count and last error
   SHALL be left exactly as they were, so the log records what was replaced.
4. **REP-11** — WHEN a user is removed while an `Add` or `Update` is `Pending` for a device THEN the
   `Remove` SHALL supersede it AND SHALL itself be persisted as `Pending` — the removal still
   executes, because a pending `Add` is not evidence that no earlier `Add` succeeded.
5. **REP-12** — WHEN a replication is `InProgress` THEN a newly arriving intent for the same pair
   SHALL NOT supersede it; the new intent SHALL be inserted as `Pending` and the in-flight row left
   to reach its own terminal status.
6. **REP-13** — WHEN two upserts for the same user commit concurrently THEN the queue SHALL end
   with exactly one `Pending` row for each (user, device) pair, that row SHALL carry one of the two
   intents in full rather than a mixture of both, and neither caller SHALL receive a `500` — the
   partial unique index is the arbiter, so the loser is resolved against the index, never by a
   read-then-write pre-check.

**Independent Test**: Upsert the same user three times against one device, then read the queue —
one `Pending` row and two `Superseded` rows, and the `Pending` one carries the newest intent.

---

### P1: Record a new device's backfill in one write

**User Story**: As an operator registering a reader on match day, I want registration to return
immediately and the fleet-wide backfill to be recorded as owed, so that adding hardware never
blocks on 50,000 inserts.

**Why P1**: Fan-out on registration is one of the four rules the ROADMAP names, and the naive form
puts a 50,000-row insert inside an HTTP request.

**Acceptance Criteria**:

1. **REP-14** — WHEN a device is registered THEN the system SHALL persist exactly one backfill
   intent with status `Pending`, in the same transaction as the device row, and SHALL persist no
   per-user replication.
2. **REP-15** — WHEN a device is registered and no users exist THEN the backfill intent SHALL still
   be persisted, because users may arrive before it is expanded.
3. **REP-16** — WHEN a device is deleted THEN its backfill intent and every replication referencing
   it SHALL be removed with it (DEV-25 is a hard delete).
4. **REP-17** — WHEN the expansion operation is invoked for a pending backfill intent THEN the
   system SHALL persist one replication per **active** user with operation `Add`, lane `Bulk`,
   status `Pending`.
5. **REP-18** — WHEN the expansion runs THEN tombstoned users SHALL be skipped.
6. **REP-19** — WHEN the expansion encounters a (user, device) pair that already has a `Pending`
   replication THEN it SHALL leave that row untouched and SHALL NOT insert a `Bulk` duplicate — a
   live intent outranks a backfill one.
7. **REP-20** — WHEN the expansion completes THEN the backfill intent SHALL reach a terminal
   `Expanded` status and a second invocation SHALL persist nothing further.

**Independent Test**: Register a device with 5 active and 2 tombstoned users, invoke the expansion
directly, read the queue — five `Bulk` `Add` rows, intent `Expanded`; invoke again, nothing changes.

---

### P1: Refuse a reader that can never hold the fleet

**User Story**: As an operator, I want a device whose face library is too small to be rejected when
I register it, so that I find out at the catalogue and not at a turnstile during an event.

**Why P1**: AD-021 makes this mitigation **required**, and ROADMAP § Known Gaps lists it as
mandatory and closing with this feature. Silent enrolment failure at a turnstile is this system's
worst failure mode.

**Acceptance Criteria**:

1. **REP-21** — WHEN a device is registered whose `FaceCapacity` is below the current active user
   count THEN the system SHALL refuse the registration with a conflict naming both the device's
   capacity and the active user count.
2. **REP-22** — WHEN a device is registered whose `FaceCapacity` is greater than or equal to the
   active user count THEN registration SHALL proceed unchanged.
3. **REP-23** — WHEN a device's `FaceCapacity` is updated to a value below the active user count
   THEN the update SHALL be refused on the same terms as REP-21.
4. **REP-24** — WHEN creating a user takes the active user count above a registered device's
   `FaceCapacity` THEN the user SHALL still be accepted (`2xx`) AND the system SHALL emit **both** a
   warning log carrying the device identifier, its capacity and the new active count, and the
   counter of REP-37. The log carries the three values because a counter tag cannot: the count is
   unbounded cardinality.
5. **REP-25** — WHEN the active user count is at or below every registered device's capacity THEN no
   capacity-exceeded signal SHALL be emitted.

**Independent Test**: With 10 active users, register a device of capacity 5 — refused, message names
5 and 10. Register one of capacity 10 — accepted. Add an eleventh user — accepted, signal emitted
naming that device.

---

### P1: A queue that models execution without performing it

**User Story**: As the author of feature 4, I want the aggregate to already own the status model,
attempt count and last error with enforced transitions, so that the worker adds draining and not
domain rules.

**Why P1**: AD-039 makes runner-agnosticism a constraint on this feature, and the transitions are
domain rules whether or not anything drains yet.

**Acceptance Criteria**:

1. **REP-26** — WHEN a replication is created THEN its status SHALL be `Pending`, its attempt count
   `0`, and its last error unset.
2. **REP-27** — WHEN a transition is requested THEN the aggregate SHALL permit exactly:
   `Pending → InProgress`, `InProgress → Succeeded`, `InProgress → Failed`, `Failed → Pending`,
   `Pending → Superseded`.
3. **REP-28** — WHEN any transition outside REP-27 is requested THEN the aggregate SHALL refuse it
   with a validation error and SHALL leave every field unchanged.
4. **REP-29** — WHEN a replication reaches `Succeeded` or `Superseded` THEN every further transition
   SHALL be refused — both are terminal.
5. **REP-30** — WHEN an attempt is recorded as failed THEN the attempt count SHALL increase by
   exactly one and the last error SHALL be stored.
6. **REP-31** — WHEN a failed replication returns to `Pending` THEN the attempt count SHALL be
   preserved, not reset.
7. **REP-32** — WHEN any transition occurs THEN the timestamp SHALL come from a clock passed in,
   never read from the ambient clock (AD-023).
8. **REP-33** — WHEN a last error longer than **1,000 characters** is recorded THEN it SHALL be
   stored truncated to 1,000 characters rather than rejected or stored unbounded, and the
   transition SHALL still succeed.
9. **REP-34** — WHEN the API assembly is inspected THEN it SHALL contain no `IHostedService`, no
   `BackgroundService`, no scheduler and no job-runner package reference (AD-039), and every
   acceptance criterion above SHALL be reachable by direct invocation.

**Independent Test**: Drive the aggregate through every legal transition and every illegal one by
direct invocation, with no host running.

---

### P2: See the queue filling

**User Story**: As an operator during an event, I want enqueue activity instrumented, so that the
AD-038 latency budget can be attributed rather than guessed at.

**Why P2**: Nothing drains yet, so the operational value lands with feature 4 — but instrumenting
the write path later means re-opening it.

**Acceptance Criteria**:

1. **REP-35** — WHEN replications are enqueued THEN the system SHALL increment a counter tagged by
   operation and lane.
2. **REP-36** — WHEN a replication is superseded THEN the system SHALL increment a supersession
   counter.
3. **REP-37** — WHEN a capacity-exceeded signal fires (REP-24) THEN the system SHALL increment a
   counter tagged by device.
4. **REP-38** — WHEN a user write fans out THEN the system SHALL record the fan-out size, so the
   per-device write cost of AD-038's envelope is observable.
5. **REP-39** — WHEN the application starts THEN every meter these instruments use SHALL be
   registered on the metrics provider via `AddMeter` — an instrument with no reader records into
   nothing in production while a test that installs its own listener still passes (L-037).

**Independent Test**: Enqueue, supersede and trip the capacity signal; assert each counter moved,
and assert the meter name appears in the provider registration rather than only in a test listener.

---

## Edge Cases

- **REP-40** — WHEN a device is deleted while replications for it are `Pending` THEN those rows
  SHALL be removed with the device and the delete SHALL NOT be refused (DEV-25).
- **REP-41** — WHEN a user is removed who has never been successfully replicated anywhere THEN
  `Remove` rows SHALL still be queued, because the queue cannot know what a device holds.
- **REP-42** — WHEN a user is removed twice THEN the second `DELETE` SHALL NOT enqueue a second set
  of `Remove` rows. This is a live trap, not a theoretical one: USR-32 / A-16 make the second
  removal return **success, not `404`**, so an enqueue hung off a successful response would fan out
  again against an already-tombstoned spectator.
- **REP-43** — WHEN a device is registered twice at the same address THEN the existing `ip:port`
  uniqueness rule SHALL still reject the second, and no backfill intent SHALL be left behind by the
  rejected attempt.
- **REP-44** — WHEN the expansion is invoked for a backfill intent whose device has since been
  deleted THEN it SHALL persist nothing and SHALL NOT throw.
- **REP-45** — WHEN the expansion is invoked concurrently for the same intent THEN at most one
  invocation SHALL persist rows and no duplicate `Pending` row SHALL result (REP-09 holds).
- **REP-46** — WHEN a user is created at the same moment a device is registered THEN the spectator
  SHALL end up queued for that device by exactly one path — the live fan-out or the backfill
  expansion — and never twice (REP-19).
- **REP-47** — WHEN the active user count is zero THEN a device of any capacity SHALL register
  successfully.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| REP-01…REP-07 | P1: Queue work whenever a spectator changes | Design | Pending |
| REP-08…REP-13 | P1: Never accumulate duplicate pending work | Design | Pending |
| REP-14…REP-20 | P1: Record a new device's backfill in one write | Design | Pending |
| REP-21…REP-25 | P1: Refuse a reader that can never hold the fleet | Design | Pending |
| REP-26…REP-34 | P1: A queue that models execution without performing it | Design | Pending |
| REP-35…REP-39 | P2: See the queue filling | Design | Pending |
| REP-40…REP-47 | Edge cases | Design | Pending |

**ID format:** `REP-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 47 total, 0 mapped to tasks, 47 unmapped ⚠️ (tasks.md not yet written)

---

## Success Criteria

- [ ] A user written with devices registered is queued for every one of them, in one transaction.
- [ ] Repeated edits to one spectator leave exactly one `Pending` row per device, proven under
      concurrent upserts rather than sequential ones.
- [ ] Registering a device is O(1) writes regardless of user count.
- [ ] A device too small for the fleet cannot enter the catalogue.
- [ ] `grep` finds no hosted service, scheduler or job-runner reference in the API assembly, and the
      full suite passes with no host running.

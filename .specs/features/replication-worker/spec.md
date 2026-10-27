# replication-worker Specification

## Problem Statement

`replication-queue` records what must be enrolled where, and nothing acts on it: a spectator who
buys a ticket before kickoff is queued for every reader and enrolled on none. This feature drains
that queue — live work ahead of a running backfill, one operation per reader at a time, and an
offline reader paused and caught up rather than failed — so that AD-038's p95 < 30 s holds in the
exact scenario it was written for.

## Goals

- [ ] A live upsert reaches `Succeeded` on every healthy device within the AD-038 bound **while a
      bulk backfill is still pending**, proven by an integration test and exposed as a histogram.
- [ ] A device that is unreachable for any length of time loses no work and dead-letters nothing;
      its backlog drains in order once it answers again.
- [ ] Only items a device actively rejects are dead-lettered, after 5 attempts (configurable).
- [ ] At most one operation is in flight per device, across every worker instance, enforced by the
      database.
- [ ] OD-3 is closed: no job-runner package is in the solution (AD-043).

## Out of Scope

| Feature | Reason |
| --- | --- |
| The real ISAPI adapter | Feature 5 `isapi-device-client`. This feature ships the `IDeviceClient` port and a fake adapter only (AD-017). |
| Persisted device reachability, its API, alerting | Feature 6 `device-health`. The pause here lives in worker memory (context.md). |
| Drift detection between queue and device | Feature 7 `reconciliation`. |
| Any read or re-drive API over the queue, including dead letters | Feature 8 `replication-visibility`. |
| Parallel operations against one device | Decided against (context.md); readers' tolerance for it is unknown until Phase 3. |
| Push wake-up (`LISTEN/NOTIFY`) | Polling is assumed sufficient; deferred unless it becomes the SLO's binding cost. |
| Retention of terminal rows | Still deferred, as in `replication-queue`. |
| Auth | Feature 9. No endpoint is added here. |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --- | --- | --- | --- |
| What drains the queue (OD-3) | A hosted worker inside the API claiming from `replications`; no job runner | Ordering and priority must live in our table either way (AD-039); a second job store duplicates state | y |
| Concurrency per device | Exactly one in flight; devices in parallel | Per-pair ordering for free; live waits behind ≤ 1 bulk call | y |
| Failure classes | `Unreachable` pauses the device and counts nothing; `Rejected` counts an attempt | Outages of hours must not dead-letter a backlog | y |
| Max attempts for a rejected item | **5**, configurable | A rejection is usually deterministic | y |
| Device pause mechanism | Polly circuit breaker per device, tripping on `Unreachable` only; in memory | User asked; fits the device half only. Item retry is durable, not Polly | y |
| Priority | Strict live-first; bulk may wait indefinitely under continuous live load | Protects the SLO; pre-event live volume is far below a device's capacity | y |
| SLO proof | Integration test with a bulk backlog + delivery-latency histogram | User choice | y |
| Device pause trigger and backoff | Paused after **2** consecutive `Unreachable` results; probe after 5 s, doubling to a **60 s** ceiling; first success resumes | A reader back from an outage should be caught up within a minute | n — agent default |
| Device-call timeout | **10 s**, counts as `Unreachable` | Upper bound on how long a live item can wait behind one bulk call | n — agent default |
| Rejection backoff | Eligible again after 2 s × 2^(attempts − 1), capped at 60 s (2, 4, 8, 16 s) | Short enough to absorb a device-side glitch inside the SLO | n — agent default |
| Idle poll interval | **1 s**, configurable | Keeps pickup latency a small fraction of 30 s with no notification channel | n — agent default |
| Crash recovery | An `InProgress` row older than a **120 s** lease is released to `Pending` with no attempt counted | A crash is not the item's fault; lease ≫ call timeout so live calls are never reclaimed | n — agent default |
| Multiple worker instances | Supported: per-device exclusivity is a database invariant; each instance keeps its own pause state | Running two API replicas must not double-deliver or break ordering | n — agent default |
| An unfinished item overtaken by a newer intent | Ends `Superseded`, keeping the attempt count and last error of its last attempt | REP-12 lets a newer `Pending` exist beside it; returning it to `Pending` would break `IX_replications_pending` and replay stale intent | n — agent default |
| Add/Update for a user tombstoned during delivery | No device call; the row ends `Superseded` | REP-03 guarantees a `Remove` is pending for the pair, and USR-32 has destroyed the face | n — agent default |
| A final rejection while a newer intent is already `Pending` for the pair | Ends `Superseded`, not dead-lettered (WRK-20 takes precedence over WRK-17) | The newer intent replaces what the device refused; a dead letter would flag work nobody still wants | y |
| `REP-34` (no hosted service in the assembly) | **Superseded by this feature** | It bound `replication-queue` while OD-3 was open (AD-039); AD-043 closes it | n — agent default |

**Open questions:** none — all resolved or logged above.

---

## Implicit-Requirement Dimensions Sweep

| Dimension | Resolution |
| --- | --- |
| Input validation & bounds | WRK-30 — every interval and limit is validated at startup; `LastError` truncation already bounded (REP-33) |
| Failure / partial-failure states | WRK-11…WRK-19, WRK-24 |
| Idempotency / retry / duplicate handling | WRK-14…WRK-17, WRK-20, WRK-22 |
| Auth boundaries & rate limits | **N/A because** no endpoint is added; device credentials are read and decrypted only to call the device (AD-008 unchanged) |
| Concurrency / ordering | WRK-05…WRK-08, WRK-21 |
| Data lifecycle / expiry | WRK-25; retention deferred (Out of Scope) |
| Observability | WRK-26…WRK-29 |
| External-dependency failure | WRK-11…WRK-15 — the device is the external dependency |
| State-transition integrity | WRK-03, WRK-16, WRK-19, WRK-20 |

---

## User Stories

### P1: Deliver queued work to every device ⭐ MVP

**User Story**: As the propagation service, I want each pending replication delivered to its
device and its outcome recorded, so that a queued spectator becomes an enrolled one.

**Acceptance Criteria**:

1. **WRK-01** — WHEN a replication is `Pending`, eligible, and its device is registered THEN the
   worker SHALL deliver its operation (`Add`, `Update` or `Remove`) for that user to that device
   through `IDeviceClient` within one idle poll interval plus the call's own duration.
2. **WRK-02** — WHEN `Add` or `Update` is delivered THEN the call SHALL carry the user's
   `ExternalRef`, access code and normalized face picture; WHEN `Remove` is delivered THEN it SHALL
   carry the `ExternalRef` only.
3. **WRK-03** — WHEN the worker takes a replication THEN it SHALL move it `Pending → InProgress`
   before the device call, and WHEN the device accepts THEN to `Succeeded`, each with the time
   from the injected clock (AD-023).
4. **WRK-04** — WHEN a backfill intent is `Pending` THEN the worker SHALL expand it with the
   existing `ExpandBackfillAsync` and commit the expansion in one save (AD-041), and SHALL then
   deliver the resulting `Bulk` rows like any other.

**Independent Test**: register 3 devices, upsert a user, and observe the fake adapter receive one
`Add` per device and three `Succeeded` rows.

---

### P1: Live work first, one operation per device ⭐ MVP

**User Story**: As a spectator buying a ticket during a pre-event seed, I want my enrolment to
overtake the backfill so I get through the turnstile.

**Acceptance Criteria**:

5. **WRK-05** — WHEN a device has eligible `Live` and eligible `Bulk` work THEN the next item it
   takes SHALL be `Live`; within a lane, the oldest by creation time first.
6. **WRK-06** — WHEN any number of worker instances run THEN at most one replication per device
   SHALL be `InProgress` at any moment, enforced by a database constraint rather than by
   in-memory coordination or a read-then-write check (AD-022).
7. **WRK-07** — WHEN one device's call is slow or paused THEN work for every other device SHALL
   continue to be taken without waiting on it.
8. **WRK-08** — WHEN two intents for the same (user, device) exist — one `InProgress`, a newer one
   `Pending` (REP-12) — THEN the newer SHALL NOT be delivered before the older reaches a terminal
   status or is released.
9. **WRK-09** — WHEN at least 100 `Bulk` rows per device are `Pending`, 20 fake devices answer
   each call after 50 ms, and a user is then upserted THEN that user's replication SHALL reach
   `Succeeded` on all 20 devices within **5 s** of the upsert completing, AND at that moment `Bulk`
   rows SHALL still be `Pending` on every device. (5 s is the deterministic test bound: one
   in-flight bulk call + one poll interval + the live call, well inside AD-038's 30 s.)
10. **WRK-10** — WHEN a device is registered or deleted while the worker runs THEN the worker SHALL
    start or stop taking work for it within one idle poll interval, without a restart.

**Independent Test**: WRK-09 itself.

---

### P1: Survive an offline reader without losing or dead-lettering work ⭐ MVP

**User Story**: As an operator, I want a reader that drops off the network for minutes or hours to
catch up on its own when it returns, without its backlog ending up in a dead letter.

**Acceptance Criteria**:

11. **WRK-11** — WHEN `IDeviceClient` reports a call as `Unreachable`, or the call exceeds the
    **10 s** timeout THEN the replication SHALL return to `Pending` with its attempt count
    **unchanged**, and SHALL keep its position in the device's order.
12. **WRK-12** — WHEN a device returns `Unreachable` on **2** consecutive calls THEN the worker
    SHALL pause that device: no further item is taken for it until a probe is due.
13. **WRK-13** — WHEN a device is paused THEN one probe call SHALL be made with its next item after
    5 s, the delay doubling on each failed probe up to **60 s**; WHEN a probe succeeds THEN the
    device SHALL resume normal delivery.
14. **WRK-14** — WHEN a device is unreachable for any duration, of any number of probe cycles THEN
    no replication for it SHALL become `Failed`, and every one SHALL be delivered once it answers,
    in WRK-05's order.
15. **WRK-15** — WHEN `Rejected` (the device answered and refused the record) is reported THEN the
    device SHALL NOT be paused and its next item SHALL be taken.

**Independent Test**: make one fake device unreachable for longer than several probe cycles with
work queued, restore it, and observe every row `Succeeded` with attempt count `0`, in order.

---

### P1: Retry rejected items, then dead-letter them ⭐ MVP

**User Story**: As an operator, I want a record that a device refuses to be retried a few times and
then set aside, so one bad face never blocks a reader.

**Acceptance Criteria**:

16. **WRK-16** — WHEN a delivery is `Rejected` and the attempt count after recording it is below
    the maximum (**5**) THEN the replication SHALL be `Pending`, its attempt count increased by one
    (REP-30), its last error stored (REP-33), and SHALL NOT be taken again before
    `2 s × 2^(attempts − 1)`, capped at 60 s, has passed.
17. **WRK-17** — WHEN a delivery is `Rejected` and the attempt count reaches the maximum THEN the
    replication SHALL stay `Failed` and SHALL never be taken again — this is the dead letter.
18. **WRK-18** — WHEN a dead-lettered replication exists for a pair and a newer intent arrives for
    it THEN the newer intent SHALL be queued and delivered normally (REP-08 supersedes only
    `Pending`), and the dead letter SHALL be left as it is.
19. **WRK-19** — WHEN a replication waiting out its rejection backoff is superseded by a newer
    intent THEN the newer one SHALL be eligible immediately, not after the old backoff.

---

### P1: Never replay stale intent

**Acceptance Criteria**:

20. **WRK-20** — WHEN an attempt ends without success — `Unreachable`, `Rejected`, timeout or an
    expired lease — and a newer intent is already `Pending` for the same pair THEN the older
    replication SHALL end `Superseded`, keeping the attempt count and last error of that attempt,
    and SHALL NOT return to `Pending`.
21. **WRK-21** — WHEN the worker is about to deliver `Add` or `Update` and the user has been
    tombstoned THEN it SHALL make no device call and the replication SHALL end `Superseded`.

---

### P1: Recover from a crash

**Acceptance Criteria**:

22. **WRK-22** — WHEN a replication has been `InProgress` for longer than the **120 s** lease THEN
    it SHALL be released to `Pending` with its attempt count unchanged (or `Superseded` per WRK-20),
    and its device SHALL be able to take work again.
23. **WRK-23** — WHEN the application stops THEN in-flight calls SHALL be cancelled and their
    replications released as in WRK-11, with no attempt counted.
24. **WRK-24** — WHEN recording a delivery's outcome fails because the row no longer exists (the
    device was deleted mid-call, REP-16) THEN the worker SHALL discard the outcome and continue;
    the loop for every other device SHALL be unaffected.
25. **WRK-25** — WHEN a device is deleted THEN the worker SHALL make no further call to it, even
    if an item for it had been read before the delete.

---

### P2: See the worker draining

**Acceptance Criteria**:

26. **WRK-26** — WHEN a replication reaches `Succeeded` THEN the system SHALL record the seconds
    from its creation to success in a histogram tagged by lane and operation — the live-lane p95
    is the AD-038 reading.
27. **WRK-27** — WHEN a delivery attempt ends THEN the system SHALL increment a counter tagged by
    outcome: `succeeded`, `rejected`, `unreachable`.
28. **WRK-28** — WHEN a replication is dead-lettered THEN the system SHALL increment a dead-letter
    counter and log the replication id, user id, device id, attempt count and last error.
29. **WRK-29** — WHEN a device is paused or resumed THEN the system SHALL log the device id and the
    transition, and increment a counter tagged by transition.

---

### P1: Configuration

30. **WRK-30** — WHEN the application starts with a non-positive interval, timeout, lease or a
    maximum attempt count below 1, or a lease not greater than the call timeout THEN it SHALL fail
    to start with a message naming the setting.

---

## Edge Cases

- **WRK-31** — WHEN the queue is empty THEN the worker SHALL make no device call and SHALL wait one
  idle poll interval between checks.
- **WRK-32** — WHEN no devices are registered THEN the worker SHALL run and do nothing.
- **WRK-33** — WHEN a device's address or credentials are updated THEN the next call SHALL use the
  new values.
- **WRK-34** — WHEN `ExpandBackfillAsync` runs while a live upsert for the same device commits THEN
  REP-19/REP-46 still hold: the spectator is queued once.
- **WRK-35** — WHEN the solution is inspected THEN it SHALL contain no job-runner package (Hangfire,
  Quartz or similar) — OD-3 closed by AD-043.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| WRK-01…WRK-04 | P1: Deliver queued work | Design | Pending |
| WRK-05…WRK-10 | P1: Live first, one per device | Design | Pending |
| WRK-11…WRK-15 | P1: Survive an offline reader | Design | Pending |
| WRK-16…WRK-19 | P1: Retry, then dead-letter | Design | Pending |
| WRK-20…WRK-21 | P1: Never replay stale intent | Design | Pending |
| WRK-22…WRK-25 | P1: Recover from a crash | Design | Pending |
| WRK-26…WRK-29 | P2: See the worker draining | Design | Pending |
| WRK-30 | P1: Configuration | Design | Pending |
| WRK-31…WRK-35 | Edge cases | Design | Pending |

**Coverage:** 35 total, 0 mapped to tasks.

---

## Success Criteria

- [ ] WRK-09 passes in CI against Testcontainers PostgreSQL, deterministically.
- [ ] An outage longer than several probe cycles ends with zero `Failed` rows for that device.
- [ ] The live-lane delivery histogram is visible in Grafana.

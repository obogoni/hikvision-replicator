# replication-queue Context

**Gathered:** 2026-10-02
**Spec:** `.specs/features/replication-queue/spec.md`
**Status:** Ready for design

---

## Feature Boundary

The `Replication` aggregate and the rules that put work into it: real FKs to user and device,
`Failed` status, attempt count, last error, priority lane; fan-out for user-created,
user-updated, user-deleted and device-registered; and the idempotency rules that stop pending
work accumulating. It ships **runner-agnostic** (AD-039) — no execution path, no scheduling,
nothing that drains. Feature 4 `replication-worker` owns draining and the job-runner choice
(OD-3).

It also owns the **fleet-admission half** of the AD-021 capacity guard, which ROADMAP § Known
Gaps lists as mandatory and closing with this feature.

---

## Implementation Decisions

### Supersession and the shape of the queue

- The queue is an **intent log**, not a desired-state table. A superseded row moves to a
  **terminal `Superseded` status**; the new intent is a new row.
- Chosen over one-row-per-pair-mutated-in-place so feature 8 `replication-visibility` can show an
  operator what was queued, what replaced it and when. The cost — a table that grows with churn —
  is accepted and recorded as debt below.
- **A removal supersedes a pending Add and still executes.** The queue does not track what a
  device actually holds (that is feature 7 `reconciliation`), so a pending `Add` is not evidence
  the face never reached the device — an earlier `Add` for the same pair may already have
  succeeded. A delete the device rejects as absent is harmless; a face left on a turnstile after
  the spectator was removed is not.
- **An in-flight row is never superseded.** Only a `Pending` row can be; a new intent arriving
  against an `InProgress` row is inserted as `Pending` and the in-flight row is left to finish.

### Device-registration backfill

- Registering a device commits **one backfill intent row**, not 50,000 replication rows.
  Registration stays fast and stays atomic.
- The **expansion rule belongs to this feature; running it does not.** The expansion is a domain
  operation invoked directly — which is exactly how AD-039 says this feature must be testable —
  and feature 4 is what will call it on a schedule.
- Expansion covers **active users only**; tombstoned spectators are not enrolled on a new device.

### Priority lane

- **Two fixed lanes: `Live` and `Bulk`.** Live is anything user-triggered; Bulk is
  device-registration backfill.
- Chosen over an integer priority because AD-038 requires exactly one preemption relationship —
  a ticket purchase must beat a seed — and an unbounded knob would make feature 4's ordering
  guarantee harder to state than to honour.

### Capacity guard

- Basis is the **active (non-tombstoned) user count**, not queue depth. AD-015 sends every user to
  every device, so a device's eventual load *is* the active user count; queue depth is near zero on
  a drained queue and would read clear on a device already full.
- Because the basis does not depend on which spectator is being queued, the guard is **not
  per-enqueue**. It is a fleet-admission rule evaluated at the two moments that can change the
  answer:
  1. **Device registration is refused** when the device's `FaceCapacity` is below the active user
     count — a reader that can never hold the fleet does not enter the catalogue.
  2. **Crossing a registered device's ceiling is signalled** when a new spectator takes the active
     count above it. The user is still accepted.
- **Individual replications are never blocked**, and a drain that would overfill still fails in
  feature 4. This reconciles the drain-time preference with AD-021's "required mitigation" and
  ROADMAP § Known Gaps' "closes with feature 3" — neither needs superseding.

### Enqueue atomicity

- Fan-out rows commit **in the same transaction as the user write**. The AD-034 argument applied
  again: a crash between two writes would leave a spectator who exists and is queued nowhere, and
  nothing would detect it until a turnstile.
- Accepted cost: the user write path now also writes one row per registered device (20 at AD-038's
  fleet size).

### Agent's Discretion

- **A deleted device takes its queued work with it.** `RemoveDevice` is a hard delete by design
  (DEV-25, to free the `ip:port` index entry), so pending replications and the backfill intent for
  that device are removed with it. Not discussed; the alternative — refusing the delete while work
  is pending — would break DEV-25, and no Remove can be sent to a decommissioned reader anyway.
  Logged as an assumption in the spec.
- Exact metric names, log wording, and the bound on the stored `LastError` string.

### Declined / Undiscussed Gray Areas → Assumptions

All four offered gray areas were discussed. The ones carried into the spec's Assumptions table as
agent defaults are the device-delete cascade above and the `LastError` bound.

---

## Specific References

- The supersession and no-op rules build on `user-registry`'s existing behaviour: USR-26 already
  guarantees that an upsert changing nothing does not advance `UpdatedAt`, which is what makes
  "a no-op upsert enqueues nothing" testable rather than aspirational.
- L-037 is live here: `Program.cs` now has `.WithMetrics(…)`, so a new meter must be added to
  `AddMeter` explicitly or its instruments record into nothing in production while tests that
  install their own listener still pass.

---

## Deferred Ideas

- **Retention / purge of terminal rows.** Deliberately unspecified. At 20 devices and 50,000 users
  steady-state churn is modest, and a purge rule written before feature 8 exists would guess at
  what operators need to keep. Recorded as debt in the spec and bound for ROADMAP § Known Gaps.
- **Enrolled-state tracking** (what a device actually holds) — feature 7 `reconciliation`.
- **Any query API over the queue** — feature 8 `replication-visibility`.
- **Draining, retry scheduling, backoff, dead-lettering, the `IDeviceClient` port** — feature 4,
  which also resolves OD-3.

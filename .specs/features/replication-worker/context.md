# replication-worker Context

**Gathered:** 2026-10-26
**Spec:** `.specs/features/replication-worker/spec.md`
**Status:** Ready for design

---

## Feature Boundary

Drain the queue `replication-queue` built: claim pending work per device, deliver it through an
`IDeviceClient` port to a fake adapter, record the outcome on the `Replication` aggregate, and
expand backfill intents. Live before bulk, one operation in flight per device, an offline reader
paused rather than failed, and the AD-038 SLO asserted under a running backfill. No real ISAPI
adapter (feature 5), no persisted device health (feature 6), no read API (feature 8).

---

## Implementation Decisions

### OD-3 — what drains the queue

- **A purpose-built hosted worker inside the API, no job runner.** Hangfire rejected: the queue
  already is our table, with its own state machine, supersession and lane; Hangfire would add a
  second job store with its own states and retry counter, it offers no ordering guarantee, and on
  PostgreSQL its queue priority is alphabetical by name (AD-039). Per-device ordering and live-first
  priority would have to live in our query under either choice.
- Claiming is done against the `replications` table itself.

### Per-device concurrency

- **One operation in flight per device, devices in parallel.** Ordering per (user, device) follows
  from it, and a live item waits behind at most one bulk item. Not configurable in this feature.

### Failure model — two kinds of failure

- **Device unreachable** (timeout, connection refused, transport error): the *device* is paused,
  its work stays `Pending`, **no attempt is counted**, and it is probed on a capped backoff. An
  outage of minutes or hours must drain in order on recovery, with nothing dead-lettered.
- **Item rejected** (the device answered and refused this record): only that item fails, its
  attempt count increases, it is retried on its own backoff, and after **5 attempts (configurable)**
  it is dead-lettered. The device carries on with its other work.
- User confirmed this is a circuit breaker and asked whether Polly fits. **Agreed for the
  device-level pause only**: a Polly circuit breaker per device, tripping on *unreachable* outcomes
  only. Item retry is **not** Polly — it must be durable across restarts and spans minutes, so it
  lives in the table. Polly v8 option names and minimums are to be verified against current docs
  in Design before anything depends on them.

### Device health scope

- **Pause lives in worker memory only.** Persisted reachability status, its API and alerting are
  feature 6 `device-health`.

### Priority

- **Strict live-first.** Bulk work on a device runs only when that device has no eligible live
  work. Starvation of bulk under a continuous live stream is accepted.

### SLO proof

- **Integration test plus metric.** A test runs a bulk backlog against fake devices with injected
  latency, upserts a user, and asserts it is delivered to every device well inside 30 s *while the
  backlog is still pending*. A delivery-latency histogram exposes the real p95 in Grafana.

### Agent's Discretion

- Numeric defaults not named by the user (device-call timeout, pause backoff, idle poll interval,
  in-progress lease) — chosen in the spec's Assumptions, all configurable.

### Declined / Undiscussed Gray Areas → Assumptions

- Multi-instance deployment, crash recovery, the tombstone-during-delivery race and a released
  item colliding with a newer intent were not discussed. Each is an agent default in the spec's
  Assumptions table.

---

## Specific References

- "Devices may be offline for a few minutes or even hours during a sync. Our app should be able to
  recover from this and still sync pending users without sending them all to a dead letter queue."

---

## Deferred Ideas

- Persisted device online/offline status and alerting → feature 6 `device-health`.
- Operator re-drive of dead-lettered items → feature 8 `replication-visibility` or later.
- Push wake-up (`LISTEN/NOTIFY`) instead of polling → only if the poll interval proves to be the
  SLO's binding cost.

# Roadmap — hikvision-replicator (rewrite)

**Status**: draft for confirmation · 2026-08-02
**Supersedes**: nothing — first roadmap. Governed by AD-013 (rewrite from scratch).

---

## Product Goal

Live synchronization of users to Hikvision facial-recognition access devices, in a
**performant and fault-tolerant** manner.

Driving scenario — **stadium access control**: a spectator buys a ticket minutes
before kickoff and must be able to walk through a turnstile using their face. The
system's value is the *latency* between "user created via API" and "user is
enrolled on every reader", and its resilience when individual readers are offline.

**System of record is external.** An integrator adds and removes users through this
API; this service owns propagation to devices, not the ticketing truth.

---

## Scale Targets

| Dimension | Target | Source |
|---|---|---|
| Users | up to 50,000 | confirmed |
| **Enrolled faces per device** | **10,000 (hard hardware limit)** | confirmed — AD-020 |
| Devices / readers | **unknown — needed** | ⚠️ open |
| Face picture size | ≤ 200 KB each | current domain rule |
| Live-sync latency (single new user → all devices) | **unknown — needed** | ⚠️ open |
| Bulk-load window (full 50k seed before an event) | **unknown — needed** | ⚠️ open |

**Derived load** (arithmetic, not a measurement): a full fan-out is
`50,000 × D` replication operations and `10 GB × D` of face-image transfer.
At D=50 that is 2.5M operations; at D=100, 5M. This number sizes every
decision in Phase 2.

---

## Open Decisions (must resolve before the phase that depends on them)

| # | Decision | Recommendation | Blocks |
|---|---|---|---|
| ~~OD-1~~ | **RESOLVED — capacity is modelled per device** (AD-020); bench unit holds 10,000. | — | — |
| ~~OD-2~~ | **RESOLVED — PostgreSQL from the first commit** (AD-018). Integration tests move to Testcontainers (AD-019). | — | — |
| OD-3 | **Job runner.** Reopened by AD-018: Hangfire on PostgreSQL is now viable, where Hangfire on SQLite was not. **No incumbent to default to** — AD-030 supersedes AD-010's Hangfire mandate, and the solution contains no job runner of any kind. | Hangfire+PostgreSQL for Phase 1's simple enqueue needs; validate under the derived Phase 2 load before committing, with a purpose-built hosted worker polling the replication table as the fallback (the queue design supports either). **Recommendation only — this decision is not taken.** | Phase 2 |
| ~~OD-6~~ | **RESOLVED — higher-capacity hardware** (AD-021). AD-015's all-users-to-all-devices rule stands; scoping stays out of scope. Carries a standing risk: the fleet runs near 100% of each device's face library with no headroom, and the 10,000-face bench unit cannot validate full-scale enrolment. Mitigated by the mandatory `Device.FaceCapacity` guard. | — | — |
| OD-4 | **Face image storage.** 10 GB of BLOBs inside the transactional database bloats it and slows every query. | Store images outside the row (filesystem/object store), keep a content hash on `User` for change detection and dedup. Decide in `user-registry`. | Phase 1 |
| OD-5 | **Live-sync latency SLO.** "A few minutes" needs a number to be testable — it becomes an acceptance criterion. | Propose: p95 under 30s from `POST /api/users` to enrolled on all healthy devices. Confirm or replace. | Phase 2 |
| OD-7 | **Ciphertext format for device passwords.** AES-256-CBC (AD-008) gives confidentiality but no integrity check, so a tampered ciphertext fails at decrypt time rather than being detected (assumption A-8 of `device-registry`). | Move to AES-GCM behind a **versioned ciphertext prefix**. Decide before the first production deployment — a format migration is far cheaper while no real credentials are stored than after. | First production deploy |

---

## Known Gaps

Current-state debts of the shipped code, promoted from the retired `ARCHITECTURE.md` § 8
(AD-029). The rewrite already closed the defects the pre-rewrite implementation carried —
`EnsureCreated()` alongside migrations, the racy read-then-write uniqueness check,
unnormalized IP storage, and the unconditional `UpdatedAt` advance. What remains:

| Gap | Standing | Closes with |
|---|---|---|
| **The product's core capability does not exist yet.** Nothing replicates a user to a device: no user catalogue, no replication queue, no worker, no ISAPI client. | By design — Phases 1–3 exist to build exactly this. | features 2–5 |
| **No auth, no rate limiting.** Every endpoint is anonymous, including the ones that accept and store device credentials. Accepted for now (assumption A-6 of `device-registry`) with a hard deployment constraint: **this must not reach a routable network before `api-auth` ships.** | Accepted risk, bounded by the deployment constraint. | feature 9 `api-auth` |
| **AES-256-CBC carries no integrity check.** `EncryptionService` provides confidentiality only; a tampered ciphertext surfaces as a decrypt failure rather than as detected tampering. | Open — sequencing matters, see `OD-7`. | `OD-7`, before first deploy |
| **The device catalogue is unpaginated.** `GET /api/devices` returns a bare array, which pins the empty case to `[]`. DEV-26 ships as a paged shape behind query parameters or a `v2` route — never by mutating this response. | Specified and deliberately unbuilt (P3). | not scheduled |
| **Face capacity is declared but not enforced.** `Device.FaceCapacity` is modelled and validated; the guard that refuses a replication which would overfill a device is the **required** AD-021 mitigation, not an optional extra. Silent enrolment failure at a turnstile is this system's worst failure mode. | Mandatory. | feature 3 `replication-queue` |

---

## Phases

Each numbered item becomes `.specs/features/[name]/`. Phase 1 + Phase 2 = MVP.

### Phase 1 — Foundation & Catalogues

The walking skeleton rides in feature 1: solution layout, `Shared/` contracts,
`AppDbContext` with **real migrations** (`Migrate()`, never `EnsureCreated()`),
ProblemDetails pipeline, OpenTelemetry, Scalar.

| # | Feature | Delivers |
|---|---|---|
| 1 | `device-registry` | Register / list / get / update / delete devices. Encrypted credentials (AES-256, reversible per AD-008). Unique `ip:port`. |
| 2 | `user-registry` | Create / update / **delete** users keyed by `ExternalRef`. Access code, face picture (see OD-4). Delete is first-class from day one — the integrator owns removals, so the Remove path cannot be an afterthought. |

### Phase 2 — Replication Engine ⭐ the product

Built entirely against an `IDeviceClient` **port with a fake adapter** — no hardware
required, fully testable, real adapter drops in at Phase 3.

| # | Feature | Delivers |
|---|---|---|
| 3 | `replication-queue` | Redesigned `Replication` aggregate: real FKs to user/device, `Failed` status, attempt count, last error, priority lane. Fan-out rules for user-created / user-updated / user-deleted / **device-registered** (backfill). Idempotency: re-upserting a user must not accumulate duplicate pending work; a newer intent supersedes an older pending one for the same (user, device). |
| 4 | `replication-worker` | Drains the queue. **Priority lanes** — a single fresh ticket purchase preempts a 5M-row bulk backfill; without this, live sync dies the moment someone seeds an event. Per-device parallelism with per-device ordering (Add-then-Remove for one user must not race). Retry with backoff, dead-letter after N attempts. |

### Phase 3 — Real Hardware

| # | Feature | Delivers |
|---|---|---|
| 5 | `isapi-device-client` | The real ISAPI adapter behind `IDeviceClient`: user enrol/update/delete, face upload, access code. Digest auth, timeouts, device-specific error mapping. |

### Phase 4 — Production Readiness

| # | Feature | Delivers |
|---|---|---|
| 6 | `device-health` | Reachability tracking + circuit breaker. One dead turnstile must never stall the queue for the other 99. |
| 7 | `reconciliation` | Drift detection: what we believe is enrolled vs. what the device actually holds. The only defence against silent partial failure. |
| 8 | `replication-visibility` | Query API: where is user X provisioned, what is failing, how far behind is device Y. Operators need this during an event, not after. |
| 9 | `api-auth` | Every endpoint is anonymous today, including ones that store device credentials. Required before anything faces a network. |

---

## Deferred / Out of Scope for MVP

| Item | Reason |
|---|---|
| Access scoping (user → subset of devices) | Confirmed: MVP is every user on every device. Revisit if OD-1 forces it, or when zoned access (VIP/sector) is needed. |
| Pull-based sync from the ticketing system | Integrator pushes via API — confirmed. |
| Admin UI | API-first; operators use `replication-visibility` + Grafana. |
| Multi-tenant / multi-venue | Single venue assumed. Not contradicted, but never stated — flagged rather than assumed. |
| Card / fingerprint credentials | Face + access code only. |
| Turnstile event ingestion (who passed through, when) | This service provisions identities; it does not collect access logs. |

---

## Sequencing Notes

- **Phases 1 and 2 are the MVP.** Phase 3 makes it real; Phase 4 makes it survivable.
- Phase 2 is where the product's stated goal — performant, fault-tolerant live sync —
  is actually won or lost. It deserves full Specify → Design → Tasks → Execute.
- OD-1 and OD-2 must resolve before feature 3 is specified. OD-5 becomes an
  acceptance criterion inside feature 4.
- `src/` is deleted in the first commit of feature 1 (AD-013); git history retains it.

# isapi-device-client Specification

## Problem Statement

The replication engine (features 3–4) drains its queue into an in-memory fake, and startup refuses
every adapter except `Fake`. So no turnstile knows anybody, and production cannot start at all.
This feature ships the real adapter behind `IDeviceClient` (AD-017). It speaks Hikvision ISAPI over
HTTPS with digest authentication, maps every response from a reader onto exactly one of
`Delivered`, `Rejected` or `Unreachable`, and is proven on the bench reader, which also closes
`user-registry` assumption A-13.

## Goals

- [ ] With `DeviceClient:Adapter = Isapi`, a spectator created through the API reaches
      `Succeeded` on the bench reader and is then recognised there by face and by PIN (UAT).
- [ ] Every reader response maps to one outcome by a single rule: **a failure that would hit every
      spectator on that reader is `Unreachable`; a failure specific to this spectator is
      `Rejected`.** One wrong password therefore pauses a reader and never dead-letters its
      roster.
- [ ] A-13 is closed with measured evidence from the bench reader: accepted sizes and dimensions,
      plus where the reader's real limits are.
- [ ] No device password, PIN, face byte or `Authorization` header appears in a log, a span, a
      metric tag or an outcome reason.

## Out of Scope

| Feature | Reason |
| --- | --- |
| Persisted reader health, its API, alerting | Feature 6 `device-health`. The adapter only classifies; the worker's in-memory `DevicePause` acts on that. |
| Reading a reader's roster, drift detection | Feature 7 `reconciliation`. This adapter only writes. |
| Certificate pinning or a trust store for readers | Decided: the reader's self-signed certificate is accepted (context.md). Pinning is a later hardening. |
| Per-device HTTP/HTTPS choice | Decided: HTTPS only (context.md). No change to the `Device` aggregate. |
| ISUP 5.0, cards, fingerprints | The product enrols face and PIN only (WRK-02). |
| Per-spectator validity windows, door and schedule plans | Ticket truth lives with the integrator. Every spectator gets one fixed, permissive right (A-8). |
| Reading face capacity from the reader | `FaceCapacity` stays declared at registration (AD-020). |
| Retries or parallel calls inside the adapter | The worker owns retries, backoff, timeout and the one-call-per-reader limit (AD-043, WRK-06). |
| Changing the normalizer's envelope | If the UAT shows A-13 is wrong, that becomes a follow-up fix with its own spec. This feature measures, it does not retune. |

---

## Assumptions & Open Questions

Facts marked *unverified* come from community material or my recollection rather than from
Hikvision documentation for this model. The official ISAPI wiki requires a login. Each one is
confirmed or corrected by the bench UAT (ISA-33…ISA-38) before the feature closes.

| # | Assumption / decision | Chosen default | Rationale | Confirmed? |
| --- | --- | --- | --- | --- |
| A-1 | ISAPI calls used | The person-based flow: `UserInfo` Record / Modify / Delete, plus the `FDLib` face record under `FDID=1`, `faceLibType=blackFD`, with `FPID` = `employeeNo`. Exact calls (`SetUp` vs Record+Modify) are a design choice. | Matches the community reference and the official wiki's snippets. A community report says `FDSetUp` returned 404/405 on one firmware, so design must not depend on a single call existing. *Unverified on the DS-K1T342.* | n — UAT |
| A-2 | Mapping `ExternalRef` → `employeeNo` | Lowercase hex of the first 16 bytes of SHA-256 over the UTF-8 `ExternalRef`: 32 characters, stable across calls and processes. | `employeeNo` is capped at 32 characters (community reference, *unverified*) while `ExternalRef` allows 255. 128 bits makes a collision across 50,000 spectators negligible. No schema change. | y |
| A-3 | Face refused after the person was created (Add) | Delete the person that was just written, then return `Rejected`. | A reader must never hold a spectator who can enter by PIN but has no face. The retry starts clean. | y |
| A-4 | Classification rule | Everything that would fail every spectator on the reader (transport, TLS, 401/403, lockout, 404/405, 5xx, an unparseable answer) → `Unreachable`. A `ResponseStatus` refusal of this record → `Rejected`. | A firmware or configuration problem is fixed on the reader, not per spectator. Classing it as `Unreachable` pauses the reader, and once the reader is updated or fixed the replicator drains its backlog with nothing lost. Classing it as `Rejected` would dead-letter the whole roster. | y |
| A-5 | Transport | HTTPS, with the reader's self-signed certificate accepted **only by the adapter's HTTP client**. | Faces and PINs are biometric and secret data on the wire; a process-wide bypass would weaken every other HTTPS call. | y |
| A-6 | Name on the reader | `DeviceEnrolment` gains `Name` from `User.Name` (≤ 100 characters). A name longer than the reader's limit is **truncated, never refused**. The limit is a constant measured at UAT. | A name must never stop a spectator getting in. The reader's name limit is unknown (*unverified*). | Name y · limit n — UAT |
| A-7 | PIN | `AccessCode` is written as the person's PIN through `UserInfo`. | The datasheet confirms "platform-applied personal PIN". The field name is *unverified*. | n — UAT |
| A-8 | Access right | Every person is enabled, with door right 1 under plan template 1 and a validity window that never expires in practice. Exact bounds are fixed at design. | Ticket validity is out of scope; the reader only needs to open the door for anyone enrolled. | n |
| A-9 | `Device.Port` | The reader's HTTPS port (443 by default on the reader). | No new device field (Out of Scope). Devices registered with an HTTP port must be re-registered; none exist outside development. | n |
| A-10 | Bench capacity | The DS-K1T342MFWX holds **1,500 faces and 1,500 users** (Hikvision datasheet and release note). It is registered with `FaceCapacity = 1500` and the UAT roster stays ≤ 1,500. | **No 10,000-face unit exists** (user, 2026-11-12), so AD-020/AD-021's "10,000-face bench unit" is wrong. Correcting them, and validating enrolment at full scale, is deferred to the roadmap (`ROADMAP.md` § Known Gaps). This feature proves correctness, not scale. | y |
| A-11 | Retries inside the adapter | None. One attempt per step. | The worker owns retries and the timeout (AD-043). A hidden retry would also count failed logins toward the reader's lockout. | y |
| A-12 | Cancelled mid-enrol (worker timeout, WRK-11) | The adapter stops, compensates nothing and lets the cancellation surface. A half-written person is converged by the retry (ISA-02). | The token is gone, so a compensating call would itself outlive the deadline. | n |
| A-13′ | Image envelope | The stored 40–200 KB, ≥ 640×480 JPEG is expected to be accepted. | Community guidance gives ≤ 200 KB and ≥ 80×80, and firmware V4.48.0 for this series accepts pictures larger than 200 KB. Our band sits inside both. The UAT measures it. | n — UAT |
| A-14 | Lockout | A 401 whose body reports a locked account → `Unreachable`, with the unlock time in the reason. | The reader locks the account after repeated failures (community: about 5 failures, 30 minutes). With pausing after 2 consecutive `Unreachable` outcomes (default), the worker stops before triggering that. | n — UAT |

**Open questions:** none. Everything is resolved, or logged above for confirmation at the UAT.

---

## User Stories

### P1: Enrol a spectator on a reader ⭐ MVP

**User Story**: As a stadium operator, I want a spectator the integrator just created to be
recognisable at every turnstile, so that they can walk in by face or PIN minutes after buying.

**Acceptance Criteria**:

1. **ISA-01** — WHEN `AddAsync` is called THEN the adapter SHALL write the person with
   `employeeNo` = the A-2 hash of `ExternalRef`, the enrolment's name (ISA-26) and its access code
   as PIN, then SHALL write the face under `FDID=1`, `faceLibType=blackFD`, `FPID` = the same
   `employeeNo`, and SHALL return `Delivered` only after both steps succeeded.
2. **ISA-02** — WHEN the person already exists on the reader (for example a retry after a call that
   landed but timed out) THEN `AddAsync` SHALL converge the person to the enrolment's values and
   SHALL NOT return `Rejected` because the person already exists.
3. **ISA-03** — WHEN a face already exists for that `employeeNo` THEN `AddAsync` SHALL leave the
   enrolment's face as the reader's only face for that person and return `Delivered`.
4. **ISA-04** — WHEN the person step succeeds and the reader refuses the face (a `ResponseStatus`
   failure) THEN `AddAsync` SHALL delete that person from the reader and SHALL return `Rejected`
   with a reason naming the face step and the reader's `subStatusCode`.
5. **ISA-05** — WHEN that compensating delete itself fails THEN `AddAsync` SHALL still return
   `Rejected` with the face step's reason, and SHALL say in the reason that compensation failed.
6. **ISA-06** — WHEN the person step succeeds and the face step classifies as `Unreachable`
   THEN `AddAsync` SHALL return `Unreachable` and SHALL NOT attempt compensation.

**Independent Test**: Against the ISAPI stub, an Add produces exactly one person and one face
under the hashed key; a stub that refuses the face leaves no person behind.

---

### P1: Update a spectator on a reader ⭐ MVP

**User Story**: As an integrator, I want a corrected name, PIN or photograph to replace what the
turnstiles hold, so that a spectator is recognised by their current details.

**Acceptance Criteria**:

1. **ISA-07** — WHEN `UpdateAsync` is called THEN the adapter SHALL set the person's name and PIN
   and replace the face, returning `Delivered` only after both steps succeeded.
2. **ISA-08** — WHEN the person is absent from the reader THEN `UpdateAsync` SHALL create the
   person and the face and return `Delivered`.
3. **ISA-09** — WHEN the reader refuses the new face THEN `UpdateAsync` SHALL return `Rejected`
   and SHALL NOT delete the person. The spectator was enrolled before, and deleting them would
   lock them out over a bad photograph.

**Independent Test**: Against the stub, an Update of an existing person changes name, PIN and
face; an Update of an unknown person creates it.

---

### P1: Remove a spectator from a reader ⭐ MVP

**User Story**: As an integrator, I want a deleted spectator gone from every turnstile, so that a
refunded ticket no longer opens a gate and their biometric is not retained (USR-32).

**Acceptance Criteria**:

1. **ISA-10** — WHEN `RemoveAsync` is called THEN the adapter SHALL delete the face and the
   person for the hashed `employeeNo`, returning `Delivered` only after both are gone. Deleting
   the person alone is not enough: the reader keeps a deleted person's face (official wiki note).
2. **ISA-11** — WHEN the face, the person or both are already absent THEN that step SHALL count
   as succeeded. Removing an unknown spectator returns `Delivered`.

**Independent Test**: Against the stub, Remove leaves neither a person nor a face; removing twice
returns `Delivered` both times.

---

### P1: Classify every reader answer ⭐ MVP

**User Story**: As the replication worker, I need every call to end in exactly one of `Delivered`,
`Rejected` or `Unreachable`, so that a bad record is dead-lettered and a bad reader is paused,
and never the other way round.

**Acceptance Criteria**:

1. **ISA-12** — WHEN a step's response is HTTP 2xx with `statusCode` 1 THEN the step SHALL count as
   succeeded.
2. **ISA-13** — WHEN the reader answers with a `ResponseStatus` whose `statusCode` is not 1, on an
   HTTP status other than those in ISA-15…ISA-17, THEN the call SHALL return `Rejected` with the
   reason `"{step}: {subStatusCode} ({errorCode}) {errorMsg}"`, omitting the parts the reader left out.
3. **ISA-14** — WHEN the connection is refused, the host cannot be resolved, the TLS handshake fails,
   or the connection drops before a complete response, THEN the call SHALL return `Unreachable`
   with a reason naming the failure class.
4. **ISA-15** — WHEN the reader answers 401 after the digest exchange, or 403, THEN the call SHALL
   return `Unreachable` with the reason `"authentication failed"`. WHEN the 401 body reports a
   locked account THEN the reason SHALL be `"account locked"` plus the unlock time, if the reader
   gave one.
5. **ISA-16** — WHEN the reader answers 404 or 405 THEN the call SHALL return `Unreachable` with a
   reason naming the ISAPI path the reader does not support.
6. **ISA-17** — WHEN the reader answers 5xx THEN the call SHALL return `Unreachable` with the HTTP
   status in the reason.
7. **ISA-18** — WHEN a response cannot be parsed as a `ResponseStatus` THEN the call SHALL return
   `Unreachable` with the reason `"unrecognised response"` and the HTTP status.
8. **ISA-37** — WHEN a reader has answered `"authentication failed"` or `"account locked"` for a
   given set of credentials THEN every later call to that reader with the **same** credentials
   SHALL return `Unreachable` with the same reason **without contacting the reader**. This lasts
   until the reader's unlock time if it gave one, otherwise for a configured hold (default
   30 minutes), or until the device's credentials change, whichever comes first. *Added at design:
   without it, the worker's pause-then-probe cadence sends about 5 failed logins in 35 s and locks
   the account (ISA-34).*
9. **ISA-19** — The adapter SHALL make at most one HTTP attempt per step, plus at most one
   compensating delete (ISA-04), and SHALL NOT retry anything itself.
10. **ISA-20** — WHEN the cancellation token fires during a call THEN the adapter SHALL stop issuing
   requests, SHALL NOT compensate, and SHALL let `OperationCanceledException` propagate. The worker
   already maps it to `Unreachable` (WRK-11).

**Independent Test**: The stub is scripted per status class; each class yields the specified
outcome and reason.

---

### P1: Reach the reader securely ⭐ MVP

**User Story**: As an operator, I want the adapter to talk to readers the way they are actually
deployed, without exposing credentials or biometrics, so that turning it on is a configuration
change.

**Acceptance Criteria**:

1. **ISA-21** — The adapter SHALL send every request over HTTPS to `IpAddress:Port` of the
   `DeviceEndpoint`, authenticated by HTTP digest with its `Username` and `Password`.
2. **ISA-22** — The adapter SHALL accept the reader's self-signed certificate, and that acceptance
   SHALL apply only to the adapter's HTTP client. Every other outbound HTTPS connection in the
   process SHALL keep default certificate validation.
3. **ISA-23** — WHEN `DeviceClient:Adapter` is `Isapi` THEN `IDeviceClient` SHALL resolve to the
   ISAPI adapter. WHEN it is `Fake` THEN behaviour is unchanged. WHEN it is missing or anything
   else THEN startup SHALL fail with a message naming the setting and both valid values.
4. **ISA-24** — No device password, PIN, face byte or `Authorization` header value SHALL appear
   in any log entry, span attribute, metric tag, or `Rejected`/`Unreachable` reason produced
   while serving a call, whether the call succeeds, is rejected or is unreachable.
5. **ISA-25** — An outcome reason SHALL carry only the step, the HTTP status and the
   `ResponseStatus` fields of ISA-13, never a raw response body.

---

### P1: Carry the name to the reader ⭐ MVP

**Acceptance Criteria**:

1. **ISA-26** — `DeviceEnrolment` SHALL carry the spectator's `Name`, filled from `User.Name` when
   the worker builds an enrolment. WHEN the name exceeds the reader's name limit (A-6) THEN the
   adapter SHALL truncate it to that limit on a character boundary and SHALL NOT refuse the
   enrolment.
2. **ISA-27** — `FakeDeviceClient` SHALL record the `Name` it receives, so that worker tests can
   assert it.

---

### P2: Observe calls to readers

**User Story**: As an operator, I want to see which reader step failed and how long each took, so
that a slow or refusing reader can be diagnosed without packet captures.

**Acceptance Criteria**:

1. **ISA-28** — WHEN tracing is enabled THEN each HTTP request to a reader SHALL produce a client
   span carrying the device id and the step name, exactly one of `person`, `person-modify`,
   `face-delete`, `face` or `person-delete`. A compensating delete (ISA-04) is a `person-delete` span.
2. **ISA-29** — Each adapter call SHALL write one structured log entry with the device id,
   operation, outcome, failing step (if any), HTTP status (if any), `subStatusCode` (if any) and
   duration, at Information for `Delivered` and Warning otherwise.

---

### P1: Prove it on the bench reader (UAT) ⭐ MVP

Run manually against the DS-K1T342MFWX. Evidence (sizes, screenshots or the reader's web UI,
observations) is recorded in `validation.md`.

1. **ISA-30** — WHEN the reader is registered with `FaceCapacity = 1500` and the app runs with
   `Adapter = Isapi` THEN a spectator created through `PUT /api/users/{externalRef}` SHALL reach
   `Succeeded` on that reader, and the spectator SHALL be recognised there by face and, separately,
   by PIN.
2. **ISA-31** — WHEN that spectator's name, PIN and photograph are updated THEN the reader SHALL
   show the new name and accept the new PIN and the new face, and SHALL refuse the old PIN.
3. **ISA-32** — WHEN that spectator is deleted THEN neither their face nor their PIN SHALL open the
   reader, and the reader's own user list SHALL no longer contain them.
4. **ISA-33** — **A-13 closure**: normalized pictures at the edges of the band (as close to 40 KB
   and to 200 KB as the normalizer produces, and at the 640×480 minimum) SHALL be accepted. The
   accepted and refused sizes and dimensions, plus the reader's answers outside the band, SHALL be
   recorded.
5. **ISA-34** — WHEN the reader's registered password is wrong THEN the reader SHALL be paused
   with no item dead-lettered, and the reader's account SHALL NOT be locked.
6. **ISA-35** — WHEN the reader is disconnected mid-backfill and reconnected THEN its backlog
   SHALL drain to `Succeeded` with nothing dead-lettered (WRK-12…WRK-14 against real hardware).
7. **ISA-36** — The reader's actual `employeeNo` and name length limits, the ISAPI calls it
   accepted, and its firmware version SHALL be recorded, confirming or correcting A-1, A-2, A-6
   and A-7.

---

## Edge Cases

- WHEN two different `ExternalRef`s hash to the same `employeeNo` THEN behaviour is undefined.
  The A-2 hash makes this negligible, and no criterion is written for it.
- WHEN the face library on the reader is full THEN the reader's refusal SHALL be `Rejected` under
  ISA-13. This is the capacity case `replication-worker` deferred, and it surfaces as a dead letter
  by design.
- WHEN the reader's clock drifts more than about 5 minutes and digest is refused THEN the call is
  `Unreachable` under ISA-15. Fixing the reader's clock is an operator action.
- WHEN `ExternalRef` contains non-ASCII characters THEN A-2 hashes its UTF-8 bytes, so the
  `employeeNo` is ASCII hex regardless.

---

## Implicit-Requirement Dimensions

| Dimension | Resolution |
| --- | --- |
| Input validation & bounds | ISA-26 (name truncation), A-2 (`employeeNo` length); the face envelope is checked at ingest (USR-15/16), measured by ISA-33 |
| Failure / partial-failure states | ISA-04…ISA-06, ISA-09, ISA-10, ISA-20 |
| Idempotency / retry / duplicate handling | ISA-02, ISA-03, ISA-08, ISA-11, ISA-19 |
| Auth boundaries & rate limits | ISA-15, ISA-21, ISA-34, ISA-37, A-14. Rate: one call per reader at a time is already enforced (WRK-06). |
| Concurrency / ordering | N/A because the worker never issues two calls to one reader at once (WRK-06), and the steps inside one call are sequential. |
| Data lifecycle / expiry | ISA-10 (biometric removed from the reader on delete). Validity windows are out of scope (A-8). |
| Observability | ISA-28, ISA-29; secrets excluded by ISA-24 |
| External-dependency failure | ISA-13…ISA-18; pausing and recovery are the worker's (WRK-11…WRK-14) and are proven on hardware by ISA-35 |
| State-transition integrity | N/A because the adapter holds no persisted state; its only memory is the ISA-37 hold, in process. Replication transitions stay in the aggregate (AD-044). |

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| ISA-01…ISA-06 | P1: Enrol | Design | Pending |
| ISA-07…ISA-09 | P1: Update | Design | Pending |
| ISA-10, ISA-11 | P1: Remove | Design | Pending |
| ISA-12…ISA-20, ISA-37 | P1: Classify | Design | Pending |
| ISA-21…ISA-25 | P1: Reach securely | Design | Pending |
| ISA-26, ISA-27 | P1: Name | Design | Pending |
| ISA-28, ISA-29 | P2: Observe | Design | Pending |
| ISA-30…ISA-36 | P1: Bench UAT | Design | Pending |

**Coverage:** 37 total, 0 mapped to tasks, 37 unmapped ⚠️ (tasks not yet written)

---

## Success Criteria

- [ ] Production starts with `DeviceClient:Adapter = Isapi`, and the `Fake`-only restriction is gone
      from `ROADMAP.md` § Known Gaps.
- [ ] Bench UAT ISA-30…ISA-36 passes, with evidence in `validation.md`.
- [ ] A-13 is closed (confirmed or corrected) in `user-registry`'s spec and `STATE.md`.
- [ ] Zero dead letters caused by a reader-level failure during the UAT.

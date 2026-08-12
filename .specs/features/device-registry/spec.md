# Device Registry Specification

**Feature**: `device-registry` · Phase 1, item 1 · **Scope: Large**
**Status**: confirmed · 2026-08-12
**Governed by**: AD-001…AD-010, AD-013, AD-014, AD-018, AD-019, AD-020, AD-021
**Lessons loaded**: `lessons.py list --status confirmed` → none (store is empty; first feature)

---

## Problem Statement

Nothing can be replicated to a device the system does not know about. This feature
establishes the catalogue of Hikvision readers — their network address, the
credentials needed to authenticate against them, and the face-library capacity that
bounds how many users they can hold. It is the first feature of the rewrite, so it
also carries the walking skeleton: solution layout, PostgreSQL with real migrations,
the error pipeline, tracing, and the test harness every later feature builds on.

---

## Goals

- [ ] An operator can register, inspect, amend, and remove a Hikvision reader through the API.
- [ ] Two devices can never occupy the same network address — enforced by the database, not by a racy read-then-write check.
- [ ] Device passwords are stored reversibly encrypted and are never exposed in any response, log, or trace.
- [ ] Each device declares its face-library capacity, so Phase 2 can refuse to overfill it (AD-021 mitigation).
- [ ] A running solution on PostgreSQL with applied migrations, RFC 7807 errors, OpenTelemetry traces, and a Testcontainers-backed integration test suite.

---

## Out of Scope

| Feature | Reason |
|---|---|
| Verifying credentials against the real device at registration | Requires `IDeviceClient`, which is faked until Phase 3 (AD-017). Registration accepts credentials on trust. |
| Device reachability / health tracking | Phase 4 `device-health`. |
| Replication fan-out when a device is registered | Phase 2 `replication-queue`. This feature only stores devices. |
| Authentication and authorization on the endpoints | Phase 4 `api-auth`. See assumption A-6 — this is a knowingly accepted risk, not an oversight. |
| Device grouping, gates, zones, access scoping | Excluded by AD-015 and AD-021. |
| Firmware/model detection, capability discovery | No use case yet; capacity is operator-supplied (AD-020). |
| Bulk device import | Device counts are in the dozens-to-hundreds; one-at-a-time registration is sufficient. |

---

## Assumptions & Open Questions

Every ambiguity is resolved or recorded here — nothing is left silently unclear.

| # | Assumption / decision | Chosen default | Rationale | Confirmed? |
|---|---|---|---|---|
| A-1 | Which IP versions are accepted | Any address `IPAddress.TryParse` accepts (IPv4 or IPv6) | The parser is the validation rule; restricting to IPv4 would be an extra constraint with no stated need | **y** |
| A-2 | How addresses are compared for uniqueness | Store and compare the **normalized** form (`IPAddress.ToString()`), so `192.168.001.001` and `192.168.1.1` collide | Without normalization the unique index is bypassable by rewriting the same address | **y** |
| A-3 | Uniqueness enforcement mechanism | A database unique index on `(IpAddress, HttpPort)`; the pre-check exists only to return a friendly 409 | A read-then-write check alone is racy under concurrent registration — the reference implementation had exactly this bug | **y** |
| A-4 | Face capacity bounds | Required, integer, `1…1,000,000` | Must be positive to be meaningful; the upper bound is a sanity guard against typos, not a hardware fact | **y** |
| A-5 | What happens to a device's replications when it is deleted | Deleting a device cancels its pending replications | Phase 2 concern, stated now so `replication-queue` inherits a decided rule rather than inventing one. No effect on this feature's code. | **y** |
| A-6 | No authentication in this feature | Endpoints are anonymous | Deferred to Phase 4 by roadmap. **Accepted risk**: these endpoints accept and store device credentials, so anyone with network access can register devices or harvest the catalogue. Must not reach a routable network before `api-auth` ships. | **y** |
| A-7 | Password update semantics | Omitting `password` on update leaves the stored one unchanged; supplying it replaces it | Matches partial-update semantics of the other fields; there is no way to "clear" a password since it is mandatory | **y** |
| A-8 | Encryption mode | AES-256-CBC as inherited from AD-008 | Carried forward unchanged. **Noted weakness**: CBC gives confidentiality without integrity — a tampered ciphertext fails at decrypt time rather than being detected. Upgrading to AES-GCM is a candidate for `api-auth`/security hardening, not this feature. | **y** |
| A-9 | Device name uniqueness | Names need not be unique | No stated need; the address is the identity | **y** |

**Open questions:** none — all resolved or logged above.

---

## Implicit-Requirement Dimensions Sweep

Full sweep (Large scope) — every dimension resolves to a requirement or an explicit `N/A`.

| Dimension | Resolution |
|---|---|
| Input validation & bounds | DEV-02, DEV-03, DEV-04 |
| Failure / partial-failure states | DEV-14 (database unavailable), DEV-15 (misconfigured encryption key fails at startup) |
| Idempotency / retry / duplicate handling | DEV-05 — re-registering the same address is rejected, not duplicated. No client-supplied idempotency key: creation is not retried automatically by any caller. |
| Auth boundaries & rate limits | **N/A because** authentication is Phase 4 `api-auth` (assumption A-6, accepted risk). No rate limiting: the caller is a single trusted integrator on a private network. |
| Concurrency / ordering | DEV-06 — concurrent registration of one address yields exactly one device and one conflict |
| Data lifecycle / expiry | DEV-11 (removal). **N/A** for TTL/archival — devices are permanent until explicitly removed. |
| Observability | DEV-16, DEV-17 |
| External-dependency failure | **N/A because** this feature contacts no device or external service; it is database-only. Device communication arrives in Phase 3. |
| State-transition integrity | **N/A because** `Device` has no lifecycle states in the MVP — it exists or it does not. Health states arrive in Phase 4. |

---

## User Stories

### P1: Register a device ⭐ MVP

**User Story**: As an operator, I want to register a Hikvision reader with its address, credentials, and face capacity, so that the system can later replicate users to it.

**Why P1**: Nothing downstream exists without a device catalogue.

**Acceptance Criteria**:

1. `DEV-01` — WHEN a registration request supplies a valid name, IP address, HTTP port, username, password, and face capacity THEN the system SHALL persist the device and respond `201 Created` with a `Location` header of `/api/devices/{id}` and a body containing the device's `id`, `name`, `ipAddress`, `httpPort`, `username`, `faceCapacity`, `createdAt`, and `updatedAt`.
2. `DEV-02` — WHEN a registration request omits or blanks `name`, `ipAddress`, `httpPort`, `username`, `password`, or `faceCapacity` THEN the system SHALL reject it with `400` and a validation problem naming the specific offending field.
3. `DEV-03` — WHEN `name` or `username` exceeds 100 characters THEN the system SHALL reject with `400` naming that field.
4. `DEV-04` — WHEN `ipAddress` is not a parseable IP address, or `httpPort` is outside `1…65535`, or `faceCapacity` is outside `1…1,000,000` THEN the system SHALL reject with `400` naming that field.
5. `DEV-05` — WHEN a registration supplies an address already held by another device THEN the system SHALL reject it with `409 Conflict` and SHALL NOT create a second device.
6. `DEV-06` — WHEN two registrations for the same address are submitted concurrently THEN the system SHALL persist exactly one device and reject the other with `409`, with no unhandled exception surfacing to either caller.
7. `DEV-07` — WHEN a device is stored THEN the system SHALL store the password AES-256 encrypted, SHALL store the IP address in normalized form, and SHALL NOT include the password or its ciphertext in the response body, application logs, or trace attributes.

**Independent Test**: `POST /api/devices` with a valid body returns 201 and a `Location`; following that `Location` returns the same device with no password field; a second POST to the same `ipAddress:httpPort` returns 409; the persisted row's password column is unreadable ciphertext.

---

### P1: Inspect the device catalogue ⭐ MVP

**User Story**: As an operator, I want to list all registered devices and retrieve one by id, so that I can confirm what the system will replicate to.

**Why P1**: Registration is unverifiable without read-back, and Phase 2 fan-out is defined over "all registered devices".

**Acceptance Criteria**:

1. `DEV-08` — WHEN devices are registered THEN listing them SHALL return `200` with every registered device, each carrying the same fields as the registration response and never a password.
2. `DEV-09` — WHEN no devices are registered THEN listing them SHALL return `200` with an empty array, not `404`.
3. `DEV-10` — WHEN a device is requested by an id that exists THEN the system SHALL return `200` with that device; WHEN the id does not exist THEN the system SHALL return `404` with an RFC 7807 problem body.

**Independent Test**: List with an empty database returns `[]`; register two devices and list returns both; get by a known id returns one; get by an unknown id returns 404.

---

### P1: Operational foundation ⭐ MVP

**User Story**: As a developer, I want the solution to run on PostgreSQL with applied migrations, consistent error responses, and traces, so that every later feature builds on a working skeleton instead of re-deciding infrastructure.

**Why P1**: This is the rewrite's first commit (AD-013). Every subsequent feature depends on it.

**Acceptance Criteria**:

1. `DEV-12` — WHEN the application starts against an empty PostgreSQL database THEN it SHALL apply all EF Core migrations and start successfully. The system SHALL NOT use `EnsureCreated()`.
2. `DEV-13` — WHEN the integration test suite runs THEN it SHALL execute against a real PostgreSQL instance provisioned by Testcontainers (AD-019), with state isolated between tests.
3. `DEV-14` — WHEN the database is unreachable during a request THEN the system SHALL return `503` as an RFC 7807 problem body and SHALL NOT leak a stack trace or connection string to the caller.
4. `DEV-15` — WHEN `Encryption:Key` is missing or is not a 32-byte Base64 value THEN the application SHALL fail at startup with a clear diagnostic, rather than starting and failing on the first registration.
5. `DEV-16` — WHEN a request is handled THEN the system SHALL emit an OpenTelemetry trace containing the HTTP span and its child EF Core spans, exported only when `OpenTelemetry:OtlpEndpoint` is configured.
6. `DEV-17` — WHEN the application runs in Development THEN it SHALL expose the OpenAPI document and Scalar UI; WHEN it runs outside Development THEN it SHALL NOT.

**Independent Test**: `docker compose up -d` then `dotnet run` against an empty database starts and serves requests; `dotnet test` passes with only Docker available; removing `Encryption:Key` prevents startup.

---

### P2: Amend a registered device

**User Story**: As an operator, I want to correct a device's address, credentials, name, or capacity, so that a re-addressed or re-credentialed reader keeps working without being removed and re-added.

**Why P2**: Recoverable by delete-then-register in a pinch, so it is not strictly MVP — but re-adding loses the device's identity, which Phase 2 replications will reference.

**Acceptance Criteria**:

1. `DEV-18` — WHEN an update supplies a subset of fields THEN the system SHALL apply only the supplied fields, leave the others unchanged, and return `200` with the updated device.
2. `DEV-19` — WHEN an update supplies a field that violates any DEV-03/DEV-04 rule THEN the system SHALL reject with `400` naming that field and SHALL persist no partial change.
3. `DEV-20` — WHEN an update moves a device onto an address held by a *different* device THEN the system SHALL reject with `409`; WHEN the address resolves to the device being updated itself THEN the system SHALL accept it.
4. `DEV-21` — WHEN an update omits `password` THEN the stored password SHALL be unchanged; WHEN it supplies one THEN the stored ciphertext SHALL be replaced.
5. `DEV-22` — WHEN an update targets an unknown id THEN the system SHALL return `404`.
6. `DEV-23` — WHEN any field actually changes THEN `updatedAt` SHALL advance and `createdAt` SHALL NOT.

**Independent Test**: Register a device, update only its name, and confirm address and capacity are untouched and `updatedAt` advanced; attempt to move it onto a second device's address and get 409.

---

### P2: Remove a device

**User Story**: As an operator, I want to remove a decommissioned reader, so that the system stops treating it as a replication target.

**Why P2**: A stale device causes failing replications rather than incorrect access, so it degrades operations without breaking them.

**Acceptance Criteria**:

1. `DEV-11` — WHEN an existing device is removed THEN the system SHALL return `204`, and the device SHALL no longer appear in the catalogue or be retrievable by id.
2. `DEV-24` — WHEN removal targets an unknown id THEN the system SHALL return `404`.
3. `DEV-25` — WHEN a device is removed THEN its address SHALL become available for a new registration.

**Independent Test**: Register, delete, then confirm get-by-id returns 404 and re-registering the same address succeeds.

---

### P3: Paginate the catalogue

**User Story**: As an operator with a large reader fleet, I want the device list paginated, so that the response stays bounded.

**Why P3**: Device counts are dozens to hundreds; an unbounded list is acceptable at that size and can be added without breaking callers.

**Acceptance Criteria**:

1. `DEV-26` — WHEN the list is requested with paging parameters THEN the system SHALL return that page plus a total count, with a documented default and maximum page size.

---

## Edge Cases

- WHEN the same address is written in a non-canonical form (`192.168.001.001`) THEN the system SHALL treat it as equal to its canonical form and reject the duplicate (A-2).
- WHEN `httpPort` is `0` or `65536` THEN the system SHALL reject with `400` — the boundaries `1` and `65535` SHALL be accepted.
- WHEN `faceCapacity` is `0` or negative THEN the system SHALL reject with `400`.
- WHEN `name` or `username` is exactly 100 characters THEN the system SHALL accept it; at 101 it SHALL reject.
- WHEN an update body is entirely empty THEN the system SHALL return `200` with the device unchanged and `updatedAt` unadvanced (DEV-23 — no change means no touch).
- WHEN a password contains multi-byte UTF-8 characters THEN it SHALL round-trip through encryption and decryption unchanged.
- WHEN the request body is malformed JSON THEN the system SHALL return `400` as a problem body, not `500`.
- WHEN a device id is a valid integer that has never existed, or has been deleted, THEN both SHALL return `404` indistinguishably.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
|---|---|---|---|
| DEV-01 | P1: Register | Design | Pending |
| DEV-02 | P1: Register | Design | Pending |
| DEV-03 | P1: Register | Design | Pending |
| DEV-04 | P1: Register | Design | Pending |
| DEV-05 | P1: Register | Design | Pending |
| DEV-06 | P1: Register | Design | Pending |
| DEV-07 | P1: Register | Design | Pending |
| DEV-08 | P1: Inspect | Design | Pending |
| DEV-09 | P1: Inspect | Design | Pending |
| DEV-10 | P1: Inspect | Design | Pending |
| DEV-12 | P1: Foundation | Design | Pending |
| DEV-13 | P1: Foundation | Design | Pending |
| DEV-14 | P1: Foundation | Design | Pending |
| DEV-15 | P1: Foundation | Design | Pending |
| DEV-16 | P1: Foundation | Design | Pending |
| DEV-17 | P1: Foundation | Design | Pending |
| DEV-18 | P2: Amend | Design | Pending |
| DEV-19 | P2: Amend | Design | Pending |
| DEV-20 | P2: Amend | Design | Pending |
| DEV-21 | P2: Amend | Design | Pending |
| DEV-22 | P2: Amend | Design | Pending |
| DEV-23 | P2: Amend | Design | Pending |
| DEV-11 | P2: Remove | Design | Pending |
| DEV-24 | P2: Remove | Design | Pending |
| DEV-25 | P2: Remove | Design | Pending |
| DEV-26 | P3: Paginate | — | Pending |

**Coverage:** 26 total, 0 mapped to tasks, 26 unmapped ⚠️ (expected — Tasks phase not yet run)

---

## Success Criteria

- [ ] All 25 P1+P2 requirements verified by integration tests running against Testcontainers PostgreSQL.
- [ ] A concurrent double-registration test proves DEV-06 — exactly one device persisted, one 409, no unhandled exception.
- [ ] No password, ciphertext, or encryption key appears in any response body, log line, or trace attribute across the whole suite.
- [ ] `dotnet run` against an empty PostgreSQL database starts, migrates, and serves a registration end-to-end with no manual database step.
- [ ] The reference implementation under `src/` is deleted in this feature's first commit (AD-013).

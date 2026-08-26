# User Registry Specification

**Feature**: `user-registry` — Phase 1, item 2 of [`ROADMAP.md`](../../ROADMAP.md)
**Status**: **confirmed** · 2026-08-24
**Scope**: Large — full Specify → Design → Tasks → Execute
**Governed by**: AD-014 (latency is the primary quality attribute), AD-015 (every user on
every device), AD-016 (external system of record, delete is first-class), AD-021 (no scoping),
AD-022 (DB-enforced uniqueness), AD-023 (injected `TimeProvider`), AD-024/AD-026 (test levels).

---

## Problem Statement

The product's core capability — putting a spectator's face on every turnstile — has nothing to
operate on. There is no user catalogue. An external integrator sells a ticket and must be able to
push that spectator into this service, correct them, and remove them, keyed by the integrator's
own identifier.

The registry is also where the face image problem is solved or deferred. Hikvision terminals
accept a *narrow* image envelope (JPEG, ≥ 640×480, **40–200 KB**), while integrators hold
multi-megabyte phone photos. If this feature does not reconcile the two, every integrator
reimplements the same resize pipeline — and gets it wrong in the same place — with the failure
surfacing at a gate, three phases later, as a spectator who cannot get in.

## Goals

- [ ] An integrator can create, correct, look up and remove a spectator through one
      `ExternalRef`-keyed resource, with retries that are safe by construction.
- [ ] Any reasonable photograph an integrator holds is accepted and converted into an image the
      device will actually enrol — the device's byte and pixel envelope is never the caller's problem.
- [ ] A removal leaves behind exactly what Phase 2 needs to push a Remove to every device,
      and nothing more — the biometric is destroyed at the moment of deletion.
- [ ] Resolve ROADMAP **OD-4** (face-image storage) with a decision the replication engine inherits.

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| Replication fan-out when a user is created, changed or deleted | Phase 2 `replication-queue`. This feature only owns the catalogue. |
| Purging tombstoned users | The purge trigger is "every device confirmed removal", which only Phase 2 can know. This feature sets the tombstone and never clears it (A-6). |
| Face *detection* — verifying a single, frontal, unobstructed face | Needs a detection model, not a resize pipeline. Deliberate known gap; see § Known Gap. |
| Authentication, authorization, rate limiting | Phase 4 `api-auth`. See A-11 — a knowingly accepted risk that is **worse here than in `device-registry`**. |
| Access scoping (user → subset of devices) | Excluded by AD-015 / AD-021. |
| Bulk import endpoint | 50,000 users is a client-side loop over an idempotent PUT. Revisit only if the bulk-load window (ROADMAP, open) proves it insufficient. |
| Serving the stored image back to callers | No stated use case. Responses carry the hash, byte size and dimensions, never the bytes. |
| Storing the integrator's original upload | 50k × 4 MB ≈ 200 GB, and it doubles the biometric data at risk (A-8). |

---

## Assumptions & Open Questions

Every ambiguity raised during clarification is resolved here or logged. Nothing is left silently unclear.

| # | Assumption / decision | Chosen default | Rationale | Confirmed? |
| - | --------------------- | -------------- | --------- | ---------- |
| A-1 | **OD-4 — where the face image lives** | A `FacePicture` table, 1:1 with `User`, holding the bytes; `User` carries only the content hash, byte size and dimensions | Solves OD-4's real complaint — a fat `User` row bloating every catalogue query — without a second consistency domain. No orphaned blobs, no compensating writes, one transaction. The hash gives Phase 2 free change detection. Postgres TOASTs ~7.5 GB of `bytea` without difficulty. **Resolves OD-4.** | **y** |
| A-2 | Write API shape | `PUT /api/users/{externalRef}` as an idempotent upsert; 201 on create, 200 on update | The integrator owns the key, so it can name the resource. A retried ticket-purchase call cannot duplicate or spuriously 409 — the idempotency dimension is satisfied by construction rather than by a rule. Deliberately diverges from `device-registry`'s POST+PATCH, because there the server owns the key. | **y** |
| A-3 | Face picture required at creation | Mandatory — a user cannot be created without one | The product is facial recognition; a faceless record is a spectator who will be stopped at the gate. **Consequence, accepted:** the integrator must buffer ticket-sold-but-no-photo-yet; no user record exists until a photo does, so no replication and no gate entry. This is a constraint on the integration contract, not an implementation detail. | **y** |
| A-4 | Face picture on **update** | Omitting it means "keep the stored image"; supplying it replaces the stored image | Preserves A-3 — there is no path to a faceless record — while making a name-only correction cheap. Exact precedent: `device-registry` A-7 does this for passwords. Cost: `PUT` is not a pure full-representation replace. | **y** |
| A-5 | Deletion semantics | Tombstone: the row survives marked deleted; **the face bytes are destroyed immediately** | Phase 2's Remove work needs a live FK target and the identity fields; it does **not** need the biometric. Destroying the image at deletion is the strongest privacy posture available and reclaims the storage at once. | **y** |
| A-6 | Who purges tombstones | Nobody, in this feature | The trigger is "all devices confirmed removal", knowable only by `replication-queue`/`replication-worker`. Stated now so Phase 2 inherits a decided rule instead of inventing one. | **y** |
| A-7 | Re-upserting a tombstoned `ExternalRef` | Resurrects the user: the tombstone clears and the record is rewritten. Because A-5 destroyed the image, **a face is mandatory again** — a resurrection is a create for validation purposes | Follows from upsert semantics, and the resulting pending-Remove-then-Add race is already covered by the ROADMAP's feature-3 rule that a newer intent supersedes an older pending one for the same (user, device). | **y** |
| A-8 | The integrator's original upload | Discarded after normalization; only the canonical derivative is stored | Retaining originals costs ~200 GB and doubles biometric exposure. **Accepted cost:** if Phase 3 reveals a different real device envelope, images must be re-collected, not re-derived. | **y** |
| A-9 | Image transport | Base64 inside the JSON body, not `multipart/form-data` | `System.Text.Json` maps `byte[]` to base64 automatically; it matches the JSON-everywhere style of the device slices. Base64 inflation (8 MB → ~10.7 MB) is accepted for one content type and simpler tests. | **y** |
| A-10 | Access code rules | Required, digits only, 4–20 characters, **unique across users** | Two spectators sharing a PIN is an access-control defect — either can open the gate as the other. Enforced per AD-022 by a DB unique index surfaced as `ConflictError`. Cost: a second conflict path, constraining the integrator's code-generation scheme. | **y** |
| A-11 | No authentication in this feature | Endpoints are anonymous, per Phase 4 deferral | **Accepted risk, and materially worse than `device-registry` A-6.** These endpoints (a) ingest and store *biometric* personal data and (b) decode attacker-supplied images, making them a CPU and memory exhaustion vector. USR-19/USR-20 bound the decode cost, but nothing bounds request *rate*. **Must not reach a routable network before `api-auth` ships.** | **y** |
| A-12 | Resolution floor reading | The device rule "more than 640 × 480" is read as **shorter edge ≥ 480 px and longer edge ≥ 640 px**, so it holds in either orientation | The documented phrasing is ambiguous between "both dimensions", "that orientation" and "pixel count". This reading satisfies every interpretation and admits ordinary portrait photographs, which a literal width-≥-640 reading would reject. | **y** |
| A-13 | Accepted output envelope | JPEG, shorter edge ≥ 480 and longer edge ≥ 640, shorter edge < 2160 and longer edge < 3840, **40 KB ≤ size ≤ 200 KB** | Taken from Hikvision's official DS-K1T606 face-terminal documentation. A community ISAPI guide states a laxer 80×80 / ≤ 200 KB envelope for `/ISAPI/Intelligent/FDLib/FaceDataRecord`, but presents it as the author's own device testing, not cited documentation; the official ISAPI wiki is behind a JS app and could not be fetched. **Designing to the stricter official figure**: satisfying it is free, and erring the other way means spectators failing at a gate. Revisit in Phase 3 `isapi-device-client` against real hardware. | **y** |
| A-14 | Imaging library | **SkiaSharp** (MIT), with `SkiaSharp.NativeAssets.Linux` for containers | ImageSharp v4 fails the build without a committed `sixlabors.lic`, and its free sample licence expires 2026-09-04 — an unacceptable CI dependency. Magick.NET (Apache-2.0) is viable but a far larger native footprint than decode/rotate/resize/encode needs. | **y** |
| A-15 | `ExternalRef` character set and comparison | Any non-blank string ≤ 255 chars **that does not contain `/`**, compared **case-sensitively** and byte-exactly | It is an opaque foreign identifier; imposing a format would reject valid integrator keys. Case-sensitive because folding could collide two distinct upstream identities — a silent, unrecoverable merge of two spectators. **Amended 2026-08-25 (T24), narrowing the character set:** the reference is addressed as a single path segment, and ASP.NET Core routing deliberately leaves `%2F` encoded in a route value rather than decoding it into a separator. A key containing `/` therefore arrives as the literal text `TICKET%2F2026` and is registered under *that*, not under `TICKET/2026` — a silent substitution of one identity for another, which is worse than a refusal. Confirmed against both the in-memory test server and a real Kestrel socket, so it is not a harness artefact. **No code changed to hide it**: the `/` never reaches the application, so there is nothing for the domain to reject. `/` is excluded from the integrator contract instead. | **y** |
| A-16 | Repeated `DELETE` | Deleting an already-tombstoned user returns 204, not 404 | Makes `DELETE` idempotent so an integrator retry is safe, consistent with A-2's motivation. 404 is reserved for an `ExternalRef` that was never registered. | **y** |

**Open questions:** none — every assumption above is confirmed. A-7, A-12, A-15 and A-16 were
agent-chosen defaults, reviewed and accepted on 2026-08-24. **A-13 is confirmed as the design
target but carries a standing Phase 3 verification obligation**: the official ISAPI face-record
envelope could not be read directly, so `isapi-device-client` must verify the 40–200 KB band and
the 640×480 floor against real hardware and supersede this assumption if they differ.

---

## Implicit-Requirement Dimensions Sweep

Large scope — every dimension resolves to a requirement or an explicit `N/A because …`.

| Dimension | Resolution |
| --------- | ---------- |
| Input validation & bounds | USR-03, USR-04, USR-05 (identity fields); USR-15…USR-21 (image envelope, upload cap, decode-bomb cap) |
| Failure / partial-failure states | USR-10 — the `User` row and its `FacePicture` are written in one transaction, so a failed image write never leaves a user without a face (A-3); USR-27 — a rejected update leaves the aggregate untouched; USR-39 — database unavailability surfaces as ProblemDetails |
| Idempotency / retry / duplicate handling | USR-01/USR-23 — `PUT` upsert is idempotent by construction (A-2); USR-26 — a byte-identical re-upsert does not advance `UpdatedAt`; USR-32 — repeated `DELETE` is idempotent (A-16) |
| Auth boundaries & rate limits | **N/A because** authentication is Phase 4 `api-auth` (A-11). No rate limiting: a single trusted integrator on a private network. **Recorded as an elevated accepted risk** — this endpoint decodes untrusted images without authentication; USR-19/USR-20 bound per-request cost, nothing bounds request rate. |
| Concurrency / ordering | USR-07 — concurrent upsert of one `ExternalRef` yields exactly one user; USR-08 — a concurrent access-code collision yields exactly one conflict. Both per AD-022: the DB constraint is the authority, never a pre-check. |
| Data lifecycle / expiry | USR-29/USR-30 — tombstone on delete, face bytes destroyed immediately (A-5); A-6 — purge is Phase 2's. **N/A** for TTL/archival: a spectator record has no expiry independent of the integrator's own delete. |
| Observability | USR-40, USR-41 — traced spans and metrics, including **normalization duration**, which is the one CPU-bound step on the write path AD-014 makes latency-critical |
| External-dependency failure | **N/A because** this feature makes no outbound call. Normalization is in-process (A-14); device communication arrives in Phase 3. |
| State-transition integrity | USR-29, USR-31, USR-34 — `Active → Deleted` is the only transition; a tombstoned user is invisible to reads, and `Deleted → Active` occurs only through the resurrection path of A-7, which re-imposes every create-time rule |

---

## User Stories

### P1: Register a spectator ⭐ MVP

**User Story**: As an integrator, I want to push a ticket-holder into the registry under my own
identifier, so that they become eligible for enrolment on every turnstile.

**Why P1**: Nothing else in the product exists without a user catalogue.

**Acceptance Criteria**:

1. **USR-01** — WHEN a valid `PUT /api/users/{externalRef}` names an `ExternalRef` that is not registered THEN the system SHALL create the user and respond `201 Created` with a `Location` header addressing that same URL.
2. **USR-02** — WHEN `{externalRef}` is blank or exceeds 255 characters THEN the system SHALL reject the request with a validation error naming the `externalRef` field.
3. **USR-03** — WHEN `name` is absent, blank, or exceeds 100 characters THEN the system SHALL reject the request with a validation error naming the `name` field.
4. **USR-04** — WHEN `accessCode` is absent, contains any non-digit character, or is shorter than 4 or longer than 20 digits THEN the system SHALL reject the request with a validation error naming the `accessCode` field.
5. **USR-05** — WHEN a create request omits the face picture THEN the system SHALL reject it with a validation error naming the `facePicture` field (A-3).
6. **USR-06** — WHEN `accessCode` is already held by another active user THEN the system SHALL reject the request as a conflict, and the rejection SHALL originate from the database constraint, not from a pre-check (AD-022).
7. **USR-07** — WHEN two requests concurrently upsert the same unregistered `ExternalRef` THEN exactly one SHALL create the user and the registry SHALL hold exactly one user for that `ExternalRef`.
8. **USR-08** — WHEN two requests concurrently create distinct users claiming the same `accessCode` THEN exactly one SHALL succeed and the other SHALL be rejected as a conflict.
9. **USR-09** — WHEN any user representation is returned THEN it SHALL contain the content hash, byte size and pixel dimensions of the stored face picture, and SHALL NOT contain the image bytes.
10. **USR-10** — WHEN the face picture cannot be persisted THEN the system SHALL persist no user for that `ExternalRef`, so a user without a face picture cannot exist in the registry.
11. **USR-11** — WHEN a user is created THEN `CreatedAt` and `UpdatedAt` SHALL both be the value supplied by the injected `TimeProvider` (AD-023).

**Independent Test**: `PUT /api/users/TICKET-1` with a name, an access code and a photograph
returns 201; a follow-up `GET /api/users/TICKET-1` returns that spectator with an image hash and
no image bytes.

---

### P1: Normalize the face picture ⭐ MVP

**User Story**: As an integrator, I want to send the photograph I already hold and have it accepted,
so that I never have to learn or reimplement a device's image envelope.

**Why P1**: This is the difference between an integration that works and one where every partner
independently rediscovers a 40 KB lower bound at a turnstile. It is also where OD-4's storage
figure is actually determined.

**Acceptance Criteria**:

1. **USR-12** — WHEN a decodable image is supplied in any accepted input format THEN the system SHALL store a canonical **JPEG** derivative and SHALL NOT store the original (A-8).
2. **USR-13** — WHEN the source image carries an EXIF orientation tag THEN the system SHALL apply that rotation to the pixels **before** discarding metadata, so the stored face is upright.
3. **USR-14** — WHEN an image is normalized THEN the stored derivative SHALL carry no EXIF or metadata from the source, in particular no GPS location.
4. **USR-15** — WHEN an image is normalized THEN the stored derivative's byte size SHALL be **at least 40 KB and at most 200 KB** (A-13). *An upper bound alone is insufficient: over-compression is a device rejection cause.*
5. **USR-16** — WHEN an image is normalized THEN the stored derivative SHALL have a shorter edge ≥ 480 px and a longer edge ≥ 640 px, and a shorter edge < 2160 px and a longer edge < 3840 px (A-12, A-13).
6. **USR-17** — WHEN the source image is smaller than the floor in USR-16 THEN the system SHALL reject it with a validation error stating the minimum, and SHALL NOT upscale it to satisfy the floor. *Upscaling would manufacture a compliant file that cannot be recognised.*
7. **USR-18** — WHEN the source image's aspect ratio differs from the derivative's target THEN the system SHALL preserve the source aspect ratio and SHALL NOT crop.
8. **USR-19** — WHEN the supplied image exceeds 8 MB of decoded-from-base64 bytes THEN the system SHALL reject it without attempting to decode it.
9. **USR-20** — WHEN the supplied image declares pixel dimensions above the decode cap THEN the system SHALL reject it **before** allocating a decode buffer, so a small compressed file that expands to gigabytes cannot exhaust memory.
10. **USR-21** — WHEN the supplied bytes are not a decodable image THEN the system SHALL reject them with a validation error naming the `facePicture` field.
11. **USR-22** — WHEN an image is stored THEN the system SHALL record its content hash, byte size and pixel dimensions on the user, so Phase 2 can detect a changed face without reading the bytes (A-1).

**Independent Test**: `PUT` a 4 MB, 4000×3000, EXIF-rotated JPEG; the stored derivative is upright,
between 40 and 200 KB, within the pixel envelope, and carries no GPS tag. `PUT` a 320×240 thumbnail;
it is rejected rather than upscaled.

---

### P1: Amend a registered spectator ⭐ MVP

**User Story**: As an integrator, I want to correct a spectator's details — including replacing a
poor photograph — so that a bad record can be fixed before the gates open.

**Why P1**: A misspelled name or an unusable photo discovered at 19:00 must be fixable at 19:01.

**Acceptance Criteria**:

1. **USR-23** — WHEN a valid `PUT /api/users/{externalRef}` names an already-registered `ExternalRef` THEN the system SHALL update that user and respond `200 OK`.
2. **USR-24** — WHEN an update omits the face picture THEN the stored image, its hash, its byte size and its dimensions SHALL all remain unchanged (A-4).
3. **USR-25** — WHEN an update supplies a face picture THEN it SHALL be normalized under every rule of the Normalize story and SHALL replace the stored image and its recorded hash, size and dimensions.
4. **USR-26** — WHEN an update supplies values byte-identical to the stored ones THEN `UpdatedAt` SHALL NOT advance (AD-023).
5. **USR-27** — WHEN any field of an update is rejected THEN no field SHALL be applied and the stored user, including its image, SHALL be left exactly as it was.
6. **USR-28** — WHEN an update sets `accessCode` to one already held by a different active user THEN the system SHALL reject the request as a conflict.

**Independent Test**: `PUT` an existing user with a corrected name and no image; the response shows
the new name and the original image hash, and a second identical `PUT` leaves `UpdatedAt` unmoved.

---

### P1: Remove a spectator ⭐ MVP

**User Story**: As an integrator, I want to remove a spectator, so that a refunded or revoked ticket
stops opening a turnstile.

**Why P1**: AD-016 makes removal first-class — the integrator owns removals, and Phase 2's Remove
path only ever fires from here.

**Acceptance Criteria**:

1. **USR-29** — WHEN `DELETE /api/users/{externalRef}` names an active user THEN the system SHALL respond `204 No Content` and mark the user deleted **without removing its row**, so Phase 2 retains a valid replication target (A-5).
2. **USR-30** — WHEN a user is deleted THEN its stored face picture bytes SHALL be destroyed in the same transaction, while its identity fields survive (A-5).
3. **USR-31** — WHEN a user is deleted THEN it SHALL become invisible to every read path, which SHALL report it as not found.
4. **USR-32** — WHEN `DELETE` names an already-deleted user THEN the system SHALL respond `204 No Content` (A-16).
5. **USR-33** — WHEN `DELETE` names an `ExternalRef` that was never registered THEN the system SHALL respond with a not-found error.
6. **USR-34** — WHEN a `PUT` names a deleted `ExternalRef` THEN the system SHALL resurrect that user, clearing the deleted mark, SHALL require a face picture in that request exactly as at creation (A-7), and SHALL respond **`201 Created`** with a `Location` header. *Added 2026-08-26: the original criterion named no status. `201` because USR-31 makes a removed spectator report as not found on every read path — a client that saw 404 on `GET` and then 200 on `PUT` would be told the record had been there all along. The surviving row is bookkeeping for Phase 2's Remove work (A-5), not something the caller can observe; USR-23's `200` applies to an* active *registration.*

**Independent Test**: Delete a user, confirm `GET` reports not found and the image bytes are gone
from storage, then `PUT` the same `ExternalRef` without an image and see it rejected — and with an
image, see it resurrected.

---

### P1: Look up a spectator ⭐ MVP

**User Story**: As an operator, I want to ask whether a spectator is registered and what we hold for
them, so that I can answer a question at a gate during an event.

**Why P1**: Without it, "is this person in the system?" requires database access during a live event.

**Acceptance Criteria**:

1. **USR-35** — WHEN `GET /api/users/{externalRef}` names an active user THEN the system SHALL respond `200 OK` with that user's identity fields, timestamps, and face-picture hash, byte size and dimensions.
2. **USR-36** — WHEN `GET /api/users/{externalRef}` names an unregistered or deleted `ExternalRef` THEN the system SHALL respond with a not-found error.
3. **USR-37** — WHEN a user is returned THEN the response SHALL NOT contain the face picture bytes (USR-09).

**Independent Test**: `GET` a registered spectator and read back the same hash the `PUT` reported.

---

### P1: Operational foundation ⭐ MVP

**User Story**: As an operator, I want the registry's schema, failures and timings to be visible,
so that I can diagnose it under event load.

**Why P1**: AD-014 makes write latency the primary quality attribute, and normalization is the
only CPU-bound step on that path — an unmeasured one is unmanageable.

**Acceptance Criteria**:

1. **USR-38** — WHEN the application starts THEN the `User` and `FacePicture` schema SHALL be created by an EF Core migration applied at startup, never by `EnsureCreated()`.
2. **USR-39** — WHEN the database is unavailable THEN write and read requests SHALL fail with a ProblemDetails response and SHALL NOT leak connection details or stack traces.
3. **USR-40** — WHEN a user request is handled THEN it SHALL emit a trace span, and image normalization SHALL be recorded as a distinct child span with its duration.
4. **USR-41** — WHEN a face picture is normalized THEN the system SHALL record a metric of the normalization duration and of the resulting byte size.

**Independent Test**: Point the app at a stopped database and see a ProblemDetails 5xx with no
connection string; upload an image and find a normalization span with a duration in the trace.

---

### P2: Browse the registry

**User Story**: As an operator, I want to page through registered spectators, so that I can audit
what the registry holds without querying the database.

**Why P2**: Useful for support and pre-event auditing, but no gate depends on it.

**Acceptance Criteria**:

1. **USR-42** — WHEN `GET /api/users` is called THEN the system SHALL return a **paged** response carrying the page's items and enough information to request the next page. *Ships paged from day one, unlike `device-registry` (DEV-26 known gap); 50,000 users makes a bare array indefensible.*
2. **USR-43** — WHEN a page size is requested above the permitted maximum THEN the system SHALL clamp it to that maximum rather than honouring it.
3. **USR-44** — WHEN pages are requested THEN ordering SHALL be stable and total, so no user is skipped or repeated across page boundaries.
4. **USR-45** — WHEN the registry is listed THEN deleted users SHALL be excluded, and an empty registry SHALL return an empty page rather than an error.

**Independent Test**: Register 3 users, list with a page size of 2, and confirm both pages together
contain exactly the 3 users with no duplicates.

---

## Edge Cases

- WHEN `accessCode` contains Unicode digits (e.g. Arabic-Indic) THEN the system SHALL reject it — "digits" means ASCII `0`–`9`, since the device keypad has no other keys.
- WHEN `name` contains leading or trailing whitespace THEN it SHALL be trimmed before the length check, and a name that is only whitespace SHALL be rejected as blank.
- WHEN `{externalRef}` contains URL-reserved characters other than `/` — `%`, a space, `+`, `#`, or non-ASCII text — THEN the decoded path segment SHALL be the identifier, so the reference round-trips through `PUT`, `GET` and `DELETE`.
- WHEN `{externalRef}` contains `/` THEN the escaped text SHALL be taken as the identifier, because routing leaves `%2F` encoded (A-15, amended): such a key is registered, read and removed under its escaped form and is outside the supported character set.
- WHEN two `ExternalRef` values differ only by letter case THEN they SHALL be two distinct users (A-15).
- WHEN the supplied face picture is a valid image of zero bytes' content or 1×1 pixels THEN it SHALL be rejected by the resolution floor (USR-17), not by the size band.
- WHEN the source image is a grayscale, CMYK or ICC-profiled photograph THEN the derivative SHALL be sRGB, so the device is never handed a colour space it may not decode.
- WHEN normalization cannot land inside the 40–200 KB band at any permitted quality and dimension THEN the request SHALL be rejected with a validation error rather than a derivative stored outside the band.
- WHEN a face picture is supplied that normalizes to bytes identical to the stored derivative THEN the stored hash SHALL be unchanged and `UpdatedAt` SHALL NOT advance (USR-26).

---

## Known Gap: semantic image quality is not verified

USR-12…USR-22 guarantee the derivative is *mechanically* acceptable — right format, right pixels,
right byte band. They guarantee nothing about whether it is a **usable face**. Hikvision's own
requirements also demand a full-face view, directly facing the camera, with no hat or head covering,
and a single face in frame. None of that is detectable by a resize pipeline.

The consequence, stated plainly: a photograph of three people, a profile shot, or a spectator in a
cap will pass every check in this specification and fail at a turnstile. Nothing downstream catches
it either — Phase 4 `reconciliation` compares our belief against the device, not against reality.

Closing this needs face detection at ingest, which is a materially larger dependency than
normalization and is deliberately excluded here. **This gap belongs in `ROADMAP.md` § Known Gaps.**

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| USR-01 … USR-11 | P1: Register | Design | Pending |
| USR-12 … USR-22 | P1: Normalize | Design | Pending |
| USR-23 … USR-28 | P1: Amend | Design | Pending |
| USR-29 … USR-34 | P1: Remove | Design | Pending |
| USR-35 … USR-37 | P1: Look up | Design | Pending |
| USR-38 … USR-41 | P1: Foundation | Design | Pending |
| USR-42 … USR-45 | P2: Browse | Design | Pending |

**ID format:** `USR-[NUMBER]`
**Status values:** Pending → In Design → In Tasks → Implementing → Verified
**Coverage:** 45 total, 0 mapped to tasks, 45 unmapped ⚠️ (expected — Tasks phase not yet run)

---

## Success Criteria

- [ ] An integrator holding an ordinary phone photograph can register a spectator in one call,
      without resizing, re-encoding, or reading any device documentation.
- [ ] Every stored face picture satisfies the device envelope of A-13 — including the 40 KB
      **lower** bound — or was rejected at the API boundary with a reason naming the failed rule.
- [ ] Repeating any request — `PUT`, `DELETE`, or both — leaves the registry in the same state as
      issuing it once.
- [ ] A deleted spectator retains exactly what Phase 2 needs to push a Remove, and no biometric data.
- [ ] ROADMAP **OD-4** is closed by A-1, with a storage figure (~7.5 GB at 50k users) that Phase 2
      can plan against.

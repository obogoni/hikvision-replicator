# isapi-device-client Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Spec**: `.specs/features/isapi-device-client/spec.md`
**Design**: `.specs/features/isapi-device-client/design.md`
**Status**: Approved

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec — confirm before Execute. Guidelines found:
> `CLAUDE.md`, `docs/test-patterns.md`, `.specs/STATE.md` (AD-024, AD-026, AD-036, AD-037),
> `.github/workflows/ci.yml`, and the approved Testing Strategy in `design.md`.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| --- | --- | --- | --- | --- |
| Pure ISAPI logic (`IsapiIdentity`, `IsapiOutcomes`, `IsapiRequests`, `IsapiOptions` validator) | unit | All branches; 1:1 with ISA criteria; every row of the design's classification table; reasons and hashes pinned as **literals**, never production constants | `src/HikvisionReplicator.Tests/Infrastructure/Isapi/**` | `dotnet test src/HikvisionReplicator.Tests` |
| Adapter sequencing and connection cache (`IsapiDeviceClient`, `ReaderConnections`) over a scripted `HttpMessageHandler` — no socket | unit | Every operation's exact request sequence; every failure branch (refused, reader down, auth, cancel) at every step; compensation and its failure; hold start, expiry and release | `src/HikvisionReplicator.Tests/Infrastructure/Isapi/**` | `dotnet test src/HikvisionReplicator.Tests` |
| End-to-end through the API against `StubReader` (real HTTPS, real digest) | integration, **black box** (AD-036): drive `PUT`/`DELETE /api/users`, observe on the stub and in the database | Happy path per operation; each reader-level and record-level failure; no secret leaks; poll-until with a deadline, never fixed sleeps | `src/HikvisionReplicator.IntegrationTests/{Situation}Tests.cs` on `WorkerPostgresFixture.StartIsolatedHostAsync` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Startup / wiring (adapter selection, `Isapi` options, `ActivitySource` registration) | integration | Every failing configuration fails startup naming the setting; `Isapi` starts | `StartupTests.cs`, `TracingTests.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Bench reader | manual UAT | ISA-30…ISA-36, with evidence in `validation.md` | `.specs/features/isapi-device-client/uat.md` | — |
| DI lines, docs | none: build gate only | — | — | build gate only |

**Baseline (do not regress):** 465 unit · 340 integration, both green; **17 warnings, 0 errors**.
Every task's count is a **minimum**. No existing test may be deleted or weakened. The **only**
sanctioned change to an existing assertion is `StartupTests.Application_does_not_start_with_a_device_adapter_it_does_not_have`
(T11), whose premise ISA-23 supersedes. Its replacement keeps the "unknown adapter fails" check
with a name that is still unknown.

## Gate Check Commands

> Generated from codebase (`CLAUDE.md` § Gate commands, `.github/workflows/ci.yml`) — confirm before Execute.

| Gate Level | When to Use | Command |
| --- | --- | --- |
| Quick | After tasks with unit tests only (Docker-free) | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` |
| Full | After any task with integration tests (needs Docker) | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests && dotnet test src/HikvisionReplicator.IntegrationTests` |
| Build | After each phase, and for docs-only tasks | `dotnet build HikvisionReplicator.slnx --no-incremental` |

**L-007 (confirmed ×2):** a "no new warnings" claim comes from a `--no-incremental` build only.
**Never bare `dotnet format`** — use `dotnet format whitespace`.
**Lessons store:** `scripts/lessons.py list`/`status` are read-only since `a9a3c31`; **never run
`prune --apply`** (36 candidates are past the window, by design).

---

## Execution Plan

Phases run in order, and tasks within a phase run in order.

### Phase 1: Port change and pure ISAPI logic

```
T1 → T2 → T3 → T4 → T5
```

### Phase 2: The adapter (unit, scripted handler)

```
T6 → T7 → T8 → T9 → T10
```

### Phase 3: Wiring and integration against the stub reader

```
T11 → T12 → T13 → T14 → T15 → T16
```

### Phase 4: Bench UAT and close-out (orchestrator, with the user)

```
T17 → T18
```

---

## Task Breakdown

### T1: `DeviceEnrolment` carries `Name`

**What**: Add `Name` to `DeviceEnrolment` (after `ExternalRef`). `DeviceLoop.EnrolmentOf` fills it from `user.Name`. Update every existing construction site in tests to compile, and add a `DeliveryTests` assertion that the fake received the user's name.
**Where**: `Shared/IDeviceClient.cs`, `Infrastructure/Worker/DeviceLoop.cs:340`, `Tests/Infrastructure/FakeDeviceClientTests.cs`, `IntegrationTests/DeliveryTests.cs` (+ any other construction site the compiler names)
**Depends on**: None
**Reuses**: `FakeDeviceClient` stores the whole enrolment, so no fake change is needed
**Requirement**: ISA-26 (port half), ISA-27
**Tools**: MCP: NONE · Skill: NONE
**Done when**:
- [ ] A user upserted with name `"Ana Souza"` reaches the fake with `Enrolment.Name == "Ana Souza"` (literal) on add and on update
- [ ] Existing tests changed only to pass the new constructor argument
- [ ] Full gate passes; ≥ 465 unit · ≥ 341 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(worker): carry the spectator's name to the reader`

---

### T2: `IsapiOptions` and its validator

**What**: `IsapiOptions` (section `Isapi`) with the design's fields and defaults; `IsapiOptionsValidator` rejects a non-positive `NameMaxLength`, a non-positive `ConnectionLifetime` or `AuthFailureHold`, `ValidFrom ≥ ValidTo`, a blank `FaceLibraryId`/`FaceLibraryType`/`PlanTemplateNo`, and `DoorNo < 1`, reporting every failure rather than the first.
**Where**: `Infrastructure/Isapi/IsapiOptions.cs`, `Tests/Infrastructure/Isapi/IsapiOptionsValidatorTests.cs`
**Depends on**: T1
**Reuses**: `FaceImageOptions` / `FaceImageOptionsValidator` pattern (AD-033)
**Requirement**: A-6, A-8, ISA-11, ISA-37 (configuration)
**Done when**:
- [ ] Defaults validate; each invalid field fails with a message naming `Isapi:{Field}`; two invalid fields yield two failures
- [ ] Quick gate passes; ≥ 473 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): add validated adapter options`

---

### T3: `IsapiIdentity` — `employeeNo` and name truncation

**What**: `EmployeeNo(externalRef)`, the A-2 hash; `TruncateName(name, max)` on text-element boundaries.
**Where**: `Infrastructure/Isapi/IsapiIdentity.cs`, `Tests/Infrastructure/Isapi/IsapiIdentityTests.cs`
**Depends on**: T2
**Requirement**: A-2, ISA-26
**Done when**:
- [ ] A pinned vector: `EmployeeNo("TICKET-0001")` equals a literal computed independently in the test (e.g. with `sha256sum`), 32 lowercase hex characters
- [ ] Same input gives the same output; inputs differing by one character give different outputs; a 255-character and a non-ASCII `ExternalRef` give 32 ASCII hex characters
- [ ] A name at, under and over the limit; a cut that would split a surrogate pair or a combining sequence keeps it whole and stays ≤ the limit
- [ ] Quick gate passes; ≥ 481 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): derive the reader key and fit the name`

---

### T4: `IsapiOutcomes` — the classification rule

**What**: `StepResult` and the two `Classify` overloads, exactly per the design's table. The reason is built only from step, status and `ResponseStatus` fields, with `errorMsg` capped at 200 characters.
**Where**: `Infrastructure/Isapi/IsapiOutcomes.cs`, `Tests/Infrastructure/Isapi/IsapiOutcomesTests.cs`
**Depends on**: T2
**Requirement**: ISA-12…ISA-18, ISA-25, A-4, A-14
**Done when**:
- [ ] One test per table row, reasons asserted as **literal strings**
- [ ] 401 with a locked-account body (JSON and XML forms) → `AuthFailed("account locked …")` with the parsed unlock time; 401 without → `"authentication failed"`; 403 → same as 401
- [ ] Each `HttpRequestError` value used (`NameResolutionError`, `ConnectionError`, `SecureConnectionError`, `ResponseEnded`, other) → `ReaderDown` naming the class
- [ ] A refused answer whose body contains extra fields (and a password-like string) yields a reason with none of them (ISA-25)
- [ ] Quick gate passes; ≥ 497 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): classify every reader answer onto one outcome`

---

### T5: `IsapiRequests` — the ISAPI shapes

**What**: One builder per step (`PersonCreate`, `PersonModify`, `PersonDelete`, `FaceDelete`, `FaceCreate`) producing the method, path and body in the design's table, using `IsapiOptions` constants.
**Where**: `Infrastructure/Isapi/IsapiRequests.cs`, `Tests/Infrastructure/Isapi/IsapiRequestsTests.cs`
**Depends on**: T3
**Requirement**: A-1, A-7, A-8, ISA-01 (shape half)
**Done when**:
- [ ] Each step: method, relative path and query asserted as literals; JSON body parsed and its fields asserted (`employeeNo`, `name` truncated, `password` = PIN, `Valid`, `RightPlan`)
- [ ] `FaceCreate` is multipart with exactly two parts in order, `FaceDataRecord` (JSON with `faceLibType`, `FDID`, `FPID`) then `FaceImage` (`image/jpeg`, the exact bytes)
- [ ] Quick gate passes; ≥ 503 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): build the person and face requests`

---

### T6: `ReaderConnections` — a client per reader, and the auth-failure hold

**What**: `IReaderConnections` + `ReaderConnections`: a client per key `(DeviceId, Ip, Port, Username, SHA-256(Password))`; a new key for a device disposes the old client; `Hold`/`IsHeld` on the injected `TimeProvider`; `Dispose` releases all. The handler factory is `internal`, so tests inspect its configuration: digest-only `CredentialCache`, an accept-all callback on **this** handler, `AllowAutoRedirect = false`, `PooledConnectionLifetime`, `Timeout = Infinite`, HTTPS base address with bracketed IPv6.
**Where**: `Infrastructure/Isapi/ReaderConnections.cs`, `Tests/Infrastructure/Isapi/ReaderConnectionsTests.cs`
**Depends on**: T5
**Reuses**: `TimeProvider` / `FakeTimeProvider` (AD-023)
**Requirement**: ISA-21, ISA-22 (handler half), ISA-37 (store)
**Done when**:
- [ ] The same endpoint twice gives the same client; a changed password gives a new client and the old one is disposed; a different device is unaffected
- [ ] A hold is reported until its time, not after (both sides of the boundary); a changed password is not held
- [ ] Handler configuration asserted as listed; `https://[::1]:8443/` for an IPv6 endpoint
- [ ] Quick gate passes; ≥ 511 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): keep one authenticated connection per reader`

---

### T7: `IsapiDeviceClient.AddAsync` — enrol with compensation

**What**: The Add sequence, the person upsert (`AlreadyExists` ⇒ Modify), face replace (`Absent` ok), compensation on a refused face, no compensation on reader-down, cancellation propagating with no compensation, and one exchange per step.
**Where**: `Infrastructure/Isapi/IsapiDeviceClient.cs`, `Tests/Infrastructure/Isapi/IsapiDeviceClientTests.Add.cs` (+ a shared scripted-handler helper)
**Depends on**: T6
**Requirement**: ISA-01…ISA-06, ISA-19, ISA-20
**Done when**:
- [ ] Happy path: exact request sequence `PersonCreate, FaceDelete, FaceCreate` → `Delivered`
- [ ] Person exists → `PersonCreate, PersonModify, FaceDelete, FaceCreate` → `Delivered`
- [ ] Face refused → `… FaceCreate, PersonDelete` → `Rejected` naming the face step and `subStatusCode` (literal); compensation also refused → `Rejected` whose reason says compensation failed
- [ ] Face step reader-down or 5xx → `Unreachable`, no `PersonDelete` sent
- [ ] Token cancelled during `FaceCreate` → `OperationCanceledException`, no further request
- [ ] No step is sent twice in any test (ISA-19)
- [ ] Quick gate passes; ≥ 519 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): enrol a spectator, never leaving a PIN-only person`

---

### T8: `IsapiDeviceClient.UpdateAsync` and `RemoveAsync`

**What**: Update (same upsert + face replace, **no** compensation) and Remove (`FaceDelete` → `PersonDelete`, `Absent` ok at both).
**Where**: `Infrastructure/Isapi/IsapiDeviceClient.cs`, `Tests/Infrastructure/Isapi/IsapiDeviceClientTests.Update.cs`, `.Remove.cs`
**Depends on**: T7
**Requirement**: ISA-07…ISA-11
**Done when**:
- [ ] Update of a present person and of an absent one → `Delivered` with the exact sequences
- [ ] Update with the face refused → `Rejected`, no `PersonDelete` sent
- [ ] Remove → `FaceDelete, PersonDelete` → `Delivered`; either or both answering a configured absent code → `Delivered`; a refusal at either → `Rejected`
- [ ] Quick gate passes; ≥ 525 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): update and remove a spectator on a reader`

---

### T9: Auth failures pause, and the hold stops repeat logins

**What**: On `AuthFailed` at any step, the client records a hold (unlock time if given, else `AuthFailureHold`) and returns `Unreachable`; a call on a held key returns `Unreachable` with the same reason and sends **no** request.
**Where**: `Infrastructure/Isapi/IsapiDeviceClient.cs`, `Tests/Infrastructure/Isapi/IsapiDeviceClientTests.Auth.cs`
**Depends on**: T8
**Requirement**: ISA-15, ISA-37
**Done when**:
- [ ] 401 → `Unreachable("authentication failed")`; the next three calls send zero requests and return the same reason
- [ ] After the hold lapses (advance `FakeTimeProvider`), the next call contacts the reader again
- [ ] A locked answer with an unlock time holds until exactly that time
- [ ] An endpoint with a changed password is contacted at once
- [ ] Quick gate passes; ≥ 530 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): hold refused credentials so probes cannot lock the reader`

---

### T10: Step spans and the per-call log entry

**What**: `ActivitySource("HikvisionReplicator.Isapi")` with one `Client` activity per step and the design's tags; one `[LoggerMessage]` per call (Information on `Delivered`, Warning otherwise).
**Where**: `Infrastructure/Isapi/IsapiDeviceClient.cs`, `Tests/Infrastructure/Isapi/IsapiDeviceClientTests.Observability.cs`
**Depends on**: T9
**Requirement**: ISA-28, ISA-29, ISA-24 (unit half)
**Done when**:
- [ ] An `ActivityListener` sees one activity per step with `device.id`, `isapi.step`, `http.response.status_code`, and `isapi.sub_status_code` on a refusal
- [ ] A capturing logger sees exactly one entry per call with the listed fields, at the right level
- [ ] No captured tag or log text contains the device password, the PIN, or `Digest ` (literal values from the test)
- [ ] Quick gate passes; ≥ 534 unit

**Tests**: unit · **Gate**: quick · **Commit**: `feat(isapi): trace each step and log each call`

---

### T11: Wire `Isapi` as a selectable adapter

**What**: `DeviceClientOptions.IsapiAdapter`; the validator accepts `Fake` and `Isapi` and its messages name both; `Program.cs` registers `IsapiOptions` (`ValidateOnStart`), `IReaderConnections`, `IsapiDeviceClient`, the adapter switch, and `AddSource(IsapiDeviceClient.ActivitySourceName)`. Rewrite the superseded startup test (see baseline note).
**Where**: `Infrastructure/DeviceClientOptions.cs`, `Program.cs`, `IntegrationTests/StartupTests.cs`
**Depends on**: T10
**Requirement**: ISA-23
**Done when**:
- [ ] `Adapter = Isapi` starts and resolves `IDeviceClient` to `IsapiDeviceClient`
- [ ] `Adapter = Bogus` fails startup; the message names `DeviceClient:Adapter`, `Fake` and `Isapi` (literals)
- [ ] An invalid `Isapi:NameMaxLength = 0` with `Adapter = Isapi` fails startup naming the setting
- [ ] Full gate passes; ≥ 534 unit · ≥ 343 integration

**Tests**: integration · **Gate**: full · **Commit**: `feat(isapi): make the ISAPI adapter selectable at startup`

---

### T12: `StubReader` — an ISAPI reader on loopback

**What**: A test helper: Kestrel on `127.0.0.1:0` over HTTPS with an in-memory self-signed certificate; a **real** digest challenge (MD5, `qop=auth`) that verifies the response against a configured password and counts failed logins; in-memory persons and faces keyed by `employeeNo`; the five design endpoints; scripting (refuse a step with a given `subStatusCode`, answer a status, go offline). Self-tests in `HarnessTests`.
**Where**: `IntegrationTests/StubReader.cs`, `IntegrationTests/HarnessTests.cs`
**Depends on**: T11
**Requirement**: enables ISA-21, ISA-22 and the integration half of every ISA criterion
**Done when**:
- [ ] A raw `HttpClient` without credentials gets 401 with a `Digest` challenge; with the right password it gets `statusCode 1`; with a wrong one, 401, and the failed-login count rises by one
- [ ] A `PersonCreate` + `FaceCreate` through `IsapiDeviceClient` against the stub leaves exactly one person and one face
- [ ] Full gate passes; ≥ 346 integration

**Tests**: integration · **Gate**: full · **Commit**: `test(isapi): add a loopback ISAPI reader for integration tests`

---

### T13: `IsapiDeliveryTests` — through the API to the stub

**What**: On an isolated worker host with `Adapter = Isapi`, a device registered at the stub's address and port: `PUT /api/users/{ref}` → the stub holds the hashed person with the name and PIN, plus the face bytes, and the replication is `Succeeded`; amend → updated; `DELETE` → gone. Each item is a separate test.
**Where**: `IntegrationTests/IsapiDeliveryTests.cs`
**Depends on**: T12
**Reuses**: `WorkerApiTests` base, `StartIsolatedHostAsync`, `UntilAsync`
**Requirement**: ISA-01, ISA-07, ISA-10, ISA-21, ISA-26
**Done when**:
- [ ] Enrol, update and remove each proven on the stub's state and the replication row
- [ ] A user whose name exceeds `NameMaxLength` arrives truncated, not refused
- [ ] Full gate passes; ≥ 350 integration

**Tests**: integration · **Gate**: full · **Commit**: `test(isapi): prove enrol, update and remove through the API`

---

### T14: `IsapiReaderFailureTests` — reader-level and record-level failures

**What**: Wrong device password → reader paused, zero dead letters, and the stub counts **exactly one** failed login while the worker keeps probing for several probe intervals; fixing the password via `PUT /api/devices/{id}` → backlog drains. Stub refuses the face → dead letter after `MaxAttempts`, and the stub holds no person. Stub offline → paused; back online → drains with nothing dead-lettered.
**Where**: `IntegrationTests/IsapiReaderFailureTests.cs`
**Depends on**: T13
**Requirement**: ISA-04, ISA-14, ISA-15, ISA-37 (and the stub analogues of ISA-34, ISA-35)
**Done when**:
- [ ] Each scenario above is its own test with poll-until deadlines
- [ ] Full gate passes; ≥ 354 integration

**Tests**: integration · **Gate**: full · **Commit**: `test(isapi): pause on reader faults and dead-letter only refused records`

---

### T15: Secrets stay off the wire's edges, and TLS bypass stays local

**What**: Extend `CredentialLeakageTests`: across a successful enrol, a refused face and a wrong password, the log sink, exported spans and `LastError` contain neither the device password, the PIN, nor `Authorization`/`Digest ` values. A new test: a plain `HttpClient` from the host's `IHttpClientFactory` fails TLS against the stub, while the adapter succeeds against the same stub.
**Where**: `IntegrationTests/CredentialLeakageTests.cs`, `IntegrationTests/StartupTests.cs`
**Depends on**: T14
**Requirement**: ISA-22, ISA-24, ISA-25
**Done when**:
- [ ] Three leakage flows asserted; the TLS-scope test asserts an `HttpRequestException` with `SecureConnectionError` for the default client
- [ ] Full gate passes; ≥ 358 integration

**Tests**: integration · **Gate**: full · **Commit**: `test(isapi): prove no credential leaks and no process-wide TLS bypass`

---

### T16: Reader spans reach the exporter

**What**: Extend `TracingTests`: with an export endpoint configured and `Adapter = Isapi`, an enrol exports one `HikvisionReplicator.Isapi` span per step with the design's tags.
**Where**: `IntegrationTests/TracingTests.cs`
**Depends on**: T15
**Requirement**: ISA-28
**Done when**:
- [ ] Spans for `person`, `face-delete` and `face` exported for one enrol, each tagged with the device id
- [ ] Full gate passes; ≥ 359 integration; `--no-incremental` build shows **17 warnings, 0 errors**

**Tests**: integration · **Gate**: full · **Commit**: `test(isapi): export a span per reader step`

---

### T17: Bench UAT on the DS-K1T342MFWX (with the user)

**What**: Write `uat.md`, a step-by-step runbook for ISA-30…ISA-36: registering the reader (`FaceCapacity = 1500`, HTTPS port), the sizes to probe for A-13 (including an `FDSetUp` probe), the wrong-password check, the disconnect check, and what to record. Then run it **with the user**, who operates the reader. Every correction to A-1, A-2, A-6, A-7, A-13′ or `AbsentSubStatusCodes` becomes a numbered fix task (T17.n) with its own tests and commit.
**Where**: `.specs/features/isapi-device-client/uat.md`; evidence goes to `validation.md` § UAT
**Depends on**: T16
**Requirement**: ISA-30…ISA-36
**Done when**:
- [ ] Every UAT step is recorded pass or fail, with evidence
- [ ] Every fail is either fixed by a T17.n task (gate green) or recorded as an accepted deviation by the user
- [ ] The firmware version, the real limits and the ISAPI calls accepted are recorded

**Tests**: manual UAT · **Gate**: build · **Commit**: `docs(specs): record the bench UAT for isapi-device-client`

---

### T18: Close the gaps the feature closes

**What**: `ROADMAP.md` § Known Gaps, close "No real device adapter yet". `user-registry` spec A-13, confirmed or corrected per the UAT. `docs/test-patterns.md`, add the new situation classes and `StubReader` to the class tables. `appsettings.json`, an `Isapi` section with the UAT-confirmed defaults.
**Where**: `.specs/ROADMAP.md`, `.specs/features/user-registry/spec.md`, `docs/test-patterns.md`, `src/HikvisionReplicator.Api/appsettings.json`
**Depends on**: T17
**Requirement**: Success criteria 1 and 3
**Done when**:
- [ ] Each document matches the code and the UAT record; build gate passes

**Tests**: none · **Gate**: build · **Commit**: `docs(specs): close the real-adapter gap and settle A-13`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4

Phase 1:  T1 ──→ T2 ──→ T3 ──→ T4 ──→ T5        (T4 needs T2; T5 needs T3)
Phase 2:  T6 ──→ T7 ──→ T8 ──→ T9 ──→ T10
Phase 3:  T11 ──→ T12 ──→ T13 ──→ T14 ──→ T15 ──→ T16
Phase 4:  T17 ──→ T18                             (orchestrator + user)
```

**Batches (~7 tasks, whole phases):** batch A = Phase 1 (5), batch B = Phase 2 (5), batch C =
Phase 3 (6). Phase 4 needs the user at the reader, so it stays with the orchestrator, followed by
the Verifier.

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T1 | one record field + one call site + its assertion | ⚠️ cohesive (port change) |
| T2 | one options class + validator | ✅ |
| T3 | one static class, two functions | ✅ |
| T4 | one classifier | ✅ |
| T5 | one request-builder file | ✅ |
| T6 | one component | ✅ |
| T7 | one operation | ✅ |
| T8 | two small operations in one file | ⚠️ cohesive (same sequencing machinery) |
| T9 | one behaviour (hold) | ✅ |
| T10 | one concern (observability) | ✅ |
| T11 | one wiring change + its startup tests | ✅ |
| T12 | one test helper + self-tests | ✅ |
| T13 | one situation class | ✅ |
| T14 | one situation class | ✅ |
| T15 | one concern across two existing classes | ⚠️ cohesive (security) |
| T16 | one test extension | ✅ |
| T17 | one runbook + its run | ✅ |
| T18 | doc sync | ✅ |

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T1 | None | start | ✅ |
| T2 | T1 | T1 → T2 | ✅ |
| T3 | T2 | T2 → T3 | ✅ |
| T4 | T2 | T3 → T4 (sequential; T2 precedes) | ✅ |
| T5 | T3 | T4 → T5 (sequential; T3 precedes) | ✅ |
| T6 | T5 | T5 → T6 | ✅ |
| T7 | T6 | T6 → T7 | ✅ |
| T8 | T7 | T7 → T8 | ✅ |
| T9 | T8 | T8 → T9 | ✅ |
| T10 | T9 | T9 → T10 | ✅ |
| T11 | T10 | T10 → T11 | ✅ |
| T12 | T11 | T11 → T12 | ✅ |
| T13 | T12 | T12 → T13 | ✅ |
| T14 | T13 | T13 → T14 | ✅ |
| T15 | T14 | T14 → T15 | ✅ |
| T16 | T15 | T15 → T16 | ✅ |
| T17 | T16 | T16 → T17 | ✅ |
| T18 | T17 | T17 → T18 | ✅ |

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T1 | port + worker call site | integration (worker behaviour) | integration | ✅ |
| T2 | pure options validator | unit | unit | ✅ |
| T3 | pure identity | unit | unit | ✅ |
| T4 | pure classifier | unit | unit | ✅ |
| T5 | pure request builders | unit | unit | ✅ |
| T6 | connection cache | unit | unit | ✅ |
| T7 | adapter sequencing | unit | unit | ✅ |
| T8 | adapter sequencing | unit | unit | ✅ |
| T9 | adapter sequencing | unit | unit | ✅ |
| T10 | adapter observability | unit | unit | ✅ |
| T11 | startup / wiring | integration | integration | ✅ |
| T12 | test helper | integration | integration | ✅ |
| T13 | end-to-end | integration | integration | ✅ |
| T14 | end-to-end | integration | integration | ✅ |
| T15 | end-to-end + startup | integration | integration | ✅ |
| T16 | wiring (tracing) | integration | integration | ✅ |
| T17 | bench reader | manual UAT | manual UAT | ✅ |
| T18 | docs, config | none | none | ✅ |

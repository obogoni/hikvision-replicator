# Device Registry Validation — Iteration 3 (final)

**Date**: 2026-08-12
**Spec**: `.specs/features/device-registry/spec.md`
**Diff range**: `4764df9..HEAD` (`253413e`, branch `feat/device-registry`) — 24 commits
**Verifier**: independent sub-agent (author ≠ verifier ≠ iteration-1 verifier ≠ iteration-2 verifier), evidence-or-zero
**Iteration**: 3 of a maximum of 3 (final allowed iteration)

**Verdict**: ❌ **FAIL** — 24/25 in-scope ACs fully covered, 1 new partial (DEV-23). Both
iteration-1 and iteration-2 gaps are confirmed closed. Two new surviving mutants found by
an independently-designed sensor, both in the same family: the per-field "did this field
actually change" guard behind DEV-23 is proven at only two of six fields.

Coverage was re-derived from `spec.md` from scratch by reading every cited test file's actual
current content (not iteration 2's line numbers, which shift once T22 inserted a test).
Iteration 2's conclusions were treated as claims to re-test, not as established fact.

---

## Prior-Gap Regression Re-check (mandatory, both confirmed closed)

| # | Mutation | File:line | Result | Tests failed |
|---|---|---|---|---|
| R1 | Removed `.AddAspNetCoreInstrumentation()` | `Program.cs:56` | ✅ **Killed** | 2 — `TracingTests.Handled_request_produces_a_span_naming_the_route_that_served_it`, `TracingTests.Database_work_is_traced_as_a_child_of_the_request_that_caused_it` |
| R2 | Removed `.AddEntityFrameworkCoreInstrumentation()` | `Program.cs:57` | ✅ **Killed** | 3 — `TracingTests.Database_work_is_traced_as_a_child_of_the_request_that_caused_it`, `TracingTests.No_span_attribute_ever_carries_the_password`, `TracingTests.No_span_attribute_ever_carries_the_encryption_key` |
| R3 | `string.IsNullOrWhiteSpace(request.Password)` → `request.Password is null` | `RegisterDeviceService.cs:23` | ✅ **Killed** | 1 — `DeviceEndpointsTests.Device_with_a_blank_password_is_invalid` (`Expected: BadRequest, Actual: Created`) |

**Both prior gaps: genuinely closed.** DEV-16's span-emission clause (T21, `7411707`) and
DEV-02's blank-password-on-registration clause (T22, `253413e`) each fail the exact mutation
that exposed them in their originating iteration, and pass on the unmutated tree. All three
mutations were applied and reverted in a scratch `git worktree`; the real tree was never
touched (verified below).

---

## Spec-Anchored Acceptance Criteria

All paths relative to repo root, all line numbers read fresh at `253413e` (not copied from
iteration 2 — T22's insertion shifted every subsequent line in `DeviceEndpointsTests.cs` by
+9).

### P1: Register a device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-01 | `201`, `Location: /api/devices/{id}`, body carries all 8 fields | `DeviceEndpointsTests.cs:78` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`; `:83` — `Assert.Equal($"/api/devices/{id}", response.Headers.Location?.ToString())`; `:84-88` — name/ipAddress/httpPort/username/faceCapacity each `Assert.Equal`; `:89-92` — `createdAt == updatedAt`. Domain: `DeviceCreateTests.cs:28-33`,`41-42` | ✅ PASS |
| DEV-02 | `400` naming the field, for omitted **or blank**, across all six fields | **Omitted** (all six): `DeviceEndpointsTests.cs:111,136,153,170,187,212`. **Blank**: name `:119`; **password `:195` (T22, NEW)**; username `DeviceCreateTests.cs:111-112`; ipAddress `ValueObjectTests.cs:63-65`. httpPort/faceCapacity are numeric — omission is the whole rule. Sensor R3 confirms the password clause is load-bearing, not vacuous | ✅ **PASS — gap closed**. All 12 omitted/blank sub-cases across the 6 fields now have a citation |
| DEV-03 | `400` naming the field, name/username > 100 | `DeviceEndpointsTests.cs:226` (`name: 101` → `:230` assert); `:242` (`username: 101` → `:246` assert). Messages: `DeviceCreateTests.cs:143,162` | ✅ PASS |
| DEV-04 | `400` naming the field: bad ip / port ∉ 1…65535 / capacity ∉ 1…1e6 | ip `DeviceEndpointsTests.cs:252-256`; port `:259-267` (`[InlineData(0)][InlineData(65536)]` → `:266`); capacity `:279-290` (`[InlineData(0)][InlineData(-1)][InlineData(1_000_001)]` → `:289`). Messages: `ValueObjectTests.cs:44-45,89-90,125-126` | ✅ PASS |
| DEV-05 | `409`, exactly one row | `DeviceEndpointsTests.cs:311-312` — `Assert.Equal(HttpStatusCode.Conflict, …)` + `Assert.Equal(1, await CountDevicesAsync())` | ✅ PASS |
| DEV-06 | 1×`201`, rest `409`, no `500`, one row | `DeviceEndpointsTests.cs:340-343` — `Assert.Equal(1, statuses.Count(==Created))`, `Assert.Equal(attempts-1, statuses.Count(==Conflict))`, `Assert.DoesNotContain(InternalServerError, statuses)`, `Assert.Equal(1, await CountDevicesAsync())`. `23505` fallback: `DeviceRepositoryTests.cs:76-77` | ✅ PASS |
| DEV-07 | password + ciphertext absent from response/logs/trace; ip normalized at rest | Response: `DeviceEndpointsTests.cs:354,357-360`; `CredentialLeakageTests.cs:175,179-182`. Logs: `CredentialLeakageTests.cs:193,197-200`. Trace: `TracingTests.cs:216,221-222`. At rest: `CredentialLeakageTests.cs:226-232`. Normalized ip: `DeviceCreateTests.cs:48-50` | ✅ PASS |

### P1: Inspect the device catalogue

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-08 | `200`, every device, full field set, no password | `DeviceEndpointsTests.cs:472-478` per-field `Assert.Equal`; no password at `:505-508` | ✅ PASS |
| DEV-09 | `200` + `[]` | `DeviceEndpointsTests.cs:457,460-461` | ✅ PASS |
| DEV-10 | `200` / `404` RFC 7807 | `DeviceEndpointsTests.cs:387,390-395` (found); `:403-410` (404 + `application/problem+json` + `status==404`) | ✅ PASS |

### P1: Operational foundation

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-12 | migrations applied, no `EnsureCreated()` | `HarnessTests.cs:39-40` — `Assert.Contains("devices", tables)` + migration-history table present; `:48-49` — applied migrations non-empty, contains `InitialCreate` | ✅ PASS |
| DEV-13 | real PG via Testcontainers, isolated | `PostgresFixture.cs` builds a real `postgres:17-alpine` container + Respawn reset; isolation proven by the paired `HarnessTests.cs:57-64` / `:66-74` | ✅ PASS |
| DEV-14 | `503` RFC 7807, no leak | `ErrorHandlingTests.cs:62-63,67-75` (status/title/detail); `:93-94,106-107` (no connection details, no stack trace, no provider name). Non-DB stays `500`: `:136-151` | ✅ PASS |
| DEV-15 | startup aborts, message names setting | `StartupTests.cs:60-63` (missing key); `:73-76` (wrong length). Validator rules: `EncryptionServiceTests.cs:88-95,97-105,107-115,117-128` | ✅ PASS |
| DEV-16 | HTTP span + EF Core child spans; export gated on config | (a) `TracingTests.cs:187-190` (server span, correct DisplayName); `:204-206` (DB spans non-empty, correctly parented, `ActivityKind.Client`). (b) `StartupTests.cs:86,94` (no `TracerProvider` without endpoint, present with one) | ✅ PASS |
| DEV-17 | Dev exposes docs; elsewhere it does not | `StartupTests.cs:107,118` (200 in Dev); `:129,140` (404 outside) | ✅ PASS |

### P2: Amend a registered device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-18 | only supplied fields change | `DeviceEndpointsTests.cs:551-567` (name changes, other 4 fields unchanged); `DeviceUpdateTests.cs:51-72` (all 6 applied when all supplied) | ✅ PASS |
| DEV-19 | `400` naming field, zero partial change | `DeviceEndpointsTests.cs:578,581-589` (name+httpPort=0 → only httpPort rejected, nothing persisted, `updatedAt` unmoved). Domain: `DeviceUpdateTests.cs:143-159` (all fields + `UpdatedAt` untouched) | ✅ PASS |
| DEV-20 | `409` on another device's address; `200` on own | `DeviceEndpointsTests.cs:630,633-643` (409, both devices' state unchanged); `:653-657` (self-address 200). Spec: `DeviceRepositoryTests.cs:200-201,221` | ✅ PASS |
| DEV-21 | omit → unchanged; supply → replaced | `DeviceEndpointsTests.cs:669` (`Assert.Equal(storedBefore, …)`); `:683-684` (`Assert.NotEqual(storedBefore, storedAfter)` + no plaintext); blank rejected + preserved `:695-696` | ✅ PASS |
| DEV-22 | `404` | `DeviceEndpointsTests.cs:704-708` | ✅ PASS |
| DEV-23 | any real change advances `updatedAt`; `createdAt` never moves | Name: `DeviceEndpointsTests.cs:721-729`; `DeviceUpdateTests.cs:110-117,120-127`. Password: `DeviceUpdateTests.cs:130-138`. **ipAddress-only, httpPort-only, username-only, faceCapacity-only real-value changes: no test asserts `updatedAt` advances for any of these individually** — only the no-change case (`DeviceUpdateTests.cs:77-84,87-94,97-107`) and the all-six-at-once case (`:51-72`, which never reads `UpdatedAt`) touch these fields. **Sensor mutations M3/M4 (below) prove this is exploitable**: silently dropping the `changed = true` flag on the `FaceCapacity` or `HttpPort` branch of `Device.Update` — while still applying the new value — passes the full 156-test suite | ⚠️ **PARTIAL — new gap**. Covered for 2 of 6 independently-updatable fields (name, password); unproven for the other 4 |
| DEV-11 (Remove) | `204`, gone from catalogue and by id | `DeviceEndpointsTests.cs:778,781` | ✅ PASS |
| DEV-24 | `404` | `DeviceEndpointsTests.cs:805-809` | ✅ PASS |
| DEV-25 | address freed | `DeviceEndpointsTests.cs:822,825-826` | ✅ PASS |

### P3 (out of scope)

| Criterion | Result |
|---|---|
| DEV-26 — pagination | ⏭️ Deliberately unscheduled (P3). Not a gap. |

**Status**: ⚠️ 24/25 fully covered · **1 new partial (DEV-23)** · 0 spec-precision gaps
(DEV-23 is a coverage gap, not an imprecise-outcome gap — the spec's outcome is exact,
the tests simply don't exercise 4 of the 6 fields the "any field" language commits to).

---

## Edge Cases

| # | Edge case | `file:line` + assertion | Result |
|---|---|---|---|
| 1 | Non-canonical address collides with canonical | `DeviceEndpointsTests.cs:319,323-324`; `ValueObjectTests.cs:17-18` | ✅ |
| 2 | `httpPort` 0/65536 rejected; 1/65535 accepted | `DeviceEndpointsTests.cs:259-267` / `:269-277` | ✅ |
| 3 | `faceCapacity` 0/negative rejected | `DeviceEndpointsTests.cs:279-290` | ✅ |
| 4 | name/username exactly 100 accepted, 101 rejected | `DeviceEndpointsTests.cs:217-223,225-231,233-239,241-247` | ✅ |
| 5 | Entirely empty update body → 200, unchanged, `updatedAt` unadvanced | `DeviceEndpointsTests.cs:739,742-749` | ✅ |
| 6 | Multi-byte UTF-8 password round-trips | `EncryptionServiceTests.cs:33-40` | ✅ |
| 7 | Malformed JSON → 400 problem, not 500 | `DeviceEndpointsTests.cs:865-871` | ✅ |
| 8 | Never-existed id and deleted id both 404, indistinguishably | `DeviceEndpointsTests.cs:838-847` (`ProblemFieldsAsync` compares every field but `traceId`) | ✅ |

**Edge cases: 8/8 covered.** (These are distinct from the DEV-23 gap above — none of the 8
listed edge cases in `spec.md` mention per-field timestamp semantics.)

---

## Discrimination Sensor

**Depth**: lightweight-to-moderate, deliberately disjoint from both prior iterations' sets.
Prior iterations killed: IP normalization, `Device.Update` changed-guard (name only), swallowed
`23505`, ciphertext in response, port-0 off-by-one, raw exception in 503 body, soft-delete,
password-overwrite-on-omission, self-address exemption, 404-on-empty-list, skipped `Migrate()`,
101-char name, constant IV, missing `UseStatusCodePages`. This round targets: Location header
correctness, per-field `updatedAt` semantics, `CancellationToken` propagation on the write
path, and RFC 7807 status-code fidelity for both conflict and outage mapping.

**Scratch state**: a detached `git worktree` at `…/scratchpad/mut3` (branch `feat/device-registry`
at `253413e`), reverted after every mutation and removed at the end. The real working tree was
never modified — confirmed by `git status --short` / `git diff --stat` returning empty both
mid-run and at the close of this report.

| # | File:line | Mutation | Tests failed | Killed? |
|---|---|---|---|---|
| M1 | `RegisterDeviceService.Endpoint.cs:25` | `Results.Created($"/api/devices/{response.Id}", …)` → `{response.Id + 1}` | 2 — `New_device_is_created_and_returned` (Location string mismatch), `Device_is_retrievable_at_the_location_it_reports` (follows the bad Location → 404) | ✅ Killed |
| M2 | `DeviceRepository.cs:35` | `SaveChangesAsync(cancellationToken)` → `SaveChangesAsync()` | **0** — `Passed! Failed: 0, Passed: 156` | ❌ **SURVIVED** |
| M3 | `Device.cs:171-175` | Dropped `changed = true;` from the `FaceCapacity` branch of `Update` (value still applied) | **0** — `Passed! Failed: 0, Passed: 156` | ❌ **SURVIVED** |
| M4 | `Device.cs:153-157` | Dropped `changed = true;` from the `HttpPort` branch of `Update` (value still applied) | **0** — `Passed! Failed: 0, Passed: 156` | ❌ **SURVIVED** |
| M5 | `DomainErrorExtensions.cs:20` | `ConflictError` → `Status400BadRequest` instead of `409` | 4 — `Moving_a_device_onto_another_devices_address_is_rejected`, `Device_reusing_a_registered_address_is_rejected`, `Address_written_in_a_non_canonical_form_collides_with_its_canonical_form`, `Simultaneous_registrations_of_one_address_yield_a_single_device` | ✅ Killed |
| M6 | `GlobalExceptionHandler.cs:60-69` | `IsDatabaseFailure` hard-coded to always return `false` | 1 — `DatabaseUnreachableTests.Request_made_while_the_database_is_unreachable_reports_a_service_outage` (`Expected: ServiceUnavailable, Actual: InternalServerError`) | ✅ Killed |

**Result**: 6 new mutations — **3/6 killed, 3 survived**. Combined with the 3/3 regression
re-checks above: **9/9 mutation runs total, 6 killed, 3 survived** — ❌ **FAIL**.

**On the survivors.** M3 and M4 are the same fault in two independent branches of the same
`if`-ladder in `Device.Update` — dropping the `changed = true;` side effect while still
mutating the backing field. Both pass the full suite. This is not a one-off: the code repeats
an identical four-line pattern once per field (name, ipAddress, httpPort, username,
encryptedPassword, faceCapacity), and the test suite only ever exercises the name and password
copies of that pattern with a real value-change-plus-timestamp assertion. By inspection, the
same fault on the `ipAddress` or `username` branches would very likely also survive, for the
identical reason — no test isolates a real change to either field and checks `updatedAt`.
This directly narrows DEV-23's coverage (above) rather than standing apart from it: the AC
table entry and this sensor result are two views of the same finding.

M2 is a different kind of gap: dropping `CancellationToken` from the one `SaveChangesAsync`
call that persists every write (register, update, and — via `AddIfAddressFreeAsync`, which
calls the same method — remove reads separately) leaves all 156 tests green because no test in
the suite ever supplies an already-cancelled token and asserts the write aborts. CLAUDE.md
requires `CancellationToken` be threaded to "every async call," which the code satisfies
syntactically — the gap is that nothing proves the token is *honored*, only that it's present
in the signature. This is not tied to a specific spec AC (the spec does not mention
cancellation), so it does not move any AC's status, but it is a real, reproducible blind spot
in the suite's guarantees and is ranked below DEV-23 for that reason.

M1, M5, and M6 killed cleanly and specifically — each failure set names exactly the tests that
own the mutated behavior, which is a healthy sign for the rest of the suite.

---

## Gate Check

- **Build**: `dotnet build HikvisionReplicator.slnx` → **exit 0**, 0 errors (18 warnings, all
  `NU1902`/`NU1903` OpenTelemetry + `Microsoft.OpenApi` advisories and one `Testcontainers`
  obsolete-constructor warning — all pre-existing, known, out of scope per task brief)
- **Test**: `dotnet test src/HikvisionReplicator.Tests` → **exit 0**
  - `Passed! - Failed: 0, Passed: 156, Skipped: 0, Total: 156, Duration: 6 s`
- **Skipped tests**: none
- **Failures**: none
- **Test count at iteration 2**: 155 (before its T22 fix landed)
- **Test count at iteration 3**: 156
- **Delta**: **+1** — `DeviceEndpointsTests.Device_with_a_blank_password_is_invalid` (T22,
  `253413e`), the fix for iteration 2's DEV-02 gap. No test removed, no assertion weakened.
- **Test Integrity**: ✅ count increased, no deletions, no weakened assertions.
- **E2E**: `src/HikvisionReplicator.E2ETests/DeviceEndpointsTests.cs` — **9** NUnit `[Test]`
  methods confirmed present by direct count. **Reported but NOT verified by this Verifier**
  per task instructions (requires a live API; not run).

---

## Code Quality

| Principle | Status |
|---|---|
| Minimum code — no features beyond the spec | ✅ |
| No abstractions for single-use code | ✅ |
| No unnecessary "flexibility" | ✅ |
| Only touched files required for the feature | ✅ |
| Didn't "improve" unrelated code | ✅ |
| Matches existing patterns/style (`CLAUDE.md`: vertical slices, `OneOf`, `Specification<T>`, no `AppDbContext` in services, `CancellationToken` last and required) | ✅ — the CT-propagation sensor finding (M2) is a test-strength gap, not a code-shape violation; the parameter is present and threaded per the guideline |
| Would a senior engineer approve? | ✅ |
| Tests map to ACs and are non-shallow | ⚠️ — true for 24/25; DEV-23 is shallow across 4 of its 6 fields |
| Spec-anchored outcome check (asserted values match spec) | ⚠️ — 24/25 exact; DEV-23's per-field timestamp semantics under-asserted |
| Per-layer Coverage Expectation (domain 1:1 ACs; routes happy+edge+error) | ✅ for every AC except the DEV-23 sub-cases identified above |
| Every test maps to a spec AC / edge case / Done-when — no unclaimed tests | ✅ |
| Documented guidelines followed | ✅ — `CLAUDE.md` + `docs/test-patterns.md` (behaviour-based naming observed throughout) |

---

## Fix Plan

### Fix 1 — DEV-23: per-field `updatedAt` advance is proven for only 2 of 6 fields (Major)

- **Root cause**: `Device.Update` (`Domain/Device.cs:141-175`) repeats an identical
  "assign + mark changed" block once per field. Only the `name` branch (endpoint + domain) and
  the `encryptedPassword` branch (domain only) have a test that changes *that* field alone to a
  genuinely different value and then asserts `updatedAt` advanced. The `ipAddress`, `httpPort`,
  `username`, and `faceCapacity` branches have no such test; two of the four
  (`httpPort`, `faceCapacity`) were proven exploitable by sensor mutations M3/M4 — dropping
  `changed = true` on either branch alone still passes all 156 tests.
- **Fix task**: Add one test per remaining field (domain-level, matching the existing
  `Update_that_changes_a_value_advances_the_update_time` pattern in `DeviceUpdateTests.cs`):
  update only `ipAddress` to a genuinely different address and assert `UpdatedAt == Later`;
  same for `httpPort`, `username`, `faceCapacity`. Four new `[Fact]`s, ~4 lines each.
- **Verify**: re-run mutations M3 and M4 (and the equivalent on the `ipAddress`/`username`
  branches) and confirm each newly-added test fails.
- **Priority**: **Major** — production behaviour is very likely correct (the pattern is
  mechanically identical across all six branches and the two proven-good copies work), but
  DEV-23's "any field" wording is currently backed by evidence for one-third of the fields it
  names.

### Fix 2 — CancellationToken is threaded but never proven honored on the write path (Minor, not spec-mapped)

- **Root cause**: no test in the suite cancels a token before an operation that should observe
  it; `SaveChangesAsync(cancellationToken)` at `DeviceRepository.cs:35` can be silently changed
  to `SaveChangesAsync()` with all 156 tests staying green (sensor M2).
- **Fix task**: not required by any spec AC (out of scope for this feature's spec) — recorded
  here as an architectural robustness gap for the team to schedule at their discretion, e.g. a
  single integration test that cancels before `PostAsJsonAsync` and asserts the request is
  aborted rather than completed.
- **Priority**: **Minor** — no spec AC references cancellation; this does not block the
  feature, but leaves the CLAUDE.md CancellationToken discipline structurally present yet
  functionally unverified.

---

## Requirement Traceability Update

| Requirement | Iteration 2 | Iteration 3 |
|---|---|---|
| DEV-01 | ✅ Verified | ✅ Verified |
| DEV-02 | ⚠️ Partial | ✅ **Verified — gap closed** |
| DEV-03 … DEV-22 | ✅ Verified | ✅ Verified |
| DEV-23 | ✅ Verified | ⚠️ **Partial — new gap** (covered for 2/6 fields; sensor-proven exploitable for 2 more) |
| DEV-24, DEV-25 | ✅ Verified | ✅ Verified |
| DEV-26 | ⏭️ Out of scope | ⏭️ Out of scope (P3, deliberately unscheduled) |

---

## Working-Tree Integrity

`git status --short` and `git diff --stat` on the real repository (not the scratch worktree)
returned empty both before and after the full sensor run. All nine mutations (3 regression
re-checks + 6 new) were applied and reverted exclusively inside a detached `git worktree` at
`/tmp/…/scratchpad/mut3`, which was removed with `git worktree remove --force` at the end of
this session. No mutation leaked into the working tree.

---

## Summary

**Overall**: ❌ **Not Ready** — one AC-level gap (DEV-23), one unmapped robustness gap
(CancellationToken), both grounded in surviving mutants, not speculation.

**Prior gaps**: DEV-16 span emission — ✅ closed (R1, R2 killed). DEV-02 blank password —
✅ closed (R3 killed).
**Spec-anchored check**: 24/25 ACs matched the spec-defined outcome exactly · 1 new partial
(DEV-23) · 0 spec-precision gaps.
**Edge cases**: 8/8 covered.
**Sensor**: 6 new mutations, 3 killed, 3 survived (plus 3/3 regression re-checks killed).
**Gate**: 156 passed, 0 failed, 0 skipped (build exit 0, test exit 0).

**What works**: Both prior iterations' fixes hold under direct re-attack. 24 of 25 in-scope
ACs have precise, value-level evidence, independently re-derived from the actual test files at
HEAD rather than carried over from prior reports. Location header correctness, RFC 7807 status
fidelity for both the conflict and outage paths, and the two previously-fixed observability/
validation gaps all survived fresh, differently-targeted attacks this round.

**Issues found**:
1. **DEV-23** — `Device.Update`'s per-field "did this actually change" guard is verified for
   only 2 of the 6 independently-updatable fields; the same fault silently applied to
   `FaceCapacity` or `HttpPort` alone passes the entire suite (Fix 1, Major).
2. **CancellationToken propagation on the write path is unverified** — not spec-mapped, so it
   does not change any AC's status, but a real, reproducible test-strength gap (Fix 2, Minor).

**Next steps**: This is iteration 3 of a maximum of 3 — the fix→re-verify loop is exhausted.
Per the skill's bound, this FAIL escalates to the user rather than looping automatically.
Fix 1 is small (four short domain tests, no production-code change expected) and should be
picked up before the feature is considered closed; Fix 2 is discretionary.

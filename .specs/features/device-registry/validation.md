# Device Registry Validation

**Date**: 2026-08-12
**Spec**: `.specs/features/device-registry/spec.md`
**Diff range**: `4764df9..HEAD` (`7e281cd`, branch `feat/device-registry`) — 21 commits
**Verifier**: independent sub-agent (author ≠ verifier), evidence-or-zero

**Verdict**: ❌ **FAIL** — 23/25 ACs fully covered, 1 partial, 1 not covered. One surviving mutant.

The implementation is of high quality and the gate is green; the failure is narrow and
confined to observability (DEV-16, and the trace-attribute clause of DEV-07). Every
behavioural criterion around registration, validation, uniqueness, encryption, amendment
and removal is covered by assertions that target the spec-defined outcome.

---

## Spec-Anchored Acceptance Criteria

All paths below are relative to the repository root. Line numbers are at `7e281cd`.

### P1: Register a device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-01 — valid registration persists + `201` + `Location` + body fields | `201`, `Location: /api/devices/{id}`, body carries id, name, ipAddress, httpPort, username, faceCapacity, createdAt, updatedAt | `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs:78` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`; `:83` — `Assert.Equal($"/api/devices/{id}", response.Headers.Location?.ToString())`; `:84-88` — `Assert.Equal("Front Gate Reader", body.GetProperty("name").GetString())`, `Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString())`, `Assert.Equal(80, body.GetProperty("httpPort").GetInt32())`, `Assert.Equal("admin", body.GetProperty("username").GetString())`, `Assert.Equal(10_000, body.GetProperty("faceCapacity").GetInt32())`; `:89-92` — `Assert.Equal(body.GetProperty("createdAt").GetDateTime(), body.GetProperty("updatedAt").GetDateTime())`. Domain: `Domain/DeviceCreateTests.cs:28-33`, `:41-42` | ✅ PASS — every named field asserted on value (payload rule satisfied) |
| DEV-02 — omitted/blank required field → `400` naming it | `400` + validation problem naming the offending field | `DeviceEndpointsTests.cs:54` — `Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)` and `:58-61` — `Assert.True(errors.TryGetProperty(expectedField, out var messages))`, driven per field at `:111` (name), `:119` (blank name), `:136` (ipAddress), `:153` (httpPort), `:170` (username), `:187` (password), `:204` (faceCapacity). Domain messages: `Domain/DeviceCreateTests.cs:61-62,71-72,81-82,91-92,101-102,111-112,121-122` | ✅ PASS |
| DEV-03 — name/username > 100 → `400` naming it | `400` naming that field | `DeviceEndpointsTests.cs:222` / `:238` — `AssertRejectedFieldAsync(response, "name"/"username")` for `new string('n',101)`; message asserted at `Domain/DeviceCreateTests.cs:143` — `Assert.Equal(Device.Errors.NameTooLong, result.AsT1.Message)` and `:162` — `UsernameTooLong` | ✅ PASS |
| DEV-04 — unparseable ip / port ∉ 1…65535 / capacity ∉ 1…1e6 → `400` naming it | `400` naming that field | `DeviceEndpointsTests.cs:248` (ipAddress), `:258` (`[InlineData(0)] [InlineData(65536)]`), `:281` (`[InlineData(0)] [InlineData(-1)] [InlineData(1_000_001)]`). Messages: `Domain/ValueObjectTests.cs:89-90` — `Assert.Equal(Port.Errors.OutOfRange, result.AsT1.Message)`; `:125-126` — `FaceCapacity.Errors.OutOfRange`; `:44-45` — `IpAddress.Errors.InvalidFormat` | ✅ PASS |
| DEV-05 — address already held → `409`, no second device | `409 Conflict`, exactly one row | `DeviceEndpointsTests.cs:303-304` — `Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)` + `Assert.Equal(1, await CountDevicesAsync())` | ✅ PASS |
| DEV-06 — concurrent same address → exactly one device, one `409`, no unhandled exception | 1 × `201`, rest `409`, no `500`, one row | `DeviceEndpointsTests.cs:332-335` — `Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created))`, `Assert.Equal(attempts-1, statuses.Count(s => s == HttpStatusCode.Conflict))`, `Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses)`, `Assert.Equal(1, await CountDevicesAsync())`. Deterministic 23505 fallback: `DeviceRepositoryTests.cs:76-80` — `Assert.True(result.IsT1)` + `Assert.Equal(IDeviceRepository.AddressAlreadyRegistered, result.AsT1.Message)` + `Assert.Equal(1, await verification.Devices.CountAsync())` | ✅ PASS — see note below |
| DEV-07 — AES-256 ciphertext at rest, normalized ip, never in response / logs / trace attributes | password + ciphertext absent from response body, application logs, trace attributes; ip stored normalized | **Response**: `DeviceEndpointsTests.cs:346` — `Assert.DoesNotContain(SentinelPassword, json)`, `:349-352` — no property name matching `password`; `CredentialLeakageTests.cs:179-182` — across all 5 route responses, `Assert.DoesNotContain(SentinelPassword/ReplacementPassword/_storedCiphertext/_replacedCiphertext, body)`. **Logs**: `CredentialLeakageTests.cs:193` — `Assert.Contains(_logSink.Lines, line => line.Contains("Executed DbCommand"))` (sink liveness) then `:197-200` same four sweeps per line; key: `:211-215`. **At rest**: `:226-232` — `Assert.NotEqual(SentinelPassword, _storedCiphertext)` + `Assert.Equal(SentinelPassword, encryptionService.Decrypt(_storedCiphertext))`. **Normalized ip**: `Domain/DeviceCreateTests.cs:50` — `Assert.Equal("192.168.1.1", device.IpAddress.Value)`. **Trace attributes**: no assertion found | ⚠️ **PARTIAL** — 2 of 3 leak channels proven; the "trace attributes" clause has no `file:line` |

**DEV-06 note.** Mutation 3 (repository swallows `23505`) did **not** fail the concurrency test at
`DeviceEndpointsTests.cs:320` — under that run the service-level pre-check absorbed all seven
duplicates, so the database fallback never fired. The fallback is nevertheless proven
deterministically by `DeviceRepositoryTests.cs:64-81`, which deliberately bypasses the pre-check.
DEV-06 is covered, but the coverage rests on the repository test, not the race test; the race test
alone is timing-dependent and must not be treated as the sole guard.

### P1: Inspect the device catalogue

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-08 — listing returns every device, same fields, never a password | `200`, all devices, full field set, no password | `DeviceEndpointsTests.cs:464` — `Assert.Equal(HttpStatusCode.OK, ...)`; `:467` — `Assert.Equal(2, listed.Count)`; `:472-478` — `Assert.True(frontGate.GetProperty("id").GetInt32() > 0)`, `Assert.Equal("192.168.1.10", ...ipAddress...)`, `Assert.Equal(80, ...httpPort...)`, `Assert.Equal("admin", ...username...)`, `Assert.Equal(10_000, ...faceCapacity...)`, `Assert.NotEqual(default, ...createdAt...)`, `Assert.NotEqual(default, ...updatedAt...)`; no password at `:494-500` | ✅ PASS |
| DEV-09 — no devices → `200` empty array, not `404` | `200` + `[]` | `DeviceEndpointsTests.cs:449` — `Assert.Equal(HttpStatusCode.OK, ...)`; `:452` — `Assert.Equal(JsonValueKind.Array, body.ValueKind)`; `:453` — `Assert.Empty(body.EnumerateArray())` | ✅ PASS |
| DEV-10 — known id → `200` with device; unknown → `404` RFC 7807 | `200`/`404` + problem body | `DeviceEndpointsTests.cs:379-387` — `Assert.Equal(HttpStatusCode.OK, ...)` + per-field equality incl. `Assert.Equal(id, body.GetProperty("id").GetInt32())`; `:395-402` — `Assert.Equal(HttpStatusCode.NotFound, ...)`, `Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType)`, `Assert.Equal(404, body.GetProperty("status").GetInt32())` | ✅ PASS |

### P1: Operational foundation

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-12 — empty PG → migrations applied, starts; no `EnsureCreated()` | schema created *via migrations*, history recorded | `HarnessTests.cs:39-40` — `Assert.Contains("devices", tables)` + `Assert.Contains(PostgresFixture.MigrationHistoryTable, tables)`; `:48-49` — `Assert.NotEmpty(applied)` + `Assert.Contains(applied, m => m.EndsWith("InitialCreate"))`. The `__EFMigrationsHistory` row is the behavioural discriminator against `EnsureCreated()`, which writes no history. Schema is produced by the app's own startup `Migrate()` (`PostgresFixture.cs:39`) | ✅ PASS |
| DEV-13 — suite runs on real PG via Testcontainers, state isolated | real PostgreSQL container; no test sees another's rows | `PostgresFixture.cs:20-22` — `new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build()`; `:43-51` — `Respawner.CreateAsync(...)`; isolation asserted by the paired tests `HarnessTests.cs:59,63` and `:69,73` — each `Assert.Equal(0, await CountDevicesAsync())` then `Assert.Equal(1, ...)`; whichever runs second fails if reset did not happen | ✅ PASS |
| DEV-14 — DB unreachable → `503` RFC 7807, no stack trace / connection string | `503`, problem body, no leak | `ErrorHandlingTests.cs:62-63` — `Assert.Equal(HttpStatusCode.ServiceUnavailable, ...)` + `Assert.Equal("application/problem+json", ...)`; `:67-75` — `Assert.Equal(503, ...GetProperty("status")...)`, `Assert.Equal(GlobalExceptionHandler.DatabaseUnavailableTitle, ...)`, `Assert.Equal(GlobalExceptionHandler.DatabaseUnavailableDetail, ...)`; `:93-94` — `Assert.DoesNotContain(detail, body)` for host, port, database, username, password; `:106-107` — same for `Npgsql`, `Exception`, `stacktrace`, `"   at "`, `Host=`, `Password=`, `Connection refused`. Non-DB failure stays `500`: `:142-151` | ✅ PASS |
| DEV-15 — missing / non-32-byte Base64 key → fail at startup with clear diagnostic | startup aborts, message names the setting | `StartupTests.cs:60-63` — `Assert.Throws<OptionsValidationException>(() => factory.CreateClient())` + `Assert.Contains(EncryptionOptionsValidator.KeyPath, exception.Message)` + `Assert.Contains(EncryptionOptionsValidator.MissingKeyMessage, ...)`; `:73-76` — same for a 3-byte key with `WrongLengthKeyMessage`. Validator rules: `Domain/EncryptionServiceTests.cs:92-94, 103-104, 113-114, 125-127` (`[InlineData(16)] [InlineData(31)] [InlineData(33)]`) | ✅ PASS |
| DEV-16 — a handled request emits a trace with the HTTP span **and its child EF Core spans**, exported only when `OpenTelemetry:OtlpEndpoint` is set | (a) spans actually emitted for a request, HTTP parent + EF Core children; (b) export gated on config | (b) **covered**: `StartupTests.cs:86` — `Assert.Null(factory.Services.GetService<TracerProvider>())` with empty endpoint; `:94` — `Assert.NotNull(...)` with `http://localhost:4317`. (a) **no evidence**: `grep -rn "Activity\|ActivityListener\|span\|Span"` over `src/HikvisionReplicator.Tests/` and `src/HikvisionReplicator.E2ETests/` returns **zero** hits. No test observes an emitted span, its name, or its parent/child relationship | ❌ **GAP** — clause (a) not covered |
| DEV-17 — Development exposes OpenAPI + Scalar; outside Development it does not | `200` in Dev, `404` otherwise, both surfaces | `StartupTests.cs:107` — `Assert.Equal(HttpStatusCode.OK, ...)` for `/openapi/v1.json`; `:118` — same for `/scalar/v1`; `:129` and `:140` — `Assert.Equal(HttpStatusCode.NotFound, ...)` for both outside Development | ✅ PASS |

### P2: Amend a registered device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-18 — subset applied, others unchanged, `200` | only supplied fields change | `DeviceEndpointsTests.cs:540` — `Assert.Equal(HttpStatusCode.OK, ...)`; `:543` — `Assert.Equal("Side Gate Reader", reread.GetProperty("name").GetString())`; `:544-559` — ipAddress, httpPort, username, faceCapacity each `Assert.Equal(original…, reread…)`. Domain: `Domain/DeviceUpdateTests.cs:26-31` | ✅ PASS |
| DEV-19 — invalid field → `400` naming it, no partial change | `400` + zero persisted change | `DeviceEndpointsTests.cs:570` — `AssertRejectedFieldAsync(response, "httpPort")` for `{ name = "Side Gate Reader", httpPort = 0 }`; `:573-581` — `Assert.Equal(original…name…, reread…name…)`, `…httpPort…`, `…updatedAt…`. Domain: `Domain/DeviceUpdateTests.cs:150-158` — all six fields plus `Assert.Equal(CreatedOn, device.UpdatedAt)` | ✅ PASS |
| DEV-20 — onto another device's address → `409`; onto its own → accept | `409` / `200` | `DeviceEndpointsTests.cs:622` — `Assert.Equal(HttpStatusCode.Conflict, ...)`; `:625-635` — moving device's ipAddress and updatedAt unchanged, occupier still holds the address; self-address accepted `:645-649` — `Assert.Equal(HttpStatusCode.OK, ...)` + `Assert.Equal("192.168.1.10", body…ipAddress…)` + `Assert.Equal(80, body…httpPort…)`. Spec behaviour: `DeviceRepositoryTests.cs:200-201` / `:221` | ✅ PASS |
| DEV-21 — omit password → unchanged; supply → ciphertext replaced | stored ciphertext identical / different | `DeviceEndpointsTests.cs:661` — `Assert.Equal(storedBefore, await ReadStoredPasswordAsync(id))`; `:675-676` — `Assert.NotEqual(storedBefore, storedAfter)` + `Assert.DoesNotContain("a-different-Passw0rd", storedAfter)`. Blank password rejected and stored value preserved: `:687-688` | ✅ PASS |
| DEV-22 — update unknown id → `404` | `404` | `DeviceEndpointsTests.cs:696-700` — `Assert.Equal(HttpStatusCode.NotFound, ...)` + `Assert.Equal("application/problem+json", ...)` | ✅ PASS |
| DEV-23 — real change advances `updatedAt`, never `createdAt` | updatedAt strictly greater; createdAt equal | `DeviceEndpointsTests.cs:713-717` — `Assert.True(body…updatedAt… > original…updatedAt…)`; `:718-721` — `Assert.Equal(original…createdAt…, body…createdAt…)`. Domain: `Domain/DeviceUpdateTests.cs:116` — `Assert.Equal(Later, device.UpdatedAt)`; `:126` — `Assert.Equal(CreatedOn, device.CreatedAt)`; no-change cases `:83`, `:93`, `:106` | ✅ PASS |

### P2: Remove a device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-11 — removal → `204`, gone from catalogue and by id | `204`, then `404`, absent from list | `DeviceEndpointsTests.cs:770` — `Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode)`; `:773` — `Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode)`; `:788-789` — `Assert.DoesNotContain(listed, d => d…id… == removedId)` + `Assert.Contains(listed, d => d…id… == keptId)` | ✅ PASS |
| DEV-24 — removal of unknown id → `404` | `404` | `DeviceEndpointsTests.cs:797-801` — `Assert.Equal(HttpStatusCode.NotFound, ...)` + `Assert.Equal("application/problem+json", ...)` | ✅ PASS |
| DEV-25 — removed address becomes available | new registration at that address succeeds | `DeviceEndpointsTests.cs:814` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`; `:817-818` — `Assert.NotEqual(id, body…id…)` + `Assert.Equal("192.168.1.10", body…ipAddress…)` | ✅ PASS |

### P3 (out of scope)

| Criterion | Result |
|---|---|
| DEV-26 — pagination | ⏭️ Deliberately unscheduled (P3). Not a gap. |

**Status**: ❌ 23/25 fully covered · 1 partial (DEV-07 trace-attribute clause) · 1 not covered (DEV-16 clause a)

No spec-precision gaps: every in-scope criterion states a precise outcome, and every located
assertion targets that exact outcome rather than merely asserting that a call occurred.

---

## Edge Cases

| # | Edge case (from spec.md) | `file:line` + assertion | Result |
|---|---|---|---|
| 1 | Non-canonical address (`192.168.001.001`) equals canonical, duplicate rejected | `DeviceEndpointsTests.cs:315-316` — `Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)` + `Assert.Equal(1, await CountDevicesAsync())`; `Domain/ValueObjectTests.cs:17-18` — `Assert.Equal("192.168.1.1", nonCanonical.Value)` + `Assert.Equal(canonical, nonCanonical)` | ✅ |
| 2 | `httpPort` `0`/`65536` reject; `1`/`65535` accept | `DeviceEndpointsTests.cs:251-258` (`[InlineData(0)] [InlineData(65536)]` → 400 naming httpPort) and `:261-268` (`[InlineData(1)] [InlineData(65535)]` → `Assert.Equal(HttpStatusCode.Created, ...)`); `Domain/ValueObjectTests.cs:73-90` | ✅ |
| 3 | `faceCapacity` `0` or negative → `400` | `DeviceEndpointsTests.cs:271-281` — `[InlineData(0)] [InlineData(-1)] [InlineData(1_000_001)]` → `AssertRejectedFieldAsync(response, "faceCapacity")`; `Domain/ValueObjectTests.cs:116-126` | ✅ |
| 4 | `name`/`username` exactly 100 accepted, 101 rejected | `DeviceEndpointsTests.cs:214` / `:230` — `Assert.Equal(HttpStatusCode.Created, ...)` at 100; `:222` / `:238` — rejected at 101; `Domain/DeviceCreateTests.cs:132-133`, `:151-152` | ✅ |
| 5 | Entirely empty update body → `200`, unchanged, `updatedAt` unadvanced | `DeviceEndpointsTests.cs:731` — `Assert.Equal(HttpStatusCode.OK, ...)` for `UpdateAsync(id, new { })`; `:734-741` — `Assert.Equal(original…updatedAt…, body…updatedAt…)` + `Assert.Equal(original…name…, body…name…)`; `Domain/DeviceUpdateTests.cs:83` | ✅ |
| 6 | Multi-byte UTF-8 password round-trips unchanged | `Domain/EncryptionServiceTests.cs:36-40` — `password = "señha-日本語-Ωμέγα-🔐"`, `Assert.Equal(password, service.Decrypt(service.Encrypt(password)))` | ✅ |
| 7 | Malformed JSON body → `400` problem body, not `500` | `DeviceEndpointsTests.cs:859-863` — `Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)` + `Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType)` for body `"{ not json"` | ✅ |
| 8 | Never-existed id and deleted id both `404`, indistinguishably | `DeviceEndpointsTests.cs:830-839` — `Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode)`, `Assert.Equal(neverRegistered.StatusCode, removed.StatusCode)`, and `Assert.Equal(await ProblemFieldsAsync(neverRegistered), await ProblemFieldsAsync(removed))` comparing every problem field except the per-request `traceId` | ✅ |

**Edge cases: 8/8 covered.**

---

## Discrimination Sensor

**Depth**: P0-full (7 behaviour-level mutations — this feature carries data-integrity and
credential-handling paths). **Scratch state**: a detached `git worktree` at
`…/scratchpad/mut`, removed afterwards. The real working tree was never modified.

| # | File:line | Mutation | Tests failed | Killed? |
|---|---|---|---|---|
| 1 | `Domain/IpAddress.cs:25` | Removed normalization — `return new IpAddress(parsed.ToString())` → `return new IpAddress(value)` | 5 — incl. `DeviceEndpointsTests.Address_written_in_a_non_canonical_form_collides_with_its_canonical_form`, `ValueObjectTests.Address_is_stored_in_normalized_form`, `DeviceUpdateTests.Address_repeated_in_non_canonical_form_is_not_treated_as_a_change` | ✅ Killed |
| 2 | `Domain/Device.cs:177-178` | Deleted the `changed` guard so `UpdatedAt = now` always runs | 4 — `DeviceUpdateTests.Update_supplying_no_fields_leaves_the_update_time_unadvanced`, `…Update_repeating_the_current_values…`, `…Address_repeated_in_non_canonical_form…`, `DeviceEndpointsTests.Update_that_changes_nothing_leaves_the_update_timestamp_unadvanced` | ✅ Killed |
| 3 | `Infrastructure/DeviceRepository.cs:40` | Repository swallows the `23505` unique violation and returns `new Success()` | 2 — `DeviceRepositoryTests.Device_reusing_a_registered_address_is_rejected_as_a_conflict`, `…Address_change_onto_another_devices_address_is_rejected_as_a_conflict` | ✅ Killed |
| 4 | `Features/Devices/RegisterDevice/RegisterDeviceService.Interface.cs:20-41` | Added `string Password` to `DeviceResponse`, populated from `device.EncryptedPassword` | 2 — `DeviceEndpointsTests.Device_response_never_includes_the_password`, `CredentialLeakageTests.No_device_response_ever_carries_the_password` | ✅ Killed |
| 5 | `Domain/Port.cs:23` | Off-by-one on the lower bound — `value < Minimum` → `value < Minimum - 1`, accepting port `0` | 5 — incl. `DeviceEndpointsTests.Device_with_an_http_port_outside_the_permitted_range_is_invalid(httpPort: 0)`, `…Rejected_update_persists_no_partial_change`, `ValueObjectTests.Port_outside_the_valid_range_is_rejected(value: 0)` | ✅ Killed |
| 6 | `Infrastructure/GlobalExceptionHandler.cs:50` | Problem `Detail` echoes the raw exception — `Detail = exception.ToString()` | 3 — `DatabaseUnreachableTests.Service_outage_response_describes_nothing_about_the_database_connection`, `…Request_made_while_the_database_is_unreachable_reports_a_service_outage`, `ErrorHandlingTests.Unexpected_failure_reports_an_internal_error` | ✅ Killed |
| 7 | `Program.cs:56-57` | Removed **both** `.AddAspNetCoreInstrumentation()` and `.AddEntityFrameworkCoreInstrumentation()` — the app emits no HTTP spans and no EF Core spans at all | **0** — `Passed! Failed: 0, Passed: 151` | ❌ **SURVIVED** |

**Result**: 6/7 killed — ❌ **FAIL**

Mutation 7 is the empirical proof of the DEV-16 gap. A regression that silently removes all
tracing instrumentation — the exact failure DEV-16 exists to prevent — is invisible to the
suite, because the only tracing assertions check whether a `TracerProvider` is registered in DI,
never whether a span is produced.

---

## Gate Check

- **Build**: `dotnet build HikvisionReplicator.slnx` → **exit 0**, 0 errors, 16 warnings
  (all `NU1902`/`NU1903` OpenTelemetry + Microsoft.OpenApi advisories — known, awaiting user decision)
- **Test**: `dotnet test src/HikvisionReplicator.Tests` → **exit 0**
  - `Passed! - Failed: 0, Passed: 151, Skipped: 0, Total: 151, Duration: 6 s`
- **Skipped tests**: none
- **Failures**: none
- **Test count before feature** (`4764df9`): 35 test attributes, all against the reference
  implementation (17 device + 15 user + 3 user-sync)
- **Test count after feature**: 135 test attributes → **151 resolved cases**
  (58 attributes under `Domain/` unit, 77 at the integration root)
- **Delta**: +116 cases
- **Test Integrity**: the removed `UserEndpointsTests` (15) and `UserSyncJobTests` (3) are
  **justified** — spec Success Criteria requires "the reference implementation under `src/` is
  deleted in this feature's first commit (AD-013)". No assertion in the retained suite was
  weakened; every retained device assertion was strengthened from status-only to field-level.
- **E2E**: `src/HikvisionReplicator.E2ETests/DeviceEndpointsTests.cs` declares **9 NUnit `[Test]`
  methods** (lines 95, 120, 134, 152, 162, 182, 209, 222, 236). **Reported but NOT verified by this
  Verifier** — the suite requires a live API and was deliberately not run.

---

## Code Quality

| Principle | Status |
|---|---|
| Minimum code — no features beyond the spec | ✅ |
| No abstractions for single-use code | ✅ |
| No unnecessary "flexibility" | ✅ |
| Only touched files required for the feature | ✅ |
| Didn't "improve" unrelated code | ✅ |
| Matches existing patterns/style (`CLAUDE.md`: vertical slices, `OneOf`, `Specification<T>`, no `AppDbContext` in services, `CancellationToken` last and required) | ✅ |
| Would a senior engineer approve? | ✅ |
| Tests map to ACs and are non-shallow | ✅ — value-level assertions throughout; `CredentialLeakageTests.cs:193` even asserts sink liveness so the leak sweep cannot vacuously pass |
| Spec-anchored outcome check | ⚠️ — 23/25 exact; DEV-16 clause (a) unasserted, DEV-07 trace clause unasserted |
| Per-layer Coverage Expectation (domain 1:1 ACs; routes happy + edge + error) | ✅ — all five routes covered on happy, edge and error paths |
| Every test maps to a spec AC / edge case / Done-when — no unclaimed tests | ✅ — `DeviceRepositoryTests.Failure_that_is_not_an_address_collision_is_not_reported_as_a_conflict` maps to AD-022, which governs DEV-06 |
| Documented guidelines followed | ✅ — `CLAUDE.md` + `docs/test-patterns.md` (behaviour-based test naming observed throughout) |

Notable strengths worth preserving: the error-message constants are shared between production
and test code (`GlobalExceptionHandler.DatabaseUnavailableDetail`, `IDeviceRepository.AddressAlreadyRegistered`),
so an accidental message change fails a test rather than silently drifting; and
`ProblemFieldsAsync` (`DeviceEndpointsTests.cs:842-848`) compares whole problem bodies minus
`traceId`, which is a genuinely strong way to assert indistinguishability.

---

## Fix Plans

### Fix 1 — DEV-16: no test observes an emitted span (Blocker for this AC)

- **Root cause**: `StartupTests.cs:82-95` asserts only the presence/absence of `TracerProvider`
  in the DI container. Nothing exercises a request with tracing active and inspects the
  resulting spans, so `AddAspNetCoreInstrumentation()` / `AddEntityFrameworkCoreInstrumentation()`
  can both be deleted with the suite fully green (mutation 7).
- **Fix task**: Add a test that boots the factory with `OpenTelemetry:OtlpEndpoint` configured,
  registers an in-memory span exporter (`.AddInMemoryExporter(exportedItems)` from
  `OpenTelemetry.Exporter.InMemory`) — or attaches an `ActivityListener` to the
  `Microsoft.AspNetCore` and `OpenTelemetry.Instrumentation.EntityFrameworkCore` sources — issues
  one `GET /api/devices` that touches the database, then asserts: (a) an HTTP server span exists
  for the route, (b) at least one EF Core span exists, and (c) the EF Core span's `ParentSpanId`
  equals the HTTP span's `SpanId`.
- **Verify**: re-run mutation 7 (remove both instrumentation registrations) and confirm the new
  test now fails.
- **Priority**: **Major** — this is an observability AC, not a data-correctness one, but it is
  currently 100 % undetectable.

### Fix 2 — DEV-07: the "trace attributes" leak channel is unasserted (Minor)

- **Root cause**: `CredentialLeakageTests` sweeps response bodies and log lines but never
  inspects span attributes. In the test environment no OTLP endpoint is configured, so tracing
  is inactive and the channel is never exercised.
- **Fix task**: Extend the Fix 1 in-memory-exporter test to sweep every exported span's
  `Tags`/attributes for the sentinel password, the replacement password and both ciphertexts —
  mirroring the existing `CredentialLeakageTests.cs:197-200` sweep.
- **Verify**: temporarily enable `EnableSensitiveDataLogging()` or add the password as a span tag
  and confirm the new assertion fails.
- **Priority**: **Minor** — the design already mitigates this (EF instrumentation is left at
  defaults so SQL parameters are not captured, per `Program.cs:46-47`), and the Trace-level log
  sweep covers the highest-risk channel. But per evidence-or-zero the clause is unproven.

---

## Requirement Traceability Update

| Requirement | Previous | New |
|---|---|---|
| DEV-01 … DEV-06 | Pending | ✅ Verified |
| DEV-07 | Pending | ⚠️ Partial — response + logs verified; trace attributes unverified |
| DEV-08 … DEV-15 | Pending | ✅ Verified |
| DEV-16 | Pending | ❌ Needs Fix |
| DEV-17 … DEV-25 | Pending | ✅ Verified |
| DEV-26 | Pending | ⏭️ Out of scope (P3, deliberately unscheduled) |

---

## Summary

**Overall**: ⚠️ Issues — not ready to close until Fix 1 lands

**Spec-anchored check**: 23/25 ACs matched the spec-defined outcome exactly · 1 partial · 1 gap · 0 spec-precision gaps
**Edge cases**: 8/8 covered
**Sensor**: 6/7 mutations killed — 1 survived
**Gate**: 151 passed, 0 failed, 0 skipped (build exit 0, test exit 0)

**What works**: Every behavioural requirement of the feature. Registration, the full validation
matrix with exact boundary coverage, database-enforced address uniqueness with a deterministic
`23505` translation test, credential encryption with a genuinely rigorous end-to-end leak sweep
across all five routes and the Trace-level log stream, partial-update semantics including the
subtle "no change means no touch" timestamp rule, removal and address reuse, RFC 7807 error
shaping with `503`/`500` discrimination and no diagnostic leakage, migration-based schema
creation, and Testcontainers isolation. Six of seven injected faults were caught, several by
multiple independent tests at both the domain and HTTP layers.

**Issues found**:
1. DEV-16 clause (a) — no test observes an emitted span; all tracing instrumentation can be
   deleted with the suite green (Fix 1).
2. DEV-07 — the "trace attributes" leak channel has no assertion (Fix 2).

**Next steps**: Route Fix 1 (and Fix 2, which shares its harness) to an implementer, then
re-dispatch the Verifier. Iteration 1 of a maximum of 3.

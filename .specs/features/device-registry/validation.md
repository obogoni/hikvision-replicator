# Device Registry Validation — Iteration 2

**Date**: 2026-08-12
**Spec**: `.specs/features/device-registry/spec.md`
**Diff range**: `4764df9..HEAD` (`70d0694`, branch `feat/device-registry`) — 22 commits
**Verifier**: independent sub-agent (author ≠ verifier ≠ iteration-1 verifier), evidence-or-zero
**Iteration**: 2 of a maximum of 3

**Verdict**: ❌ **FAIL** — 24/25 ACs fully covered, 1 partial. One surviving mutant.

Coverage was re-derived from `spec.md` from scratch; iteration 1's findings were treated as
claims to re-test, not as established fact. The iteration-1 gap is genuinely closed. The
failure this round is new and narrow: a single unasserted sub-clause of DEV-02 — a
whitespace-only `password` on **registration** — which the discrimination sensor proved is
undetectable by the suite.

---

## Iteration-1 Regression Re-check

Iteration 1 failed on one surviving mutant: removing both OpenTelemetry instrumentation
registrations from `Program.cs` left the suite fully green (DEV-16 clause (a) and the
trace-attribute clause of DEV-07 were unobservable). Fix task T21 (commit `7411707`) added
`src/HikvisionReplicator.Tests/TracingTests.cs`. Both halves of the original mutation were
re-run independently in scratch state:

| # | Mutation | Result | Tests failed |
|---|---|---|---|
| R1 | `Program.cs:56` — removed `.AddAspNetCoreInstrumentation()` | ✅ **Killed** (was green in iteration 1) | 2 — `TracingTests.Handled_request_produces_a_span_naming_the_route_that_served_it`, `TracingTests.Database_work_is_traced_as_a_child_of_the_request_that_caused_it` |
| R2 | `Program.cs:57` — removed `.AddEntityFrameworkCoreInstrumentation()` | ✅ **Killed** (was green in iteration 1) | 3 — `TracingTests.Database_work_is_traced_as_a_child_of_the_request_that_caused_it`, `TracingTests.No_span_attribute_ever_carries_the_password`, `TracingTests.No_span_attribute_ever_carries_the_encryption_key` |

**Iteration-1 gap: closed.** Both mutations were reverted.

The fix is not merely present but load-bearing in the right way: `TracingTests.cs:216` and
`:229` (`Assert.NotEmpty(DatabaseSpans())`) are liveness guards, so the credential sweep over
span attributes cannot vacuously pass on an empty trace — which is why R2 kills the DEV-07
trace-attribute tests as well as the DEV-16 ones.

---

## Spec-Anchored Acceptance Criteria

All paths are relative to the repository root. Line numbers are at `70d0694`.

### P1: Register a device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-01 — valid registration persists + `201` + `Location` + body fields | `201`, `Location: /api/devices/{id}`, body carries id, name, ipAddress, httpPort, username, faceCapacity, createdAt, updatedAt | `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs:78` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`; `:83` — `Assert.Equal($"/api/devices/{id}", response.Headers.Location?.ToString())`; `:84-88` — `Assert.Equal("Front Gate Reader", body.GetProperty("name").GetString())`, `Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString())`, `Assert.Equal(80, body.GetProperty("httpPort").GetInt32())`, `Assert.Equal("admin", body.GetProperty("username").GetString())`, `Assert.Equal(10_000, body.GetProperty("faceCapacity").GetInt32())`; `:89-92` — `Assert.Equal(body.GetProperty("createdAt").GetDateTime(), body.GetProperty("updatedAt").GetDateTime())`. Domain: `Domain/DeviceCreateTests.cs:28-33`, `:41-42` | ✅ PASS — all eight named fields asserted on value |
| DEV-02 — omitted **or blanked** required field → `400` naming it | `400` + validation problem naming the offending field, for each of the six fields, omitted **or blank** | **Omitted**, all six: `DeviceEndpointsTests.cs:111` (name), `:136` (ipAddress), `:153` (httpPort), `:170` (username), `:187` (password), `:204` (faceCapacity), each via the helper at `:54` — `Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)` and `:58-62` — `Assert.True(errors.TryGetProperty(expectedField, out var messages))` + `Assert.NotEmpty(messages.EnumerateArray())`. **Blank**: name `:119`; username `Domain/DeviceCreateTests.cs:111-112`; ipAddress `Domain/ValueObjectTests.cs:63-65`. `httpPort`/`faceCapacity` are numeric — "blank" is not expressible, so omission is the whole rule. **Blank `password` on registration: no assertion found** (the only blank-password test, `DeviceEndpointsTests.cs:685`, is on the *update* route) | ⚠️ **PARTIAL** — 11 of 12 sub-cases covered; the blank-password-on-registration clause has no `file:line`. Proven undetectable by sensor mutation M11 |
| DEV-03 — name/username > 100 → `400` naming it | `400` naming that field | `DeviceEndpointsTests.cs:222` / `:238` — `await AssertRejectedFieldAsync(response, "name" / "username")` for `new string('n', 101)` / `new string('u', 101)`. Message: `Domain/DeviceCreateTests.cs:143` — `Assert.Equal(Device.Errors.NameTooLong, result.AsT1.Message)`; `:162` — `Assert.Equal(Device.Errors.UsernameTooLong, result.AsT1.Message)` | ✅ PASS |
| DEV-04 — unparseable ip / port ∉ 1…65535 / capacity ∉ 1…1e6 → `400` naming it | `400` naming that field | `DeviceEndpointsTests.cs:248` (ipAddress `"not-an-address"`); `:251-258` (`[InlineData(0)] [InlineData(65536)]` → `AssertRejectedFieldAsync(response, "httpPort")`); `:271-281` (`[InlineData(0)] [InlineData(-1)] [InlineData(1_000_001)]` → `AssertRejectedFieldAsync(response, "faceCapacity")`). Messages: `Domain/ValueObjectTests.cs:44-45` — `Assert.Equal(IpAddress.Errors.InvalidFormat, result.AsT1.Message)`; `:89-90` — `Assert.Equal(Port.Errors.OutOfRange, result.AsT1.Message)`; `:125-126` — `Assert.Equal(FaceCapacity.Errors.OutOfRange, result.AsT1.Message)` | ✅ PASS |
| DEV-05 — address already held → `409`, no second device | `409 Conflict`, exactly one row | `DeviceEndpointsTests.cs:303-304` — `Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)` + `Assert.Equal(1, await CountDevicesAsync())` | ✅ PASS |
| DEV-06 — concurrent same address → exactly one device, one `409`, no unhandled exception | 1 × `201`, rest `409`, no `500`, one row | `DeviceEndpointsTests.cs:332-335` — `Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created))`, `Assert.Equal(attempts - 1, statuses.Count(status => status == HttpStatusCode.Conflict))`, `Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses)`, `Assert.Equal(1, await CountDevicesAsync())`. Deterministic `23505` fallback (pre-check deliberately bypassed): `DeviceRepositoryTests.cs:76-80` — `Assert.True(result.IsT1)` + `Assert.Equal(IDeviceRepository.AddressAlreadyRegistered, result.AsT1.Message)` + `Assert.Equal(1, await verification.Devices.CountAsync())` | ✅ PASS |
| DEV-07 — AES-256 ciphertext at rest, normalized ip, never in response / logs / **trace attributes** | password + ciphertext absent from all three channels; ip stored normalized | **Response**: `DeviceEndpointsTests.cs:346` — `Assert.DoesNotContain(SentinelPassword, json)`, `:349-352` — no property name containing `password`; `CredentialLeakageTests.cs:175` — `Assert.Equal(5, _responseBodies.Count)` then `:179-182` — `Assert.DoesNotContain(SentinelPassword / ReplacementPassword / _storedCiphertext / _replacedCiphertext, body)` across all five route responses. **Logs**: `CredentialLeakageTests.cs:193` — `Assert.Contains(_logSink.Lines, line => line.Contains("Executed DbCommand"))` (liveness) then `:197-200` the same four sweeps per line; key at `:207-216`. **Trace attributes**: `TracingTests.cs:216` — `Assert.NotEmpty(DatabaseSpans())` (liveness) then `:221-222` — `Assert.DoesNotContain(SentinelPassword, attribute, StringComparison.OrdinalIgnoreCase)` + `Assert.DoesNotContain(_storedCiphertext, attribute, StringComparison.Ordinal)` over every exported span tag; key at `:229-238`. **At rest**: `CredentialLeakageTests.cs:226-232` — `Assert.NotEqual(SentinelPassword, _storedCiphertext)` + `Assert.Equal(SentinelPassword, encryptionService.Decrypt(_storedCiphertext))`. **Normalized ip**: `Domain/DeviceCreateTests.cs:50` — `Assert.Equal("192.168.1.1", device.IpAddress.Value)` for input `"192.168.001.001"` | ✅ PASS — **all three leak channels now proven** (iteration-1 gap closed) |

### P1: Inspect the device catalogue

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-08 — listing returns every device, same fields, never a password | `200`, all devices, full field set, no password | `DeviceEndpointsTests.cs:464` — `Assert.Equal(HttpStatusCode.OK, response.StatusCode)`; `:467` — `Assert.Equal(2, listed.Count)`; `:472-478` — `Assert.True(frontGate.GetProperty("id").GetInt32() > 0)`, `Assert.Equal("192.168.1.10", frontGate.GetProperty("ipAddress").GetString())`, `Assert.Equal(80, …httpPort…)`, `Assert.Equal("admin", …username…)`, `Assert.Equal(10_000, …faceCapacity…)`, `Assert.NotEqual(default, …createdAt…)`, `Assert.NotEqual(default, …updatedAt…)`; no password at `:494-500` | ✅ PASS |
| DEV-09 — no devices → `200` empty array, not `404` | `200` + `[]` | `DeviceEndpointsTests.cs:449` — `Assert.Equal(HttpStatusCode.OK, response.StatusCode)`; `:452` — `Assert.Equal(JsonValueKind.Array, body.ValueKind)`; `:453` — `Assert.Empty(body.EnumerateArray())` | ✅ PASS |
| DEV-10 — known id → `200` with that device; unknown → `404` RFC 7807 | `200` / `404` + problem body | `DeviceEndpointsTests.cs:379-387` — `Assert.Equal(HttpStatusCode.OK, …)` + `Assert.Equal(id, body.GetProperty("id").GetInt32())` and per-field equality; `:395-402` — `Assert.Equal(HttpStatusCode.NotFound, …)`, `Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType)`, `Assert.Equal(404, body.GetProperty("status").GetInt32())` | ✅ PASS |

### P1: Operational foundation

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-12 — empty PG → migrations applied, starts; no `EnsureCreated()` | schema created *via migrations*, history recorded | `HarnessTests.cs:39-40` — `Assert.Contains("devices", tables)` + `Assert.Contains(PostgresFixture.MigrationHistoryTable, tables)`; `:48-49` — `Assert.NotEmpty(applied)` + `Assert.Contains(applied, migration => migration.EndsWith("InitialCreate"))`. The `__EFMigrationsHistory` rows are the behavioural discriminator against `EnsureCreated()`, which writes none. The schema is produced by the application's own startup `Migrate()` (`PostgresFixture.cs:39`), not by the harness | ✅ PASS — sensor M5 confirms |
| DEV-13 — suite runs on real PG via Testcontainers, state isolated | real PostgreSQL container; no test sees another's rows | `PostgresFixture.cs:20-22` — `new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build()`; `:43-51` — `Respawner.CreateAsync(…)`; `:54` — `ResetAsync() => _respawner.ResetAsync(_connection)`. Isolation asserted by the paired tests `HarnessTests.cs:59,63` and `:69,73` — each `Assert.Equal(0, await CountDevicesAsync())` then `Assert.Equal(1, await CountDevicesAsync())`; whichever runs second fails if the reset did not happen | ✅ PASS — sensor M10 confirms |
| DEV-14 — DB unreachable → `503` RFC 7807, no stack trace / connection string | `503`, problem body, no leak | `ErrorHandlingTests.cs:62-63` — `Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode)` + `Assert.Equal("application/problem+json", …)`; `:67-75` — `Assert.Equal(503, …GetProperty("status")…)`, `Assert.Equal(GlobalExceptionHandler.DatabaseUnavailableTitle, …)`, `Assert.Equal(GlobalExceptionHandler.DatabaseUnavailableDetail, …)`; `:93-94` — `Assert.DoesNotContain(detail, body, …)` for host, port, database, username, password; `:106-107` — same for `Npgsql`, `Exception`, `stacktrace`, `"   at "`, `Host=`, `Password=`, `Connection refused`. Non-database failure stays `500`: `:136-151` | ✅ PASS |
| DEV-15 — missing / non-32-byte Base64 key → fail at startup with a clear diagnostic | startup aborts; message names the setting | `StartupTests.cs:60-63` — `Assert.Throws<OptionsValidationException>(() => factory.CreateClient())` + `Assert.Contains(EncryptionOptionsValidator.KeyPath, exception.Message)` + `Assert.Contains(EncryptionOptionsValidator.MissingKeyMessage, exception.Message)`; `:73-76` — same for a 3-byte key with `WrongLengthKeyMessage`. Validator rules: `Domain/EncryptionServiceTests.cs:92-94`, `:103-104`, `:113-114`, `:125-127` (`[InlineData(16)] [InlineData(31)] [InlineData(33)]`) | ✅ PASS |
| DEV-16 — a handled request emits a trace with the HTTP span **and its child EF Core spans**, exported only when `OpenTelemetry:OtlpEndpoint` is set | (a) spans really emitted, HTTP parent + EF Core children; (b) export gated on config | (a) `TracingTests.cs:187-190` — `var requestSpan = Assert.Single(RequestSpans())` + `Assert.Equal(ActivityKind.Server, requestSpan.Kind)` + `Assert.Equal("POST /api/devices", requestSpan.DisplayName)`; `:204-206` — `Assert.NotEmpty(databaseSpans)` + `Assert.All(databaseSpans, span => Assert.Equal(requestSpan.SpanId, span.ParentSpanId))` + `Assert.All(databaseSpans, span => Assert.Equal(ActivityKind.Client, span.Kind))`. (b) `StartupTests.cs:86` — `Assert.Null(factory.Services.GetService<TracerProvider>())` with an empty endpoint; `:94` — `Assert.NotNull(…)` with `http://localhost:4317` | ✅ PASS — **both clauses now covered** (iteration-1 gap closed); sensors R1, R2, M9 confirm |
| DEV-17 — Development exposes OpenAPI + Scalar; outside Development it does not | `200` in Dev, `404` otherwise, both surfaces | `StartupTests.cs:107` — `Assert.Equal(HttpStatusCode.OK, response.StatusCode)` for `/openapi/v1.json`; `:118` — same for `/scalar/v1`; `:129` and `:140` — `Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)` for both outside Development | ✅ PASS |

### P2: Amend a registered device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-18 — subset applied, others unchanged, `200` | only supplied fields change | `DeviceEndpointsTests.cs:540` — `Assert.Equal(HttpStatusCode.OK, response.StatusCode)`; `:543` — `Assert.Equal("Side Gate Reader", reread.GetProperty("name").GetString())`; `:544-559` — ipAddress, httpPort, username, faceCapacity each `Assert.Equal(original.GetProperty(…), reread.GetProperty(…))`. Domain: `Domain/DeviceUpdateTests.cs:26-31` | ✅ PASS |
| DEV-19 — invalid field → `400` naming it, no partial change | `400` + zero persisted change | `DeviceEndpointsTests.cs:570` — `await AssertRejectedFieldAsync(response, "httpPort")` for `{ name = "Side Gate Reader", httpPort = 0 }`; `:573-581` — `Assert.Equal(original…name…, reread…name…)`, `…httpPort…`, `…updatedAt…`. Domain: `Domain/DeviceUpdateTests.cs:150-158` — all six fields plus `Assert.Equal(CreatedOn, device.UpdatedAt)` | ✅ PASS |
| DEV-20 — onto another device's address → `409`; onto its own → accept | `409` / `200` | `DeviceEndpointsTests.cs:622` — `Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)`; `:625-635` — moving device's ipAddress and updatedAt unchanged, occupier still at `"192.168.1.10"`; self-address accepted at `:645-649` — `Assert.Equal(HttpStatusCode.OK, …)` + `Assert.Equal("192.168.1.10", body…ipAddress…)` + `Assert.Equal(80, body…httpPort…)`. Spec behaviour: `DeviceRepositoryTests.cs:200-201` — `Assert.NotNull(found)` + `Assert.Equal(holderId, found.Id)`; `:221` — `Assert.Null(found)` when the device excluded is the holder | ✅ PASS — sensor M3 confirms |
| DEV-21 — omit password → unchanged; supply → ciphertext replaced | stored ciphertext identical / different | `DeviceEndpointsTests.cs:661` — `Assert.Equal(storedBefore, await ReadStoredPasswordAsync(id))`; `:675-676` — `Assert.NotEqual(storedBefore, storedAfter)` + `Assert.DoesNotContain("a-different-Passw0rd", storedAfter)`. Blank password rejected and stored value preserved: `:687-688` | ✅ PASS — sensor M2 confirms |
| DEV-22 — update unknown id → `404` | `404` | `DeviceEndpointsTests.cs:696-700` — `Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)` + `Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType)` | ✅ PASS |
| DEV-23 — real change advances `updatedAt`, never `createdAt` | updatedAt strictly greater; createdAt equal | `DeviceEndpointsTests.cs:713-717` — `Assert.True(body…updatedAt…GetDateTime() > original…updatedAt…GetDateTime())`; `:718-721` — `Assert.Equal(original…createdAt…, body…createdAt…)`. Domain: `Domain/DeviceUpdateTests.cs:116` — `Assert.Equal(Later, device.UpdatedAt)`; `:126` — `Assert.Equal(CreatedOn, device.CreatedAt)`; no-change cases `:83`, `:93`, `:106` | ✅ PASS |

### P2: Remove a device

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
|---|---|---|---|
| DEV-11 — removal → `204`, gone from catalogue and by id | `204`, then `404`, absent from list | `DeviceEndpointsTests.cs:770` — `Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode)`; `:773` — `Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode)`; `:788-789` — `Assert.DoesNotContain(listed, device => device.GetProperty("id").GetInt32() == removedId)` + `Assert.Contains(listed, device => …== keptId)` | ✅ PASS |
| DEV-24 — removal of unknown id → `404` | `404` | `DeviceEndpointsTests.cs:797-801` — `Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)` + `Assert.Equal("application/problem+json", …)` | ✅ PASS |
| DEV-25 — removed address becomes available | a new registration at that address succeeds | `DeviceEndpointsTests.cs:814` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`; `:817-818` — `Assert.NotEqual(id, body.GetProperty("id").GetInt32())` + `Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString())` | ✅ PASS — sensor M1 confirms (killed *only* by this test) |

### P3 (out of scope)

| Criterion | Result |
|---|---|
| DEV-26 — pagination | ⏭️ Deliberately unscheduled (P3). Not a gap. |

**Status**: ⚠️ 24/25 fully covered · 1 partial (DEV-02 blank-password-on-registration).

**Spec-precision gaps: none.** Every in-scope criterion states a precise outcome, and every
located assertion targets that exact outcome rather than merely asserting that something
happened. The DEV-02 shortfall is a coverage gap, not a precision gap: the spec is precise
("omits **or blanks**"), the tests simply do not exercise one of the enumerated cases.

---

## Edge Cases

| # | Edge case (from `spec.md`) | `file:line` + assertion | Result |
|---|---|---|---|
| 1 | Non-canonical address (`192.168.001.001`) equals canonical; duplicate rejected | `DeviceEndpointsTests.cs:315-316` — `Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)` + `Assert.Equal(1, await CountDevicesAsync())`; `Domain/ValueObjectTests.cs:17-18` — `Assert.Equal("192.168.1.1", nonCanonical.Value)` + `Assert.Equal(canonical, nonCanonical)` | ✅ |
| 2 | `httpPort` `0`/`65536` rejected; `1`/`65535` accepted | `DeviceEndpointsTests.cs:251-258` (`[InlineData(0)] [InlineData(65536)]` → `AssertRejectedFieldAsync(response, "httpPort")`) and `:261-268` (`[InlineData(1)] [InlineData(65535)]` → `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`); `Domain/ValueObjectTests.cs:73-79` (accept) and `:84-90` (reject) | ✅ |
| 3 | `faceCapacity` `0` or negative → `400` | `DeviceEndpointsTests.cs:271-281` — `[InlineData(0)] [InlineData(-1)] [InlineData(1_000_001)]` → `AssertRejectedFieldAsync(response, "faceCapacity")`; `Domain/ValueObjectTests.cs:116-126` | ✅ |
| 4 | `name`/`username` exactly 100 accepted, 101 rejected | `DeviceEndpointsTests.cs:214` / `:230` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)` at 100; `:222` / `:238` — rejected at 101; `Domain/DeviceCreateTests.cs:132-133` — `Assert.True(result.IsT0)` + `Assert.Equal(100, result.AsT0.Name.Length)`; `:151-152` for username | ✅ — sensor M6 confirms |
| 5 | Entirely empty update body → `200`, unchanged, `updatedAt` unadvanced | `DeviceEndpointsTests.cs:731` — `Assert.Equal(HttpStatusCode.OK, response.StatusCode)` for `UpdateAsync(id, new { })`; `:734-741` — `Assert.Equal(original…updatedAt…, body…updatedAt…)` + `Assert.Equal(original…name…, body…name…)`; `Domain/DeviceUpdateTests.cs:83` — `Assert.Equal(CreatedOn, device.UpdatedAt)` | ✅ |
| 6 | Multi-byte UTF-8 password round-trips unchanged | `Domain/EncryptionServiceTests.cs:36-40` — `const string password = "señha-日本語-Ωμέγα-🔐"`; `Assert.Equal(password, service.Decrypt(service.Encrypt(password)))` | ✅ |
| 7 | Malformed JSON body → `400` problem body, not `500` | `DeviceEndpointsTests.cs:859-863` — `Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)` + `Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType)` for body `"{ not json"` | ✅ — sensor M8 confirms |
| 8 | Never-existed id and deleted id both `404`, indistinguishably | `DeviceEndpointsTests.cs:830-839` — `Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode)`, `Assert.Equal(neverRegistered.StatusCode, removed.StatusCode)`, and `Assert.Equal(await ProblemFieldsAsync(neverRegistered), await ProblemFieldsAsync(removed))` comparing every problem field except the per-request `traceId` | ✅ |

**Edge cases: 8/8 covered.**

---

## Discrimination Sensor

**Depth**: P0-full. **11 new behaviour-level mutations** (deliberately disjoint from
iteration 1's set) plus the 2 iteration-1 regression re-checks — 13 runs in total.
**Scratch state**: a detached `git worktree` at `…/scratchpad/mut2`, reverted between every
mutation and removed afterwards. The real working tree was never modified.

| # | File:line | Mutation | Tests failed | Killed? |
|---|---|---|---|---|
| R1 | `Program.cs:56` | Removed `.AddAspNetCoreInstrumentation()` | 2 (`TracingTests`) | ✅ Killed |
| R2 | `Program.cs:57` | Removed `.AddEntityFrameworkCoreInstrumentation()` | 3 (`TracingTests`) | ✅ Killed |
| M1 | `RemoveDeviceService.cs:23` + `DeviceConfiguration.cs:43` | Soft-delete instead of hard-delete: the row is tombstoned and hidden by a query filter, so it disappears from the catalogue but keeps its unique-index entry | **1** — `DeviceEndpointsTests.Address_of_a_removed_device_is_free_for_a_new_registration` | ✅ Killed — pinpoint discrimination: DEV-11 still passes, only DEV-25 fires |
| M2 | `UpdateDeviceService.cs:27-34` | Update overwrites the stored password even when none is supplied | 2 — `…Update_that_omits_the_password_leaves_the_stored_one_untouched`, `…Update_that_changes_nothing_leaves_the_update_timestamp_unadvanced` | ✅ Killed |
| M3 | `DeviceByAddressExcludingSpec.cs:16` | Dropped `device.Id != excludedDeviceId`, so the spec also matches the device being updated | 11 — incl. `DeviceRepositoryTests.Device_is_never_matched_by_its_own_address_when_it_is_the_one_excluded`, `DeviceEndpointsTests.Resubmitting_a_devices_own_address_is_accepted` | ✅ Killed |
| M4 | `ListDevicesService.Endpoint.cs:15-16` | Empty catalogue answers `404` instead of `200` + `[]` | **1** — `DeviceEndpointsTests.Listing_devices_with_none_registered_returns_empty` | ✅ Killed |
| M5 | `Program.cs:73` | Startup no longer calls `db.Database.Migrate()` | 84 — incl. `HarnessTests.Application_boots_against_an_empty_database_and_creates_its_schema`, `HarnessTests.Schema_is_recorded_as_applied_migrations` | ✅ Killed |
| M6 | `Domain/Device.cs:55` | Off-by-one on the name bound — `name.Length > MaxNameLength` → `> MaxNameLength + 1`, accepting 101 characters | 2 — `DeviceCreateTests.Device_with_a_name_beyond_the_maximum_length_is_invalid`, `DeviceEndpointsTests.Device_name_longer_than_one_hundred_characters_is_invalid` | ✅ Killed |
| M7 | `EncryptionService.cs:25` | `aes.GenerateIV()` → `aes.IV = new byte[16]`, making the IV constant across calls | **1** — `EncryptionServiceTests.Encrypting_one_password_twice_produces_different_ciphertext` | ✅ Killed |
| M8 | `Program.cs:80` | Removed `app.UseStatusCodePages()`, so framework-generated bodiless failures lose their RFC 7807 body | **1** — `DeviceEndpointsTests.Malformed_request_body_is_rejected_as_a_bad_request` | ✅ Killed |
| M9 | `Program.cs:49` | Tracing registered unconditionally, ignoring the `OtlpEndpoint` gate | 2 — `StartupTests.Traces_are_not_collected_without_a_configured_export_endpoint`, `TracingTests.Database_work_is_traced_as_a_child_of_the_request_that_caused_it` | ✅ Killed |
| M10 | `PostgresFixture.cs:54` | `ResetAsync()` becomes a no-op — no state isolation between tests | 45 — incl. both `HarnessTests` isolation tests | ✅ Killed |
| M11 | `RegisterDeviceService.cs:23` | `string.IsNullOrWhiteSpace(request.Password)` → `request.Password is null`, so a **whitespace-only password is accepted at registration** | **0** — `Passed! Failed: 0, Passed: 155` | ❌ **SURVIVED** |

**Result**: 12/13 killed — ❌ **FAIL**

**On the survivor.** DEV-02 names `password` among the fields that must be rejected when
*blanked*, not only when omitted. With M11 applied, `POST /api/devices` with
`password: "   "` returns `201 Created` and the device is registered with three encrypted
spaces as its credential — a device that can never authenticate against its reader. The
suite does not notice. The asymmetry is instructive: the *update* route has exactly this
test (`DeviceEndpointsTests.cs:680-689`, `Update_with_a_blank_password_is_invalid`), and the
registration route has only the *omitted*-password test (`:174-188`). The blank case was
covered on one route and not the other.

Note also how sharply several of these mutations were discriminated — M1, M4, M7 and M8 each
failed exactly one test, the one that owns the criterion. That is a healthy sign: the suite
is not passing by accident or by broad coupling.

---

## Gate Check

- **Build**: `dotnet build HikvisionReplicator.slnx` → **exit 0**, 0 errors
  (warnings are `NU1902`/`NU1903` OpenTelemetry + `Microsoft.OpenApi` advisories — known,
  awaiting a user decision, out of scope for this verification)
- **Test**: `dotnet test src/HikvisionReplicator.Tests` → **exit 0**
  - `Passed! - Failed: 0, Passed: 155, Skipped: 0, Total: 155, Duration: 6 s`
- **Skipped tests**: none
- **Failures**: none
- **Test count at iteration 1**: 151 resolved cases
- **Test count at iteration 2**: 155 resolved cases
- **Delta**: **+4** — the four `TracingTests` cases added by T21. No test was removed, and no
  assertion was weakened; the T21 commit is purely additive to the test surface.
- **Test Integrity**: ✅ count increased; no deletions; no weakened assertions.
- **E2E**: `src/HikvisionReplicator.E2ETests/DeviceEndpointsTests.cs` declares **9 NUnit
  `[Test]` methods**. **Reported but NOT verified by this Verifier** — the suite requires a
  live API and was deliberately not run.

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
| Tests map to ACs and are non-shallow | ✅ — value-level assertions throughout; both leak-sweep tests carry explicit liveness guards (`CredentialLeakageTests.cs:193`, `TracingTests.cs:216`) so they cannot pass vacuously |
| Spec-anchored outcome check (asserted values match spec) | ⚠️ — 24/25 exact; DEV-02's blank-password-on-registration sub-case unasserted |
| Per-layer Coverage Expectation (domain 1:1 ACs; routes happy + edge + error) | ✅ — all five routes covered on happy, edge and error paths |
| Every test maps to a spec AC / edge case / Done-when — no unclaimed tests | ✅ — `DeviceRepositoryTests.Failure_that_is_not_an_address_collision_is_not_reported_as_a_conflict` maps to AD-022, which governs DEV-06 |
| Documented guidelines followed | ✅ — `CLAUDE.md` + `docs/test-patterns.md` (behaviour-based naming observed throughout, including in the new `TracingTests`) |

`TracingTests.cs` is a good addition on its own terms: it attaches an in-memory exporter to
the application's *own* tracer via `ConfigureOpenTelemetryTracerProvider` rather than building
a parallel tracing stack, so the production composition root is exercised exactly as it ships;
and `WaitForRequestSpanAsync` (`:162-167`) handles the fact that a server span is exported only
after the pipeline unwinds, which is the usual source of flakiness in tests like this.

---

## Fix Plans

### Fix 1 — DEV-02: a blank password is accepted at registration (Major)

- **Root cause**: `DeviceEndpointsTests` covers an *omitted* password on registration
  (`:174-188`) and a *blank* password on update (`:680-689`), but never a blank password on
  registration. `RegisterDeviceService.cs:23` does implement the rule correctly today
  (`string.IsNullOrWhiteSpace`); it is simply unguarded, so any weakening of it —
  a refactor to `is null`, a nullable-reference cleanup, a move of the check into the
  aggregate — ships green.
- **Fix task**: Add one integration test to `DeviceEndpointsTests` mirroring
  `Device_with_a_blank_name_is_invalid` (`:114-120`):
  `var response = await RegisterAsync(ValidRegistration(password: "   "));`
  then `await AssertRejectedFieldAsync(response, "password");`. Consider also asserting the
  message equals `RegisterDeviceService.PasswordRequired`, matching the constant-sharing
  pattern already used elsewhere in the suite.
- **Verify**: re-run mutation M11 (`string.IsNullOrWhiteSpace(request.Password)` →
  `request.Password is null`) and confirm the new test fails.
- **Priority**: **Major** — the production behaviour is correct, so there is no live defect;
  the failure is that DEV-02's blank clause is currently undetectable on the registration
  route, which is exactly the class of regression this AC exists to prevent.

---

## Requirement Traceability Update

| Requirement | Previous (iteration 1) | New (iteration 2) |
|---|---|---|
| DEV-01 | ✅ Verified | ✅ Verified |
| DEV-02 | ✅ Verified | ⚠️ **Partial** — blank-password-on-registration unasserted (survivor M11) |
| DEV-03 … DEV-06 | ✅ Verified | ✅ Verified |
| DEV-07 | ⚠️ Partial | ✅ **Verified** — all three leak channels proven |
| DEV-08 … DEV-15 | ✅ Verified | ✅ Verified |
| DEV-16 | ❌ Needs Fix | ✅ **Verified** — both clauses proven |
| DEV-17 … DEV-25 | ✅ Verified | ✅ Verified |
| DEV-26 | ⏭️ Out of scope | ⏭️ Out of scope (P3, deliberately unscheduled) |

---

## Summary

**Overall**: ⚠️ Issues — one fix task before the feature closes

**Iteration-1 regression**: ✅ closed — both halves of the surviving mutant are now killed
**Spec-anchored check**: 24/25 ACs matched the spec-defined outcome exactly · 1 partial · 0 spec-precision gaps
**Edge cases**: 8/8 covered
**Sensor**: 12/13 mutations killed — 1 survived (11 new mutations + 2 regression re-checks)
**Gate**: 155 passed, 0 failed, 0 skipped (build exit 0, test exit 0)

**What works**: Everything iteration 1 credited, re-derived independently and confirmed —
plus the observability gap it found is now genuinely shut. Eleven fresh mutations across
removal semantics, partial-update password handling, the self-address exemption, the empty-list
contract, startup migration, the name boundary, IV freshness, the problem-body fallback, the
tracing gate and harness isolation were all caught, several by exactly one owning test.

**Issues found**:
1. DEV-02 — a whitespace-only `password` is accepted at registration with the suite green
   (survivor M11). The rule is implemented; it is simply untested on that route (Fix 1).

**Next steps**: Route Fix 1 to an implementer, then re-dispatch the Verifier. This is
iteration 2 of a maximum of 3.

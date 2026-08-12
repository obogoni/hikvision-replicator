# Device Registry Tasks

## Execution Protocol (MANDATORY — do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Design**: `.specs/features/device-registry/design.md` (Approved · 2026-08-12)
**Spec**: `.specs/features/device-registry/spec.md` (Confirmed · 2026-08-12)
**Status**: All phases complete (T1–T20) · 151 xUnit tests green (69 unit / 82 integration) + 9 E2E

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec — confirm before Execute.
> **Guidelines found**: `CLAUDE.md` (Tests section), `docs/test-patterns.md` (behaviour-based naming), `.specs/STATE.md` AD-019 (Testcontainers PostgreSQL) and **AD-024** (test level chosen by layer). No coverage threshold is configured anywhere — `coverlet.collector` is referenced but no gate is set, and there is no CI workflow — so the **strong default depth applies**: every spec AC and every listed edge case is covered.
> **Sampled**: `src/HikvisionReplicator.Tests/{DeviceEndpointsTests,UserEndpointsTests,UserSyncJobTests,TestWebApplicationFactory}.cs`, `src/HikvisionReplicator.E2ETests/{Device,User}EndpointsTests.cs`, all three `.csproj` files, `HikvisionReplicator.slnx`.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
|---|---|---|---|---|
| Domain — value objects & aggregate (`Domain/*.cs`) | unit | All branches; 1:1 to spec ACs; every listed edge case has a test | `src/HikvisionReplicator.Tests/Domain/*Tests.cs`, `[Trait("Category","Unit")]` | `dotnet test src/HikvisionReplicator.Tests --filter "Category=Unit"` |
| Repository & specifications (`Infrastructure/*Repository.cs`, `Domain/Specs/*`) | integration | Key query paths + the constraint-violation error path (AD-022) | `src/HikvisionReplicator.Tests/*Tests.cs` | `dotnet test src/HikvisionReplicator.Tests` |
| Feature slices & routes (`Features/Devices/**`) | integration | Every route in scope: happy path + every listed edge case + error paths | `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs` | `dotnet test src/HikvisionReplicator.Tests` |
| Startup & cross-cutting (`Program.cs`, `GlobalExceptionHandler`, options validation) | integration | Every startup-behaviour AC: DEV-12, DEV-14, DEV-15, DEV-16, DEV-17 | `src/HikvisionReplicator.Tests/StartupTests.cs` | `dotnet test src/HikvisionReplicator.Tests` |
| HTTP surface, out-of-process | e2e | The five routes: happy path + one error path each | `src/HikvisionReplicator.E2ETests/DeviceEndpointsTests.cs` | `dotnet test src/HikvisionReplicator.E2ETests` (needs a live API) |
| EF configuration, migrations, project scaffolding | none | build gate only — exercised transitively by the integration suite | — | build gate only |

**Test level is chosen by layer — AD-024 (confirmed 2026-08-12).** Pure logic with no I/O (domain aggregates, value objects, `EncryptionService`, options validation) is unit-tested in isolation under `Tests/Domain/` with `[Trait("Category","Unit")]`, so it runs without Docker. Everything touching I/O or wiring — slices and routes, repositories and specifications, startup, cross-cutting handlers — is integration-tested through the HTTP surface against Testcontainers PostgreSQL. E2E stays a thin out-of-process confirmation, not a coverage layer. Unit tests **add depth**; they never replace endpoint-level AC coverage, which every route keeps.

## Gate Check Commands

> Generated from codebase — confirm before Execute. No CI workflow exists, so these are derived from the `.slnx`, the test `.csproj` files, and `CLAUDE.md`'s Commands section.

| Gate Level | When to Use | Command |
|---|---|---|
| Build | Scaffolding, contracts, EF config, docs — tasks with no required tests | `dotnet build HikvisionReplicator.slnx` |
| Quick | After tasks whose only required tests are domain unit tests | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests --filter "Category=Unit"` |
| Full | After any task with integration tests | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` (**requires a Docker daemon** — Testcontainers, AD-019) |
| E2E | Only T19 | `docker compose up -d && dotnet run --project src/HikvisionReplicator.Api &` then `dotnet test src/HikvisionReplicator.E2ETests` (one-time: `pwsh …/playwright.ps1 install`) |

---

## Execution Plan

Phases are ordered and run sequentially — each phase completes before the next begins, and tasks within a phase execute in order.

### Phase 1: Scaffold & Domain (5 tasks)

Nothing can be tested until the solution compiles; the domain is pure and testable the moment it exists.

```
T1 → T2 → T3 → T4 → T5
```

### Phase 2: Persistence & Test Harness (4 tasks)

The Testcontainers harness is the gate for every integration test that follows, so it lands before the first slice.

```
T6 → T7 → T8 → T9
```

### Phase 3: Feature Slices (6 tasks)

One slice per task, each self-testable through the harness.

```
T10 → T11 → T12 → T13 → T14 → T15
```

### Phase 4: Cross-Cutting, E2E & Docs (5 tasks)

Behaviours that span the whole app and cannot be attributed to a single slice.

```
T16 → T17 → T18 → T19 → T20
```

---

## Task Breakdown

### T1: Scaffold the rewrite solution

**Status**: ✅ Complete — `08a7dbb`

**What**: Delete `src/`, recreate the three projects with pinned packages, and add a `postgres` service to `docker-compose.yml`.
**Where**: `src/HikvisionReplicator.{Api,Tests,E2ETests}/*.csproj`, `HikvisionReplicator.slnx`, `docker-compose.yml`
**Depends on**: None
**Reuses**: Existing `.csproj` files and `docker-compose.yml` (Tempo + Grafana services kept verbatim)
**Requirement**: AD-013, AD-018

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `src/` reference implementation is deleted (git history retains it — AD-013)
- [x] Api project references `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Ardalis.Specification.EntityFrameworkCore` 9.3.1, `OneOf` 3.0.271, `CSharpFunctionalExtensions` 3.7.0, `Microsoft.AspNetCore.OpenApi` 10.0.5, `Scalar.AspNetCore` 2.13.22, the `OpenTelemetry.*` set, `Microsoft.EntityFrameworkCore.Design`
- [x] **No** Hangfire and **no** SQLite package anywhere
- [x] Tests project references `xunit` 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.5, `Testcontainers.PostgreSql` 4.13.0, `Respawn` 7.0.0
- [x] E2ETests project keeps its Playwright/NUnit package set unchanged
- [x] `docker-compose.yml` has a `postgres` service with a named volume; Tempo and Grafana are untouched
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx`

**Tests**: none · **Gate**: build
**Commit**: `feat(skeleton): scaffold rewrite solution on PostgreSQL`

---

### T2: Define the shared contracts

**Status**: ✅ Complete — `02c9868`

**What**: `IAggregateRoot`, `IRepository<T>`, the standalone error records, and `IEncryptionService` — all in `Shared/`.
**Where**: `src/HikvisionReplicator.Api/Shared/{IAggregateRoot,IRepository,Errors,IEncryptionService}.cs`
**Depends on**: T1
**Reuses**: Reference `Shared/{IAggregateRoot,IRepository,Errors}.cs` verbatim
**Requirement**: AD-002, AD-005, AD-006

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Errors.cs` holds `ValidationError`, `NotFoundError`, `ConflictError`, `Success` as standalone records with no base class
- [x] `IEncryptionService` lives in `Shared/`, **not** `Infrastructure/` (design decision — removes the `Features → Infrastructure` edge)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx`

**Tests**: none · **Gate**: build
**Commit**: `feat(shared): add aggregate, repository, and error contracts`

---

### T3: Implement the domain value objects

**Status**: ✅ Complete — `d4be32b`

**What**: `IpAddress` (storing the **normalized** form), `Port`, and `FaceCapacity`, each with a `Create` factory returning `OneOf<T, ValidationError>`.
**Where**: `src/HikvisionReplicator.Api/Domain/{IpAddress,Port,FaceCapacity}.cs` + `src/HikvisionReplicator.Tests/Domain/ValueObjectTests.cs`
**Depends on**: T2
**Reuses**: Reference `IpAddress.cs` and `Port.cs` — **with the normalization fix**
**Requirement**: DEV-04 (partial), A-1, A-2, A-4

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `IpAddress.Create` stores `System.Net.IPAddress.Parse(value).ToString()`, so `192.168.001.001` and `192.168.1.1` produce equal value objects
- [x] IPv6 addresses are accepted (A-1)
- [x] `Port` accepts `1` and `65535`, rejects `0` and `65536`
- [x] `FaceCapacity` accepts `1` and `1_000_000`, rejects `0`, negatives, and `1_000_001`
- [x] Each type exposes `internal static FromPersistence(...)` and nested `Errors` constants
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests --filter "Category=Unit"`
- [x] Test count: ≥ 12 tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): add IpAddress, Port, and FaceCapacity value objects`

---

### T4: Implement `Device.Create`

**Status**: ✅ Complete — `f5e9cad`

**What**: The `Device` aggregate root with its private constructors and the `Create` factory.
**Where**: `src/HikvisionReplicator.Api/Domain/Device.cs` + `src/HikvisionReplicator.Tests/Domain/DeviceCreateTests.cs`
**Depends on**: T3
**Reuses**: Reference `Device.cs` structure — private setters, private EF ctor, nested `Errors`
**Requirement**: DEV-02, DEV-03, DEV-04

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Create(name, ipAddress, httpPort, username, encryptedPassword, faceCapacity, now)` returns `OneOf<Device, ValidationError>`
- [x] Each of `name`, `ipAddress`, `httpPort`, `username`, `faceCapacity` missing or blank yields a `ValidationError` naming that exact field (DEV-02)
- [x] `name` and `username` accept exactly 100 characters and reject 101 (DEV-03 + edge case)
- [x] `CreatedAt` and `UpdatedAt` are both set from the passed-in `now` — the aggregate never reads `DateTime.UtcNow` (AD-023)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests --filter "Category=Unit"`
- [x] Test count: ≥ 14 tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): add Device aggregate with Create factory`

---

### T5: Implement `Device.Update`

**Status**: ✅ Complete — `a2b856f`

**What**: The partial-update mutator — validate everything before mutating anything, and advance `UpdatedAt` only when a value actually changed.
**Where**: `src/HikvisionReplicator.Api/Domain/Device.cs` (modify) + `src/HikvisionReplicator.Tests/Domain/DeviceUpdateTests.cs`
**Depends on**: T4
**Reuses**: Reference `Device.Update` — **with the changed-guard and `now`-parameter fixes**
**Requirement**: DEV-18, DEV-19, DEV-23

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Update(..., DateTime now)` returns `OneOf<Success, ValidationError>` and takes `now` as a parameter (AD-023)
- [x] Every field is validated **before** any field is assigned, so a rejected update leaves the aggregate byte-identical (DEV-19)
- [x] `null` means "leave unchanged"; only non-null fields are applied (DEV-18)
- [x] An update whose values all equal the current ones leaves `UpdatedAt` **unadvanced** (DEV-23 + the empty-body edge case)
- [x] An update that changes at least one value advances `UpdatedAt` and leaves `CreatedAt` untouched (DEV-23)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests --filter "Category=Unit"`
- [x] Test count: ≥ 12 tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(domain): add Device.Update with change detection`

---

### T6: Add the EF Core model and initial migration

**Status**: ✅ Complete — `ea74bad`

**What**: `AppDbContext`, `DeviceConfiguration` with value converters and a **named** unique index, and one fresh `InitialCreate` migration for PostgreSQL.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/{AppDbContext,DeviceConfiguration}.cs`, `Infrastructure/Migrations/`
**Depends on**: T5
**Reuses**: Reference `AppDbContext.cs` and `DeviceConfiguration.cs`
**Requirement**: DEV-05, DEV-06 (constraint), AD-009

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `OnModelCreating` calls `ApplyConfigurationsFromAssembly`
- [x] `IpAddress`, `Port`, and `FaceCapacity` map through `ValueConverter` + `FromPersistence`
- [x] The unique index on `(IpAddress, HttpPort)` is created with an explicit `HasDatabaseName(...)` so T10's `23505` translation can key off the name
- [x] Exactly one migration exists, generated against Npgsql, and no `EnsureCreated` call appears anywhere in the solution
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx`

**Tests**: none (EF config layer — build gate only per the matrix) · **Gate**: build
**Commit**: `feat(data): add AppDbContext, device mapping, and initial migration`

---

### T7: Implement encryption with startup-validated configuration

**Status**: ✅ Complete — `8b582af`

**What**: `EncryptionService` (AES-256-CBC, `IV:ciphertext`) plus `EncryptionOptions` wired for `ValidateOnStart()`.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/{EncryptionService,EncryptionOptions}.cs` + `src/HikvisionReplicator.Tests/Domain/EncryptionServiceTests.cs`
**Depends on**: T2
**Reuses**: Reference `EncryptionService.cs` ciphertext format verbatim (AD-008, A-8)
**Requirement**: DEV-07 (storage half), DEV-15 (validation rule)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Encrypt`/`Decrypt` round-trip ASCII and **multi-byte UTF-8** passwords unchanged (edge case)
- [x] Two encryptions of the same plaintext produce different ciphertext (fresh IV per call)
- [x] Ciphertext never contains the plaintext
- [x] `EncryptionOptions` validation rejects a missing key and a non-32-byte Base64 key with a named, actionable message — the rule is unit-testable in isolation; the startup abort itself is asserted in T17
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests --filter "Category=Unit"`
- [x] Test count: ≥ 7 tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(infra): add AES-256 encryption with validated key configuration`

---

### T8: Wire the composition root

**Status**: ✅ Complete — `1783c43`

**What**: `Program.cs` — Npgsql, `Migrate()` at startup, ProblemDetails, conditional OpenTelemetry, Development-gated OpenAPI/Scalar, `TimeProvider.System`.
**Where**: `src/HikvisionReplicator.Api/Program.cs`, `appsettings*.json`
**Depends on**: T6, T7
**Reuses**: Reference `Program.cs` — **minus `EnsureCreated()`, minus Hangfire**
**Requirement**: DEV-12, DEV-16, DEV-17

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `db.Database.Migrate()` runs at startup; `EnsureCreated()` appears nowhere (DEV-12)
- [x] OpenTelemetry is registered **only** when `OpenTelemetry:OtlpEndpoint` is non-empty (DEV-16)
- [x] `MapOpenApi()` and `MapScalarApiReference()` are inside an `IsDevelopment()` guard (DEV-17)
- [x] `EnableSensitiveDataLogging()` is never called and EF instrumentation is left at its default (no SQL text) — DEV-07 precondition
- [x] `TimeProvider.System` and `IEncryptionService` are registered (AD-023)
- [x] `public partial class Program { }` is present so `WebApplicationFactory<Program>` can bind
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx`

**Tests**: none yet — no harness exists; DEV-12/16/17 are asserted in T9 and T17, the earliest points they become runnable · **Gate**: build
**Commit**: `feat(skeleton): wire composition root with migrations and tracing`

---

### T9: Build the Testcontainers test harness

**Status**: ✅ Complete — `47cc5f5`

**What**: `PostgresFixture` (container + migrations + Respawn) and `TestWebApplicationFactory`, proven by a boot smoke test.
**Where**: `src/HikvisionReplicator.Tests/{PostgresFixture,TestWebApplicationFactory,HarnessTests}.cs`
**Depends on**: T8
**Reuses**: Reference `TestWebApplicationFactory.cs` config-override shape — **database half fully rewritten**
**Requirement**: DEV-12, DEV-13

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] One `PostgreSqlContainer` starts per test collection and applies migrations once (AD-019)
- [x] `Respawner.ResetAsync()` runs between tests, so a test never sees another's rows
- [x] The factory overrides the connection string and injects a valid `Encryption:Key`
- [x] A smoke test asserts the app boots against an **empty** database and the `devices` table plus the migration-history table exist afterwards (DEV-12)
- [x] An isolation test asserts state written by one test is absent in the next (DEV-13)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 3 tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `test(harness): add Testcontainers PostgreSQL fixture with Respawn`

---

### T10: Implement the device repository and specifications

**Status**: ✅ Complete — `c766a7e`

**Status**: ✅ Complete

**What**: `IDeviceRepository` + `DeviceRepository` translating PostgreSQL `23505` into `ConflictError`, plus the two address specifications.
**Where**: `src/HikvisionReplicator.Api/Shared/IDeviceRepository.cs`, `Infrastructure/DeviceRepository.cs`, `Domain/Specs/{DeviceByAddressSpec,DeviceByAddressExcludingSpec}.cs` + `src/HikvisionReplicator.Tests/DeviceRepositoryTests.cs`
**Depends on**: T9
**Reuses**: Reference `DeviceRepository.cs` and `DeviceByAddressSpec.cs`
**Requirement**: DEV-05, DEV-06, DEV-20 — AD-022

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `AddIfAddressFreeAsync` returns `ConflictError` when the unique index rejects the insert, and `Success` otherwise
- [x] `SaveIfAddressFreeAsync` does the same for the update path
- [x] Only `23505` on the **named device address index** is translated; any other `DbUpdateException` propagates unchanged
- [x] `DeviceByAddressExcludingSpec(ip, port, excludeId)` matches a conflicting device but never the device being updated (DEV-20)
- [x] A test inserts a duplicate address **bypassing the pre-check** (calling the repository directly) and asserts a `ConflictError` rather than an exception — this is the DEV-06 mechanism
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 6 tests pass (9 new, 82 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(infra): add device repository with unique-violation translation`

---

### T11: Implement the `RegisterDevice` slice

**Status**: ✅ Complete — `caa92b3`

**Status**: ✅ Complete

**What**: The three-file registration slice and its endpoint tests, including the concurrency and secrecy criteria.
**Where**: `src/HikvisionReplicator.Api/Features/Devices/RegisterDevice/RegisterDeviceService.{Interface,,Endpoint}.cs` + `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs`
**Depends on**: T10
**Reuses**: Reference `CreateDevice` slice shape and `DomainErrorExtensions`
**Requirement**: DEV-01, DEV-02, DEV-03, DEV-04, DEV-05, DEV-06, DEV-07

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `POST /api/devices` returns `201` with a `Location` of `/api/devices/{id}` and the full response body (DEV-01)
- [x] Missing/blank/oversized/out-of-range fields each return `400` naming the offending field (DEV-02, DEV-03, DEV-04)
- [x] A duplicate address returns `409` and creates no second row (DEV-05)
- [x] **Concurrency test**: N simultaneous `POST`s of one address via `Task.WhenAll` yield exactly one `201`, N−1 `409`s, and zero `500`s (DEV-06)
- [x] **Normalization test**: registering `192.168.1.1` then `192.168.001.001` returns `409` (A-2 edge case)
- [x] The response body contains no password or ciphertext field, and the persisted column holds neither the plaintext nor an empty value (DEV-07)
- [x] Malformed JSON returns a `400` problem body, not `500` (edge case) — needed `app.UseStatusCodePages()` so the framework's bodiless 400 gains an RFC 7807 body
- [x] `DomainErrorExtensions.ToMinimalApiResult()` exists and the endpoint uses `.Match()` with descriptive parameter names (AD-003)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 16 tests pass (28 new, 110 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): add device registration endpoint`

---

### T12: Implement the `GetDevice` slice

**Status**: ✅ Complete — `a8c2b5a`

**Status**: ✅ Complete

**What**: Retrieve one device by id.
**Where**: `src/HikvisionReplicator.Api/Features/Devices/GetDevice/GetDeviceService.{Interface,,Endpoint}.cs` + `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs` (modify)
**Depends on**: T11
**Reuses**: Reference `GetDevice` slice
**Requirement**: DEV-10

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `GET /api/devices/{id}` returns `200` with the device for a known id (DEV-10)
- [x] An unknown id returns `404` with an RFC 7807 problem body (DEV-10)
- [x] Following the `Location` header from T11 returns the same device, with no password field
- [x] A never-used id and a deleted id are indistinguishable — both `404` (edge case; deleted-id half re-asserted in T15)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 4 new tests pass (4 new, 114 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): add get-device-by-id endpoint`

---

### T13: Implement the `ListDevices` slice

**Status**: ✅ Complete — `4159138`

**Status**: ✅ Complete

**What**: List the whole catalogue — an infallible query returning the value directly (AD-003).
**Where**: `src/HikvisionReplicator.Api/Features/Devices/ListDevices/ListDevicesService.{Interface,,Endpoint}.cs` + `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs` (modify)
**Depends on**: T12
**Reuses**: Reference `GetDevices` slice
**Requirement**: DEV-08, DEV-09

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] The service returns `Task<IReadOnlyList<DeviceResponse>>` — no `OneOf`, since it cannot fail (AD-003)
- [x] An empty catalogue returns `200` with `[]`, never `404` (DEV-09)
- [x] Two registered devices both appear, each with the full field set and no password (DEV-08)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 3 new tests pass (3 new, 117 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): add list-devices endpoint`

---

### T14: Implement the `UpdateDevice` slice

**Status**: ✅ Complete — `dc5d37e`

**Status**: ✅ Complete

**What**: Partial update over `PUT`, including the self-address exemption and password-retention rule.
**Where**: `src/HikvisionReplicator.Api/Features/Devices/UpdateDevice/UpdateDeviceService.{Interface,,Endpoint}.cs` + `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs` (modify)
**Depends on**: T13
**Reuses**: Reference `UpdateDevice` slice, `Device.Update` (T5), `DeviceByAddressExcludingSpec` (T10)
**Requirement**: DEV-18, DEV-19, DEV-20, DEV-21, DEV-22, DEV-23

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] Updating only `name` returns `200` and leaves address, username, and capacity untouched (DEV-18)
- [x] An invalid field returns `400` naming it, and a re-read shows **no partial change persisted** (DEV-19)
- [x] Moving onto another device's address returns `409`; re-submitting the device's **own** address succeeds (DEV-20)
- [x] Omitting `password` leaves the stored ciphertext byte-identical; supplying one replaces it (DEV-21, A-7)
- [x] An unknown id returns `404` (DEV-22)
- [x] A real change advances `updatedAt` and leaves `createdAt` fixed; an entirely empty body returns `200` with `updatedAt` **unadvanced** (DEV-23 + edge case)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 10 new tests pass (14 new, 131 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): add update-device endpoint`

---

### T15: Implement the `RemoveDevice` slice

**Status**: ✅ Complete — `adb4b36`

**Status**: ✅ Complete

**What**: Hard-delete a device and free its address.
**Where**: `src/HikvisionReplicator.Api/Features/Devices/RemoveDevice/RemoveDeviceService.{Interface,,Endpoint}.cs` + `src/HikvisionReplicator.Tests/DeviceEndpointsTests.cs` (modify)
**Depends on**: T14
**Reuses**: Reference `DeleteDevice` slice
**Requirement**: DEV-11, DEV-24, DEV-25

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `DELETE /api/devices/{id}` returns `204`, and the device is then absent from the list and `404` by id (DEV-11)
- [x] An unknown id returns `404` (DEV-24)
- [x] Re-registering the removed device's address succeeds (DEV-25)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 4 new tests pass (5 new, 136 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(devices): add remove-device endpoint`

---

### T16: Implement the global exception handler

**Status**: ✅ Complete

**What**: `GlobalExceptionHandler` mapping database-unreachable failures to `503` without leaking connection details, and everything else to `500`.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/GlobalExceptionHandler.cs`, `Program.cs` (register) + `src/HikvisionReplicator.Tests/ErrorHandlingTests.cs`
**Depends on**: T15
**Reuses**: Reference `GlobalExceptionHandler.cs` structure
**Requirement**: DEV-14

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] A request made while PostgreSQL is unreachable returns `503` as an RFC 7807 problem body (DEV-14) — provoked by pausing or stopping the container, or pointing the factory at a dead port
- [x] The response contains **no** stack trace, host, port, database name, username, or connection string (DEV-14) — asserted against the full serialized body
- [x] Non-database exceptions still map to `500` with a problem body
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 3 tests pass (3 new, 139 total)

**Tests**: integration · **Gate**: full
**Commit**: `feat(infra): map database failures to 503 problem responses`

---

### T17: Verify startup and environment behaviour

**Status**: ✅ Complete

**What**: Tests for the three startup-gated behaviours — key validation, tracing registration, and Development-only docs.
**Where**: `src/HikvisionReplicator.Tests/StartupTests.cs`
**Depends on**: T16
**Reuses**: `TestWebApplicationFactory` (T9), configuration-override pattern
**Requirement**: DEV-15, DEV-16, DEV-17

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] Booting with `Encryption:Key` missing throws at **startup**, before any request is served, with a message naming the key (DEV-15)
- [x] Booting with a non-32-byte Base64 key fails the same way (DEV-15)
- [x] With `OpenTelemetry:OtlpEndpoint` unset, no tracer provider is registered; with it set, one is (DEV-16)
- [x] In the Development environment `/openapi/v1.json` and the Scalar route respond; outside Development both `404` (DEV-17)
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 6 tests pass (8 new, 147 total)

**Tests**: integration · **Gate**: full
**Commit**: `test(startup): verify key validation, tracing, and docs gating`

---

### T18: Sweep for credential leakage

**Status**: ✅ Complete

**What**: A suite-wide assertion that no plaintext password reaches a response body or a log line.
**Where**: `src/HikvisionReplicator.Tests/CredentialLeakageTests.cs`
**Depends on**: T17
**Reuses**: `TestWebApplicationFactory` (T9) with an in-memory log sink attached
**Requirement**: DEV-07

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] Every device route is exercised with a distinctive sentinel password, and no response body across them contains it or its ciphertext (DEV-07)
- [x] No captured log line from any of those requests contains the sentinel, the ciphertext, or the encryption key (DEV-07, addresses the design's "no test asserts logs are password-free" risk)
- [x] The persisted `EncryptedPassword` column is asserted to differ from the sentinel and to decrypt back to it
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests`
- [x] Test count: ≥ 4 tests pass (4 new, 151 total)

**Tests**: integration · **Gate**: full
**Commit**: `test(security): assert credentials never leak to responses or logs`

---

### T19: Add the E2E suite

**Status**: ✅ Complete

**What**: Playwright/NUnit tests covering the five routes against a live API.
**Where**: `src/HikvisionReplicator.E2ETests/DeviceEndpointsTests.cs`
**Depends on**: T18
**Reuses**: Reference `E2ETests/DeviceEndpointsTests.cs` `APIRequest` pattern
**Requirement**: DEV-01, DEV-08, DEV-10, DEV-11, DEV-18 (out-of-process confirmation)

**Tools**: MCP: `playwright` · Skill: NONE

**Done when**:
- [x] Register → get → list → update → remove runs end-to-end against a live API on the configured base URL
- [x] One error path per route is covered (duplicate address, unknown id)
- [x] `E2E_BASE_URL` override still works
- [x] Test names follow `docs/test-patterns.md`
- [x] Gate check passes: E2E gate — `dotnet test src/HikvisionReplicator.E2ETests` against a running API
- [x] Test count: ≥ 7 tests pass (9 new)

**Tests**: e2e · **Gate**: e2e
**Commit**: `test(e2e): add device endpoint end-to-end suite`

---

### T20: Refresh the project documentation

**Status**: ✅ Complete

**What**: Bring `CLAUDE.md`, `README.md`, and `.specs/ARCHITECTURE.md` in line with the rewritten solution.
**Where**: `CLAUDE.md`, `README.md`, `docs/test-patterns.md`, `.specs/ARCHITECTURE.md`
**Depends on**: T19
**Reuses**: Existing documents
**Requirement**: AD-013, AD-018, AD-019, AD-024 (accuracy)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `CLAUDE.md` Commands section reflects PostgreSQL, the Docker prerequisite, and the real `dotnet ef --project src/HikvisionReplicator.Api` path — the stale `HikvisionReplicator.Data` line is gone
- [x] The stack line reads PostgreSQL, not SQLite; Hangfire is absent until Phase 2 reintroduces it
- [x] `README.md` documents `docker compose up -d` as a prerequisite for both running and testing
- [x] `docs/test-patterns.md` gains a **"Choosing the test level"** section stating AD-024 — unit for pure no-I/O logic under `Tests/Domain/` with `[Trait("Category","Unit")]`, integration through the HTTP surface for slices/repositories/startup, E2E as thin out-of-process confirmation — and `CLAUDE.md`'s Tests section links to it
- [x] `CLAUDE.md` records both gate commands: the Docker-free unit filter and the full integration run
- [x] `ARCHITECTURE.md` describes the rewritten solution, and its "Known Gaps" list drops the items this feature fixed
- [x] Gate check passes: `dotnet build HikvisionReplicator.slnx`

**Tests**: none · **Gate**: build
**Commit**: `docs: align project documentation with the PostgreSQL rewrite`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4

Phase 1:  T1 ──→ T2 ──→ T3 ──→ T4 ──→ T5
Phase 2:  T6 ──→ T7 ──→ T8 ──→ T9
Phase 3:  T10 ──→ T11 ──→ T12 ──→ T13 ──→ T14 ──→ T15
Phase 4:  T16 ──→ T17 ──→ T18 ──→ T19 ──→ T20
```

Execution is strictly sequential — there is no intra-phase parallelism.

**Note on T7**: `Depends on: T2` (not T6) — encryption needs only the `Shared` contracts. It is placed in Phase 2 because T8 consumes it, and Phase 1 is already at budget.

---

## Task Granularity Check

| Task | Scope | Status |
|---|---|---|
| T1: Scaffold solution | 3 csproj + slnx + compose — one cohesive "solution compiles" deliverable | ✅ Granular |
| T2: Shared contracts | 4 tiny contract files, one concept | ✅ Granular |
| T3: Value objects | 3 value objects, same pattern, same test file | ✅ Granular (cohesive) |
| T4: `Device.Create` | 1 factory on 1 aggregate | ✅ Granular |
| T5: `Device.Update` | 1 mutator on 1 aggregate | ✅ Granular |
| T6: EF model + migration | 1 DbContext + 1 config + 1 generated migration | ✅ Granular (cohesive) |
| T7: Encryption + options | 1 service + its options type | ✅ Granular |
| T8: Composition root | 1 file | ✅ Granular |
| T9: Test harness | 2 fixture files + smoke tests | ✅ Granular (cohesive) |
| T10: Repository + specs | 1 repository + 2 specs it exists to serve | ✅ Granular (cohesive) |
| T11–T15: One slice each | 1 endpoint each (3 files per AD-001) | ✅ Granular |
| T16: Exception handler | 1 component | ✅ Granular |
| T17: Startup tests | 1 test file, one concern | ✅ Granular |
| T18: Leak sweep | 1 test file, one concern | ✅ Granular |
| T19: E2E suite | 1 test file | ✅ Granular |
| T20: Docs | 3 documents, one concern | ✅ Granular (cohesive) |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T1 | None | phase start | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 (phase boundary) | ✅ Match |
| T7 | T2 | T6 → T7 | ✅ Match — T2 is in an earlier phase, so the sequential arrow is a superset of the real dependency (noted above) |
| T8 | T6, T7 | T7 → T8 | ✅ Match |
| T9 | T8 | T8 → T9 | ✅ Match |
| T10 | T9 | T9 → T10 (phase boundary) | ✅ Match |
| T11 | T10 | T10 → T11 | ✅ Match |
| T12 | T11 | T11 → T12 | ✅ Match |
| T13 | T12 | T12 → T13 | ✅ Match |
| T14 | T13 | T13 → T14 | ✅ Match |
| T15 | T14 | T14 → T15 | ✅ Match |
| T16 | T15 | T15 → T16 (phase boundary) | ✅ Match |
| T17 | T16 | T16 → T17 | ✅ Match |
| T18 | T17 | T17 → T18 | ✅ Match |
| T19 | T18 | T18 → T19 | ✅ Match |
| T20 | T19 | T19 → T20 | ✅ Match |

No task depends on a task in a later phase.

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
|---|---|---|---|---|
| T1 | Project scaffolding | none | none | ✅ OK |
| T2 | Shared contracts (interfaces/records only) | none | none | ✅ OK |
| T3 | Domain — value objects | unit | unit | ✅ OK |
| T4 | Domain — aggregate | unit | unit | ✅ OK |
| T5 | Domain — aggregate | unit | unit | ✅ OK |
| T6 | EF configuration + migration | none | none | ✅ OK |
| T7 | Infrastructure service with pure logic | unit | unit | ✅ OK |
| T8 | Startup wiring | integration | none | ⚠️ **Resolved by merge-forward** — no harness exists at T8, so DEV-12 is asserted in T9 and DEV-16/17 in T17, the earliest tasks where they are runnable. Per the skill's compilation-dependency rule this is a restructure, not deferral: T9 and T17 own those ACs outright. |
| T9 | Test harness + startup verification | integration | integration | ✅ OK |
| T10 | Repository + specifications | integration | integration | ✅ OK |
| T11 | Feature slice + route | integration | integration | ✅ OK |
| T12 | Feature slice + route | integration | integration | ✅ OK |
| T13 | Feature slice + route | integration | integration | ✅ OK |
| T14 | Feature slice + route | integration | integration | ✅ OK |
| T15 | Feature slice + route | integration | integration | ✅ OK |
| T16 | Cross-cutting handler | integration | integration | ✅ OK |
| T17 | Startup behaviour | integration | integration | ✅ OK |
| T18 | Cross-cutting security | integration | integration | ✅ OK |
| T19 | HTTP surface, out-of-process | e2e | e2e | ✅ OK |
| T20 | Documentation | none | none | ✅ OK |

---

## Requirement Traceability

| Requirement | Tasks |
|---|---|
| DEV-01 | T11, T19 |
| DEV-02 | T4, T11 |
| DEV-03 | T4, T11 |
| DEV-04 | T3, T11 |
| DEV-05 | T6, T10, T11 |
| DEV-06 | T6, T10, T11 |
| DEV-07 | T7, T8, T11, T18 |
| DEV-08 | T13, T19 |
| DEV-09 | T13 |
| DEV-10 | T12, T19 |
| DEV-11 | T15, T19 |
| DEV-12 | T8, T9 |
| DEV-13 | T9 |
| DEV-14 | T16 |
| DEV-15 | T7, T17 |
| DEV-16 | T8, T17 |
| DEV-17 | T8, T17 |
| DEV-18 | T5, T14, T19 |
| DEV-19 | T5, T14 |
| DEV-20 | T10, T14 |
| DEV-21 | T14 |
| DEV-22 | T14 |
| DEV-23 | T5, T14 |
| DEV-24 | T15 |
| DEV-25 | T15 |
| DEV-26 | **not scheduled** — P3, deliberately out of this build (see design Risks) |

**Coverage**: 25 of 26 requirements mapped to tasks. DEV-26 is the sole unmapped requirement and is intentionally deferred.

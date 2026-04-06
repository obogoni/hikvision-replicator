# Tasks: Hikvision Device Management API

**Input**: Design documents from `/specs/001-hikvision-device-api/`
**Branch**: `001-hikvision-device-api`
**Prerequisites**: plan.md, spec.md, data-model.md, contracts/devices.md, research.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

> **Tests**: Integration tests are included — explicitly chosen in research.md Decision #4 (xUnit + WebApplicationFactory) as part of the technical design.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create the .NET 10 solution and wire up project references.

- [x] T001 Create .NET 10 solution with three projects: `dotnet new sln -n HikvisionReplicator`, `dotnet new webapi -n HikvisionReplicator.Api`, `dotnet new classlib -n HikvisionReplicator.Data`, `dotnet new xunit -n HikvisionReplicator.Tests`; add all three to the solution
- [x] T00X Add project references: Api → Data, Tests → Api; add NuGet packages: `Microsoft.EntityFrameworkCore.Sqlite` + `Microsoft.EntityFrameworkCore.Design` to Data; `Microsoft.AspNetCore.Mvc.Testing` to Tests; remove default boilerplate files from all three projects
- [x] T00X [P] Configure `HikvisionReplicator.Api/appsettings.json` with `ConnectionStrings.DefaultConnection: "Data Source=devices.db"` and `Encryption.Key` (32-byte base64 string); configure `appsettings.Development.json` with in-memory connection string `Data Source=:memory:`

**Checkpoint**: `dotnet build` succeeds with zero errors across all three projects

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before any user story endpoint can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T00X Create `HikvisionReplicator.Data/Entities/Device.cs` — Device entity with fields: `Id` (int, PK auto-increment), `Name` (string, max 100, required), `IpAddress` (string, required), `HttpPort` (int, required), `Username` (string, max 100, required), `EncryptedPassword` (string, required), `CreatedAt` (DateTime UTC), `UpdatedAt` (DateTime UTC)
- [x] T00X Create `HikvisionReplicator.Data/AppDbContext.cs` — DbContext with `DbSet<Device> Devices`; configure via `OnModelCreating`: unique index on `(IpAddress, HttpPort)`, max-length constraints for `Name` and `Username`, `CreatedAt`/`UpdatedAt` as required
- [x] T00X [P] Create `HikvisionReplicator.Api/Infrastructure/EncryptionService.cs` — interface `IEncryptionService` with `string Encrypt(string plaintext)` and `string Decrypt(string ciphertext)`; implement using `System.Security.Cryptography.Aes` (AES-256-CBC); read key from `IConfiguration["Encryption:Key"]`; store IV prepended to ciphertext as `base64(iv):base64(ciphertext)`
- [x] T00X Generate EF Core initial migration: `dotnet ef migrations add InitialCreate --project HikvisionReplicator.Data --startup-project HikvisionReplicator.Api`; verify `HikvisionReplicator.Data/Migrations/` contains the generated files
- [x] T00X [P] Create `HikvisionReplicator.Api/Features/Devices/DeviceResponse.cs` — record or class with: `Id` (int), `Name`, `IpAddress`, `HttpPort` (int), `Username`, `CreatedAt`, `UpdatedAt`; add static factory method `FromEntity(Device d)` for mapping; no password field
- [x] T00X [P] Create `HikvisionReplicator.Api/Features/Devices/DeviceRequest.cs` — `CreateDeviceRequest` record (Name, IpAddress, HttpPort, Username, Password all required); `UpdateDeviceRequest` record (all fields nullable/optional; null Password means retain existing)
- [x] T01X [P] Create `HikvisionReplicator.Api/Features/Devices/ErrorResponse.cs` — record with `Type` (string), `Message` (string), and optional `Errors` (Dictionary<string, string[]>); add static factory helpers: `NotFound(string message)`, `Conflict(string message)`, `Validation(Dictionary<string, string[]> errors)`
- [x] T01X Register services in `HikvisionReplicator.Api/Program.cs`: add `AppDbContext` with SQLite connection string from config; add `IEncryptionService`/`EncryptionService` as singleton; call `dotnet ef database update` on startup (or `db.Database.EnsureCreated()` for simplicity); configure `app.UseExceptionHandler` to return JSON `ErrorResponse` for unhandled exceptions; add placeholder call to `app.MapDevicesEndpoints()` (stub — will be filled in Phase 3)
- [x] T01X Create `HikvisionReplicator.Tests/TestWebApplicationFactory.cs` — subclass `WebApplicationFactory<Program>`; override `ConfigureWebHost` to replace the SQLite connection string with `Data Source=:memory:`; ensure `db.Database.EnsureCreated()` is called per test run

**Checkpoint**: `dotnet build` passes; `dotnet test` compiles (no tests yet but project structure is valid)

---

## Phase 3: User Story 1 — Register a Device (Priority: P1) 🎯 MVP

**Goal**: `POST /api/devices` creates a device record and returns 201 with `DeviceResponse` + `Location` header. Conflicts and validation errors return appropriate error responses.

**Independent Test**: Register a device via POST, verify 201 + Location header + response body (no password); attempt duplicate IP+port, verify 409; attempt missing field, verify 400 with field-level error.

- [x] T01X [P] [US1] Write integration tests for `POST /api/devices` in `HikvisionReplicator.Tests/DeviceEndpointsTests.cs` covering: (1) valid registration returns 201 + Location header + DeviceResponse with no password field; (2) duplicate IpAddress+HttpPort returns 409; (3) missing required field returns 400 with field-level `errors`; (4) invalid IPv4 format returns 400; (5) port out of range returns 400 — ensure all tests FAIL before T014
- [x] T01X [US1] Implement `POST /api/devices` in `HikvisionReplicator.Api/Features/Devices/DeviceEndpoints.cs` — validate `CreateDeviceRequest` (non-empty name/username/password, valid IPv4 regex, port 1–65535); check uniqueness of IpAddress+HttpPort (return 409 on conflict); encrypt password via `IEncryptionService`; set `CreatedAt`/`UpdatedAt` to `DateTime.UtcNow`; save entity; return `Created($"/api/devices/{id}", DeviceResponse.FromEntity(device))`
- [x] T01X [US1] Wire `MapDevicesEndpoints()` extension method in `HikvisionReplicator.Api/Features/Devices/DeviceEndpoints.cs` using `app.MapGroup("/api/devices")`; update the stub call in `Program.cs`

**Checkpoint**: `dotnet test --filter "Post"` passes all 5 POST tests

---

## Phase 4: User Story 2 — Retrieve Device Information (Priority: P2)

**Goal**: `GET /api/devices` returns all devices (empty array if none); `GET /api/devices/{id}` returns single device or 404.

**Independent Test**: Register a device, list all devices (verify it appears, no password in response), fetch by ID (verify data matches), fetch unknown ID (verify 404 with ErrorResponse).

- [x] T01X [P] [US2] Write integration tests for `GET /api/devices` and `GET /api/devices/{id}` in `HikvisionReplicator.Tests/DeviceEndpointsTests.cs` covering: (1) list returns 200 + empty array when no devices registered; (2) list returns all registered devices with no password field; (3) get by ID returns 200 + correct DeviceResponse; (4) get by unknown ID returns 404 with ErrorResponse — ensure all tests FAIL before T017
- [x] T01X [US2] Implement `GET /api/devices` in `HikvisionReplicator.Api/Features/Devices/DeviceEndpoints.cs` — query all Device records; return 200 with array of `DeviceResponse.FromEntity(d)` (empty array if none)
- [x] T01X [US2] Implement `GET /api/devices/{id}` in `HikvisionReplicator.Api/Features/Devices/DeviceEndpoints.cs` — find Device by int id; return 200 + `DeviceResponse` if found; return 404 + `ErrorResponse.NotFound(...)` if not

**Checkpoint**: `dotnet test --filter "Get"` passes all 4 GET tests; all Phase 3 POST tests still pass

---

## Phase 5: User Story 3 — Update Device Information (Priority: P3)

**Goal**: `PUT /api/devices/{id}` updates only supplied fields; omitted password retains existing credential; returns updated `DeviceResponse` or 400/404/409.

**Independent Test**: Register a device, update only `name` (verify other fields unchanged), update `ipAddress`+`httpPort` to a conflicting value (verify 409), update with invalid IP (verify 400), update unknown ID (verify 404), update without password field (verify password retained).

- [x] T01X [P] [US3] Write integration tests for `PUT /api/devices/{id}` in `HikvisionReplicator.Tests/DeviceEndpointsTests.cs` covering: (1) partial update (name only) returns 200 + updated device, other fields unchanged; (2) password omitted retains existing encrypted credential; (3) IP+port conflict with another device returns 409; (4) invalid field values return 400 with field-level errors; (5) unknown ID returns 404 — ensure all tests FAIL before T020
- [x] T02X [US3] Implement `PUT /api/devices/{id}` in `HikvisionReplicator.Api/Features/Devices/DeviceEndpoints.cs` — find Device by id (404 if missing); for each non-null field in `UpdateDeviceRequest`, validate and apply; if IpAddress or HttpPort changed, re-check uniqueness constraint (409 on conflict); if `Password` is non-null and non-empty, encrypt and replace `EncryptedPassword`; update `UpdatedAt`; save; return 200 + `DeviceResponse.FromEntity(device)`

**Checkpoint**: `dotnet test --filter "Put"` passes all 5 PUT tests; all prior tests still pass

---

## Phase 6: User Story 4 — Delete a Device (Priority: P4)

**Goal**: `DELETE /api/devices/{id}` permanently removes the device and returns 204; unknown ID returns 404.

**Independent Test**: Register a device, delete it (verify 204), attempt to GET deleted device (verify 404), attempt to DELETE unknown ID (verify 404).

- [x] T02X [P] [US4] Write integration tests for `DELETE /api/devices/{id}` in `HikvisionReplicator.Tests/DeviceEndpointsTests.cs` covering: (1) delete existing device returns 204 with empty body; (2) subsequent GET on deleted ID returns 404; (3) delete unknown ID returns 404 with ErrorResponse — ensure all tests FAIL before T022
- [x] T02X [US4] Implement `DELETE /api/devices/{id}` in `HikvisionReplicator.Api/Features/Devices/DeviceEndpoints.cs` — find Device by id (404 if missing); remove entity; save; return `Results.NoContent()`

**Checkpoint**: `dotnet test` passes all integration tests across all 4 user stories

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, data annotations review, and quickstart verification.

- [x] T02X [P] Add `[Required]`, `[MaxLength]`, `[Range]` data annotations to `CreateDeviceRequest` and `UpdateDeviceRequest` in `HikvisionReplicator.Api/Features/Devices/DeviceRequest.cs` and register `app.UseValidation()` or endpoint filter for automatic model validation, reducing manual validation code in endpoints
- [x] T02X [P] Validate all five curl examples from `specs/001-hikvision-device-api/quickstart.md` against the running API: `dotnet run --project HikvisionReplicator.Api` then execute each curl command and confirm expected status codes and response shapes
- [x] T02X Run `dotnet test` and confirm all integration tests pass with exit code 0; confirm no `EncryptedPassword` field leaks into any response body

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Phase 2 — no dependencies on other stories
- **US2 (Phase 4)**: Depends on Phase 2 — no dependencies on US1 (GET needs no POST to function structurally, but tests seed data via POST)
- **US3 (Phase 5)**: Depends on Phase 2 + US1 (PUT needs a registered device to update)
- **US4 (Phase 6)**: Depends on Phase 2 + US1 (DELETE needs a registered device to delete)
- **Polish (Phase 7)**: Depends on all user story phases

### User Story Dependencies

| Story | Depends On | Can Run After |
|---|---|---|
| US1 Register | Phase 2 only | Phase 2 complete |
| US2 Retrieve | Phase 2 only | Phase 2 complete |
| US3 Update | Phase 2 + US1 | US1 complete |
| US4 Delete | Phase 2 + US1 | US1 complete |

### Within Each User Story

1. Write integration tests first (T013/T016/T019/T021) — verify they FAIL
2. Implement endpoint
3. Run tests — verify they PASS
4. Checkpoint before moving to next story

### Parallel Opportunities (Phase 2)

```
After T004 (Device entity) and T005 (AppDbContext) complete:

Parallel batch:
  T006  EncryptionService (HikvisionReplicator.Api/Infrastructure/)
  T008  DeviceResponse DTO (HikvisionReplicator.Api/Features/Devices/)
  T009  DeviceRequest DTOs (HikvisionReplicator.Api/Features/Devices/)
  T010  ErrorResponse types (HikvisionReplicator.Api/Features/Devices/)
  T012  TestWebApplicationFactory (HikvisionReplicator.Tests/)

Then sequentially:
  T007  EF migration (depends on T004 + T005)
  T011  Program.cs wiring (depends on T006 + all DTOs)
```

---

## Implementation Strategy

### MVP (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (**critical — blocks everything**)
3. Complete Phase 3: US1 (POST /api/devices)
4. **STOP and VALIDATE**: Run POST tests, test manually with quickstart.md curl examples
5. Ship MVP — operators can register devices

### Incremental Delivery

1. Phase 1 + Phase 2 → Foundation ready
2. Phase 3 → POST works → MVP
3. Phase 4 → GET works → Can list and inspect devices
4. Phase 5 → PUT works → Can update device config
5. Phase 6 → DELETE works → Full CRUD
6. Phase 7 → Polish + validation

---

## Notes

- `[P]` tasks touch different files and have no inter-dependencies
- `[Story]` label maps each task to a specific user story for traceability
- `EncryptedPassword` must never appear in any `DeviceResponse` — add an explicit assertion to T025
- The AES encryption key in `appsettings.json` is for development only; in production inject via environment variable or secret store
- SQLite `:memory:` in TestWebApplicationFactory means each `WebApplicationFactory` instance gets a fresh isolated database — no test cleanup needed

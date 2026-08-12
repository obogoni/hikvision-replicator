# Architecture Map — hikvision-replicator

Reverse-engineered from the codebase at commit `ebfc510` (2026-08-02). This is the
baseline snapshot for adopting spec-driven development on an existing project:
what exists, how it is layered, and where the gaps are.

Conventions that future features must follow are recorded as `AD-NNN` entries in
[STATE.md](STATE.md) — this document is the *map*, STATE.md is the *contract*.

---

## 1. Purpose

An ASP.NET Core 10 Minimal API that owns a catalogue of Hikvision access-control
devices and a catalogue of users, and is meant to **replicate** user records
(name, access code, face picture) out to every registered device.

Today the API manages both catalogues and *queues* replication work. The step that
actually talks to a device over Hikvision ISAPI does not exist yet — see §7.

---

## 2. Solution Layout

```text
hikvision-replicator/
├── HikvisionReplicator.slnx          # 3 projects
├── docker-compose.yml                # Tempo + Grafana (observability only)
├── docker/
│   ├── tempo/tempo.yaml
│   └── grafana/provisioning/datasources/tempo.yaml
├── docs/test-patterns.md             # behaviour-based test naming rules
├── .specs/                           # ← spec-driven development artifacts
│   ├── ARCHITECTURE.md               # this file
│   ├── STATE.md                      # AD-NNN decision log + handoff snapshot
│   ├── LESSONS.md / lessons.json     # self-improving lessons (script-owned)
│   └── features/[feature]/           # spec.md · design.md · tasks.md · validation.md
├── scripts/lessons.py                # lessons bookkeeping (do not hand-edit output)
└── src/
    ├── HikvisionReplicator.Api/
    ├── HikvisionReplicator.Tests/        # xUnit · in-memory SQLite · in-process
    └── HikvisionReplicator.E2ETests/     # NUnit + Playwright APIRequest · needs live API
```

---

## 3. API Project — Layer Map

```text
src/HikvisionReplicator.Api/
├── Program.cs                    ← composition root: DI, pipeline, route mapping
│
├── Domain/                       ← LAYER 1 · no framework deps beyond OneOf/CSharpFunctionalExtensions
│   ├── Device.cs                     aggregate root
│   ├── User.cs                       aggregate root
│   ├── Replication.cs                aggregate root
│   ├── IpAddress.cs / Port.cs        value objects (Device)
│   ├── AccessCode.cs                 value object (User)
│   ├── UserStatus.cs                 enum: PendingAdd | PendingRemove
│   ├── ReplicationType.cs            enum: Add | Remove
│   ├── ReplicationStatus.cs          enum: Pending | Processed | Canceled
│   └── Specs/                        Ardalis Specification<T> query objects
│       ├── DeviceByAddressSpec.cs        uniqueness of (IpAddress, HttpPort)
│       └── UserByExternalRefSpec.cs      upsert lookup key
│
├── Features/                     ← LAYER 2 · vertical slices, 3 files each
│   ├── Devices/{CreateDevice,GetDevice,GetDevices,UpdateDevice,DeleteDevice}/
│   └── Users/{UpsertUser,GetUser}/  + SyncUser/UserSyncJob.cs (background job)
│
├── Infrastructure/               ← LAYER 3 · everything framework-facing
│   ├── AppDbContext.cs               DbSets + ApplyConfigurationsFromAssembly
│   ├── {Device,User,Replication}Configuration.cs   IEntityTypeConfiguration<T>
│   ├── {Device,User,Replication}Repository.cs      Ardalis RepositoryBase<T>
│   ├── EncryptionService.cs          AES-256-CBC, reversible, key from config
│   ├── GlobalExceptionHandler.cs     IExceptionHandler → ProblemDetails
│   ├── DomainErrorExtensions.cs      error record → IResult
│   └── Migrations/                   4 EF Core migrations
│
└── Shared/                       ← LAYER 0 · contracts, referenced by all layers
    ├── IAggregateRoot.cs             Id · CreatedAt · UpdatedAt
    ├── IRepository.cs                IRepositoryBase<T> where T : IAggregateRoot
    └── Errors.cs                     ValidationError · NotFoundError · ConflictError · Success
```

**Dependency direction:** `Program.cs → Features → Domain`, `Features → Shared`,
`Infrastructure → Domain + Shared`. Features touch Infrastructure only for
`IEncryptionService` and `ToMinimalApiResult()`; they never see `AppDbContext`.

---

## 4. Vertical Slice Shape

Every operation is three files under `Features/{Resource}/{Operation}/`:

| File | Holds |
|---|---|
| `{Op}Service.Interface.cs` | Request record + Response record + `I{Op}Service` |
| `{Op}Service.cs` | Implementation — orchestrates domain + repository |
| `{Op}Service.Endpoint.cs` | `Use{Op}()` (DI) + `Map{Op}()` (route) extensions |

DTOs are **never shared across slices** — `UpsertUser` and `GetUser` each declare
their own `UserResponse` record. That duplication is deliberate (AD-004).

### Request flow (write path)

```text
HTTP POST /api/devices
  → Map{Op}() minimal-api delegate  (injects I{Op}Service, CancellationToken ct)
  → I{Op}Service.ExecuteAsync(request, ct)
       ├─ IEncryptionService.Encrypt(password)          [writes only]
       ├─ Device.Create(...)  → OneOf<Device, ValidationError>
       ├─ IRepository<Device>.AnyAsync(new DeviceByAddressSpec(...), ct)  → ConflictError
       └─ IRepository<Device>.AddAsync(device, ct)      [SaveChanges inside]
  → OneOf<...>.Match(response => Results.Created(...), err => err.ToMinimalApiResult())
```

Errors are values, never exceptions: `OneOf<TSuccess, ValidationError, ConflictError, …>`
all the way from the domain factory to the endpoint's `.Match()`.

---

## 5. Domain Model

```text
User (aggregate)                      Device (aggregate)
 ├ Id                                  ├ Id
 ├ ExternalRef  (unique, ≤255)         ├ Name (≤100)
 ├ Name (≤100)                         ├ IpAddress   ┐ unique together
 ├ AccessCode   (VO, 4–20 digits)      ├ HttpPort    ┘ (DeviceByAddressSpec)
 ├ FacePic      (byte[]?, ≤200 KB)     ├ Username (≤100)
 ├ Status       PendingAdd|PendingRemove│└ EncryptedPassword  (AES-256, never returned)
 └ Created/UpdatedAt                   └ Created/UpdatedAt
        │                                      │
        └──────────────┬───────────────────────┘
                       ▼
              Replication (aggregate)
               ├ UserId · DeviceId          ← plain ints, no EF navigation/FK
               ├ Type    Add | Remove
               ├ Status  Pending → Processed | Canceled
               └ Created/UpdatedAt
```

- Aggregates have **private setters and private constructors**; the only entry
  points are static `Create(...)` factories and instance mutators, both returning
  `OneOf<…, ValidationError>`.
- Value objects derive from `CSharpFunctionalExtensions.ValueObject` and expose an
  `internal static FromPersistence(...)` used only by the EF value converters.
- Error message constants live in a nested `static class Errors` on each type, so
  tests assert against the constant rather than a string literal.

---

## 6. Cross-Cutting Concerns

| Concern | Implementation | Notes |
|---|---|---|
| Persistence | EF Core 10 + SQLite | Connection string `DefaultConnection` |
| Repositories | Ardalis.Specification 9 `RepositoryBase<T>` | One concrete repo per aggregate, registered explicitly |
| Queries | `Specification<T>` subclasses in `Domain/Specs/` | Inline LINQ in services is banned (AD-006) |
| Password storage | AES-256-CBC, `IV:ciphertext` base64 pair | Reversible by design — devices need the plaintext |
| Background jobs | Hangfire 1.8 + SQLite storage | Dashboard at `/hangfire` (Development only) |
| Errors | `GlobalExceptionHandler` + `AddProblemDetails()` | RFC 7807 responses |
| Tracing | OpenTelemetry → OTLP/gRPC → Tempo → Grafana | Only enabled when `OpenTelemetry:OtlpEndpoint` is set |
| API docs | `AddOpenApi()` + Scalar UI | `/openapi/v1.json`, `/scalar/v1` (Development only) |
| **Auth** | **none** | No authentication, authorization, or rate limiting anywhere |

---

## 7. Feature Inventory & Coverage

| Slice | Route | Status | Spec backfilled? |
|---|---|---|---|
| CreateDevice | `POST /api/devices` | implemented | ❌ |
| GetDevices | `GET /api/devices` | implemented | ❌ |
| GetDevice | `GET /api/devices/{id}` | implemented | ❌ |
| UpdateDevice | `PUT /api/devices/{id}` | implemented | ❌ |
| DeleteDevice | `DELETE /api/devices/{id}` | implemented | ❌ |
| UpsertUser | `POST /api/users` | implemented | ❌ |
| GetUser | `GET /api/users/{id}` | implemented | ❌ |
| SyncUser (`UserSyncJob`) | Hangfire job | partial — creates `Pending`/`Add` rows only | ❌ |
| **Device push (ISAPI)** | — | **missing** | — |
| **Replications API** | — | **missing** | — |
| **Delete/deactivate user** | — | **missing** | — |

**Test coverage:** 35 xUnit integration tests (17 device · 15 user · 3 sync job) over
`TestWebApplicationFactory` with in-memory SQLite; 4 NUnit/Playwright E2E tests
against a live API. No unit tests on domain factories in isolation — validation is
exercised through the HTTP surface.

---

## 8. Known Gaps (candidate spec backlog)

Ordered by how much they block the product's stated purpose.

1. **Replication never leaves the database.** No Hikvision ISAPI client, no worker
   that consumes `Pending` replications. `Replication.MarkProcessed()` and
   `Cancel()` are never called; `ReplicationStatus.Processed`/`Canceled` are
   unreachable. This is the core feature.
2. **`UserSyncJob` is not idempotent.** It enqueues on every upsert and blindly
   adds one `Add` replication per device each time, so repeated updates accumulate
   duplicate pending rows. No dedup key, no "supersede prior pending" rule.
3. **Removal path is dead.** `UserStatus.PendingRemove` and `ReplicationType.Remove`
   are declared but never produced — nothing deletes or deactivates a user, and no
   `Remove` replication is ever created.
4. **`User.Update` resets `Status` to `PendingAdd` unconditionally**, including on a
   no-op update, and it does so outside the `changed` guard that protects `UpdatedAt`.
5. **New devices get no backfill.** Registering a device does not create `Add`
   replications for existing users — only the user side triggers sync.
6. **`Replication` has no specification and no FK.** `UserSyncJobTests` loads every
   replication and filters in memory (`ListAsync().Where(...)`), which violates the
   "always use a `Specification<T>`" rule and will not scale.
7. **`EnsureCreated()` vs. migrations.** `Program.cs:72` calls
   `db.Database.EnsureCreated()` while four EF migrations exist — `EnsureCreated`
   bypasses the migration history, so migrations will never apply on a fresh DB.
8. **Docs drift.** `CLAUDE.md` documents `dotnet ef database update --project
   src/HikvisionReplicator.Data`; no such project exists — migrations live in
   `Api/Infrastructure/Migrations/`.
9. **Stray artifact committed.** `src/HikvisionReplicator.Api/Data Source=devices-dev.db`
   is a file literally named after a connection-string fragment, created by a
   mis-quoted CLI argument. Safe to delete.
10. **No auth, no rate limiting.** Every endpoint is anonymous, including the ones
    that store device credentials.
11. **AES-256-CBC without authentication.** `EncryptionService` provides
    confidentiality but no integrity check (no HMAC / GCM tag).

---

## 9. Commands

```bash
dotnet build
dotnet run   --project src/HikvisionReplicator.Api          # http://localhost:5000
dotnet test  src/HikvisionReplicator.Tests                  # integration, no Docker needed
dotnet test  src/HikvisionReplicator.E2ETests               # requires a running API
docker compose up -d                                        # Tempo + Grafana
dotnet ef migrations add <Name> --project src/HikvisionReplicator.Api
python3 scripts/lessons.py list --status confirmed           # load lessons at Specify/Design
```

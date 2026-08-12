# Architecture Map — hikvision-replicator

Describes the solution as rebuilt by the `device-registry` feature (2026-08-12), the
first feature of the spec-first rewrite (AD-013). The pre-rewrite implementation this
replaced is preserved in git history at `ebfc510`; nothing in the working tree descends
from it except by deliberate port.

Conventions that future features must follow are recorded as `AD-NNN` entries in
[STATE.md](STATE.md) — this document is the *map*, STATE.md is the *contract*.

---

## 1. Purpose

An ASP.NET Core 10 Minimal API that owns a catalogue of Hikvision access-control
devices and, in later phases, a catalogue of users it **replicates** out to every
registered device (AD-014, AD-015).

Today the API owns the device catalogue and the walking skeleton every later feature
builds on: PostgreSQL with real migrations, RFC 7807 errors, OpenTelemetry, and a
Testcontainers-backed test harness. Users, replication, and the Hikvision ISAPI client
are Phase 1 item 2 onwards — see §7.

---

## 2. Solution Layout

```text
hikvision-replicator/
├── HikvisionReplicator.slnx          # 3 projects
├── docker-compose.yml                # postgres + Tempo + Grafana
├── docker/
│   ├── tempo/tempo.yaml
│   └── grafana/provisioning/datasources/tempo.yaml
├── docs/test-patterns.md             # test level (AD-024) + behaviour-based naming
├── .specs/                           # ← spec-driven development artifacts
│   ├── ARCHITECTURE.md               # this file
│   ├── STATE.md                      # AD-NNN decision log + handoff snapshot
│   ├── LESSONS.md / lessons.json     # self-improving lessons (script-owned)
│   └── features/[feature]/           # spec.md · design.md · tasks.md · validation.md
├── scripts/lessons.py                # lessons bookkeeping (do not hand-edit output)
└── src/
    ├── HikvisionReplicator.Api/
    ├── HikvisionReplicator.Tests/        # xUnit · unit + integration · Testcontainers PostgreSQL
    └── HikvisionReplicator.E2ETests/     # NUnit + Playwright APIRequest · needs live API
```

---

## 3. API Project — Layer Map

```text
src/HikvisionReplicator.Api/
├── Program.cs                    ← composition root: DI, pipeline, route mapping
│
├── Domain/                       ← LAYER 1 · no framework deps beyond OneOf/CSharpFunctionalExtensions
│   ├── Device.cs                     aggregate root — Create + Update
│   ├── IpAddress.cs                  value object · stores the NORMALIZED address
│   ├── Port.cs                       value object · 1…65535
│   ├── FaceCapacity.cs               value object · 1…1,000,000 (AD-020)
│   └── Specs/                        Ardalis Specification<T> query objects
│       ├── DeviceByAddressSpec.cs            uniqueness pre-check on (IpAddress, HttpPort)
│       └── DeviceByAddressExcludingSpec.cs   same, exempting the device being updated
│
├── Features/                     ← LAYER 2 · vertical slices, 3 files each
│   └── Devices/{RegisterDevice,GetDevice,ListDevices,UpdateDevice,RemoveDevice}/
│
├── Infrastructure/               ← LAYER 3 · everything framework-facing
│   ├── AppDbContext.cs               DbSets + ApplyConfigurationsFromAssembly
│   ├── AppDbContextFactory.cs        design-time factory for `dotnet ef`
│   ├── DeviceConfiguration.cs        IEntityTypeConfiguration<Device> + named unique index
│   ├── DeviceRepository.cs           Ardalis RepositoryBase<Device> + 23505 translation
│   ├── EncryptionService.cs          AES-256-CBC, reversible, key from config
│   ├── EncryptionOptions.cs          + validator, wired with ValidateOnStart()
│   ├── GlobalExceptionHandler.cs     IExceptionHandler → 503 (database) / 500 (anything else)
│   ├── DomainErrorExtensions.cs      error record → IResult
│   └── Migrations/                   one migration: InitialCreate (Npgsql)
│
└── Shared/                       ← LAYER 0 · contracts, referenced by all layers
    ├── IAggregateRoot.cs             marker for aggregates
    ├── IRepository.cs                IRepositoryBase<T> where T : IAggregateRoot
    ├── IDeviceRepository.cs          address-safe add/save returning OneOf<Success, ConflictError>
    ├── IEncryptionService.cs         port — the implementation stays in Infrastructure/
    └── Errors.cs                     ValidationError · NotFoundError · ConflictError · Success
```

**Dependency direction:** `Program.cs → Features → Domain`, `Features → Shared`,
`Infrastructure → Domain + Shared`. `IEncryptionService` lives in `Shared/`, which
removes the one backwards `Features → Infrastructure` edge the previous layout had;
Features still reach into Infrastructure for `ToMinimalApiResult()` only, and never see
`AppDbContext`.

---

## 4. Vertical Slice Shape

Every operation is three files under `Features/{Resource}/{Operation}/`:

| File | Holds |
|---|---|
| `{Op}Service.Interface.cs` | Request record + Response record + `I{Op}Service` |
| `{Op}Service.cs` | Implementation — orchestrates domain + repository |
| `{Op}Service.Endpoint.cs` | `Use{Op}()` (DI) + `Map{Op}()` (route) extensions |

DTOs are **never shared across slices** — each of the five device slices declares its own
`DeviceResponse`. That duplication is deliberate (AD-004). No response carries a password
field of any kind (DEV-07).

### Request flow (write path)

```text
HTTP POST /api/devices
  → MapRegisterDevice() minimal-api delegate  (injects IRegisterDeviceService, CancellationToken ct)
  → IRegisterDeviceService.ExecuteAsync(request, ct)
       ├─ reject a blank plaintext password         [the aggregate only ever sees ciphertext]
       ├─ IEncryptionService.Encrypt(password)
       ├─ Device.Create(..., now)  → OneOf<Device, ValidationError>     [now from TimeProvider, AD-023]
       ├─ IDeviceRepository.AnyAsync(new DeviceByAddressSpec(...), ct)  → friendly ConflictError
       └─ IDeviceRepository.AddIfAddressFreeAsync(device, ct)
              └─ 23505 on the named address index → the same ConflictError   [AD-022]
  → OneOf<...>.Match(response => Results.Created(...), err => err.ToMinimalApiResult())
```

Errors are values, never exceptions: `OneOf<TSuccess, ValidationError, ConflictError, …>`
all the way from the domain factory to the endpoint's `.Match()`.

**The database is the authority on address uniqueness.** The specification pre-check
exists only to produce a better message; a registration that wins the race past it still
comes back as `409`, never `500` (DEV-06).

---

## 5. Domain Model

```text
Device (aggregate)
 ├ Id
 ├ Name (≤100)
 ├ IpAddress          VO — normalized via IPAddress.Parse(x).ToString()  ┐ unique together
 ├ HttpPort           VO — 1…65535                                       ┘ IX_devices_IpAddress_HttpPort
 ├ Username (≤100)
 ├ EncryptedPassword  AES-256-CBC `base64(IV):base64(ciphertext)`, never returned
 ├ FaceCapacity       VO — 1…1,000,000 (AD-020/AD-021 capacity guard)
 └ Created/UpdatedAt  timestamptz — UpdatedAt advances only when a value actually changed
```

- Aggregates have **private setters and private constructors**; the only entry points are
  the static `Create(...)` factory and the `Update(...)` mutator, both returning
  `OneOf<…, ValidationError>`.
- `Update` validates **every** field before mutating **any** of them, so a rejected update
  leaves the aggregate byte-identical (DEV-19), and `null` means "leave unchanged"
  (DEV-18).
- Both take `DateTime now` as a parameter; no aggregate reads the wall clock (AD-023).
- Value objects derive from `CSharpFunctionalExtensions.ValueObject` and expose an
  `internal static FromPersistence(...)` used only by the EF value converters.
- Error message constants live in a nested `static class Errors` on each type, so tests
  assert against the constant rather than a string literal.

`User` and `Replication` are not modelled yet — they arrive with `user-registry` and
`replication-queue`.

---

## 6. Cross-Cutting Concerns

| Concern | Implementation | Notes |
|---|---|---|
| Persistence | EF Core 10 + **PostgreSQL** (Npgsql) | Connection string `DefaultConnection`; AD-018 |
| Schema | `db.Database.Migrate()` at startup | `EnsureCreated()` appears nowhere (DEV-12) |
| Repositories | Ardalis.Specification 9 `RepositoryBase<T>` | One concrete repo per aggregate, registered explicitly |
| Queries | `Specification<T>` subclasses in `Domain/Specs/` | Inline LINQ in services is banned (AD-006) |
| Invariants | DB constraint + `23505` → `ConflictError` inside the repository | Services never see a provider exception (AD-022) |
| Password storage | AES-256-CBC, `IV:ciphertext` base64 pair, fresh IV per call | Reversible by design — devices need the plaintext |
| Key configuration | `EncryptionOptions` + `ValidateOnStart()` | A missing or non-32-byte key aborts startup (DEV-15) |
| Time | `TimeProvider` injected; `now` passed into the domain | Deterministic `UpdatedAt` assertions (AD-023) |
| Errors | `GlobalExceptionHandler` + `AddProblemDetails()` + `UseStatusCodePages()` | RFC 7807 everywhere: `503` for database failures, `500` otherwise, `400` for malformed JSON |
| Tracing | OpenTelemetry → OTLP/gRPC → Tempo → Grafana | Only registered when `OpenTelemetry:OtlpEndpoint` is set (DEV-16); EF SQL text is never captured |
| API docs | `AddOpenApi()` + Scalar UI | `/openapi/v1.json`, `/scalar/v1` — Development only (DEV-17) |
| Background jobs | **none** | Hangfire was not carried over; the job runner is decided in Phase 2 (ROADMAP OD-3) |
| **Auth** | **none** | No authentication, authorization, or rate limiting anywhere |

---

## 7. Feature Inventory & Coverage

| Slice | Route | Status | Spec |
|---|---|---|---|
| RegisterDevice | `POST /api/devices` | implemented | ✅ `device-registry` |
| ListDevices | `GET /api/devices` | implemented | ✅ `device-registry` |
| GetDevice | `GET /api/devices/{id}` | implemented | ✅ `device-registry` |
| UpdateDevice | `PUT /api/devices/{id}` | implemented | ✅ `device-registry` |
| RemoveDevice | `DELETE /api/devices/{id}` | implemented | ✅ `device-registry` |
| Catalogue pagination | — | **not scheduled** — DEV-26, P3 | ✅ specified, deliberately unbuilt |
| **Users API** | — | **not built** — Phase 1 item 2 | — |
| **Replication queue + worker** | — | **not built** — Phase 2 | — |
| **Device push (ISAPI)** | — | **not built** — Phase 3 | — |

**Test coverage:** 151 xUnit tests — 69 unit (`Tests/Domain/`, `Category=Unit`, no Docker)
and 82 integration through the HTTP surface against Testcontainers PostgreSQL — plus 9
NUnit/Playwright E2E tests against a live API. The level is chosen by layer per AD-024;
see [`docs/test-patterns.md`](../docs/test-patterns.md).

---

## 8. Known Gaps (candidate spec backlog)

The rewrite closed the defects the previous implementation carried: `EnsureCreated()`
alongside migrations, the racy read-then-write uniqueness check, unnormalized IP storage,
the unconditional `UpdatedAt` advance, the stale `HikvisionReplicator.Data` path in the
docs, and the stray connection-string-named artifact. What remains:

1. **The product's core capability does not exist yet.** Nothing replicates a user to a
   device: no user catalogue, no replication queue, no worker, no Hikvision ISAPI client.
   Phases 1–3 of [ROADMAP.md](ROADMAP.md) exist to build exactly this.
2. **No auth, no rate limiting.** Every endpoint is anonymous, including the ones that
   accept and store device credentials. Accepted for now (assumption A-6 of
   `device-registry`), with a hard deployment constraint: **this must not reach a routable
   network before `api-auth` ships.**
3. **AES-256-CBC without authentication.** `EncryptionService` provides confidentiality
   but no integrity check (no HMAC / GCM tag), so a tampered ciphertext fails at decrypt
   time rather than being detected. A move to AES-GCM needs a versioned ciphertext prefix
   and is easier to add before there is data than after (A-8).
4. **The device catalogue is unpaginated.** `GET /api/devices` returns a bare array, which
   pins the empty case to `[]`; DEV-26 will ship as a paged shape behind query parameters
   or a `v2` route rather than by mutating this response.
5. **Face capacity is declared but not yet enforced.** `Device.FaceCapacity` is modelled
   and validated; the guard that refuses a replication which would overfill a device is
   the required AD-021 mitigation and lands with `replication-queue`.

---

## 9. Commands

```bash
docker compose up -d                                        # PostgreSQL + Tempo + Grafana
dotnet build HikvisionReplicator.slnx
dotnet run   --project src/HikvisionReplicator.Api          # http://localhost:5000
dotnet test  src/HikvisionReplicator.Tests --filter "Category=Unit"   # pure logic, no Docker
dotnet test  src/HikvisionReplicator.Tests                  # + integration, needs a Docker daemon
dotnet test  src/HikvisionReplicator.E2ETests               # requires a running API
dotnet ef migrations add <Name> --project src/HikvisionReplicator.Api
python3 scripts/lessons.py list --status confirmed          # load lessons at Specify/Design
```

# Slice Anatomy

How a feature is built. `POST /api/devices` is the reference shape — every path named below
exists, so read the code rather than trusting this file where the two disagree.

Sources: `.specs/STATE.md` — **AD-001** (slice layout), **AD-002**/**AD-003** (result pattern),
**AD-004** (per-slice DTOs), **AD-006** (repositories and specifications), **AD-007**
(cancellation), **AD-008** (password encryption), **AD-009** (EF configuration), **AD-022**
(the database is the authority), **AD-023** (time).

## The three files

Each feature lives under `Features/{Resource}/{Operation}/` — three files, no shared DTOs:

| File | Contains |
|---|---|
| `{Operation}Service.Interface.cs` | Request record + Response record + service interface |
| `{Operation}Service.cs` | Service implementation |
| `{Operation}Service.Endpoint.cs` | DI registration (`UseXxx()`) + route mapping (`MapXxx()`) |

See `src/HikvisionReplicator.Api/Features/Devices/RegisterDevice/` for the worked example.

- Endpoints are grouped via `MapGroup` + `MapXxxEndpoints()` extension methods.
- **DTOs are separate from EF Core entities and are never shared between features**, even when
  identical — `GetDevice.DeviceResponse` and `ListDevices.DeviceResponse` are deliberately two
  records.

## The result pattern

Use `OneOf` for all fallible operations. **No abstract base error class** — standalone records
only, in `src/HikvisionReplicator.Api/Shared/Errors.cs`:

```csharp
public record ValidationError(string Field, string Message);
public record NotFoundError(string Message);
public record ConflictError(string Message);
public readonly record struct Success;
```

**Domain layer** — factory methods return `OneOf<T, ValidationError>`; use `TryPickT1` for
nested results.

**Service layer** — return `Task<OneOf<Response, Error1, Error2...>>`, **never**
`Task<IResult>`. Infallible operations (e.g. list queries) return the value directly.

**Endpoint layer** — call `.Match()` with descriptive parameter names, never single-letter:

```csharp
return result.Match(
    response        => Results.Created($"/api/devices/{response.Id}", response),
    validationError => validationError.ToMinimalApiResult(),
    conflictError   => conflictError.ToMinimalApiResult());
```

The `ToMinimalApiResult()` overloads live in
`src/HikvisionReplicator.Api/Infrastructure/DomainErrorExtensions.cs`.

## Request flow (write path)

How the three files compose at runtime:

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

### The database is the authority on uniqueness — the pre-check is not

The specification pre-check exists **only** to produce a friendlier message. A registration that
races past it still comes back `409`, never `500`.

Translate the provider's constraint violation into a `ConflictError` **inside the repository**
(`Infrastructure/DeviceRepository.cs`), so services never catch `PostgresException`. That
translation keys off a **named** index — so renaming an index silently degrades a 409 into a 500
unless a test covers it.

## Passwords

Device passwords are AES-256 encrypted on write via `IEncryptionService` — reversible, because
the device needs the plaintext. **The encrypted value is never returned in any response**, and
the aggregate only ever holds ciphertext.

## CancellationToken

`ExecuteAsync` must accept `CancellationToken cancellationToken` as its **last parameter, with
no default**, and pass it to every async call. Endpoints declare `CancellationToken ct`;
ASP.NET Core injects it automatically.

## Repository and specifications

- Inject `IRepository<T>` (never `AppDbContext`) in services.
- One concrete repository per aggregate in `Infrastructure/`, registered explicitly in
  `Program.cs`.
- **Always query through `Specification<T>` subclasses from `Domain/Specs/`.** Inline LINQ
  predicates in services are not allowed.

## EF Core

`AppDbContext.OnModelCreating` calls `ApplyConfigurationsFromAssembly`, so an
`IEntityTypeConfiguration<T>` added under `Infrastructure/` is picked up automatically — see
`DeviceConfiguration.cs`.

Value-object mapping, time handling and aggregate construction are **not restated here** — they
are stated once in the decision log, at AD-005, AD-009 and AD-023. Read those rather than a copy
that can drift out of step with them.

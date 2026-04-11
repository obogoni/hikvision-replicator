# hikvision-replicator Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-05

## Active Technologies

- C# / .NET 10 + ASP.NET Core 10 Minimal APIs, Entity Framework Core 10, System.Security.Cryptography (001-hikvision-device-api)

## Project Structure

```text
src/
├── HikvisionReplicator.Api/        ← ASP.NET Core 10 Minimal API
│   ├── Domain/                      ← Entities, value objects, error types
│   │   └── Specs/                   ← Ardalis specifications (query filters)
│   ├── Features/Devices/            ← Vertical slices
│   ├── Infrastructure/              ← EF Core, repositories, encryption
│   ├── Shared/                      ← IAggregateRoot, IRepository<T>
│   ├── appsettings.json
│   └── Program.cs
└── HikvisionReplicator.Tests/      ← xUnit integration tests
```

## Commands

```bash
dotnet restore          # Restore NuGet packages
dotnet build            # Build solution
dotnet ef database update --project src/HikvisionReplicator.Data  # Apply migrations
dotnet run --project src/HikvisionReplicator.Api                  # Run API (http://localhost:5000)
dotnet test             # Run all tests
```

## Code Style

- C# / .NET 10 idiomatic conventions (file-scoped namespaces, primary constructors where appropriate)
- Minimal APIs: group endpoints via `MapGroup` + extension methods (`MapDevicesEndpoints()`)
- DTOs separate from EF Core entities
- Passwords: AES-256 encrypt on write (reversible — needed for future device communication), never return encrypted value in responses; decryption key in `appsettings.json`

## Recent Changes

- 001-hikvision-device-api: Added C# / .NET 10 + ASP.NET Core 10 Minimal APIs, Entity Framework Core 10, System.Security.Cryptography (AES-256 for password storage)

<!-- MANUAL ADDITIONS START -->

## Result Pattern (ADR 002)

Use `OneOf` for all operation outcomes. **No abstract base error class** — each error type is a standalone record so every possible outcome is explicit in the method signature.

### Error types — `Domain/Errors.cs`

```csharp
public record ValidationError(string Field, string Message);
public record NotFoundError(string Message);
public record ConflictError(string Message);
public readonly record struct Success;  // unit marker for void-success operations
```

Add new error types here as needed. Never group them under a base class.

### Domain layer

Factory methods return `OneOf<T, ValidationError>`. Use `TryPickT1` to unwrap nested results:

```csharp
public static OneOf<Device, ValidationError> Create(...) { ... }
public OneOf<Success, ValidationError> Update(...) { ... }
```

### Service layer

Services return `Task<OneOf<Response, Error1, Error2...>>` — never `Task<IResult>`. Every possible outcome must appear in the signature:

```csharp
Task<OneOf<DeviceResponse, ValidationError, ConflictError>> ExecuteAsync(...);
Task<OneOf<DeviceResponse, ValidationError, NotFoundError, ConflictError>> ExecuteAsync(...);
Task<OneOf<Success, NotFoundError>> ExecuteAsync(...);
```

Infallible operations (e.g. list queries) skip `OneOf` and return the value directly.

### Mapping layer — `Infrastructure/DomainErrorExtensions.cs`

One `ToMinimalApiResult()` overload per error type. Lives in Infrastructure, not Domain, because `IResult` is an HTTP concern:

```csharp
public static IResult ToMinimalApiResult(this ValidationError e) => ...
public static IResult ToMinimalApiResult(this NotFoundError e) => ...
public static IResult ToMinimalApiResult(this ConflictError e) => ...
```

### Endpoint layer

Endpoints call `.Match()` to convert the `OneOf` result to `IResult`. Every arm is explicit:

```csharp
var result = await svc.ExecuteAsync(req);
return result.Match(
    response => Results.Created($"/api/devices/{response.Id}", response),
    validationError => validationError.ToMinimalApiResult(),
    conflictError => conflictError.ToMinimalApiResult());
```

Use descriptive lambda parameter names (`validationError`, `notFoundError`) — never single-letter abbreviations.

---

## Vertical Slice feature structure

Each feature is fully self-contained under `Features/{Resource}/{Operation}/`. No DTOs are shared between features.

### Files per feature

| File | Contains |
|---|---|
| `{Operation}Service.Interface.cs` | Request record + Response record + service interface |
| `{Operation}Service.cs` | Service implementation |
| `{Operation}Service.Endpoint.cs` | DI registration + route mapping |

Request and response DTOs both live in the `.Interface.cs` file alongside the interface contract. No separate DTO files.

### Project structure (updated)

```text
Features/Devices/
├── CreateDevice/
│   ├── CreateDeviceService.Interface.cs   ← CreateDeviceRequest, DeviceResponse, ICreateDeviceService
│   ├── CreateDeviceService.cs
│   └── CreateDeviceService.Endpoint.cs
├── UpdateDevice/
│   ├── UpdateDeviceService.Interface.cs   ← UpdateDeviceRequest, DeviceResponse, IUpdateDeviceService
│   ├── UpdateDeviceService.cs
│   └── UpdateDeviceService.Endpoint.cs
└── ...
```

---

## CancellationToken

Every `ExecuteAsync` method on a service interface must accept `CancellationToken cancellationToken` (required — no default) as its last parameter, and pass it to every async operation (repository calls, external I/O, etc.).

```csharp
// interface — required, no default
Task<OneOf<DeviceResponse, NotFoundError>> ExecuteAsync(int id, CancellationToken cancellationToken);

// implementation
public async Task<OneOf<DeviceResponse, NotFoundError>> ExecuteAsync(int id, CancellationToken cancellationToken)
{
    var device = await repo.GetByIdAsync(id, cancellationToken);
    ...
}
```

Endpoints declare `CancellationToken ct` in the lambda — ASP.NET Core injects it automatically from the `HttpContext`:

```csharp
app.MapGet("/api/devices/{id:int}", async (int id, IGetDeviceService svc, CancellationToken ct) =>
{
    var result = await svc.ExecuteAsync(id, ct);
    ...
```

---

## Domain model

Every domain entity must implement `IAggregateRoot` (`Shared/IAggregateRoot.cs`):

- Exposes `Id`, `CreatedAt`, `UpdatedAt` as get-only properties
- Enforces the constraint on `IRepository<T>` — only aggregates can have repositories

```csharp
public interface IAggregateRoot
{
    int Id { get; }
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}
```

---

## Repository pattern

### Interface — `Shared/IRepository.cs`

`IRepository<T>` extends Ardalis `IRepositoryBase<T>`, constrained to aggregate roots:

```csharp
public interface IRepository<T> : IRepositoryBase<T> where T : class, IAggregateRoot { }
```

Never inject `AppDbContext` directly into feature services — always inject `IRepository<T>`.

### Implementation — one concrete class per aggregate

Name repositories after the domain aggregate, not the infrastructure technology:

- `Infrastructure/DeviceRepository.cs` → `IRepository<Device>`

```csharp
public class DeviceRepository(AppDbContext dbContext)
    : RepositoryBase<Device>(dbContext), IRepository<Device> { }
```

### DI registration — `Program.cs`

Register each concrete repository explicitly (one line per aggregate):

```csharp
builder.Services.AddScoped<IRepository<Device>, DeviceRepository>();
```

### Specifications — `Domain/Specs/`

Complex query predicates live in `Domain/Specs/` as Ardalis `Specification<T>` subclasses.
Name specs after what they filter, not how they're used:

```csharp
// Domain/Specs/DeviceByAddressSpec.cs
public class DeviceByAddressSpec : Specification<Device>
{
    public DeviceByAddressSpec(IpAddress ipAddress, Port httpPort, int? excludedId = null)
    {
        Query.Where(d => d.IpAddress == ipAddress && d.HttpPort == httpPort);
        if (excludedId.HasValue)
            Query.Where(d => d.Id != excludedId.Value);
    }
}
```

Always prefer specs over inline LINQ predicates in services — even for simple filters. Direct LINQ in services is not allowed.

---

## EF Core configuration

Each aggregate has its own `IEntityTypeConfiguration<T>` class in `Infrastructure/`:

- File name: `{Aggregate}Configuration.cs` (e.g. `DeviceConfiguration.cs`)
- `AppDbContext.OnModelCreating` uses `ApplyConfigurationsFromAssembly` — new configurations are picked up automatically with no changes to `AppDbContext`

```csharp
// AppDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

<!-- MANUAL ADDITIONS END -->

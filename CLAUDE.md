# hikvision-replicator Development Guidelines

## Stack

C# / .NET 10 · ASP.NET Core 10 Minimal APIs · Entity Framework Core 10 · System.Security.Cryptography (AES-256)

## Project Structure

```text
src/
├── HikvisionReplicator.Api/
│   ├── Domain/           ← Entities, value objects, error types
│   │   └── Specs/        ← Ardalis specifications
│   ├── Features/         ← Vertical slices (Devices/, Users/)
│   ├── Infrastructure/   ← EF Core, repositories, encryption
│   ├── Shared/           ← IAggregateRoot, IRepository<T>
│   └── Program.cs
└── HikvisionReplicator.Tests/      ← xUnit integration tests
```

## Commands

```bash
dotnet restore
dotnet build
dotnet ef database update --project src/HikvisionReplicator.Data
dotnet run --project src/HikvisionReplicator.Api          # http://localhost:5000
dotnet test src/HikvisionReplicator.Tests       # integration tests (in-memory SQLite)
dotnet test src/HikvisionReplicator.E2ETests    # E2E tests (requires running API)
```

### E2E one-time setup

```bash
dotnet build src/HikvisionReplicator.E2ETests
pwsh src/HikvisionReplicator.E2ETests/bin/Debug/net10.0/playwright.ps1 install
```

Override base URL: `E2E_BASE_URL=http://staging:5000 dotnet test src/HikvisionReplicator.E2ETests`

## Code Style

- File-scoped namespaces, primary constructors where appropriate
- Endpoints grouped via `MapGroup` + `MapXxxEndpoints()` extension methods
- DTOs separate from EF Core entities; no DTOs shared between features
- Passwords: AES-256 encrypt on write (reversible), never return encrypted value in responses

## Result Pattern

Use `OneOf` for all fallible operations. **No abstract base error class** — standalone records only (`Shared/Errors.cs`):

```csharp
public record ValidationError(string Field, string Message);
public record NotFoundError(string Message);
public record ConflictError(string Message);
public readonly record struct Success;
```

**Domain layer** — factory methods return `OneOf<T, ValidationError>`; use `TryPickT1` for nested results.

**Service layer** — return `Task<OneOf<Response, Error1, Error2...>>`, never `Task<IResult>`. Infallible operations (e.g. list queries) return the value directly.

**Endpoint layer** — call `.Match()` with descriptive parameter names (never single-letter):

```csharp
return result.Match(
    response       => Results.Created($"/api/devices/{response.Id}", response),
    validationError => validationError.ToMinimalApiResult(),
    conflictError   => conflictError.ToMinimalApiResult());
```

`ToMinimalApiResult()` overloads live in `Infrastructure/DomainErrorExtensions.cs`.

## Vertical Slice Structure

Each feature lives under `Features/{Resource}/{Operation}/` — three files, no shared DTOs:

| File | Contains |
|---|---|
| `{Operation}Service.Interface.cs` | Request record + Response record + service interface |
| `{Operation}Service.cs` | Service implementation |
| `{Operation}Service.Endpoint.cs` | DI registration (`UseXxx()`) + route mapping (`MapXxx()`) |

## CancellationToken

`ExecuteAsync` must accept `CancellationToken cancellationToken` as last parameter (required — no default) and pass it to every async call. Endpoints declare `CancellationToken ct`; ASP.NET Core injects it automatically.

## Repository & Specifications

- Inject `IRepository<T>` (never `AppDbContext`) in services
- One concrete repository per aggregate in `Infrastructure/` — register explicitly in `Program.cs`
- **Always use `Specification<T>` subclasses from `Domain/Specs/`** — inline LINQ predicates in services are not allowed

## EF Core

`AppDbContext.OnModelCreating` calls `ApplyConfigurationsFromAssembly` — add `IEntityTypeConfiguration<T>` in `Infrastructure/` and it is picked up automatically.

## Tests

When writing any test, follow the naming convention in [`docs/test-patterns.md`](docs/test-patterns.md).

# hikvision-replicator Development Guidelines

## Stack

C# / .NET 10 · ASP.NET Core 10 Minimal APIs · Entity Framework Core 10 + **PostgreSQL** (Npgsql) · System.Security.Cryptography (AES-256)

PostgreSQL is the database from the first commit of the rewrite (AD-018), so **Docker is
required** to run the app or its integration tests. Hangfire is not in the solution; the
job runner is decided in Phase 2.

## Git Workflow (AD-025)

**Never commit directly to `main`.** The only exception is an explicit, in-the-moment
instruction from the user to do so — a general "go ahead" on a task is not that
instruction. If you are on `main` and about to commit, stop and branch first.

**One branch per change.** Branch off `main`, named `<type>/<kebab-slug>` using the same
type vocabulary as the commit message — `feat/device-registry`, `fix/ip-normalization`,
`docs/test-patterns`, `chore/repo-conventions`. When work is stacked on an unmerged
branch, say so and note the rebase needed once the base lands.

**Conventional Commits.** Every commit message is `type(scope): subject`, where scope is
optional but encouraged:

| Type | Use for |
|---|---|
| `feat` | New user-visible capability |
| `fix` | Bug fix |
| `docs` | Documentation, including `.specs/` and `docs/` |
| `test` | Tests only, no production-code change |
| `refactor` | Behaviour-preserving restructuring |
| `perf` | Performance work |
| `build` | Project files, NuGet dependencies, Docker |
| `ci` | Pipeline configuration |
| `chore` | Anything else that ships no behaviour |

Subject is imperative mood, lower case, no trailing period. Scopes in use: `domain`,
`devices`, `infra`, `tests`, `e2e`, `specs`, `deps`. Spec-driven work keeps **one atomic
commit per task** — never batch tasks into one commit.

**Merge via pull request, squash strategy.** Open a PR with `gh pr create`, fill in
`.github/pull_request_template.md`, and let the user review and merge. PRs are
**squash-merged**, so the PR title becomes the commit on `main` and must itself be a
valid conventional-commit subject. Per-task commits are preserved in the PR, not on
`main` — record any commit SHAs that matter (e.g. in `validation.md`) as
pre-squash references.

## Project Structure

```text
src/
├── HikvisionReplicator.Api/
│   ├── Domain/           ← Aggregates, value objects
│   │   └── Specs/        ← Ardalis specifications
│   ├── Features/         ← Vertical slices (Devices/)
│   ├── Infrastructure/   ← EF Core, migrations, repositories, encryption, exception handler
│   ├── Shared/           ← IAggregateRoot, IRepository<T>, error records, ports
│   └── Program.cs
├── HikvisionReplicator.Tests/             ← xUnit — unit only, pure logic, no Docker
├── HikvisionReplicator.IntegrationTests/  ← xUnit — through the HTTP surface, Testcontainers
└── HikvisionReplicator.E2E/               ← NUnit + Playwright, against a live API
```

## Commands

```bash
docker compose up -d                                       # PostgreSQL + Tempo + Grafana — required
dotnet restore
dotnet build HikvisionReplicator.slnx
dotnet ef database update --project src/HikvisionReplicator.Api   # migrations live in Api/Infrastructure/Migrations
dotnet run --project src/HikvisionReplicator.Api           # http://localhost:5000
dotnet test src/HikvisionReplicator.E2E                    # E2E tests (requires a running API)
```

The API applies its migrations itself at startup, so `dotnet ef database update` is only
needed to migrate a database out of band.

### Gate commands

```bash
# Docker-free — pure logic only (AD-024, AD-026)
dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests

# Full — needs a Docker daemon for Testcontainers PostgreSQL (AD-019)
dotnet build HikvisionReplicator.slnx \
  && dotnet test src/HikvisionReplicator.Tests \
  && dotnet test src/HikvisionReplicator.IntegrationTests
```

### E2E setup

```bash
dotnet build src/HikvisionReplicator.E2E
```

The suite drives the API through Playwright's `IAPIRequestContext`, which needs only the
node driver shipped in the package — **no browser download, and no `pwsh`, is required**.
Installing browsers (`playwright.ps1 install`, or `playwright install` after
`dotnet tool install --global Microsoft.Playwright.CLI`) is only needed if browser-driven
tests are ever added.

Override base URL: `E2E_BASE_URL=http://staging:5000 dotnet test src/HikvisionReplicator.E2E`

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

Before writing any test, read [`docs/test-patterns.md`](docs/test-patterns.md) — it holds
both the **"Choosing the test level"** rules (AD-024: unit for pure no-I/O logic,
integration through the HTTP surface for slices, repositories, and startup, E2E as a thin
out-of-process confirmation) and the behaviour-based naming convention.

**The project a test lives in is what declares its level** (AD-026) — `.Tests` for unit,
`.IntegrationTests` for integration, `.E2E` for end-to-end. There is no category trait;
choosing the project is choosing the level, so put a new test in the project whose
dependencies it is allowed to have. A test that needs Docker cannot compile in `.Tests`.

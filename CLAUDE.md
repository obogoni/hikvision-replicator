# hikvision-replicator Development Guidelines

## Stack

C# / .NET 10 · ASP.NET Core 10 Minimal APIs · Entity Framework Core 10 + **PostgreSQL** (Npgsql) · System.Security.Cryptography (AES-256)

PostgreSQL is the database from the first commit of the rewrite (AD-018), so **Docker is
required** to run the app or its integration tests. Hangfire is not in the solution; the
job runner is decided in Phase 2.

## Git Workflow (AD-025)

**Never commit directly to `main` — and the server now enforces it.** Branch protection
rejects a direct push outright:

```
remote: - Changes must be made through a pull request.
remote: - Required status check "build-and-test" is expected.
 ! [remote rejected] HEAD -> main (push declined due to repository rule violations)
```

So if you are on `main` and about to commit, branch first — otherwise you will do the work
and then discover it cannot be pushed. `enforce_admins=true`, so being the repo owner does
not exempt you. Note that `git push --dry-run` does **not** test this: a dry run sends no
pack, so protection never evaluates it and the push appears to succeed.

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

**Base every PR on `main`.** A PR whose base is another *branch* merges into that branch,
**not** into `main` — GitHub retargets a child only when the base branch is deleted on
merge. This stranded work off `main` twice (PR #2, PR #4). Stack only when genuinely
required; when you do, merge the base PR with its branch deleted, or retarget the child to
`main` first. **After any merge, verify `main` itself** — a "Merged" badge only means
something merged somewhere:

```bash
git fetch --prune && git log --oneline -3 origin/main && git ls-tree -d --name-only origin/main src/
```

Squash-only and auto-delete are enforced by repository settings, not just by this file
(AD-025): `gh repo edit --enable-squash-merge=true --enable-merge-commit=false
--enable-rebase-merge=false --delete-branch-on-merge`.

**A PR cannot merge until CI passes.** `build-and-test` is a required status check with
`strict=true`, so a branch must also be up to date with `main` first — if another PR merges
ahead of yours, update from `main` and let CI re-run. A PR is required but needs **no
approval** (`required_approving_review_count=0`), because a solo maintainer cannot approve
their own PR. The exact protection payload lives in AD-025; check live state with:

```bash
gh api repos/obogoni/hikvision-replicator/branches/main/protection
```

## Spec-Driven Validation (AD-028)

**Feature-level validation runs as a fresh sub-agent, not as a self-check.** After the last
task of a feature is committed, dispatch the `tlc-spec-driven` **Verifier** as a separate
sub-agent — this is a standing instruction, so no further permission is needed for it.

**Author ≠ verifier.** The agent that wrote the code and the spec is the wrong one to confirm
they agree: a blind spot that shaped the implementation shaped the acceptance criteria too.
The Verifier receives only `spec.md`, the commit range, and the test files, and re-derives
coverage **evidence-or-zero** — every criterion needs a cited `file:line` plus the assertion
expression, or it counts as uncovered no matter how confident anyone is.

This was skipped on `test-project-conventions` and `code-style-enforcement`, and the cost is
recorded: `code-style-enforcement`'s `AC-1` demanded "zero diagnostics" while the same spec
accepted 10 `CA` warnings. The author wrote both halves, and that same intent papered over the
contradiction; it was caught incidentally, not by design.

The standalone fallback in `validate.md` is for when a sub-agent genuinely cannot run. Using
it is a **deviation to declare in `validation.md` and the PR**, not a normal path.

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

The `dotnet build` in each gate is **also the code-style gate** (AD-027) — no extra lint step,
no flags. Both commands are unchanged by that decision; they simply now fail on bad formatting.

```bash
# Docker-free — pure logic only (AD-024, AD-026)
dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests

# Full — needs a Docker daemon for Testcontainers PostgreSQL (AD-019)
dotnet build HikvisionReplicator.slnx \
  && dotnet test src/HikvisionReplicator.Tests \
  && dotnet test src/HikvisionReplicator.IntegrationTests
```

`.github/workflows/ci.yml` runs the full gate on every PR to `main` and is the enforcement
boundary — local runs are advisory (AD-027).

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

**Formatting is enforced by the compiler, not by discipline (AD-027).** `.editorconfig` at the
repo root is the single source of style rules, and `Directory.Build.props` sets
`EnforceCodeStyleInBuild`, so **every** `dotnet build` is the style gate — there is no flag to
remember and no separate lint command. `IDE0055` is an **error**: bad formatting fails the build.

Fix violations with:

```bash
dotnet format whitespace              # the whole solution
dotnet format whitespace --folder src/HikvisionReplicator.Api/Shared   # faster, one folder
```

**Never use bare `dotnet format`.** It also runs the analyzer fixers, which make semantic
edits — on this repo it "fixed" the deprecated Testcontainers `PostgreSqlBuilder` call by
stamping `[Obsolete]` onto `PostgresFixture` and `UnreachableDatabaseFixture`, silencing a real
advisory by marking the consumer obsolete. `whitespace` is all `IDE0055` needs.

**An up-to-date incremental build re-reports zero diagnostics even when the code still violates
them** (lesson L-007). The trustworthy signal is the **first** build after a change; add
`--no-incremental` when a build's silence is being used as evidence.

`AnalysisMode` is `Recommended`, pinned to `AnalysisLevel` `10.0`. It currently surfaces 10 `CA`
findings, all warnings, all enumerated in
`.specs/features/code-style-enforcement/spec.md`. Ratchet rules upward as those are cleared —
never jump to `All`. Severity belongs in `.editorconfig`, **never** `-warnaserror`: a clean build
already emits 4 `NU1903` and 4 `CS0618`, which `-warnaserror` would turn into failures.

Conventions the rules do not mechanically cover:

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

### Request flow (write path)

How the three files compose at runtime — `POST /api/devices` is the reference shape:

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

**The database is the authority on uniqueness — the pre-check is not (AD-022).** The
specification pre-check exists only to produce a friendlier message; a registration that
races past it still comes back `409`, never `500`. Translate the provider's constraint
violation into a `ConflictError` **inside the repository** so services never catch
`PostgresException`. That translation keys off a **named** index, so renaming an index
silently degrades a 409 into a 500 unless a test covers it.

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

# hikvision-replicator

## What this is

Live replication of users to Hikvision facial-recognition access devices. The driving
scenario is stadium access control: a spectator who buys a ticket minutes before kickoff
must get through a turnstile using their face.

That ordering is the point. **Latency** from user creation to enrolled-on-every-device is the
primary quality attribute; **surviving individual offline readers** is the second (AD-014). An
external integrator is the system of record — this service owns propagation, not ticketing truth.

Phases, scale targets and open decisions: [`.specs/ROADMAP.md`](.specs/ROADMAP.md).
Decisions every feature must follow or supersede: [`.specs/STATE.md`](.specs/STATE.md).

## Stack

C# / .NET 10 · ASP.NET Core 10 Minimal APIs · Entity Framework Core 10 + **PostgreSQL**
(Npgsql) · System.Security.Cryptography (AES-256).

PostgreSQL is the database from the first commit of the rewrite (AD-018), so **Docker is required**
to run the app or its integration tests. **No job runner is in the solution** — that
choice is open until Phase 2 (AD-030), so nothing may assume Hangfire.

## Project structure

```text
src/
├── HikvisionReplicator.Api/
│   ├── Domain/           ← Aggregates, value objects (Specs/ holds Ardalis specifications)
│   ├── Features/         ← Vertical slices (Devices/)
│   ├── Infrastructure/   ← EF Core, migrations, repositories, encryption, exception handler
│   ├── Shared/           ← IAggregateRoot, IRepository<T>, error records, ports
│   └── Program.cs
├── HikvisionReplicator.Tests/             ← xUnit — unit only, pure logic, no Docker
└── HikvisionReplicator.IntegrationTests/  ← xUnit — through the HTTP surface, Testcontainers
```

## Commands

```bash
docker compose up -d                              # PostgreSQL + Tempo + Grafana — required
dotnet restore
dotnet run --project src/HikvisionReplicator.Api  # http://localhost:5000
```

Migrations live in `Api/Infrastructure/Migrations` and are applied at startup, so
`dotnet ef database update --project src/HikvisionReplicator.Api` is out-of-band only.

### Gate commands

```bash
# Docker-free — pure logic only (AD-024, AD-026)
dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests

# Full — needs a Docker daemon for Testcontainers PostgreSQL (AD-019)
dotnet build HikvisionReplicator.slnx \
  && dotnet test src/HikvisionReplicator.Tests \
  && dotnet test src/HikvisionReplicator.IntegrationTests
```

## Git

**Never commit directly to `main`** — the server rejects the push, and being the repo owner
does not exempt you. If you are on `main` and about to commit, branch first. One branch per
change, named `<type>/<kebab-slug>`.

**Base every PR on `main`.** A PR based on another branch merges into *that branch*, which
stranded work twice. Merge is by squash, so the PR title becomes the commit and must be a
valid Conventional Commits subject. Spec-driven work keeps **one atomic commit per task** —
never batch.

Protection payload, the type table, the verify-`main` command, and the `git push` dry-run
trap: [`docs/git-workflow.md`](docs/git-workflow.md).

## Code style

Formatting is enforced by the compiler: `.editorconfig` is the single source of rules and
`IDE0055` is an **error**, so bad formatting fails the build. Fix with `dotnet format whitespace`.

**Never use bare `dotnet format`** — it also runs the analyzer fixers and makes semantic
edits. And do not trust a quiet build: an up-to-date incremental build re-reports zero
diagnostics even when the code still violates them, so add `--no-incremental` when a build's
silence is your evidence (L-007).

Both incidents in full, plus the analyzer ratchet: [`docs/code-style.md`](docs/code-style.md).

## Writing a feature

Vertical slices under `Features/{Resource}/{Operation}/`, three files each. DTO boundaries, the
result pattern, the write-path flow, and where uniqueness is really enforced:
[`docs/slice-anatomy.md`](docs/slice-anatomy.md).

## Tests

Read [`docs/test-patterns.md`](docs/test-patterns.md) **before writing any test** — it holds
the choose-the-level rules and the behaviour-based naming convention.

**The project a test lives in declares its level** (AD-026): `.Tests` for unit,
`.IntegrationTests` for integration. There is no category trait, and a test needing Docker
cannot compile in `.Tests`. **There is no end-to-end project** — it was retired as pure
duplication (AD-035); do not add one back without reading that entry.

## Validation

After a feature's last task, dispatch the `tlc-spec-driven` **Verifier** as a fresh sub-agent.
**Author ≠ verifier**: it re-derives coverage evidence-or-zero, so every criterion needs a cited
`file:line` or counts as uncovered. Standing authorisation (AD-028) — do not ask permission. The
standalone fallback is a **deviation to declare** in `validation.md` and the PR.

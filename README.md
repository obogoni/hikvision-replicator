# Hikvision Replicator

ASP.NET Core 10 Minimal API for managing Hikvision devices.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) — **required**, not optional. It provides the
  PostgreSQL database the API runs against, the PostgreSQL instance the integration tests
  provision through Testcontainers, and the local observability stack.

## Running locally

### 1. Start the backing services

PostgreSQL, Tempo (trace storage), and Grafana (UI) all run in Docker:

```bash
docker compose up -d
```

- PostgreSQL: `localhost:5432` (database `hikvision`, user `hikvision`)
- Grafana UI: http://localhost:3000
- Tempo OTLP gRPC: `localhost:4317`

### 2. Run the API

```bash
dotnet run --project src/HikvisionReplicator.Api
```

The API starts on http://localhost:5000 and applies its EF Core migrations at startup, so
an empty database becomes a working one with no manual step.

- OpenAPI spec: http://localhost:5000/openapi/v1.json
- Scalar UI: http://localhost:5000/scalar/v1

Both documentation endpoints are served in the Development environment only.

## Viewing traces

Once both the API and Docker stack are running:

1. Open http://localhost:3000 (Grafana — no login required)
2. Go to **Explore** in the left sidebar
3. Select the **Tempo** datasource
4. Search by service name: `hikvision-replicator`

Each HTTP request produces a trace with child spans for EF Core SQL statements.

## Running tests

```bash
# Pure-logic tests — the only ones that need nothing running
dotnet test src/HikvisionReplicator.Tests

# In-process through the HTTP surface — starts a PostgreSQL container per test collection
dotnet test src/HikvisionReplicator.IntegrationTests

# Out-of-process, against a live API
docker compose up -d
dotnet run --project src/HikvisionReplicator.Api      # in another shell
dotnet test src/HikvisionReplicator.E2E
```

The integration suite provisions its own PostgreSQL through Testcontainers, so it needs a
**running Docker daemon** but not `docker compose up`. The E2E suite needs both the
compose stack and a running API; override its target with
`E2E_BASE_URL=http://staging:5000`.

See [`docs/test-patterns.md`](docs/test-patterns.md) for which level a new test belongs at.

## Configuration

| File | Purpose |
|---|---|
| `appsettings.Development.json` | Local dev overrides (connection string, dev encryption key, OTLP endpoint) |
| `appsettings.json` | Production defaults — `Encryption:Key` ships **empty on purpose** and must be set to a Base64-encoded 32-byte key before deploying |

`Encryption:Key` is validated while the application starts: a missing or wrong-length key
aborts startup with a diagnostic naming the setting, rather than failing on the first
device registration.

The OTLP exporter is only active when `OpenTelemetry:OtlpEndpoint` is set. Without it the
API starts normally, just without tracing.

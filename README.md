# Hikvision Replicator

ASP.NET Core 10 Minimal API for managing Hikvision devices and users.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (for local observability stack)

## Running locally

### 1. Start the observability stack

Tempo (trace storage) and Grafana (UI) run in Docker:

```bash
docker compose up -d
```

- Grafana UI: http://localhost:3000
- Tempo OTLP gRPC: `localhost:4317`

### 2. Run the API

```bash
dotnet run --project src/HikvisionReplicator.Api
```

The API starts on http://localhost:5000. The SQLite database is created automatically on first run.

- OpenAPI spec: http://localhost:5000/openapi/v1.json
- Scalar UI: http://localhost:5000/scalar/v1

## Viewing traces

Once both the API and Docker stack are running:

1. Open http://localhost:3000 (Grafana — no login required)
2. Go to **Explore** in the left sidebar
3. Select the **Tempo** datasource
4. Search by service name: `hikvision-replicator`

Each HTTP request produces a trace with child spans for EF Core SQL statements.

## Running tests

```bash
dotnet test
```

Tests use an in-memory SQLite database and do not require Docker.

## Configuration

| File | Purpose |
|---|---|
| `appsettings.Development.json` | Local dev overrides (SQLite path, dev encryption key, OTLP endpoint) |
| `appsettings.json` | Production defaults — replace the encryption key before deploying |

The OTLP exporter is only active when `OpenTelemetry:OtlpEndpoint` is set. If Docker isn't running, the API starts normally without tracing.

# ADR 001 — Adopt Vertical Slice Architecture

## Status

Accepted — 2026-04-05

## Context

The initial implementation used a traditional layered structure: a shared data project for EF Core entities and migrations, and an API project with endpoint groupings and shared services (e.g. `EncryptionService`). As the number of features grows, this layout tends to produce cross-cutting coupling — adding a new operation requires touching multiple layers that have no natural co-location.

## Decision

We adopt **Vertical Slice Architecture (VSA)**. Code is organized by use case rather than by technical layer. Each use case is fully self-contained within its own folder.

### Feature folder layout

Each feature domain lives under `Features/<Domain>/`. Each use case within that domain gets its own subfolder containing exactly three files:

```
src/HikvisionReplicator.Api/
├── Features/
│   └── Devices/
│       ├── CreateDevice/
│       │   ├── ICreateDeviceService.cs      ← interface
│       │   ├── CreateDeviceService.cs       ← implementation
│       │   └── CreateDevice.Endpoint.cs     ← endpoint + DI registration
│       ├── UpdateDevice/
│       │   ├── IUpdateDeviceService.cs
│       │   ├── UpdateDeviceService.cs
│       │   └── UpdateDevice.Endpoint.cs
│       ├── GetDevice/
│       │   ├── IGetDeviceService.cs
│       │   ├── GetDeviceService.cs
│       │   └── GetDevice.Endpoint.cs
│       └── DeleteDevice/
│           ├── IDeleteDeviceService.cs
│           ├── DeleteDeviceService.cs
│           └── DeleteDevice.Endpoint.cs
├── Infrastructure/
│   └── EncryptionService.cs                 ← truly shared cross-cutting concerns
└── Program.cs
```

### Endpoint registration convention

Each `<UseCase>.Endpoint.cs` exposes a static method `Use<UseCase>(WebApplicationBuilder builder)` responsible for:

1. Registering the service with the DI container (`builder.Services.AddScoped<I...Service, ...Service>()`)
2. Mapping the HTTP route (`builder.Build()` is not called here — route mapping is chained from `Program.cs`)

`Program.cs` composes the application by calling each `Use*` method.

### Shared infrastructure

Truly cross-cutting concerns (database context, encryption, etc.) remain in `Infrastructure/` inside the Api project. The `HikvisionReplicator.Data` project continues to own EF Core entities and migrations.

### No Commands/Queries split

We do not distinguish between commands and queries at the folder level. All use cases follow the same structure regardless of whether they read or write. A dispatch mechanism (e.g. MediatR) may be introduced in a future ADR if the need arises.

### DTOs

Request and response DTOs are co-located inside the use case folder. There is no shared DTO layer.

## Consequences

**Positive:**
- Adding a new use case requires touching only one folder — no cross-layer edits.
- Each slice is independently testable and deployable in isolation.
- Navigating to a use case is predictable: `Features/<Domain>/<UseCase>/`.

**Negative / trade-offs:**
- Some duplication is expected across slices (e.g. similar DTOs). This is intentional — sharing forces coupling.
- Developers accustomed to layered architecture need to adjust their mental model.

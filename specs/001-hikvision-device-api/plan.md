# Implementation Plan: Hikvision Device Management API

**Branch**: `001-hikvision-device-api` | **Date**: 2026-04-05 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/001-hikvision-device-api/spec.md`

## Summary

A .NET 10 REST API that acts as a configuration registry for Hikvision face recognition terminals. Exposes five CRUD endpoints (create, list, get, update, delete) for Device records. Persistent storage via SQLite/EF Core; switchable to SQLite in-memory for test environments. Passwords hashed with BCrypt before storage and never returned in responses.

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: ASP.NET Core 10 Minimal APIs, Entity Framework Core 10, System.Security.Cryptography (built-in)  
**Storage**: SQLite via EF Core (persistent default) · SQLite `:memory:` (test environments)  
**Password Storage**: AES-256 symmetric encryption (reversible) — key in `appsettings.json`  
**Testing**: xUnit + WebApplicationFactory + SQLite in-memory  
**Target Platform**: Cross-platform (Linux / Windows server)  
**Project Type**: Web service (REST API)  
**Performance Goals**: <3 seconds per CRUD operation at ≤20 devices  
**Constraints**: HTTP only (internal network), no authentication, ≤20 devices, passwords never returned  
**Scale/Scope**: Single site, ≤20 devices

## Constitution Check

No project constitution defined. No gates to evaluate.

> **Note**: A constitution should be created via the template at `.specify/templates/constitution-template.md` before the project grows significantly.

## Project Structure

### Documentation (this feature)

```text
specs/001-hikvision-device-api/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/           ← Phase 1 output
│   └── devices.md
└── tasks.md             ← Phase 2 output (/speckit.tasks — not yet created)
```

### Source Code (repository root)

```text
src/
├── HikvisionReplicator.Api/          ← ASP.NET Core 10 Minimal API project
│   ├── Program.cs                     ← App bootstrap + endpoint registration
│   ├── Features/
│   │   └── Devices/
│   │       ├── DeviceEndpoints.cs     ← MapDevicesEndpoints() extension method
│   │       ├── DeviceRequest.cs       ← Create/Update request DTOs
│   │       └── DeviceResponse.cs      ← Response DTO (no password field)
│   ├── Infrastructure/
│   │   └── EncryptionService.cs       ← AES-256 encrypt/decrypt
│   ├── appsettings.json               ← Connection string, environment config
│   └── appsettings.Development.json
├── HikvisionReplicator.Data/         ← EF Core data layer
│   ├── AppDbContext.cs                ← DbContext with Device DbSet
│   ├── Entities/
│   │   └── Device.cs                  ← Device entity (includes encrypted password)
│   └── Migrations/                    ← EF Core generated migrations
└── HikvisionReplicator.Tests/        ← xUnit integration + unit tests
    ├── DeviceEndpointsTests.cs        ← WebApplicationFactory integration tests
    └── TestWebApplicationFactory.cs  ← Custom factory (SQLite :memory: override)
```

**Structure Decision**: Three-project solution. No separate Application/Services layer — business logic (validation, hashing) lives in the API project at this scale. Separation into Api + Data projects keeps persistence concerns isolated and makes the storage switch (SQLite ↔ in-memory) a single DI registration change.

## Complexity Tracking

No constitution violations detected.

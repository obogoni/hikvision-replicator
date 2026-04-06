# hikvision-replicator Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-05

## Active Technologies

- C# / .NET 10 + ASP.NET Core 10 Minimal APIs, Entity Framework Core 10, System.Security.Cryptography (001-hikvision-device-api)

## Project Structure

```text
src/
├── HikvisionReplicator.Api/        ← ASP.NET Core 10 Minimal API
│   ├── Features/Devices/            ← Device endpoints + DTOs
│   ├── Infrastructure/              ← EncryptionService
│   ├── appsettings.json
│   └── Program.cs
├── HikvisionReplicator.Data/       ← EF Core data layer
│   ├── AppDbContext.cs
│   ├── Entities/Device.cs
│   └── Migrations/
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
<!-- MANUAL ADDITIONS END -->

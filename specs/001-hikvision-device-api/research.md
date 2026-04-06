# Research: Hikvision Device Management API

**Date**: 2026-04-05  
**Branch**: `001-hikvision-device-api`

## Decision Log

---

### 1. API Style: Minimal APIs vs Controller-Based

**Decision**: ASP.NET Core 10 Minimal APIs

**Rationale**: Minimal APIs are Microsoft's recommended approach for new .NET services. For this scope (5 endpoints, ≤20 records), they eliminate boilerplate while remaining fully production-ready. .NET 10 Minimal APIs include native OpenAPI 3.1 support, endpoint filters for validation, and native AOT compatibility.

**Alternatives considered**:
- Controller-based MVC — adds significant ceremony (attribute routing, action result types, controller classes) without benefit at this scale.
- FastEndpoints (third-party) — valid pattern for larger services; unnecessary dependency here.

---

### 2. Storage: SQLite via EF Core

**Decision**: SQLite via Entity Framework Core 10 for persistent storage; SQLite `:memory:` connection string for testing environments.

**Rationale**: SQLite is a zero-configuration, single-file database. Perfect for a small internal registry with ≤20 device records. EF Core provides migrations for schema management and a clean abstraction that makes the persistent ↔ in-memory switch a single DI registration change (connection string only). SQLite's in-memory mode (`:memory:`) replicates real SQLite behavior (transactions, constraints) making tests reliable — unlike the EF Core InMemory provider which Microsoft explicitly discourages for correctness-sensitive tests.

**Alternatives considered**:
- EF Core InMemory provider — does not enforce constraints or transactions; unsuitable for accurate test behavior.
- LiteDB — document store, no EF Core integration, less portable.
- Plain JSON file — no schema enforcement, manual serialization, no querying.
- SQL Server / PostgreSQL — requires server infrastructure; disproportionate to ≤20 devices.

---

### 3. Password Storage: AES-256 Symmetric Encryption

**Decision**: AES-256-CBC symmetric encryption via `System.Security.Cryptography` (built-in to .NET — no extra NuGet package required). Encryption key stored in `appsettings.json` (or environment variable for production).

**Rationale**: Device passwords must be **recoverable** so the system can authenticate to physical Hikvision terminals in future features (e.g., HTTP Basic Auth or Digest Auth when sending commands or syncing data). One-way hashing algorithms (BCrypt, Argon2, PBKDF2) are irreversible by design and would make this impossible. AES-256 is the industry standard for symmetric encryption at rest, provides strong security, and is available in .NET's built-in `System.Security.Cryptography` namespace with no additional dependencies.

**Alternatives considered**:
- BCrypt.Net-Next — ruled out because BCrypt is a one-way hash; plaintext cannot be recovered. Only suitable if the credential never needs to leave the registry.
- Argon2id — same problem: one-way hash, not reversible.
- ASP.NET Core `PasswordHasher<T>` (PBKDF2) — also one-way; same limitation.
- Storing plaintext — unacceptable; violates FR-011 and basic security practice.

---

### 4. Testing: xUnit + WebApplicationFactory

**Decision**: xUnit with `WebApplicationFactory<Program>` for integration tests; SQLite `:memory:` as test database.

**Rationale**: This is the idiomatic .NET testing stack. `WebApplicationFactory` launches the full ASP.NET Core pipeline in-process, enabling true end-to-end HTTP testing without running a real server. A custom factory subclass overrides the DI registration to swap the SQLite file path for `:memory:`, isolating each test run. xUnit's `IClassFixture<T>` manages factory lifecycle cleanly.

**Alternatives considered**:
- NUnit — equally capable; xUnit is more idiomatic in the .NET ecosystem.
- MSTest — legacy; verbose.
- Mocking DbContext — avoids real data behavior; integration tests must hit real persistence.

---

### 5. Project Structure

**Decision**: Three-project solution — `HikvisionReplicator.Api`, `HikvisionReplicator.Data`, `HikvisionReplicator.Tests`.

**Rationale**: Separating the data layer into its own project keeps EF Core and entity definitions isolated from API concerns, and makes the DI override in tests trivial. No separate Application/Services project is warranted at this scale — 5 endpoints with simple CRUD logic do not justify an additional abstraction layer.

**Alternatives considered**:
- Single-project — simpler initially but conflates entity models with API concerns; storage swap becomes harder.
- Four-project with Application layer — premature abstraction for 5 endpoints; YAGNI.

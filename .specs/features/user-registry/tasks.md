# User Registry Tasks

## Execution Protocol (MANDATORY — do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its
Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is
the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review,
Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user — do not proceed without it.**

---

**Spec**: [`spec.md`](spec.md) · **Design**: [`design.md`](design.md)
**Status**: **Approved** · 2026-08-25 — execution by four sequential batch workers
**Baselines before this feature**: 81 unit · 88 integration · 9 E2E

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec — confirm before Execute.
> **Guidelines found**: `CLAUDE.md` (§ Tests, § Gate commands), `docs/test-patterns.md`
> (choose-the-level rules, behaviour-based naming, the process-wide-listener caution),
> `.specs/STATE.md` AD-024 (level by layer) and AD-026 (project declares level),
> `.github/workflows/ci.yml` (the authoritative gate), `Directory.Build.props` (build is the
> style gate). No coverage-threshold tool config exists — depth comes from the spec's ACs.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| Domain aggregates & value objects | unit | All branches; 1:1 with spec ACs; every listed edge case | `src/HikvisionReplicator.Tests/Domain/*.cs` | `dotnet test src/HikvisionReplicator.Tests` |
| Infrastructure pure logic (normalizer, options validator) | unit | All branches; every listed edge case; golden-hash determinism | `src/HikvisionReplicator.Tests/Infrastructure/*.cs` | `dotnet test src/HikvisionReplicator.Tests` |
| Feature slices & routes | integration | Every route: happy path + every listed edge case + every documented error path | `src/HikvisionReplicator.IntegrationTests/*.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Repositories & specifications | integration | Key query paths + both constraint races + the no-bytes-loaded guarantee | `src/HikvisionReplicator.IntegrationTests/*.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| EF configuration, migration, schema | integration | Both indexes incl. the partial filter; cascade delete | `src/HikvisionReplicator.IntegrationTests/*.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| Startup & cross-cutting handlers | integration | Startup validation; database-unavailable path | `src/HikvisionReplicator.IntegrationTests/*.cs` | `dotnet test src/HikvisionReplicator.IntegrationTests` |
| HTTP surface, out of process | e2e | One happy path + one error path per route — confirmation, not coverage | `src/HikvisionReplicator.E2E/*.cs` | `dotnet test src/HikvisionReplicator.E2E` |
| Ports / interfaces / DTO contracts (no behaviour) | none | — build gate only | `src/HikvisionReplicator.Api/Shared/*.cs` | build gate only |

> **Naming is a hard rule, not a preference** (`docs/test-patterns.md`): plain-English behaviour,
> underscore-separated. No HTTP verbs, no status codes, no method names. A reviewer seeing
> `Post_MissingFacePic_Returns400` should reject the task.

## Gate Check Commands

> Discovered from `CLAUDE.md` § Gate commands and `.github/workflows/ci.yml` — not invented.

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| **Quick** | After tasks with unit tests only (Docker-free) | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests` |
| **Full** | After tasks with integration tests (needs a Docker daemon) | `dotnet build HikvisionReplicator.slnx && dotnet test src/HikvisionReplicator.Tests && dotnet test src/HikvisionReplicator.IntegrationTests` |
| **Build** | After contract-only or config-only tasks | `dotnet build HikvisionReplicator.slnx --no-incremental` |

> **`--no-incremental` is not optional when a build's silence is the evidence** (lesson **L-007**,
> confirmed ×2). An up-to-date incremental build re-reports zero diagnostics even when the code
> still violates them.
>
> **Never run bare `dotnet format`** — it runs the analyzer fixers and makes semantic edits.
> `dotnet format whitespace` is what `IDE0055` needs.
>
> **Pre-existing, unrelated warnings**: a clean build emits 4 `NU1903` + 4 `CS0618` and 10 `CA`
> findings. These are baseline. A task is judged on whether it *added* any, which is exactly why
> the comparison needs `--no-incremental`.

---

## Execution Plan

Phases run sequentially; tasks within a phase run in order.

### ✅ Phase 1: Domain foundation (7 tasks) — complete, 187 unit tests

Pure logic, no Docker. Everything downstream depends on these types existing.

```
T1 → T2 → T3 → T4 → T5 → T6 → T7
```

### Phase 2: Image normalization (6 tasks)

The fixture bank first — the normalizer's tests cannot be written before it exists.

```
T8 → T9 → T10 → T11 → T12 → T13
```

### Phase 3: Persistence (3 tasks)

```
T14 → T15 → T16
```

### Phase 4: Slices (5 tasks)

Resurrection comes after removal, because it needs a tombstone to resurrect.

```
T17 → T18 → T19 → T20 → T21
```

### Phase 5: Observability, hardening, P2 (5 tasks)

```
T22 → T23 → T24 → T25 → T26
```

---

## Task Breakdown

### ✅ T1: `ExternalRef` value object

**What**: The integrator's opaque key as a validated value object with case-sensitive equality.
**Where**: `src/HikvisionReplicator.Api/Domain/ExternalRef.cs`
**Depends on**: None
**Reuses**: `Domain/IpAddress.cs` (`Create` + `internal FromPersistence` + `GetEqualityComponents`)
**Requirement**: USR-02, A-15

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Create` rejects null, blank and whitespace-only, and > 255 characters, each with a `ValidationError` naming `externalRef`
- [x] Two refs differing only by letter case are **not** equal
- [x] `internal static FromPersistence` bypasses validation, per AD-009
- [x] Quick gate passes
- [x] ≥ 6 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `232e924`

**Commit**: `feat(domain): add ExternalRef value object`

---

### ✅ T2: `AccessCode` value object

**What**: A 4–20 **ASCII**-digit access code as a validated value object.
**Where**: `src/HikvisionReplicator.Api/Domain/AccessCode.cs`
**Depends on**: None
**Reuses**: `Domain/FaceCapacity.cs`; the pre-rewrite `AccessCode` in git history as a starting shape
**Requirement**: USR-04, spec Edge Cases

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Create` rejects null/blank, non-digits, < 4 and > 20 characters
- [x] **Arabic-Indic digits (e.g. `٤٥٦٧`) are rejected** — the implementation must not use `char.IsDigit`, which accepts them
- [x] `internal static FromPersistence` present
- [x] Quick gate passes
- [x] ≥ 7 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `99e4cbb`

**Commit**: `feat(domain): add AccessCode value object`

---

### ✅ T3: `FaceFingerprint` value object

**What**: The face image's content hash, byte size and pixel dimensions — the denormalized half of A-1.
**Where**: `src/HikvisionReplicator.Api/Domain/FaceFingerprint.cs`
**Depends on**: None
**Reuses**: `Domain/FaceCapacity.cs` value-object shape
**Requirement**: USR-22

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] Holds `ContentHash`, `ByteSize`, `Width`, `Height`; rejects a blank hash and non-positive size/dimensions
- [x] Equality is by all four components
- [x] `internal static FromPersistence` present
- [x] Quick gate passes
- [x] ≥ 5 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `d4b34f7`

**Commit**: `feat(domain): add FaceFingerprint value object`

---

### ✅ T4: `FacePicture` entity

**What**: The bytes-only entity inside the `User` aggregate — no repository, not an `IAggregateRoot`.
**Where**: `src/HikvisionReplicator.Api/Domain/FacePicture.cs`
**Depends on**: None
**Reuses**: private-EF-constructor pattern from `Domain/Device.cs`
**Requirement**: USR-10, USR-30

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `internal static ForUser(byte[])` and `internal void Replace(byte[])` exist; both reject empty content
- [x] Type does **not** implement `IAggregateRoot` — asserted by a test, since this is the only thing keeping it out of `IRepository<T>`
- [x] Private parameterless constructor for EF Core
- [x] Quick gate passes
- [x] ≥ 4 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `b3ed610`

**Commit**: `feat(domain): add FacePicture entity`

---

### ✅ T5: `User.Create`

**What**: The aggregate and its creation factory, receiving an already-normalized image.
**Where**: `src/HikvisionReplicator.Api/Domain/User.cs`
**Depends on**: T1, T2, T3, T4
**Reuses**: `Domain/Device.cs:42-73` factory shape
**Requirement**: USR-01, USR-03, USR-11

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `Create(externalRef, name, accessCode, fingerprint, pictureContent, now)` returns `OneOf<User, ValidationError>`
- [x] `name` rejected when null/blank/whitespace-only or > 100 characters; **trimmed before the length check** (spec Edge Cases)
- [x] Value-object failures propagate as their own `ValidationError` via `TryPickT1`
- [x] `CreatedAt` and `UpdatedAt` are both the supplied `now`; the aggregate never calls `DateTime.UtcNow` (AD-023)
- [x] A created user has `DeletedAt == null` and a non-null `Picture`
- [x] Quick gate passes
- [x] ≥ 9 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `c6d7b74`

**Commit**: `feat(domain): add User aggregate with creation factory`

---

### ✅ T6: `User.Update`

**What**: The validate-everything-then-apply mutator with the change guard.
**Where**: `src/HikvisionReplicator.Api/Domain/User.cs` (modify)
**Depends on**: T5
**Reuses**: `Domain/Device.cs:81-176` — the two-phase mutator this must copy exactly
**Requirement**: USR-24, USR-26, USR-27

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] A `null` fingerprint/content pair leaves the stored image, hash, size and dimensions untouched (USR-24)
- [x] **No field is assigned before every field is validated** — a rejected update leaves the aggregate byte-for-byte as it was (USR-27), asserted per field
- [x] `UpdatedAt` advances only when a value actually differs; an identical update leaves it unmoved (USR-26)
- [x] Quick gate passes
- [x] ≥ 10 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `7913c01`

**Commit**: `feat(domain): add User update with change guard`

---

### ✅ T7: `User.MarkDeleted` and `User.Restore`

**What**: The only two lifecycle transitions — tombstone (destroying the image) and resurrection.
**Where**: `src/HikvisionReplicator.Api/Domain/User.cs` (modify)
**Depends on**: T6
**Reuses**: T5's validation path, which `Restore` must re-run in full
**Requirement**: USR-29, USR-30, USR-34

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [x] `MarkDeleted(now)` sets `DeletedAt` **and** drops the picture, while `ExternalRef`, `Name` and `AccessCode` survive (USR-30)
- [x] `MarkDeleted` on an already-deleted user is a no-op that does not move `DeletedAt` (supports A-16)
- [x] `Restore(...)` clears `DeletedAt` and **re-imposes every create-time rule**, including a mandatory picture (USR-34)
- [x] `Restore` on an active user is rejected — resurrection is not an update path
- [x] Quick gate passes
- [x] ≥ 8 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Committed**: `1933fa1`

**Commit**: `feat(domain): add User tombstone and restore transitions`

---

### T8: Face-picture fixture bank

**What**: The committed photographic fixtures, their provenance record, and the build wiring that copies them into both test projects.
**Where**: `tests/assets/**`, `tests/assets/PROVENANCE.md`, both test `.csproj` files
**Depends on**: None
**Reuses**: nothing — new asset area
**Requirement**: enables USR-12…USR-22 (no AC of its own)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Eight permissively-licensed **non-face** photographs present per design § fixture bank: EXIF-rotated portrait (origin 6), ~4000×3000 large JPEG, 320×240 sub-floor thumbnail, PNG, grayscale, progressive JPEG, ICC-profiled, GPS-tagged
- [ ] Three generated fixtures present: decode bomb, near-uniform image, not-an-image
- [ ] `PROVENANCE.md` records **source URL and licence for every committed photograph** — a fixture with no provenance entry fails this task
- [ ] **No fixture contains a human face** — stated and checked, per design § fixture bank
- [ ] Both test projects copy the assets to output (`CopyToOutputDirectory`)
- [ ] A fixture-integrity unit test asserts every declared fixture resolves at runtime, is non-empty, and matches its declared format and dimensions — this is what catches a broken copy-to-output
- [ ] Quick gate passes
- [ ] ≥ 11 new unit tests pass (one per fixture) (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `test(assets): add face-picture fixture bank`

---

### T9: `FaceImageOptions` and its validator

**What**: A-13's envelope as startup-validated configuration rather than constants.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/FaceImageOptions.cs`
**Depends on**: None
**Reuses**: `Infrastructure/EncryptionOptions.cs` + `EncryptionOptionsValidator` — direct template
**Requirement**: A-13, bounds behind USR-15/USR-16/USR-19/USR-20

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Carries `MaxUploadBytes`, `MaxDecodePixels`, `MinByteSize`, `MaxByteSize`, `MinShortEdge`, `MinLongEdge`, `MaxShortEdge`, `MaxLongEdge`, `QualityLadder`, `DownscaleFactor` with the design's defaults
- [ ] Validator rejects an inverted band (`MinByteSize >= MaxByteSize`), an inverted edge range, an empty quality ladder, a ladder value outside 1–100, and a `DownscaleFactor` outside (0,1)
- [ ] Registered with `ValidateOnStart` so a bad bound aborts startup, not the first upload
- [ ] Quick gate passes
- [ ] ≥ 8 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(infra): add validated face image options`

---

### T10: `IFaceImageNormalizer` port and `NormalizedFaceImage`

**What**: The contract only — no behaviour.
**Where**: `src/HikvisionReplicator.Api/Shared/IFaceImageNormalizer.cs`
**Depends on**: None
**Reuses**: `Shared/IEncryptionService.cs` shape
**Requirement**: design § Components

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `OneOf<NormalizedFaceImage, ValidationError> Normalize(byte[] upload)` declared
- [ ] `NormalizedFaceImage(byte[] Content, string ContentHash, int Width, int Height)` declared
- [ ] **No `CancellationToken`** — CPU-bound with no I/O, so AD-007's token has nothing to cancel; the reason is stated in a doc comment so a future reader does not "fix" it
- [ ] Build gate passes with no new diagnostics (`--no-incremental`)

**Tests**: none (contract-only layer — matrix says build gate only) · **Gate**: build
**Commit**: `feat(shared): add face image normalizer port`

---

### T11: Normalizer — rejection guards

**What**: Everything that rejects before any pixel is decoded, plus the resolution floor.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/SkiaFaceImageNormalizer.cs`
**Depends on**: T8, T9, T10
**Reuses**: `Infrastructure/EncryptionService.cs` adapter shape
**Requirement**: USR-17, USR-19, USR-20, USR-21

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `SkiaSharp` + `SkiaSharp.NativeAssets.Linux` referenced; build succeeds on Linux
- [ ] Upload over `MaxUploadBytes` rejected **without constructing a codec** (USR-19)
- [ ] `SKCodec.Create` returning null → `ValidationError` naming `facePicture` (USR-21)
- [ ] Declared `Width * Height` over `MaxDecodePixels` rejected **before any decode allocation** — verified with the decode-bomb fixture (USR-20)
- [ ] Sub-floor image rejected with a message stating the minimum, and **never upscaled** (USR-17)
- [ ] **The floor is checked against orientation-corrected dimensions** — a portrait fixture with EXIF origin 6 must not be judged as landscape
- [ ] Quick gate passes
- [ ] ≥ 9 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(infra): add face image normalizer rejection guards`

---

### T12: Normalizer — decode, orient, resize

**What**: Decode to sRGB, apply EXIF rotation manually, downscale to the ceiling without cropping.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/SkiaFaceImageNormalizer.cs` (modify)
**Depends on**: T11
**Reuses**: T11's codec inspection
**Requirement**: USR-12, USR-13, USR-16, USR-18

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Non-JPEG input (PNG fixture) produces a JPEG derivative (USR-12)
- [ ] EXIF-rotated fixture comes out **upright** — SkiaSharp has no auto-orient, so every `SKEncodedOrigin` case is handled explicitly (USR-13)
- [ ] Output is sRGB; the grayscale and ICC-profiled fixtures both normalize (spec Edge Cases)
- [ ] Above-ceiling input is downscaled within `MaxShortEdge`/`MaxLongEdge` (USR-16)
- [ ] **Aspect ratio preserved and nothing cropped** — asserted by comparing input and output ratios within a pixel-rounding tolerance (USR-18)
- [ ] Quick gate passes
- [ ] ≥ 10 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(infra): add face image decode, orientation and resize`

---

### T13: Normalizer — encode ladder, band and hash

**What**: The deterministic quality ladder that lands inside 40–200 KB, plus the content hash and golden-hash tests.
**Where**: `src/HikvisionReplicator.Api/Infrastructure/SkiaFaceImageNormalizer.cs` (modify)
**Depends on**: T12
**Reuses**: T9's ladder configuration
**Requirement**: USR-14, USR-15, USR-22 (and the determinism USR-26 depends on)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Every photographic fixture normalizes to **≥ `MinByteSize` and ≤ `MaxByteSize`** — the lower bound asserted explicitly, not just the upper (USR-15)
- [ ] The large fixture exercises the still-too-big-at-lowest-quality downscale branch, and the branch is asserted to have been taken
- [ ] The near-uniform fixture is **rejected** rather than stored below the band
- [ ] Derivative carries no EXIF, in particular no GPS — asserted against the GPS-tagged fixture (USR-14)
- [ ] SHA-256 content hash returned and matches the derivative bytes (USR-22)
- [ ] **Determinism**: normalizing the same fixture twice yields byte-identical output and an identical hash
- [ ] **Golden hashes**: each photographic fixture's expected derivative hash is recorded and asserted, with a comment in the test file stating that a SkiaSharp upgrade will change these and the response is to review and re-record, **never to loosen the assertion**
- [ ] Quick gate passes
- [ ] ≥ 12 new unit tests pass (no silent deletions)

**Tests**: unit · **Gate**: quick
**Commit**: `feat(infra): add deterministic face image encode ladder`

---

### T14: EF configuration, migration and schema

**What**: Both entity configurations, the `DbSet`, the migration, and the two deliberately asymmetric indexes.
**Where**: `Infrastructure/UserConfiguration.cs`, `Infrastructure/FacePictureConfiguration.cs`, `Infrastructure/AppDbContext.cs` (modify), `Infrastructure/Migrations/*`
**Depends on**: T5, T6, T7
**Reuses**: `Infrastructure/DeviceConfiguration.cs` — value converters and the named-index constant convention
**Requirement**: USR-38, and the index behaviour behind USR-06/USR-32

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Value converters for `ExternalRef`, `AccessCode` and `FaceFingerprint` per AD-009
- [ ] `IX_users_ExternalRef` unique across **all** rows, tombstoned included
- [ ] `IX_users_AccessCode` unique with `.HasFilter(...)` on `"DeletedAt" IS NULL`
- [ ] Both index names exposed as `public const string` so the repository can key off them
- [ ] `face_pictures` 1:1 with `users`, cascade delete; the navigation is **not auto-included**, with the reason stated in a comment at the configuration
- [ ] Migration created and applied at startup by the existing `Migrate()` call; `EnsureCreated()` is not used anywhere (USR-38)
- [ ] Integration tests assert against the real schema: both indexes exist, the partial filter is present, and **a deleted user's access code can be reused by a new user while its `ExternalRef` cannot**
- [ ] Full gate passes
- [ ] ≥ 7 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(infra): add user schema with asymmetric unique indexes`

---

### T15: `IUserRepository` and `UserRepository`

**What**: The repository with two-constraint violation translation.
**Where**: `Shared/IUserRepository.cs`, `Infrastructure/UserRepository.cs`, `Program.cs` (modify)
**Depends on**: T14
**Reuses**: `Infrastructure/DeviceRepository.cs:44-51` verbatim in shape
**Requirement**: USR-06, USR-07, USR-08

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `AddIfKeysFreeAsync` / `SaveIfKeysFreeAsync` return `OneOf<Success, ConflictError>`
- [ ] A `23505` on **each** named index maps to its own distinct message, so a caller can tell which key collided
- [ ] A constraint violation on any *other* index propagates untouched rather than being reported as one of these two
- [ ] Services never see a `PostgresException` (AD-022)
- [ ] Integration tests provoke **both** real races concurrently: one `ExternalRef` → exactly one user (USR-07); one `accessCode` → exactly one success (USR-08); each asserts 409 and **not** 500
- [ ] Full gate passes
- [ ] ≥ 6 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(infra): add user repository with constraint translation`

---

### T16: Domain specifications

**What**: The query shapes, including the two that differ only in whether tombstones are visible.
**Where**: `src/HikvisionReplicator.Api/Domain/Specs/*.cs`
**Depends on**: T14
**Reuses**: `Domain/Specs/DeviceByAddressSpec.cs`
**Requirement**: USR-31, USR-35, USR-36, and A-1's no-bytes-loaded guarantee

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `UserByExternalRefSpec` (**active only**) and `UserByExternalRefIncludingDeletedSpec` (resurrection's lookup) both exist, with a comment on each stating why the other is not interchangeable
- [ ] `ActiveUserByAccessCodeSpec` for the friendly pre-check
- [ ] `ActiveUsersPagedSpec` with stable, total ordering (USR-44)
- [ ] **No specification includes the `Picture` navigation** — an integration test asserts that querying users issues no read against `face_pictures`, which is the only thing enforcing A-1
- [ ] Full gate passes
- [ ] ≥ 6 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(domain): add user specifications`

---

### T17: `UpsertUser` slice — create path

**What**: The PUT route, its DI wiring, and the create half of the upsert.
**Where**: `Features/Users/UpsertUser/` (3 files), `Program.cs` (modify)
**Depends on**: T13, T15, T16
**Reuses**: `Features/Devices/RegisterDevice/` — all three files as the template
**Requirement**: USR-01, USR-03, USR-05, USR-09, USR-10, USR-11

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] `PUT /api/users/{externalRef}` returns **201 with a `Location`** header for a new user (USR-01)
- [ ] Service returns `OneOf<UserCreated, UserUpdated, ValidationError, ConflictError>`; the endpoint's `.Match()` maps arms to results with **no `if` in the endpoint** (AD-003)
- [ ] A create with no face picture is rejected, naming `facePicture` (USR-05)
- [ ] Response carries hash, byte size and dimensions and **never the image bytes** (USR-09)
- [ ] User and picture are written in **one transaction** — a forced picture-write failure leaves no user row (USR-10)
- [ ] `now` comes from the injected `TimeProvider` (USR-11)
- [ ] Full gate passes
- [ ] ≥ 10 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add user upsert create path`

---

### T18: `UpsertUser` slice — update path

**What**: The update half — omitted-face semantics, the change guard, and the access-code conflict.
**Where**: `Features/Users/UpsertUser/UpsertUserService.cs` (modify)
**Depends on**: T17
**Reuses**: `Features/Devices/UpdateDevice/UpdateDeviceService.cs` partial-update semantics
**Requirement**: USR-23, USR-24, USR-25, USR-26, USR-27, USR-28

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] An existing `ExternalRef` returns **200**, not 201 (USR-23)
- [ ] Omitting the face leaves the stored image, hash, size and dimensions unchanged (USR-24)
- [ ] Supplying a face re-normalizes and replaces all four (USR-25)
- [ ] Re-sending a byte-identical upload leaves `UpdatedAt` unmoved — the end-to-end proof of the normalizer's determinism (USR-26)
- [ ] A rejected update leaves the stored user, image included, exactly as it was (USR-27)
- [ ] Taking another active user's access code returns 409 (USR-28)
- [ ] Full gate passes
- [ ] ≥ 9 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add user upsert update path`

---

### T19: `GetUser` slice

**What**: Lookup by the integrator's key.
**Where**: `Features/Users/GetUser/` (3 files), `Program.cs` (modify)
**Depends on**: T16
**Reuses**: `Features/Devices/GetDevice/`
**Requirement**: USR-35, USR-36, USR-37

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] An active user returns 200 with identity fields, timestamps and the face fingerprint (USR-35)
- [ ] An unregistered ref returns 404 (USR-36)
- [ ] Response never contains image bytes (USR-37)
- [ ] Full gate passes
- [ ] ≥ 5 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add get user endpoint`

---

### T20: `RemoveUser` slice

**What**: The tombstone, the biometric destruction, and idempotent deletion.
**Where**: `Features/Users/RemoveUser/` (3 files), `Program.cs` (modify)
**Depends on**: T19
**Reuses**: `Features/Devices/RemoveDevice/`
**Requirement**: USR-29, USR-30, USR-31, USR-32, USR-33

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Deleting an active user returns 204 and **the row survives** with `DeletedAt` set (USR-29)
- [ ] **The `face_pictures` row is gone** — asserted by querying the table directly, not by asserting the API hides it (USR-30)
- [ ] A deleted user is 404 on `GET` and absent from listing (USR-31)
- [ ] Deleting an already-deleted user returns **204, not 404** (USR-32, A-16)
- [ ] Deleting a never-registered ref returns 404 (USR-33)
- [ ] Full gate passes
- [ ] ≥ 7 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add user removal with tombstone`

---

### T21: `UpsertUser` slice — resurrection path

**What**: A-7 — a `PUT` on a tombstoned ref brings the user back under full create rules.
**Where**: `Features/Users/UpsertUser/UpsertUserService.cs` (modify)
**Depends on**: T18, T20
**Reuses**: T7's `User.Restore`, T16's including-deleted specification
**Requirement**: USR-34

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] A `PUT` on a tombstoned ref clears the tombstone and rewrites the record (USR-34)
- [ ] **A face picture is mandatory on resurrection** — the tombstone destroyed the old one, so an omitted picture is rejected exactly as at creation
- [ ] Resurrection uses the including-deleted specification; a lookup that only sees active users would 404 instead and is asserted not to
- [ ] The resurrected user's access code is re-checked against active users
- [ ] Full gate passes
- [ ] ≥ 5 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add user resurrection path`

---

### T22: Normalization observability

**What**: The child span and metrics for the one CPU-bound step on the latency path.
**Where**: `Infrastructure/SkiaFaceImageNormalizer.cs` (modify), `Program.cs` (modify)
**Depends on**: T17
**Reuses**: the existing OpenTelemetry block in `Program.cs`; `IntegrationTests/TracingTests.cs` as the assertion pattern
**Requirement**: USR-40, USR-41

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] A dedicated `ActivitySource` emits a **child** span for normalization, registered with the tracer provider (USR-40)
- [ ] Metrics record normalization duration and resulting byte size (USR-41)
- [ ] **The span assertion sends a `traceparent` and filters by that trace id** — the OTel listener is process-wide, so an unfiltered assertion passes on scheduling luck (`docs/test-patterns.md`)
- [ ] Full gate passes
- [ ] ≥ 4 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add normalization tracing and metrics`

---

### T23: Request size limit and database-failure path

**What**: Stop an oversized body being buffered before the normalizer can reject it; confirm the 503 path.
**Where**: `Features/Users/UpsertUser/UpsertUserService.Endpoint.cs` (modify)
**Depends on**: T17
**Reuses**: `Infrastructure/GlobalExceptionHandler.cs` (already written — this task adds only its test)
**Requirement**: USR-19 at the transport layer, USR-39

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] An explicit request-size limit on the upsert route rejects an oversized body **before** it is buffered into memory
- [ ] The limit accounts for base64 inflation — an 8 MB image is ~10.7 MB on the wire (A-9), so a limit of exactly 8 MB would reject valid uploads
- [ ] With the database unavailable, user routes return a ProblemDetails 503 leaking no connection string or stack trace (USR-39)
- [ ] Full gate passes
- [ ] ≥ 4 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): bound upsert request size`

---

### T24: `ExternalRef` reserved-character round-trip

**What**: Settle design § Risks — whether A-15's "any non-blank string" survives a single-segment route.
**Where**: `IntegrationTests/UserEndpointsTests.cs` (modify); `spec.md` only if the assumption must change
**Depends on**: T21
**Reuses**: nothing
**Requirement**: A-15, spec Edge Cases

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Refs containing `%`, spaces, `+`, `#` and non-ASCII characters round-trip through `PUT`, `GET` and `DELETE`
- [ ] A ref containing `/` is tested explicitly
- [ ] **If `/` cannot round-trip, A-15 is amended in `spec.md` to exclude it, with the reason recorded — the code is not quietly bent to hide it.** Either outcome completes this task; a silent workaround does not
- [ ] Full gate passes
- [ ] ≥ 6 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `test(users): cover reserved characters in external refs`

---

### T25: `ListUsers` paged slice (P2)

**What**: The paged catalogue, shipped paged from day one.
**Where**: `Features/Users/ListUsers/` (3 files), `Program.cs` (modify)
**Depends on**: T16
**Reuses**: `Features/Devices/ListDevices/` — **shape only.** That slice returns a bare array (DEV-26, a recorded known gap); this one must not repeat it
**Requirement**: USR-42, USR-43, USR-44, USR-45

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] Returns a **paged** response with the page's items and the means to request the next page (USR-42)
- [ ] An over-maximum page size is **clamped, not honoured** (USR-43)
- [ ] Ordering is stable and total — 3 users at page size 2 yield exactly those 3 across both pages, no duplicates, no omissions (USR-44)
- [ ] Deleted users excluded; an empty registry returns an empty page, not an error (USR-45)
- [ ] Response items carry no image bytes
- [ ] Full gate passes
- [ ] ≥ 8 new integration tests pass (no silent deletions)

**Tests**: integration · **Gate**: full
**Commit**: `feat(users): add paged user listing`

---

### T26: E2E route confirmation

**What**: A thin out-of-process pass over each user route.
**Where**: `src/HikvisionReplicator.E2E/UserEndpointsTests.cs`
**Depends on**: T25
**Reuses**: `src/HikvisionReplicator.E2E/DeviceEndpointsTests.cs`
**Requirement**: AD-024 (E2E is confirmation, not coverage)

**Tools**: MCP: NONE · Skill: NONE

**Done when**:
- [ ] One happy path and one error path for each of `PUT`, `GET`, `DELETE`, `GET` (list)
- [ ] Uses Playwright's `IAPIRequestContext`; **no browser download required** (`docs/test-patterns.md`)
- [ ] Suite is **not** added to CI — it needs a live API, matching how the device E2E suite is treated
- [ ] Full gate passes; E2E suite passes against a locally running API
- [ ] ≥ 8 new E2E tests pass (no silent deletions)

**Tests**: e2e · **Gate**: full
**Commit**: `test(e2e): add user endpoint confirmation suite`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5

Phase 1:  T1 → T2 → T3 → T4 → T5 → T6 → T7
Phase 2:  T8 → T9 → T10 → T11 → T12 → T13
Phase 3:  T14 → T15 → T16
Phase 4:  T17 → T18 → T19 → T20 → T21
Phase 5:  T22 → T23 → T24 → T25 → T26
```

Execution is strictly sequential — no intra-phase parallelism.

**Cross-phase dependency edges** (dependencies point backward only):

```
T5  ← T1, T2, T3, T4      (same phase)
T11 ← T8, T9, T10         (same phase)
T14 ← T5, T6, T7          (Phase 1 → Phase 3)
T17 ← T13, T15, T16       (Phase 2, 3 → Phase 4)
T21 ← T18, T20            (same phase)
T22 ← T17                 (Phase 4 → Phase 5)
T23 ← T17                 (Phase 4 → Phase 5)
T24 ← T21                 (Phase 4 → Phase 5)
T25 ← T16                 (Phase 3 → Phase 5)
T26 ← T25                 (same phase)
```

**Batch packing** (~7 tasks per worker, whole phases only):

| Batch | Phases | Tasks | Count |
| ----- | ------ | ----- | ----- |
| 1 | Phase 1 | T1–T7 | 7 |
| 2 | Phase 2 | T8–T13 | 6 |
| 3 | Phase 3 + Phase 4 | T14–T21 | 8 |
| 4 | Phase 5 | T22–T26 | 5 |

26 tasks → **4 sequential batches**.

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1 `ExternalRef` | 1 value object | ✅ Granular |
| T2 `AccessCode` | 1 value object | ✅ Granular |
| T3 `FaceFingerprint` | 1 value object | ✅ Granular |
| T4 `FacePicture` | 1 entity | ✅ Granular |
| T5 `User.Create` | 1 factory in 1 file | ✅ Granular |
| T6 `User.Update` | 1 method | ✅ Granular |
| T7 `MarkDeleted` + `Restore` | 2 cohesive methods, same file, same transition concern | ⚠️ OK — cohesive |
| T8 Fixture bank | assets + build wiring, one concern | ⚠️ OK — cohesive |
| T9 `FaceImageOptions` | 1 options class + validator (mirrors `EncryptionOptions`) | ⚠️ OK — cohesive |
| T10 Normalizer port | 1 interface + 1 record | ✅ Granular |
| T11 Normalizer guards | 1 class, rejection paths only | ✅ Granular |
| T12 Normalizer decode/orient/resize | same class, one pipeline stage | ✅ Granular |
| T13 Normalizer encode/hash | same class, one pipeline stage | ✅ Granular |
| T14 Configs + migration | 2 configs + 1 migration = one schema | ⚠️ OK — cohesive; splitting yields an untestable config task |
| T15 Repository | 1 interface + 1 class | ✅ Granular |
| T16 Specifications | 4 small spec classes, one query concern | ⚠️ OK — cohesive |
| T17 Upsert create | 1 slice (3 files) | ✅ Granular |
| T18 Upsert update | 1 method path | ✅ Granular |
| T19 `GetUser` | 1 slice | ✅ Granular |
| T20 `RemoveUser` | 1 slice | ✅ Granular |
| T21 Upsert resurrect | 1 method path | ✅ Granular |
| T22 Observability | 1 concern across 2 files | ✅ Granular |
| T23 Size limit + 503 | 1 route attribute + its tests | ✅ Granular |
| T24 Reserved chars | 1 test concern | ✅ Granular |
| T25 `ListUsers` | 1 slice | ✅ Granular |
| T26 E2E | 1 test class | ✅ Granular |

No ❌ — nothing requires splitting.

---

## Diagram-Definition Cross-Check

| Task | Depends On (body) | Diagram Shows | Status |
| ---- | ----------------- | ------------- | ------ |
| T1 | None | phase start | ✅ Match |
| T2 | None | phase chain T1→T2 (order, not dependency) | ✅ Match |
| T3 | None | phase chain | ✅ Match |
| T4 | None | phase chain | ✅ Match |
| T5 | T1, T2, T3, T4 | `T5 ← T1,T2,T3,T4` | ✅ Match |
| T6 | T5 | `T5 → T6` | ✅ Match |
| T7 | T6 | `T6 → T7` | ✅ Match |
| T8 | None | phase start | ✅ Match |
| T9 | None | phase chain | ✅ Match |
| T10 | None | phase chain | ✅ Match |
| T11 | T8, T9, T10 | `T11 ← T8,T9,T10` | ✅ Match |
| T12 | T11 | `T11 → T12` | ✅ Match |
| T13 | T12 | `T12 → T13` | ✅ Match |
| T14 | T5, T6, T7 | `T14 ← T5,T6,T7` | ✅ Match |
| T15 | T14 | `T14 → T15` | ✅ Match |
| T16 | T14 | `T14 → T16` (via chain) | ✅ Match |
| T17 | T13, T15, T16 | `T17 ← T13,T15,T16` | ✅ Match |
| T18 | T17 | `T17 → T18` | ✅ Match |
| T19 | T16 | `T19 ← T16` (Phase 3, backward) | ✅ Match |
| T20 | T19 | `T19 → T20` | ✅ Match |
| T21 | T18, T20 | `T21 ← T18,T20` | ✅ Match |
| T22 | T17 | `T22 ← T17` | ✅ Match |
| T23 | T17 | `T23 ← T17` | ✅ Match |
| T24 | T21 | `T24 ← T21` | ✅ Match |
| T25 | T16 | `T25 ← T16` | ✅ Match |
| T26 | T25 | `T25 → T26` | ✅ Match |

**No task depends on a later phase.** Every dependency points backward or within its own phase.

---

## Test Co-location Validation

| Task | Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | ---------------------- | --------------- | --------- | ------ |
| T1 | Domain value object | unit | unit | ✅ OK |
| T2 | Domain value object | unit | unit | ✅ OK |
| T3 | Domain value object | unit | unit | ✅ OK |
| T4 | Domain entity | unit | unit | ✅ OK |
| T5 | Domain aggregate | unit | unit | ✅ OK |
| T6 | Domain aggregate | unit | unit | ✅ OK |
| T7 | Domain aggregate | unit | unit | ✅ OK |
| T8 | Test assets + build config | none (config) — **raised to unit** by the fixture-integrity test | unit | ✅ OK (exceeds) |
| T9 | Infrastructure pure logic | unit | unit | ✅ OK |
| T10 | Port / contract, no behaviour | none | none | ✅ OK |
| T11 | Infrastructure pure logic | unit | unit | ✅ OK |
| T12 | Infrastructure pure logic | unit | unit | ✅ OK |
| T13 | Infrastructure pure logic | unit | unit | ✅ OK |
| T14 | EF config + migration + schema | integration | integration | ✅ OK |
| T15 | Repository | integration | integration | ✅ OK |
| T16 | Specifications | integration | integration | ✅ OK |
| T17 | Feature slice + route | integration | integration | ✅ OK |
| T18 | Feature slice | integration | integration | ✅ OK |
| T19 | Feature slice + route | integration | integration | ✅ OK |
| T20 | Feature slice + route | integration | integration | ✅ OK |
| T21 | Feature slice | integration | integration | ✅ OK |
| T22 | Cross-cutting (tracing/metrics) | integration | integration | ✅ OK |
| T23 | Route config + startup handler | integration | integration | ✅ OK |
| T24 | Feature slice (routing behaviour) | integration | integration | ✅ OK |
| T25 | Feature slice + route | integration | integration | ✅ OK |
| T26 | HTTP surface out of process | e2e | e2e | ✅ OK |

**No ❌ VIOLATION.** T10 is the only `Tests: none`, and it is valid: the matrix assigns `none` to
contract-only layers, and the file contains an interface and a record with no behaviour to assert.
No task defers its tests to a later task.

---

## Requirement Coverage

All 45 acceptance criteria map to at least one task.

| Story | Criteria | Tasks |
| ----- | -------- | ----- |
| P1 Register | USR-01…USR-11 | T1–T5, T15, T17 |
| P1 Normalize | USR-12…USR-22 | T3, T8–T13 |
| P1 Amend | USR-23…USR-28 | T6, T18 |
| P1 Remove | USR-29…USR-34 | T7, T20, T21 |
| P1 Look up | USR-35…USR-37 | T16, T19 |
| P1 Foundation | USR-38…USR-41 | T14, T22, T23 |
| P2 Browse | USR-42…USR-45 | T16, T25 |

**Coverage:** 45 total, 45 mapped, 0 unmapped.

**Expected test deltas** — targets, not ceilings:
unit 81 → ~180 · integration 88 → ~160 · E2E 9 → ~17.

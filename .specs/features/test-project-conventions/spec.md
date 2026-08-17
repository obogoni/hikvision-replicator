# Test Project Naming Conventions Specification

## Problem Statement

AD-024 split test coverage into three levels — unit, integration, e2e — but only two
projects exist to hold them. Unit and integration tests share
`src/HikvisionReplicator.Tests`, separated only by a `Domain/` folder and a
`[Trait("Category", "Unit")]` attribute that every new unit test must remember to carry.
The level a test belongs to is therefore a convention inside one assembly rather than a
structural fact, and the Docker-free gate depends on an attribute that silently drops a
test from the run when omitted. The third project, `HikvisionReplicator.E2ETests`, uses a
suffix that does not match the level name it holds.

## Goals

- [ ] One project per test level, named by the suffix convention: `.Tests` (unit),
      `.IntegrationTests` (integration), `.E2E` (end-to-end)
- [ ] The Docker-free gate is a whole project, not a trait filter — `dotnet test
      src/HikvisionReplicator.Tests` with no `--filter`
- [ ] Test counts are preserved exactly: 169 in-process (81 unit / 88 integration) + 9 e2e
- [ ] Every document that names a test project or gate command is updated in the same
      change, and the convention is recorded as a decision (AD-026)

## Out of Scope

| Feature | Reason |
| --- | --- |
| Changing any test's assertions, name, or behaviour | This is a relocation, not a rewrite — the gate must prove the same tests still pass |
| Renaming test *classes* (`DeviceEndpointsTests` exists in two projects) | Assembly and namespace already disambiguate; confirmed with the user |
| Extracting a shared test-infrastructure project | `PostgresFixture` and `TestWebApplicationFactory` are used only by integration tests; a shared project would have one consumer |
| Adding CI workflow files | No `.github/workflows/` exists yet; gate commands are documented, not automated |
| Revisiting AD-024's layer rules | The *level* definitions stand unchanged; only where each level lives changes |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --- | --- | --- | --- |
| `[Trait("Category", "Unit")]` after the split | Removed from all four unit classes | The project boundary is now the signal; a per-class attribute is redundant and can drift | y |
| `EncryptionServiceTests` placement | Moves to `Tests/Infrastructure/` | It tests `Api/Infrastructure/EncryptionService`; the unit project mirrors the Api tree | y |
| Test class names | Unchanged | Project name carries the level; `docs/test-patterns.md` already states e2e classes follow the same convention | y |
| E2E directory name | `src/HikvisionReplicator.E2E/` — directory, csproj, and root namespace all renamed together | A csproj whose name differs from its directory is a trap for `dotnet test <path>` | n — agent default |
| Integration project namespace | `HikvisionReplicator.IntegrationTests` | Namespace tracks assembly name, consistent with the other two projects | n — agent default |
| Shared package versions across the split projects | Integration project keeps every package the current `.Tests` project pins; the unit project keeps only what pure-logic tests need | Preserves the "kept in step with the Api" comments where they matter and drops Testcontainers/Respawn/Mvc.Testing from the Docker-free gate | n — agent default |
| Historical `.specs/features/device-registry/` documents | Left untouched | They are a record of what was true at the time; rewriting them would falsify the audit trail |  n — agent default |

**Open questions:** none — all resolved or logged above.

---

## User Stories

### P1: One project per test level ⭐ MVP

**User Story**: As a developer, I want each test level in its own project named by its
suffix, so that the level a test belongs to is a structural fact rather than a folder
convention plus an attribute I have to remember.

**Why P1**: Every other requirement — the gate commands, the docs, the decision record —
describes this structure. Without it there is nothing to document.

**Acceptance Criteria**:

1. WHEN the solution is built THEN it SHALL contain exactly three test projects:
   `HikvisionReplicator.Tests`, `HikvisionReplicator.IntegrationTests`, and
   `HikvisionReplicator.E2E`, each registered in `HikvisionReplicator.slnx`.
2. WHEN `HikvisionReplicator.Tests` is inspected THEN it SHALL contain only the four
   pure-logic classes — `DeviceCreateTests`, `DeviceUpdateTests`, `ValueObjectTests` under
   `Domain/`, and `EncryptionServiceTests` under `Infrastructure/` — and SHALL NOT
   reference Testcontainers, Respawn, or `Microsoft.AspNetCore.Mvc.Testing`.
3. WHEN `HikvisionReplicator.IntegrationTests` is inspected THEN it SHALL contain the six
   I/O-bound classes — `DeviceEndpointsTests`, `DeviceRepositoryTests`,
   `CredentialLeakageTests`, `ErrorHandlingTests`, `HarnessTests`, `StartupTests`,
   `TracingTests` — together with `PostgresFixture` and `TestWebApplicationFactory`.
4. WHEN the e2e project is inspected THEN its directory, csproj filename, and root
   namespace SHALL all read `HikvisionReplicator.E2E`, and no path or namespace named
   `E2ETests` SHALL remain in the solution.
5. WHEN any test source file is compared against its pre-change content THEN the only
   permitted differences SHALL be the namespace declaration, `using` directives, and the
   removal of `[Trait("Category", "Unit")]` — no assertion, test name, or test body changes.

**Independent Test**: `dotnet build HikvisionReplicator.slnx` succeeds and `ls src/`
shows exactly the four expected project directories.

---

### P2: Gate commands run whole projects ⭐ MVP

**User Story**: As a developer, I want the Docker-free gate to be a whole project rather
than a trait filter, so that a newly added unit test cannot silently fall out of the fast
feedback loop.

**Why P2**: The trait removal in P1 is only safe once the gate stops depending on it;
these two ship together but are separately verifiable.

**Acceptance Criteria**:

1. WHEN `dotnet test src/HikvisionReplicator.Tests` runs with **no Docker daemon
   available** THEN it SHALL pass, reporting 81 tests.
2. WHEN the full gate runs against a Docker daemon THEN
   `HikvisionReplicator.IntegrationTests` SHALL pass, reporting 88 tests.
3. WHEN `dotnet test src/HikvisionReplicator.E2E` runs against a live API THEN it SHALL
   pass, reporting 9 tests.
4. WHEN any gate command is read anywhere in the repository THEN it SHALL NOT contain
   `--filter "Category=Unit"`.

**Independent Test**: Stop Docker, run the unit gate, observe 81 passing tests and no
container startup.

---

### P3: Documentation and decision record follow the structure

**User Story**: As a developer or agent picking up this repo, I want every document that
names a test project to name the right one, so that CLAUDE.md and `docs/test-patterns.md`
stay usable as instructions rather than as stale history.

**Why P3**: Nice-to-have for the code to run, mandatory for the convention to hold — but
it is verified by inspection, not by the test gate.

**Acceptance Criteria**:

1. WHEN `CLAUDE.md`, `README.md`, `docs/test-patterns.md`, `.specs/ARCHITECTURE.md`, and
   `.github/pull_request_template.md` are searched THEN no occurrence of `E2ETests` or
   `Category=Unit` SHALL remain, and each stated project path SHALL resolve to a real
   directory.
2. WHEN `.specs/STATE.md` is read THEN it SHALL contain a new **AD-026** recording the
   three-project convention, with AD-024's entry marked as amended by it — AD-024's layer
   definitions remaining active.

**Independent Test**: `grep -rn "E2ETests\|Category=Unit" --include="*.md" .` returns only
matches inside `.specs/features/device-registry/` (historical record, out of scope).

---

## Edge Cases

- WHEN a test file moves between projects THEN its `namespace` SHALL be rewritten to match
  the new assembly, and any consumer of a moved type SHALL be updated in the same task —
  the build is the check.
- WHEN the unit project no longer references `Testcontainers.PostgreSql` THEN a leftover
  `using Testcontainers…` in a moved file SHALL fail the build rather than resolve
  transitively.
- WHEN `dotnet test` is pointed at the solution file THEN all three test projects SHALL be
  discovered, so the full run still requires Docker — this is unchanged behaviour and is
  documented, not prevented.
- WHEN this branch is rebased onto `main` after `feat/device-registry` squash-merges THEN
  the file moves SHALL replay as renames; the branch is stacked (see Handoff).

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| TPC-01 | P1: One project per test level | Execute | ✅ Verified |
| TPC-02 | P1: Unit project holds only pure-logic classes, no I/O packages | Execute | ✅ Verified |
| TPC-03 | P1: Integration project holds the I/O classes and fixtures | Execute | ✅ Verified |
| TPC-04 | P1: E2E directory, csproj, and namespace all read `.E2E` | Execute | ✅ Verified |
| TPC-05 | P1: No test content changes beyond namespace, usings, trait removal | Execute | ✅ Verified |
| TPC-06 | P2: Docker-free gate is the whole unit project, 81 tests | Execute | ✅ Verified |
| TPC-07 | P2: Integration gate passes, 88 tests | Execute | ✅ Verified |
| TPC-08 | P2: E2E gate passes, 9 tests | Execute | ✅ Verified |
| TPC-09 | P2: No `--filter "Category=Unit"` anywhere | Execute | ✅ Verified |
| TPC-10 | P3: All docs name real projects, no `E2ETests` references | Execute | ✅ Verified |
| TPC-11 | P3: AD-026 recorded, AD-024 marked amended | Execute | ✅ Verified |

**Coverage:** 11 total, 11 mapped to tasks, 0 unmapped

Evidence per requirement is in [`validation.md`](validation.md). Note that these are
structural requirements — the proof is a build result, a file listing, or a diff, not a
test assertion.

---

## Success Criteria

- [ ] `dotnet build HikvisionReplicator.slnx` succeeds with zero warnings introduced
- [ ] 81 + 88 + 9 tests pass — identical counts to before the change
- [ ] The Docker-free gate runs with the Docker daemon stopped
- [ ] `grep -rn "E2ETests\|Category=Unit"` finds nothing outside the historical
      `device-registry` spec folder

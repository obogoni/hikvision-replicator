# Test Project Naming Conventions Validation

**Date**: 2026-08-17
**Spec**: `.specs/features/test-project-conventions/spec.md`
**Diff range**: `314f616..b22172b` (branch `refactor/test-project-layout`, 6 commits, plus
`866683c` adding this report). These are **pre-squash references** on that branch and will
not resolve on `main` after merge (AD-025). Commit *bodies* on the branch cite the
pre-rebase SHAs these were cherry-picked from — the content is identical.
**Verifier**: standalone fresh-eyes pass — sub-agent dispatch was unavailable in this
session, so the independent checks in `validate.md` (spec-anchored evidence, discrimination
sensor, code-quality review) were run directly. **Author ≠ verifier is therefore not
satisfied**; this report is a self-check and should be read as one.

---

## Task Completion

| Task | Commit | Status | Notes |
| --- | --- | --- | --- |
| Baseline the gate | — | ✅ Done | 81 / 88 / 169 confirmed by run, not by handoff |
| Fix tracing isolation | `267ab4a` | ✅ Done | Unplanned — see Deviations |
| Create IntegrationTests | `0d033af` | ✅ Done | 9 files moved as git renames |
| Trim Tests to unit-only | `ecc8371` | ✅ Done | EF Core pins retained, see Deviations |
| Rename E2ETests → E2E | `e1dad50` | ✅ Done | Directory, csproj, namespace together |
| Update developer docs | `183fdf7` | ✅ Done | CLAUDE, README, test-patterns, PR template |
| Record AD-026 | `b22172b` | ✅ Done | AD-024 marked amended; ARCHITECTURE.md refreshed |

---

## Spec-Anchored Acceptance Criteria

Most ACs here are **structural** — the evidence is a build result, a file listing, or a
diff, not a test assertion. That is appropriate for a conventions change, but it means the
test suite is not what proves them; the cited commands are.

| Criterion | Spec-defined outcome | Evidence | Result |
| --- | --- | --- | --- |
| TPC-01 three test projects, all in slnx | exactly `.Tests`, `.IntegrationTests`, `.E2E` | `HikvisionReplicator.slnx:3-5`; `ls -d src/*/` → 4 dirs (Api + 3) | ✅ PASS |
| TPC-02 unit project holds only the 4 pure-logic classes | `Domain/{DeviceCreate,DeviceUpdate,ValueObject}Tests.cs`, `Infrastructure/EncryptionServiceTests.cs` | `find src/HikvisionReplicator.Tests -name '*.cs'` → exactly those 4 | ✅ PASS |
| TPC-02 unit project references no Testcontainers / Respawn / Mvc.Testing | absent from csproj | `grep -E "Testcontainers\|Respawn\|Mvc.Testing" src/HikvisionReplicator.Tests/HikvisionReplicator.Tests.csproj` → no match | ✅ PASS |
| TPC-03 integration project holds the 7 I/O classes + 2 fixtures | 9 files | `find src/HikvisionReplicator.IntegrationTests -name '*.cs'` → exactly those 9 | ✅ PASS |
| TPC-04 e2e dir, csproj, namespace all read `.E2E` | no `E2ETests` path or namespace remains | `src/HikvisionReplicator.E2E/HikvisionReplicator.E2E.csproj`; `DeviceEndpointsTests.cs:7` — `namespace HikvisionReplicator.E2E;` | ✅ PASS |
| TPC-05 no test content change beyond namespace / usings / trait | only those lines differ | `git diff 267ab4a HEAD -M -- '*.cs'` → 11 namespace lines + 4 `[Trait]` deletions, **0 other lines** | ✅ PASS |
| TPC-06 Docker-free gate passes, 81 tests | 81 passed, no container start | `DOCKER_HOST=unix:///nonexistent/docker.sock dotnet test src/HikvisionReplicator.Tests` → `Passed: 81, Failed: 0` | ✅ PASS |
| TPC-07 integration gate passes, 88 tests | 88 passed | `dotnet test src/HikvisionReplicator.IntegrationTests` → `Passed: 88, Failed: 0` | ✅ PASS |
| TPC-08 e2e gate passes, 9 tests | 9 passed | `dotnet test src/HikvisionReplicator.E2E` against live API → `Passed: 9, Failed: 0` | ✅ PASS |
| TPC-09 no `--filter "Category=Unit"` anywhere | zero occurrences outside historical specs | `grep -rn 'Category=Unit'` → only `.specs/features/device-registry/**` and this feature's own spec | ✅ PASS |
| TPC-10 docs name real projects, no `E2ETests` | CLAUDE, README, test-patterns, ARCHITECTURE, PR template clean | `grep -rn 'E2ETests' --include='*.md'` → no hits in those five files | ✅ PASS |
| TPC-11 AD-026 recorded, AD-024 marked amended | both present | `.specs/STATE.md` AD-026 entry; AD-024 **Status** line now reads "amended by AD-026" | ✅ PASS |

**Status**: ✅ 11/11 covered, no spec-precision gaps.

**Verifier note on TPC-06.** "Runs without Docker" was exercised by pointing `DOCKER_HOST`
at a non-existent socket, not by stopping the daemon. This is a proxy: it proves nothing
in the unit project *reaches* Docker, and mutation M3 independently proves it cannot even
compile a Testcontainers reference. Stopping the daemon outright would be strictly
stronger and was not done.

---

## Discrimination Sensor

Run in a throwaway `git worktree`; the real tree was never mutated.

| # | File:line | Mutation | Result |
| --- | --- | --- | --- |
| M1 | `IntegrationTests/TracingTests.cs:~155` | Remove the `traceparent` header the fix adds | ✅ Killed — 4 failed / 84 passed |
| M2 | `IntegrationTests/TracingTests.cs:~176` | Drop `&& span.TraceId == _testTraceId` (reinstates the original defect) | ✅ Killed — 1 failed / 87 passed, reproduced 2/2 |
| M3 | `Tests/Domain/MutantIoTest.cs` (new) | Smuggle a `Testcontainers` test into the unit project | ✅ Killed — `error CS0246`, build fails |

**Sensor depth**: lightweight (3 mutations)
**Result**: 3/3 killed — PASS ✅

M3 is the one that matters for AD-026: it demonstrates the convention is enforced by
compilation rather than by convention. The retired `[Trait("Category","Unit")]` marker
could not have failed this way — an omitted attribute produced no signal at all.

---

## Gate Check

| Gate | Command | Result |
| --- | --- | --- |
| Build | `dotnet build HikvisionReplicator.slnx --no-incremental` | 0 errors |
| Unit | `dotnet test src/HikvisionReplicator.Tests` (Docker unreachable) | 81 passed, 0 failed, 0 skipped |
| Integration | `dotnet test src/HikvisionReplicator.IntegrationTests` | 88 passed, 0 failed, 0 skipped |
| E2E | `dotnet test src/HikvisionReplicator.E2E` (live API) | 9 passed, 0 failed, 0 skipped |

- **Test count before**: 169 in-process + 9 e2e
- **Test count after**: 81 + 88 = 169 in-process + 9 e2e
- **Delta**: 0 — this feature adds no tests and deletes none, which is the intent
- **Build warnings**: 4 CS0618 + 4 NU1903, **identical to `314f616`** (verified by building
  a pristine worktree of HEAD with `--no-incremental`). Both pre-date this branch.

---

## Code Quality

| Principle | Status | Note |
| --- | --- | --- |
| Minimum code | ✅ | No production code touched at all |
| Surgical changes | ✅ | TPC-05 diff proves it mechanically |
| No scope creep | ⚠️ | Two justified exceptions, both recorded below |
| Matches patterns | ✅ | csproj comments, commit style, AD entry format all follow existing conventions |
| Spec-anchored outcomes | ✅ | Structural ACs cite commands; no vague assertions accepted |
| No unclaimed tests | ✅ | No tests added |
| Guidelines followed | ✅ | `docs/test-patterns.md`, `CLAUDE.md` (AD-025 commit/branch rules) |

---

## Deviations

1. **Unplanned fix commit `267ab4a`.** The spec put "changing any test's behaviour" out of
   scope, but the split made `TracingTests` fail deterministically (3/3) where HEAD passed
   (3/3). Root cause: a `TracerProvider`'s listener on `Microsoft.AspNetCore` is
   process-wide, so the in-memory sink collected spans from `DatabaseUnreachableTests`,
   which runs in a parallel collection (`IClassFixture`, not `[Collection]`). The user was
   consulted and chose trace-id correlation, landed as a separate commit ahead of the
   split. **The defect pre-existed this branch**; only its concealment was removed.

2. **EF Core pins kept in the unit project.** Removing them alongside Testcontainers raised
   **44 MSB3277** assembly-conflict warnings, because the Api reference resolves EF Core at
   10.0.4 and 10.0.5 simultaneously. The pins are retained with a comment explaining why.
   TPC-02 forbids only Testcontainers, Respawn, and Mvc.Testing, so this stays in spec.

3. **`.specs/ARCHITECTURE.md` test counts corrected** from "151 — 69 unit, 82 integration"
   to the verified 169 / 81 / 88. Outside the literal TPC-10 wording (which covers paths),
   but the same sentence had to be rewritten anyway and leaving false counts beside
   corrected paths would be worse.

---

## Requirement Traceability Update

| Requirement | Previous | New |
| --- | --- | --- |
| TPC-01 … TPC-11 | Pending | ✅ Verified |

---

## Summary

**Overall**: ✅ Ready

**Spec-anchored check**: 11/11 ACs matched their spec-defined outcome, 0 spec-precision gaps
**Sensor**: 3/3 mutations killed
**Gate**: 81 unit + 88 integration + 9 e2e, 0 failed, 0 skipped

**What works**: One project per test level, enforced by what each project can compile
rather than by an attribute. The Docker-free gate is a whole project. All documentation
names projects that exist. AD-026 records the convention and its trade-offs.

**Issues found**: none outstanding. The one defect found during execution is fixed and
mutation-verified.

**Caveats a reviewer should weigh**:
- This report is a self-check, not an independent verification (see Verifier line above).
- TPC-06 used an unreachable `DOCKER_HOST` rather than a stopped daemon.
- Lessons L-006 and L-007 are `candidate` and need corroboration from a second feature.

**Next steps**: merge PR A (`docs/conventional-commits`, restoring AD-025 to `main` — it
was lost because its original PR #2 targeted `feat/device-registry` instead of `main`),
then merge this branch's PR B on top.

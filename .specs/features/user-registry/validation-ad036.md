# AD-036 Validation — integration suite made black box

**Date**: 2026-08-26
**Spec**: `.specs/STATE.md` § AD-036 (there is no `spec.md` for this change — the decision entry is the claim set)
**Diff range**: `6237fd0..HEAD` (`e63c772` refactor, `31356dc` follow-up fix; `e781348` is an unrelated `main` merge that touches no `src/`)
**Baseline for "before"**: `6237fd0` (and `8a5dc94` for the race assertion's provenance)
**Verifier**: independent sub-agent (author ≠ verifier, AD-028)

**Verdict: ❌ FAIL** — two live surviving mutants and one degraded guard. One of the three
is a **regression introduced by this change**, which falsifies the entry's strongest claim.

---

## AD-036 claims

Every claim is re-derived. Counts are the Verifier's own; nothing is taken from the entry.

| # | Claim | Evidence (`file:line` + assertion / measurement) | Result |
|---|---|---|---|
| C1 | Integration tests are black box, driven through HTTP, one class per use case or situation; **a test does not construct a repository, a specification or a `DbContext` in order to assert against it** | Violated in one surviving test: `src/HikvisionReplicator.IntegrationTests/UserRemovalTests.cs:106-112` — `new UserRepository(context).ListAsync(new ActiveUsersPagedSpec(0, 10), …)` then `Assert.Equal(["TICKET-2"], listed.Select(u => u.ExternalRef.Value))`. It is outside the two contract classes and carries no blind-spot sentence. Pre-existing at `6237fd0:UserRemovalTests.cs:107`, but not cleaned up by the commit whose stated scope was exactly this. Its HTTP twin already exists at `UserCatalogueTests.cs:127` | ❌ GAP |
| C2 | Reading the database to *verify* stays correct (`StoredUserAsync`, `StoredPictureAsync`, `CountUsersAsync`) | `UserApiTests.cs:102-126` — all three helpers present and used only for verification | ✅ PASS |
| C3 | The exception lives in exactly two classes, each test naming a blind-spot observable in its own doc comment | Two classes exist (`UserPersistenceContractTests.cs:68`, `DevicePersistenceContractTests.cs:21`). The blind-spot sentences are **per-class section headers**, not per-test doc comments, for 10 of the 12 tests; only `UserPersistenceContractTests.cs:261-272` and `:336-340` carry their own. `DevicePersistenceContractTests.cs:7-19` states the sentence for both its tests jointly | ⚠️ Partial — convention followed in substance, not in the letter the entry states |
| C4 | Four kinds qualify: what a read touches; the shape of the two unique indexes; which failures are *not* translated; cancellation + index→message mapping | All four present and, except as noted in C4a, empirically true (see Sensor M2/M3/M7/M14) | ✅ PASS |
| C4a | *(sub-claim)* "The shape of the two unique indexes… HTTP cannot distinguish" | **False for the access-code half.** Sensor M1 (partial filter removed) is killed by the black-box `UserRemovalTests.cs:169` — `Assert.Equal(HttpStatusCode.Created, …)` on a second spectator claiming a removed one's code. True for the external-ref half (M2 killed only by `UserPersistenceContractTests.cs:210`) | ⚠️ Overstated |
| C5 | Of 224 integration tests, 45 drove repositories, specifications and the schema directly | Re-counted: 224 tests at `6237fd0` (measured, see Gate). `[Fact]` count in the four deleted classes: `UserRepositoryTests` 11 + `UserSpecificationTests` 11 + `UserSchemaTests` 13 + `DeviceRepositoryTests` 10 = **45**; no `[Theory]`/`InlineData` in any of them | ✅ PASS |
| C6 | 34 of those asserted something a use-case test already asserted | 45 − 11 relocated = **34**. Mapping re-derived test by test; 31 of 34 have a demonstrated equivalent (see "Deleted-test disposition"). 3 do not | ❌ GAP (3 of 34) |
| C7 | `Moving_a_device_onto_another_devices_address_is_rejected` asserts the 409, that `updatedAt` did not move, and that the occupier still holds the address | `DeviceEndpointsTests.cs:636-654` — `Assert.Equal(HttpStatusCode.Conflict, …)`, `Assert.Equal(original.GetProperty("updatedAt")…, reread.GetProperty("updatedAt")…)`, `Assert.Equal("192.168.1.10", occupier.GetProperty("ipAddress")…)` | ✅ PASS |
| C8 | The 11 tests with no black-box equivalent were kept | 12 tests in the two contract classes − 1 newly added = **11** relocated. Enumerated in "Deleted-test disposition" | ✅ PASS |
| C9 | One assertion existed only below HTTP — which of the two keys collided — and was folded into `detail` before anything was deleted | `UserApiTests.cs:91-100` `AssertConflictAsync` asserts `body.GetProperty("detail").GetString()`; 7 call sites | ✅ PASS |
| C10 | Sensor: swapping the two message *constants* survived all 190 tests — the assertion is tautological | Reproduced: **M6 survives 191/191 today**, and 224/224 at `6237fd0`. Still unfixed | ✅ PASS (claim true) / ❌ gap remains open |
| C11 | Sensor: swapping which index maps to which message was killed non-deterministically | Reproduced: M5 killed `UserPersistenceContractTests.cs:274` on every run and `UserRegistrationTests.cs:239` on this run only | ✅ PASS |
| C12 | `Each_colliding_key_is_reported_as_the_key_that_actually_collided` fires on every run | `UserPersistenceContractTests.cs:290-291` — `Assert.Equal(IUserRepository.ExternalRefAlreadyRegistered, byExternalRef.AsT1.Message)` / `…AccessCodeAlreadyInUse, byAccessCode.AsT1.Message`. Killed M5 and M14 deterministically | ✅ PASS |
| C13 | AD-022's "a renamed index silently degrades a 409 into a 500" hazard is **now covered** by the race tests' `Assert.DoesNotContain(InternalServerError)` plus the new deterministic mapping test | **True for users, false for devices.** Sensor D2 (rename the address index in `DeviceRepository.cs:49`) is killed *only* by `DeviceEndpointsTests.cs:334`, a race test. At `6237fd0` it was killed deterministically by `DeviceRepositoryTests.Device_reusing_a_registered_address_is_rejected_as_a_conflict` and `…Address_change_onto_another_devices_address_is_rejected_as_a_conflict`. The entry's own rule ("a guard reachable only by winning a race is not a guard") condemns what is left | ❌ GAP |
| C14 | The 409-from-every-racer assertion predates this work — identical at `8a5dc94` | `git show 8a5dc94:…/UserRegistrationTests.cs:256-259` — `Assert.All(responses.Where(r => r.StatusCode != Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode))`. Identical | ✅ PASS |
| C15 | `PUT /api/users/{externalRef}` is an idempotent upsert, so 200 is the correct answer for a late racer | `UpsertUserService.cs` upsert path; racers use distinct access codes (`UserRegistrationTests.cs:246`) so a late one finds a row and updates it | ✅ PASS |
| C16 | The test now asserts exactly one 201, exactly one row, no 500 anywhere, and any 409 naming the external reference | `UserRegistrationTests.cs:263` `Assert.Single(responses, r => r.StatusCode == Created)`; `:264-267` `Assert.DoesNotContain(… InternalServerError)`; `:268-274` `AssertConflictAsync(loser, IUserRepository.ExternalRefAlreadyRegistered)` for every non-200 loser; `:275` `Assert.Equal(1, await CountUsersAsync())` | ✅ PASS |
| C17 | On a run where every loser updates, the external-reference race produces no 409 and guards nothing; the contract test is the only unconditional guard on that half | Confirmed by construction (the `if (… == OK) continue` at `:270-271` skips the only conflict assertion) and by M5, where the race test fired on one run and not deterministically | ✅ PASS |
| C18 | Numbers: 224 → 191; 34 deleted, 11 relocated, 1 added, 7 strengthened; unit tests untouched at 282 | Measured: 224 → 191 integration; 282 → 282 unit; `git diff --stat 6237fd0..HEAD` touches no file under `src/HikvisionReplicator.Tests/`. 45 − 11 = 34 deleted; 12 − 1 = 11 relocated; 7 `AssertConflictAsync`/`detail` call sites added in `e63c772` (3 × `UserRegistrationTests`, 1 × `UserAmendmentTests`, 1 × `UserResurrectionTests`, 2 × `DeviceEndpointsTests`) | ✅ PASS — every number correct |
| C19 | **No assertion was lost** | **Refuted.** Three deleted assertions have no survivor: the paged spec's page-size bound (proved by mutant M8, which dies at `6237fd0` and lives at HEAD), the applied-migration name, and the registry table names. See "Deleted-test disposition" | ❌ FAIL |
| C20 | Scope: deletes 4 classes, adds 2; `docs/test-patterns.md`; amends AD-024 | `git diff --stat 6237fd0..HEAD` matches exactly; AD-024 amendment recorded at `.specs/STATE.md:205` | ✅ PASS |
| C21 | The scheduling-dependent-guard rule is written into `docs/test-patterns.md` § Integration tests are black box | `docs/test-patterns.md` — "**A guard that depends on thread scheduling is not a guard.** When a use-case test can only reach something by racing, prove it deterministically as well." | ✅ PASS |

**Status**: 16 of 21 claims proved; 4 refuted or gapped (C1, C6, C13, C19); 2 overstated (C3, C4a).

---

## Deleted-test disposition (the "No assertion was lost" audit)

All 45 below-HTTP tests at `6237fd0`, recovered with `git show 6237fd0:<path>`.

### Relocated — 11 (no loss)

| Deleted from | Test | Now at |
|---|---|---|
| `UserRepositoryTests` | `Registering_a_spectator_aborts_when_the_caller_has_already_cancelled` | `UserPersistenceContractTests.cs:320` |
| `UserRepositoryTests` | `Collision_on_another_unique_index_is_not_reported_as_a_key_conflict` | `UserPersistenceContractTests.cs:230` |
| `UserRepositoryTests` | `Failure_that_is_not_a_unique_violation_is_not_reported_as_a_conflict` | `UserPersistenceContractTests.cs:295` |
| `UserSpecificationTests` | `Looking_up_a_spectator_never_reads_the_face_picture_table` | `UserPersistenceContractTests.cs:151` |
| `UserSpecificationTests` | `Listing_the_catalogue_never_reads_the_face_picture_table` | `UserPersistenceContractTests.cs:178` |
| `UserSchemaTests` | `External_reference_uniqueness_applies_to_every_row` | `UserPersistenceContractTests.cs:210` |
| `UserSchemaTests` | `Access_code_uniqueness_is_scoped_to_spectators_that_are_not_deleted` | `UserPersistenceContractTests.cs:219` |
| `UserSchemaTests` | `Face_picture_is_removed_when_its_spectators_row_is_removed` | `UserPersistenceContractTests.cs:342` |
| `UserSchemaTests` | `Reading_a_spectator_does_not_bring_its_face_picture_with_it` | `UserPersistenceContractTests.cs:197` |
| `DeviceRepositoryTests` | `Registering_a_device_aborts_when_the_caller_has_already_cancelled` | `DevicePersistenceContractTests.cs:70` |
| `DeviceRepositoryTests` | `Failure_that_is_not_an_address_collision_is_not_reported_as_a_conflict` | `DevicePersistenceContractTests.cs:45` |

### Deleted with a proven survivor — 28 of 34

Mapping verified, and for the load-bearing ones proved by killing a mutant that the deleted
test targeted:

| Deleted test | Survivor | Proof |
|---|---|---|
| `Spectator_with_free_keys_is_stored` | `UserRegistrationTests.cs:20` + `UserRemovalTests.cs:36-53` | field-by-field `Assert.Equal` on the stored row |
| `Spectator_reusing_a_registered_external_reference_is_rejected_as_a_conflict` | `UserResurrectionTests.cs:62`, `UserRegistrationTests.cs:239` | — |
| `Spectator_reusing_an_active_access_code_is_rejected_as_a_conflict` | `UserRegistrationTests.cs:228` — `AssertConflictAsync(response, IUserRepository.AccessCodeAlreadyInUse)` | — |
| `Colliding_key_is_named_differently_depending_on_which_one_collided` | `UserPersistenceContractTests.cs:274` (strictly stronger: pins *which* is which, not just `NotEqual`) | M5, M14 killed |
| `Spectators_registered_at_once_under_one_external_reference_yield_one_user` | `UserRegistrationTests.cs:239` | — |
| `Spectators_claiming_one_access_code_at_once_yield_one_user` | `UserRegistrationTests.cs:283` | — |
| `Access_code_change_onto_another_active_spectators_code_is_a_conflict` | `UserAmendmentTests.cs:235` | M11 killed |
| `Access_code_change_onto_a_free_code_is_stored` | `UserAmendmentTests.cs:36` | — |
| `Active_spectator_is_found_by_its_external_reference` | `UserLookupTests.cs:17` | M12 killed |
| `Deleted_spectator_is_invisible_to_the_active_lookup` | `UserRemovalTests.cs:88` | M12 killed |
| `Unregistered_external_reference_matches_no_spectator` | `UserLookupTests.cs:91` | — |
| `Deleted_spectator_is_still_found_by_the_lookup_that_includes_tombstones` | `UserResurrectionTests.cs:38` | M13 killed |
| `Active_spectator_is_found_by_its_access_code` | `UserRegistrationTests.cs:228` | M11 killed |
| `Deleted_spectators_access_code_matches_no_active_spectator` | `UserRemovalTests.cs:169` | M1 killed |
| `Spectator_is_never_matched_by_its_own_access_code_when_it_is_the_one_excluded` | `UserAmendmentTests.cs:254` | M11 killed |
| `Listing_the_catalogue_excludes_deleted_spectators` | `UserCatalogueTests.cs:127` | M9, M10 killed |
| `Spectator_is_stored_and_read_back_with_every_identity_field_intact` | `UserRemovalTests.cs:47-52` (ExternalRef/Name/AccessCode/DeletedAt) + `UserLookupTests.cs:40` (fingerprint) + `UserAmendmentTests.cs:178` | — |
| `Two_spectators_cannot_share_an_external_reference` | `UserRegistrationTests.cs:239`, `UserPersistenceContractTests.cs:210` | M2 killed |
| `Deleted_spectators_external_reference_stays_reserved` | `UserPersistenceContractTests.cs:210`, `UserResurrectionTests.cs:38` | M2 killed |
| `External_references_differing_only_by_letter_case_are_two_spectators` | `UserLookupTests.cs:111` | — |
| `Two_active_spectators_cannot_share_an_access_code` | `UserRegistrationTests.cs:228` | — |
| `Deleted_spectators_access_code_can_be_reused` | `UserRemovalTests.cs:169` | M1 killed |
| `Face_picture_is_stored_alongside_its_spectator` | `UserAmendmentTests.cs:27` (`picture.Content`), `UserApiTests.cs:120-126` (`StoredPictureAsync` keys on `UserId`), `UserCatalogueTests.cs:179-180` | — |
| `Device_with_a_free_address_is_stored` | `DeviceEndpointsTests.cs:75` | — |
| `Address_change_onto_a_free_address_is_stored` | `DeviceEndpointsTests.cs:548` | — |
| `Device_holding_an_address_is_found_by_that_address` | `DeviceEndpointsTests.cs:306` | — |
| `No_device_is_found_at_an_unclaimed_address` | `DeviceEndpointsTests.cs:75` | — |
| `Another_device_holding_the_address_is_found_when_excluding_the_device_being_updated` | `DeviceEndpointsTests.cs:629` | D1 killed |
| `Device_is_never_matched_by_its_own_address_when_it_is_the_one_excluded` | `DeviceEndpointsTests.cs:658` | D1 killed |

*(30 rows: two device rows cover the same pair of deleted tests as C13's degraded pair, listed separately below.)*

### Deleted with NO surviving equivalent — the losses

| # | Deleted test | What it asserted | Where it now lives | Severity |
|---|---|---|---|---|
| L1 | `UserSpecificationTests.Pages_together_contain_every_spectator_exactly_once` | `Assert.Equal(2, first.Count)` for `ActiveUsersPagedSpec(0, 2)` — the spec returns **exactly** `take` rows | **Nowhere.** `UserCatalogueTests.cs:83` asserts the union of pages, not the window size. `ListUsersService.cs:33-43` already over-fetches by one and trims with `.Take(currentSize)`, so an extra row in the spec is invisible to every HTTP assertion. Proved: mutant **M8 survives 191/191 at HEAD and is killed at `6237fd0`** | **Blocker** — regression caused by this change |
| L2 | `UserRepositoryTests.Device_reusing_a_registered_address_is_rejected_as_a_conflict` + `Address_change_onto_another_devices_address_is_rejected_as_a_conflict` (in `DeviceRepositoryTests`) | The `DeviceRepository` 23505→409 translation, asserted **below** the service pre-check | Only `DeviceEndpointsTests.cs:334`, a 4-way race. Proved: mutant **D2 killed by 2 deterministic tests at `6237fd0`, by a race test alone at HEAD** | **Major** — contradicts C13 and the rule in `docs/test-patterns.md` |
| L3 | `UserSchemaTests.Registry_schema_is_recorded_as_an_applied_migration` | `Assert.Contains(applied, m => m.EndsWith("AddUserRegistry"))` | **Nowhere.** `HarnessTests.cs:44-50` asserts only `InitialCreate`. `grep -rn AddUserRegistry src/ --include=*.cs` returns only the migration itself | Minor — USR-38's "schema comes from a migration" is now only implied |
| L4 | `UserSchemaTests.Registry_tables_are_created_when_the_application_starts` | `Assert.Contains(UserConfiguration.TableName, tables)` and `FacePictureConfiguration.TableName` | **Nowhere.** `HarnessTests.cs:35-42` names only `devices` and `__EFMigrationsHistory` | Cosmetic — every user test fails if the tables are absent |

---

## Discrimination Sensor

**Depth**: P0-full (17 behaviour-level mutations — this is a data-integrity and latency-path
refactor whose declared risk is silent coverage loss).
**Method**: two throwaway `git worktree` copies, one at `HEAD` and one at `6237fd0`, so every
survivor could be re-tested against the pre-change suite. The real working tree was never
mutated; `git status` is clean and both worktrees are removed.

Schema-shape mutations were applied to `UserConfiguration.cs`, the `AddUserRegistry` migration
**and** `AppDbContextModelSnapshot.cs` together. Mutating the configuration alone is trapped by
EF Core's `PendingModelChangesWarning` at startup, which kills every test for a reason unrelated
to the assertions under test — a false kill, not evidence.

| # | File:line | Mutation | Killed? |
|---|---|---|---|
| M1 | `UserConfiguration.cs:114` + migration `:63-68` + snapshot `:131` | Access-code unique index made all-rows (partial filter dropped) | ✅ `UserPersistenceContractTests.cs:219`, `UserRemovalTests.cs:169` |
| M2 | `UserConfiguration.cs:110-113` + migration `:70-74` + snapshot `:136` | External-ref unique index made partial `WHERE "DeletedAt" IS NULL` | ✅ `UserPersistenceContractTests.cs:210` **only** |
| M3 | `UserConfiguration.cs:103` | `Navigation(u => u.Picture).AutoInclude(false)` → `(true)` | ✅ `UserPersistenceContractTests.cs:151,178,197` **only** |
| M4 | `UserConfiguration.cs:95` + migration `:54` + snapshot `:146` | FK `DeleteBehavior.Cascade` → `Restrict` | ✅ 23 tests, incl. `UserPersistenceContractTests.cs:342`, `UserRemovalTests.cs:58,71` |
| M5 | `UserRepository.cs:62-64` | Swap which index maps to which message | ✅ `UserPersistenceContractTests.cs:274` (every run) + `UserRegistrationTests.cs:239` (this run only) |
| M6 | `IUserRepository.cs:22-26` | Swap the two message string **values** | ❌ **SURVIVED** 191/191 — and 224/224 at `6237fd0`, so pre-existing |
| M7 | `UserRepository.cs:65` | `_ => null` → `_ => ExternalRefAlreadyRegistered` (translate every 23505) | ✅ `UserPersistenceContractTests.cs:230` **only** |
| M8 | `ActiveUsersPagedSpec.cs:22` | `Take(take)` → `Take(take + 1)` | ❌ **SURVIVED** 191/191 — killed at `6237fd0` by `UserSpecificationTests.Pages_together_contain_every_spectator_exactly_once` |
| M9 | `ActiveUsersPagedSpec.cs:22` | `Skip(skip)` → `Skip(skip + 1)` | ✅ 9 tests |
| M10 | `ActiveUsersPagedSpec.cs:22` | `OrderBy(u => u.Id)` → `OrderByDescending` | ✅ 6 tests |
| M11 | `ActiveUserByAccessCodeSpec.cs:18-20` | Drop `user.Id != excludedUserId` | ✅ 7, incl. `UserAmendmentTests.cs:254` |
| M12 | `UserByExternalRefSpec.cs:24` | Drop `user.DeletedAt == null` | ✅ 4, incl. `UserRemovalTests.cs:88` |
| M13 | `UserByExternalRefIncludingDeletedSpec.cs:24` | Add `&& user.DeletedAt == null` | ✅ 7, incl. `UserResurrectionTests.cs:38` |
| M14 | `UserRepository.cs:64` | Access-code index **name** renamed in the mapping | ✅ `UserPersistenceContractTests.cs:274` **only** |
| D1 | `DeviceByAddressExcludingSpec.cs:13-17` | Drop `device.Id != excludedDeviceId` | ✅ 10, incl. `DeviceEndpointsTests.cs:658` |
| D2 | `DeviceRepository.cs:49` | Address index **name** renamed in the translation | ⚠️ **DEGRADED** — killed 3/3 runs but only by `DeviceEndpointsTests.cs:334` (race). At `6237fd0`, killed additionally by 2 deterministic repository tests |
| D3 | `DeviceByAddressSpec.cs:14` | Drop `device.HttpPort == httpPort` | ❌ **SURVIVED** 191/191 — and 224/224 at `6237fd0`, so pre-existing and outside AD-036's scope |

**Result**: 17 injected · **14 killed · 2 survived · 1 degraded** — ❌ FAIL

What the sensor also *confirms*, and this is the refactor's real vindication: M2, M3, M7 and M14
are killed by a contract test **and nothing else**. The blind-spot sentences for "what a read
touches", "which failures are not translated" and the index→message mapping are true statements,
not a convenient door. Only C4a's access-code-index sentence is overstated.

---

## Flakiness check (`31356dc`)

`UserRegistrationTests.Spectators_registered_at_once_under_one_reference_yield_one_user` and
`…claiming_one_access_code_at_once_yield_one_user`, run **10× each** (filtered run, clean tree):
**20/20 passed, no instability.**

Note this is exactly the evidence that preceded the CI failure — the entry records 12/12 local
passes before CI failed the same assertion. Local stability is weak evidence for a race; the
argument below is the load-bearing one.

**Are the new assertions correct, or merely more permissive?** Correct, independently derived:

- `Assert.Single(… == Created)` (`:263`) is a registry fact. The unique index admits exactly one
  insert; every other racer either loses it (409) or arrives after the commit (200). Neither zero
  nor two 201s is reachable.
- `Assert.Equal(1, await CountUsersAsync())` (`:275`) is unconditional and is USR-07's actual promise.
- The permissiveness is bounded, not open. `AssertConflictAsync` (`UserApiTests.cs:96`) still
  demands `Conflict`, so a loser answering 400 or 404 fails; only 200 is waived, and only 200.
- The waiver is justified: `PUT /api/users/{externalRef}` upserts, and each racer sends a distinct
  access code (`:246`), so a late racer legitimately updates the winner's row.
- **The asymmetry with the access-code twin is correct, not an oversight.** That test
  (`:283-305`) still demands 409 from *every* loser, and must: its racers use distinct external
  references (`TICKET-{index}`), so none can ever take the update path. 409 there is a registry
  fact; 409 in the external-reference test was a scheduling claim. The author got this right.
- What the fix *does* cost: `Assert.DoesNotContain(… InternalServerError)` (`:264-267`) is now
  reachable only when some racer loses the insert race — the entry says so itself (C17), and the
  deterministic replacement exists for users (`UserPersistenceContractTests.cs:274`) but **not for
  devices** (gap L2).

---

## Gate Check

- **Command**: `dotnet build HikvisionReplicator.slnx --no-restore --no-incremental && dotnet test src/HikvisionReplicator.Tests && dotnet test src/HikvisionReplicator.IntegrationTests`
- **Build**: 0 errors, 13 warnings (`--no-incremental`, per L-007) — 10 `CA` + 2 `CS0618` + 1 `NU1903`, matching the recorded baseline shape
- **Unit**: 282 passed, 0 failed, 0 skipped
- **Integration**: 191 passed, 0 failed, 0 skipped
- **Before this change** (measured by building and running `6237fd0` in a scratch worktree): **282 unit · 224 integration**, both green
- **Delta**: unit +0 · integration **−33** (34 deleted, 1 added)
- **Skipped**: none
- **Test-integrity verdict**: the decrease is justified in 31 of 34 cases and unjustified in 3 (L1, L3, L4), with a fourth case (L2) weakened rather than lost

---

## Code Quality

| Principle | Status |
|---|---|
| Minimum code | ✅ |
| Surgical changes | ✅ — `git diff --stat 6237fd0..HEAD` touches only the declared scope |
| No scope creep | ✅ |
| Matches patterns | ✅ — contract classes follow the existing `[Collection(PostgresCollection.Name)]` + `IAsyncLifetime` shape |
| Claim-anchored outcome check (asserted values match the entry's claims) | ⚠️ — C19 refuted, C13 false for devices, C1 violated once, C3/C4a overstated |
| Per-layer Coverage Expectation met | ⚠️ — `ActiveUsersPagedSpec`'s `Take` bound and `DeviceRepository`'s translation are no longer covered deterministically |
| Every test maps to a claim — no unclaimed tests | ✅ for the 12 contract tests |
| Documented guidelines followed | ✅ `docs/test-patterns.md` (updated in the same commit), `CLAUDE.md` § Tests |

**Taste-level, ranked last deliberately**: `UserPersistenceContractTests.cs:336-340`
(`Face_picture_is_removed_when_its_spectators_row_is_removed`) justifies itself with "the
application never issues this hard delete", which is a *different* argument from the
blind-spot rule the class states. M4 shows 20 HTTP tests already fail if the cascade breaks, so
it is redundant rather than blind-spot coverage. The entry acknowledges this in its Trade-off
paragraph and the test is cheap; no action needed.

---

## Fix Plans

### Fix 1 — restore a deterministic bound on the paged specification (Blocker)

- **Root cause**: `ListUsersService.cs:33-43` requests `currentSize + 1` and trims with
  `.Take(currentSize)`, so the HTTP surface is structurally blind to `ActiveUsersPagedSpec`
  returning more rows than asked. The only assertion that saw it was deleted.
- **Fix task**: add to `UserPersistenceContractTests` a test asserting
  `ActiveUsersPagedSpec(0, 2)` returns exactly 2 rows given 3 active spectators, with the
  blind-spot sentence: *the service over-fetches one row past the page to answer "is there
  another page?" and trims it before responding, so an over-wide window costs latency on the
  AD-014 path while returning a byte-identical body.*
- **Done when**: `Take(take)` → `Take(take + 1)` in `ActiveUsersPagedSpec.cs:22` fails the suite.

### Fix 2 — give the device translation the same deterministic guard the user one has (Major)

- **Root cause**: AD-036 added `Each_colliding_key_is_reported_as_the_key_that_actually_collided`
  for users but applied no equivalent to `DeviceRepository`, whose deterministic guards
  (`DeviceRepositoryTests`) were deleted in the same commit.
- **Fix task**: add to `DevicePersistenceContractTests` a test that calls
  `AddIfAddressFreeAsync` with a duplicate address and asserts
  `IDeviceRepository.AddressAlreadyRegistered` — bypassing the service pre-check, as
  `UserPersistenceContractTests.cs:274` does.
- **Done when**: renaming the constraint in `DeviceRepository.cs:49` fails a non-race test.
- Then correct C13 in AD-036, which currently claims this hazard is already covered.

### Fix 3 — assert the conflict messages against literals, not the production constants (Major)

- **Root cause**: every assertion (`UserApiTests.cs:99`, `UserPersistenceContractTests.cs:290-291`,
  `UserRegistrationTests.cs:234,274,300`, `UserAmendmentTests.cs:245`,
  `UserResurrectionTests.cs:167`) compares against `IUserRepository.*`, the same constants the
  production code emits, so the pair moves together. The entry identified this and did not close it —
  the deterministic mapping test it added is also constant-compared.
- **Fix task**: pin the literal wording in **one** place (either the contract test's two
  assertions or a dedicated wording test); leave the rest constant-compared so a wording change
  is a one-line update.
- **Done when**: swapping the two string literals in `IUserRepository.cs:22-26` fails the suite.

### Fix 4 — restore the migration and table assertions (Minor)

- **Fix task**: extend `HarnessTests.cs:44` to also assert an applied migration ending
  `AddUserRegistry`, and `HarnessTests.cs:35` to also name `UserConfiguration.TableName` and
  `FacePictureConfiguration.TableName`. Both are legitimate black-box startup assertions and need
  no contract class.

### Fix 5 — move the one remaining rule violation (Minor)

- **Fix task**: `UserRemovalTests.cs:99-113` `Removed_spectator_is_absent_from_the_catalogue`
  drives a repository and a specification. Rewrite it against `GET /api/users`, or delete it —
  `UserCatalogueTests.cs:127` already asserts the same thing through HTTP.

### Fix 6 — soften C3/C4a in the entry (Cosmetic)

- The per-test blind-spot sentence is a per-class section header for 10 of 12 tests; and the
  access-code index's shape *is* distinguishable through HTTP (`UserRemovalTests.cs:169`). Say so,
  or move the sentences onto the tests.

---

## Summary

**Overall**: ❌ Not ready — merge is not blocked on correctness of shipped behaviour, but the
entry's headline claim is false and two guards are gone.

**Claim check**: 16/21 proved · 4 refuted or gapped (C1, C6, C13, C19) · 2 overstated (C3, C4a)
**Gate**: 282 unit + 191 integration, 0 failed, 0 skipped (before: 282 + 224)
**Sensor**: 17 injected · 14 killed · 2 survived · 1 degraded
**Flakiness**: 20/20 stable; the new race assertions are correct, not merely permissive

**What works**: every number in AD-036 is exactly right, and the blind-spot argument is
empirically true for the claims that carry weight — M2, M3, M7 and M14 are each killed by a
contract test and by nothing else, which is the strongest possible defence of the exception
existing at all. The `31356dc` fix is sound and its asymmetry with the access-code race test is
deliberate and correct.

**Issues found**: the refactor deleted the only assertion on `ActiveUsersPagedSpec`'s page-size
bound (Fix 1) and downgraded `DeviceRepository`'s 23505 translation from a deterministic guard to
a race-only one (Fix 2) — the second being the very failure mode the same commit wrote into
`docs/test-patterns.md` as a rule. The tautological-constant gap the entry discovered remains
open (Fix 3).

**Next steps**: Fixes 1 and 2 before merge; 3–6 may follow on the planned follow-up branch.
Correct C13 and C19 in AD-036 either way — a decision entry that overstates its own safety is
worse than one that records the gap.

# user-registry Validation

**Date**: 2026-08-26
**Spec**: `.specs/features/user-registry/spec.md` (45 criteria, USR-01…USR-45)
**Diff range**: `738f6b3..944922f` on `feat/user-registry` (41 commits, 86 files, +9951)
**Verifier**: independent sub-agent (author ≠ verifier) — read-only over production code; every
mutation ran in scratch state and was discarded with `git checkout --`.

**Verdict: PASS ✅** — 45/45 criteria carry a `file:line` citation whose asserted value matches
the spec-defined outcome; 13 of 14 injected faults were killed and the single survivor is
provably equivalent. Six low-severity precision notes are recorded below; none blocks the merge.

---

## Task Completion

All 26 tasks are marked `### ✅ Tn` with a recorded commit hash, and every hash resolves in
`738f6b3..HEAD`. No task is partial or blocked.

**Bookkeeping drift (non-blocking)**: T14–T21 carry a `✅` heading and a `**Committed**` hash but
their `Done when` checkboxes were left as `- [ ]`. 56 unchecked boxes across the file. The work is
present and tested; only the ticks are missing.

---

## Spec-Anchored Acceptance Criteria

Evidence-or-zero: every row cites a file, a line and the actual assertion expression. A criterion
with no citation would count as uncovered.

### P1: Register a spectator

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-01 unregistered ref → create | `201 Created` + `Location` to the same URL | `IntegrationTests/UserRegistrationTests.cs:23` — `Assert.Equal(HttpStatusCode.Created, response.StatusCode)`; `:38` — `Assert.Equal(Route("TICKET-1"), response.Headers.Location?.ToString())` | ✅ |
| USR-02 blank / >255 ref | validation error naming `externalRef` | `UserRegistrationTests.cs:144` and `:153` — `AssertRejectedFieldAsync(response, ExternalRef.Errors.Field)` (asserts 400 **and** `errors["externalRef"]` non-empty, `UserApiTests.cs:74-82`); unit `Tests/Domain/ExternalRefTests.cs:37-38`, `:56-57` | ✅ |
| USR-03 name absent / blank / >100 | validation error naming `name` | `UserRegistrationTests.cs:165`, `:174`, `:186` — `AssertRejectedFieldAsync(response, User.Errors.NameField)`; unit `UserCreateTests.cs:85-86`, `:98-99`, `:127-128` — `Assert.Equal(User.Errors.NameTooLong, result.AsT1.Message)` | ✅ |
| USR-04 accessCode absent / non-digit / <4 / >20 | validation error naming `accessCode` | `UserRegistrationTests.cs:198`, `:202-210` (`"12a4"`, `"123"`, 21 digits), `:220` (Arabic-Indic); unit `AccessCodeTests.cs:51` — `Assert.Equal(AccessCode.Errors.MustBeNumeric, …)`, `:89` — `…Errors.OutOfRange` | ✅ |
| USR-05 create without face | validation error naming `facePicture` | `UserRegistrationTests.cs:109` — `AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field)`; `:110` — `Assert.Equal(0, await CountUsersAsync())` | ✅ |
| USR-06 accessCode held by another active user | conflict, **originating in the DB constraint** | API: `UserRegistrationTests.cs:233` — `Assert.Equal(HttpStatusCode.Conflict, …)`. Origin proven by bypassing the service pre-check: `UserRepositoryTests.cs:142` — `Assert.Equal(IUserRepository.AccessCodeAlreadyInUse, result.AsT1.Message)`; and `UserSchemaTests.cs:202-203` — `Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState)` + `Assert.Equal(UserConfiguration.AccessCodeIndexName, violation.ConstraintName)`. Active-only scope: `UserSchemaTests.cs:220`, `:229` | ✅ |
| USR-07 concurrent upsert of one ref | exactly one user | `UserRegistrationTests.cs:255-260` — `Assert.Single(responses, r => r.StatusCode == Created)`, losers `Conflict`, `Assert.Equal(1, await CountUsersAsync())`; repo-level race `UserRepositoryTests.cs:191-197` | ✅ |
| USR-08 concurrent claim of one accessCode | one succeeds, other conflicts | `UserRegistrationTests.cs:282-287`; `UserRepositoryTests.cs:205-210` — `Assert.Equal(IUserRepository.AccessCodeAlreadyInUse, result.AsT1.Message)` | ✅ |
| USR-09 representation carries hash/size/dims, never bytes | those exact three, no bytes | `UserRegistrationTests.cs:54-58` — `Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(picture.Content)), body.GetProperty("faceContentHash").GetString())` and `Assert.Equal(picture.Content.Length, body.GetProperty("faceByteSize").GetInt32())`; `:69` — `Assert.False(body.TryGetProperty("facePicture", out _))`; `:77-81` — base64 of the stored bytes absent from the payload | ✅ |
| USR-10 picture unwritable → no user | zero users for that ref | `UserRegistrationTests.cs:300-309` — a real `CHECK (false)` on `face_pictures`, then `Assert.Equal(0, await CountUsersAsync())` | ✅ |
| USR-11 timestamps from injected clock | both equal the `TimeProvider` reading | `UserRegistrationTests.cs:95-96` — `Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("createdAt").GetDateTime())` (and `updatedAt`); unit `UserCreateTests.cs:60-61`, `:73-74` | ✅ |

### P1: Normalize the face picture

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-12 canonical JPEG, original not stored | JPEG derivative ≠ upload | `Tests/Infrastructure/SkiaFaceImageNormalizerImageTests.cs:24-25` — `Assert.Equal(FixtureHeader.Jpeg, FixtureHeader.Read(derivative).Format)` from a PNG source; `:35` — `Assert.NotEqual(upload, derivative)`; `:44` — `Assert.Equal(JpegInspector.BaselineStartOfFrame, marker)` | ✅ |
| USR-13 EXIF rotation applied to pixels | stored face upright | `ImageTests.cs:56-57` — `Assert.Equal(900, normalized.Width); Assert.Equal(1200, normalized.Height)` for a 1200×900/Orientation=6 source; direction (not merely the swap) at `:71-74` — corner averages map stored TL→derivative TR etc. | ✅ |
| USR-14 no source metadata, no GPS | no EXIF segment, GPS bytes absent | `SkiaFaceImageNormalizerEncodingTests.cs:153` — `Assert.False(JpegInspector.HasApplicationSegment(derivative, "Exif"))`; `:169` — `Assert.Equal(-1, IndexOf(derivative, sourceExif))` (whole-file byte search) | ✅ |
| USR-15 derivative 40 KB ≤ size ≤ 200 KB | **both** bounds | `EncodingTests.cs:63-70` — `Assert.True(stored.Length >= 40 * 1024, …)` **and** `Assert.True(stored.Length <= 200 * 1024, …)` over 7 fixtures; rejection path `:106` — `Assert.Equal(SkiaFaceImageNormalizer.Errors.CannotReachMinimumSize, error.Message)` | ✅ |
| USR-16 short ≥480 & long ≥640; short <2160 & long <3840 | derivative inside both | Ceiling: `ImageTests.cs:135-142` — `Math.Min(w,h) <= options.MaxShortEdge (2159)` and `Math.Max(w,h) <= options.MaxLongEdge (3839)` for the 4000×3000 fixture. Derivative floor: guarded by the source floor plus the never-shrink-below-floor bail, empirically confirmed by Mutation 14 (`EncodingTests.cs:137` killed it) | ✅ |
| USR-17 below floor → reject stating the minimum, never upscale | error names the minimum; no derivative | `SkiaFaceImageNormalizerGuardTests.cs:122-124` — `Assert.Equal(Errors.Field, error.Field)`, `Assert.Contains("480", error.Message)`, `Assert.Contains("640", error.Message)`; `:136` — `Assert.False(result.IsT0)`; API `UserRegistrationTests.cs:133-134` | ✅ |
| USR-18 aspect preserved, no crop | source ratio == derivative ratio | `ImageTests.cs:177-180` — `Assert.True(Math.Abs(sourceRatio - derivativeRatio) <= tolerance, …)` across 7 fixtures, tolerance = one pixel on the shorter edge | ✅ |
| USR-19 >8 MB upload rejected without decoding | refused before a codec exists | `GuardTests.cs:31-32` — `Assert.Equal(Errors.UploadTooLarge, error.Message)` for bytes that are *also* undecodable (getting the size message, not `NotDecodable`, is the ordering proof); transport `UserRequestSizeTests.cs:95` — `Assert.Equal(HttpStatusCode.RequestEntityTooLarge, …)`, `:111-117` — normalizer message absent + `Assert.Equal(0, await CountUsersAsync())`; boundary `:79` | ✅ |
| USR-20 pixel cap checked before decode allocation | refused before the buffer | `GuardTests.cs:89` — `Assert.Equal(Errors.TooManyPixels, error.Message)` (the bomb's pixel data is undecodable, so a decode-first pipeline returns a different message); `:93-97` — `Assert.True(growth < 256 MB, …)` on `PrivateMemorySize64` against a declared 3.6 GB buffer | ✅ |
| USR-21 undecodable bytes | error naming `facePicture` | `GuardTests.cs:57-58` — `Assert.Equal("facePicture", error.Field)`, `Assert.Equal(Errors.NotDecodable, error.Message)`; API `UserRegistrationTests.cs:121` | ✅ |
| USR-22 hash, size, dims recorded on the user | fingerprint describes the stored bytes | `EncodingTests.cs:181` — `Assert.Equal(independently, normalized.ContentHash)` (independent SHA-256); `:191-192` — `Assert.Equal(stored.Width, normalized.Width)` after re-decoding the derivative; persisted round-trip `UserSchemaTests.cs:129-132` | ✅ |

### P1: Amend a registered spectator

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-23 existing ref → update | `200 OK`, same row | `UserAmendmentTests.cs:41` — `Assert.Equal(HttpStatusCode.OK, response.StatusCode)`; `:45` — same `id`; `:46` — `Assert.Equal(1, await CountUsersAsync())` | ✅ |
| USR-24 omitted picture keeps image + all three fields | hash, size, width, height unchanged; bytes unchanged | `UserAmendmentTests.cs:62-77` — all four response fields equal to the created ones; `:78` — `Assert.Equal(storedBefore, await StoredPictureContentAsync("TICKET-1"))` read from `face_pictures`; unit `UserUpdateTests.cs:56-61` | ✅ |
| USR-25 supplied picture replaces image + fingerprint | bytes and hash change, size matches | `UserAmendmentTests.cs:94-99` — `Assert.NotEqual(storedBefore, storedAfter)`, `Assert.NotEqual(created hash, body hash)`, `Assert.Equal(storedAfter.Length, body faceByteSize)`; `:115` — `Assert.Equal(1, await context.Set<FacePicture>().CountAsync())` (replaced, not duplicated) | ✅ |
| USR-26 byte-identical values → `UpdatedAt` frozen | unchanged despite an advanced clock | `UserAmendmentTests.cs:132-136` — clock moved +5 min, `Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("updatedAt").GetDateTime())`; unit `UserUpdateTests.cs:96`, `:108`, `:119` | ✅ |
| USR-27 rejected update applies nothing | name, hash, `UpdatedAt`, bytes all as before | `UserAmendmentTests.cs:176-179` — `Assert.Equal(DefaultName, stored.Name)`, `Assert.Equal(created hash, stored.Face.ContentHash)`, `Assert.Equal(Kickoff.UtcDateTime, stored.UpdatedAt)`, `Assert.Equal(storedBefore, …)`; `:197-198` for a bad picture; unit `UserUpdateTests.cs:188`, `:199`, `:216`, `:227` | ✅ |
| USR-28 accessCode of another active user | conflict | `UserAmendmentTests.cs:244` — `Assert.Equal(HttpStatusCode.Conflict, …)`; `:248-249` — stored values unchanged; self re-send not a conflict `:262`; DB-level `UserRepositoryTests.cs:231-237` | ✅ |

### P1: Remove a spectator

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-29 delete active user | `204`, row survives marked deleted | `UserRemovalTests.cs:32` — `Assert.Equal(HttpStatusCode.NoContent, …)`; `:48-52` — `Assert.Equal(Kickoff.AddMinutes(5).UtcDateTime, stored.DeletedAt)`, identity fields intact, `Assert.Equal(1, await CountUsersAsync())`; unit `UserLifecycleTests.cs:42` | ✅ |
| USR-30 face bytes destroyed, identity survives | `face_pictures` row gone | `UserRemovalTests.cs:67` — `Assert.Null(await StoredPictureAsync(registered.Id))`, read straight from the table; `:82` — no stray rows survive for other users; identity `:49-51`; unit `UserLifecycleTests.cs:54`, `:64-66` | ✅ |
| USR-31 invisible to every read path | not found everywhere | GET `UserRemovalTests.cs:95` — 404; list `:112` and `UserCatalogueTests.cs:134`; specification level `UserSpecificationTests.cs:149` — `Assert.Null(found)` | ✅ |
| USR-32 delete an already-deleted user | `204`, not 404 | `UserRemovalTests.cs:125` — `Assert.Equal(HttpStatusCode.NoContent, …)`; `:142` — `Assert.Equal(Kickoff.UtcDateTime, stored.DeletedAt)` (tombstone not moved) | ✅ |
| USR-33 delete a never-registered ref | not found | `UserRemovalTests.cs:154-155` — 404 + count unchanged; `:163` for an unusable ref | ✅ |
| USR-34 PUT on a deleted ref | resurrect, face mandatory, **`201`** + `Location` | `UserResurrectionTests.cs:43` — `Assert.Equal(HttpStatusCode.Created, …)`; `:46` — same row id; `:51` — `Assert.Null(stored.DeletedAt)`; `:69` — `Assert.Equal("/api/users/TICKET-1", response.Headers.Location?.OriginalString)`; face mandatory `:117`, `:121-122`; unit `UserLifecycleTests.cs:92`, `:114-116`, `:140-141` | ✅ |

### P1: Look up a spectator

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-35 GET active user | `200` + identity, timestamps, face fingerprint | `UserLookupTests.cs:28` — `Assert.Equal(HttpStatusCode.OK, …)`; `:31-36` — id, externalRef, name, accessCode, `createdAt`, `updatedAt` against a controlled clock; `:46-61` — all four face fields equal the registration's | ✅ |
| USR-36 unregistered or deleted ref | not found | `UserLookupTests.cs:97` — 404 for unregistered; `UserRemovalTests.cs:95` — 404 for deleted; `:105` for an unusable ref | ✅ |
| USR-37 no picture bytes in the response | absent | `UserLookupTests.cs:78` — `Assert.False(body.TryGetProperty("facePicture", out _))`; `:81-85` — `Assert.DoesNotContain(Convert.ToBase64String(picture.Content), payload)` | ✅ |

### P1: Operational foundation

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-38 schema by EF migration at startup, never `EnsureCreated()` | tables present, migration recorded | `UserSchemaTests.cs:99-100` — `Assert.Contains(UserConfiguration.TableName, tables)` and `FacePictureConfiguration.TableName`; `:108-111` — `Assert.Contains(applied, m => m.EndsWith("AddUserRegistry"))` against `__EFMigrationsHistory`; the harness creates the schema by starting the app's own `Migrate()` (`PostgresFixture.cs:37-39`). `EnsureCreated` appears nowhere in the repository (verified by grep; no assertion pins its absence — see Note 5) | ✅ |
| USR-39 DB unavailable → ProblemDetails, no leaks | 503 problem body, no connection details | `ErrorHandlingTests.cs:117-145` — PUT, GET and DELETE each go through `AssertServiceOutageAsync` (`:173-183`): `Assert.Equal(HttpStatusCode.ServiceUnavailable, …)`, `application/problem+json`, `Assert.Equal(503, status)`, exact `DatabaseUnavailableTitle`/`Detail`; `:147-169` — host, port, database, username, password and `Npgsql`/`Exception`/`stacktrace`/`   at `/`Host=`/`Password=` all asserted absent | ✅ |
| USR-40 request span + distinct child normalization span with duration | child of the request, own span id, measured | `UserObservabilityTests.cs:172-173` — `Assert.Equal(ActivityKind.Server, …)`, `Assert.Equal("PUT /api/users/{externalRef}", requestSpan.DisplayName)`; `:184-186` — `Assert.Equal(NormalizationSpanName, normalizationSpan.DisplayName)`, `Assert.Equal(requestSpan.SpanId, normalizationSpan.ParentSpanId)`, `Assert.NotEqual(requestSpan.SpanId, normalizationSpan.SpanId)`; `:194-197` — `Duration > TimeSpan.Zero` | ✅ |
| USR-41 metric of normalization duration and byte size | both recorded | `UserObservabilityTests.cs:205-207` — `Assert.Single(_durationMeasurements)` then `Assert.True(duration > 0, …)`; `:213-215` — `Assert.Equal(_storedFaceByteSize, byteSize)`, i.e. the measurement equals the size actually stored, not merely "a number" | ✅ (see Note 4) |

### P2: Browse the registry

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| USR-42 paged response with next-page information | items + page + pageSize + hasMore | `UserCatalogueTests.cs:50-53` — `Assert.Equal(registered, IdsOf(body))`, `page == 1`, `pageSize == DefaultPageSize`, `hasMore == false`; `:63-65` — `hasMore == true` when a page follows | ✅ |
| USR-43 oversized page size clamped | clamped to the maximum, not honoured | `UserCatalogueTests.cs:118-121` — asks for `MaxPageSize + 5_000`, `Assert.Equal(ListUsersService.MaxPageSize, body.GetProperty("pageSize").GetInt32())` | ✅ |
| USR-44 stable, total ordering | nobody skipped or repeated | `UserCatalogueTests.cs:92-93` — `Assert.Equal(registered, seen)` and `Assert.Equal(seen.Count, seen.Distinct().Count())` across a boundary at 3-over-2; `:106-107` — identical order at pageSize 3 and pageSize 1; specification level `UserSpecificationTests.cs:253-255` | ✅ (see Note 6) |
| USR-45 deleted excluded; empty registry → empty page | listed set excludes the tombstone; 200 with no items | `UserCatalogueTests.cs:134` — `Assert.Equal(new[] { registered[0], registered[2] }, IdsOf(body))`; `:142-146` — 200, `Assert.Empty(items)`, `hasMore == false`; `UserSpecificationTests.cs:270` | ✅ |

**Status**: ✅ 45/45 criteria carry a citation whose asserted value matches the spec-defined
outcome. 0 uncovered. 0 mismatched. 6 precision notes recorded below.

---

## Edge Cases

| Edge case | Evidence | Result |
| --- | --- | --- |
| Unicode (Arabic-Indic) digits rejected | `AccessCodeTests.cs:64-66` — `Assert.Equal(AccessCode.Errors.MustBeNumeric, …)`; API `UserRegistrationTests.cs:220` | ✅ |
| Name trimmed before the length check; whitespace-only blank | `UserCreateTests.cs:107` — `Assert.Equal("Ada Lovelace", user.Name)`; `:117-118` — a 100-char name with padding is accepted; `:98-99` — whitespace-only is `NameRequired` | ✅ |
| Reserved characters other than `/` round-trip through PUT/GET/DELETE | `UserExternalRefTests.cs:28-35`, `:52-55`, `:72-73` over `%`, space, `+`, `#`, non-ASCII | ✅ |
| `/` taken as its escaped text (A-15 amendment) | `UserExternalRefTests.cs:96-102` — 201, `Assert.Equal("TICKET%2F2026", body externalRef)`, `Assert.Null(await StoredUserAsync("TICKET/2026"))`, `Assert.NotNull(await StoredUserAsync("TICKET%2F2026"))`. The amendment is honestly stated in the spec (A-15) and asserted rather than hidden; the test comment explains it will fail loudly if a framework change starts decoding `%2F` | ✅ |
| Refs differing only by case are two spectators | `UserLookupTests.cs:119-121`; `UserSchemaTests.cs:178` — `Assert.Equal(2, await verification.Users.CountAsync())`; unit `ExternalRefTests.cs:68` | ✅ |
| Zero-content / 1×1 image rejected by the **floor**, not the size band | Only a 320×240 fixture exists (`GuardTests.cs:122-124`). No 1×1 or zero-content fixture | ⚠️ Note 1 |
| Grayscale, **CMYK** or ICC-profiled source → sRGB derivative | Grayscale `ImageTests.cs:85` — `Assert.Equal(3, components)`; ICC `:109` — `Assert.NotEqual(sourceProfile, ColourProfile(derivative))`; convergence `:122-123` — every fixture leaves with the *same* profile. **No CMYK fixture** | ⚠️ Note 2 |
| Cannot land in the band → reject rather than store outside it | `EncodingTests.cs:106` — `CannotReachMinimumSize`; `:137` — `CannotReachMaximumSize`; `:118` — `Assert.False(result.IsT0)` | ✅ |
| Identical normalized bytes → hash unchanged, `UpdatedAt` unmoved | `UserAmendmentTests.cs:132-136`; determinism `EncodingTests.cs:207`, `:219`, and the golden hashes `:232-238` | ✅ |

---

## Discrimination Sensor

**Depth**: P0-full (14 mutations — the spec's own worst failure mode is a spectator stopped at a
turnstile). Every mutation was applied in the working tree, built with `--no-incremental`, run
against the tests that claim the criterion, then reverted with `git checkout --`. `git status`
was verified clean after each.

| # | Criterion | File | Mutation | Result |
| --- | --- | --- | --- | --- |
| 1 | USR-15 (lower bound) | `Infrastructure/FaceImageOptions.cs:31` | `MinByteSize = 40 * 1024` → `0`, i.e. an upper-bound-only band | ✅ Killed — 2 failures (`Photograph_too_uniform_to_reach_the_minimum_size_is_rejected` / `…never_stored_below_the_band`) |
| 2 | USR-13 | `SkiaFaceImageNormalizer.cs:260-264` | `DecodeUpright` returns the decoded bitmap without `ApplyOrigin` — EXIF rotation dropped | ✅ Killed — 5 failures |
| 3a | USR-17 | `SkiaFaceImageNormalizer.cs:125-126` | floor checked against `encoded.Width/Height` instead of the oriented pair | ⚠️ **Survived — equivalent mutant.** `Math.Min`/`Math.Max` over a pair are invariant under the swap `Orient` performs, so the two forms give an identical verdict by construction. Not a test weakness; see Note 3 |
| 3b | USR-17 | `SkiaFaceImageNormalizer.cs:124-135` + `FitToCeiling` | floor rejection removed and replaced with an **upscale** into compliance — the exact fault USR-17 forbids | ✅ Killed — 2 failures |
| 4 | USR-20 | `SkiaFaceImageNormalizer.cs:115-116` | decode-pixel cap moved to *after* `DecodeUpright`, i.e. after the buffer is allocated | ✅ Killed — `Image_declaring_more_pixels_than_the_cap_is_refused_before_a_buffer_is_allocated` |
| 5 | USR-06 | migration + `UserConfiguration.cs:114` + model snapshot | `.HasFilter("\"DeletedAt\" IS NULL")` dropped from the access-code index — a removed spectator's PIN never returns to the pool | ✅ Killed — 3 failures (`Deleted_spectators_access_code_can_be_reused`, `Access_code_uniqueness_is_scoped_to_spectators_that_are_not_deleted`, `Removed_spectators_access_code_can_be_claimed_by_another_spectator`) |
| 6 | A-7 / USR-34 | migration + `UserConfiguration.cs:108` + model snapshot | `IX_users_ExternalRef` made partial on `DeletedAt IS NULL` — resurrection can no longer find the tombstone | ✅ Killed — 2 failures (`Deleted_spectators_external_reference_stays_reserved`, `External_reference_uniqueness_applies_to_every_row`) |
| 7 | USR-30 | `Domain/User.cs:136` | `Picture = null;` removed from `MarkDeleted` — the biometric survives the removal | ✅ Killed — 2 unit + 5 integration failures, including the direct `face_pictures` read |
| 8 | USR-26 | `Domain/User.cs:117-118` | `if (changed)` guard removed — `UpdatedAt` advances on a byte-identical re-upsert | ✅ Killed — 3 unit + 1 integration failure |
| 9 | A-1 / OD-4 | `Domain/Specs/ActiveUsersPagedSpec.cs:22` | `.Include(user => user.Picture)` added to the list specification — 200 KB back on every row | ✅ Killed — `Listing_the_catalogue_never_reads_the_face_picture_table` (the SQL recorder; the JSON-shape test alone did **not** catch it, exactly as the design claims) |
| 10 | USR-01 (**must-fail control**) | `UpsertUserService.Endpoint.cs:43` | `Results.Created(…)` → `Results.Ok(…)` | ✅ Killed — 29 of 135 user integration tests |
| 11 | USR-34 (amended) | `UpsertUserService.cs:209` | resurrection returns `UserUpdated` (200) instead of `UserCreated` (201) | ✅ Killed — 3 failures |
| 12 | USR-19 | `UpsertUserService.Endpoint.cs:71-72` | request limit set to `MaxUploadBytes` directly, ignoring base64's 4/3 inflation | ✅ Killed — `Picture_at_the_accepted_maximum_still_fits_inside_the_request_limit` |
| 13 | USR-43 | `ListUsersService.cs:25` | `Math.Clamp(…, 1, MaxPageSize)` → `Math.Max(…, 1)` — oversized page sizes honoured | ✅ Killed — `Page_size_above_the_permitted_maximum_is_clamped_to_it` |
| 14 | USR-16 (derivative floor) | `SkiaFaceImageNormalizer.cs:191-198` | the never-shrink-below-the-floor bail removed, so the ladder may downscale a derivative under 480/640 | ✅ Killed — `Photograph_that_cannot_reach_the_band_without_falling_below_the_floor_is_rejected` |

**Result**: 14 injected, **13 killed, 1 survived (provably equivalent)**. The must-fail control
(#10) failed loudly, confirming the mutations were landing on live code paths and not on a
detached copy.

---

## Precision Notes (non-blocking)

1. **No 1×1 or zero-content fixture.** The spec's edge case *"a valid image of zero bytes' content
   or 1×1 pixels SHALL be rejected by the resolution floor (USR-17), not by the size band"* is
   asserted only through a 320×240 fixture. The distinction the edge case cares about — *which*
   guard fires — is untested at the degenerate end. Low risk (a 1×1 image trivially fails
   `Math.Min < 480`), but the assertion is absent.

2. **No CMYK fixture.** The edge case names *"grayscale, CMYK or ICC-profiled"*. Grayscale and ICC
   are both covered with value-level assertions; CMYK is covered by neither a fixture nor an
   assertion, and `SkiaFaceImageNormalizer.cs:247` names it in a comment. A CMYK JPEG is a real
   thing integrators hold, and SkiaSharp's handling of one is untested here.

3. **The oriented-vs-encoded floor distinction does not exist.** `SkiaFaceImageNormalizer.cs:120-123`
   and `tests/assets/PROVENANCE.md` both claim the floor must be judged on oriented dimensions or a
   portrait photograph is "judged as the landscape image it is not". Because the check is
   `Math.Min(w,h) < MinShortEdge || Math.Max(w,h) < MinLongEdge`, it is invariant to the swap, and
   Mutation 3a confirmed it empirically. The distinction *does* hold for USR-13 (the derivative's
   own orientation, covered at `ImageTests.cs:56-57`) and for the width/height *reported in the
   rejection message*, which nothing asserts. The comment overstates; no code change is needed.

4. **The USR-41 metrics are recorded but never exported.** `Program.cs` wires
   `.WithTracing(… .AddSource(SkiaFaceImageNormalizer.ActivitySourceName) …)` but has no
   `.WithMetrics(…)` pipeline at all, so in production the two histograms have no reader.
   USR-41 says "record a metric", which is satisfied, and the device slices export no metrics
   either — so this is a consistent Phase-2 gap rather than a defect of this feature. Worth
   deciding deliberately before AD-014's latency budget is used in anger.

5. **"Never by `EnsureCreated()`" is proven by inspection, not by assertion.** `EnsureCreated`
   appears nowhere in the repository, and Mutation 5 incidentally proved the migration path is the
   live one (EF raised `PendingModelChangesWarning` when the snapshot diverged). No test would
   fail if someone added an `EnsureCreated()` call alongside the migration.

6. **USR-30's "same transaction" and USR-44's "total order" are argued, not exercised.** The
   removal is one `SaveChangesAsync` (`RemoveUserService.cs:53`), but unlike the create path —
   which has a real `CHECK (false)` failure injection for USR-10 — nothing forces the picture
   delete to fail. USR-44's totality rests on `OrderBy(user => user.Id)` over the primary key,
   proven at n=3 against a static catalogue; no test registers a spectator mid-pagination. Both
   are sound by construction; neither has a sensor.

**Spec bookkeeping drift** (documentation only): the § Implicit-Requirement Dimensions Sweep cites
`USR-24` for the no-op re-upsert (actually USR-26), `USR-30` for repeated `DELETE` (actually
USR-32), and `USR-42/USR-43` for tracing and metrics (actually USR-40/USR-41).
`tests/assets/PROVENANCE.md` refers to `SkiaFaceImageNormalizerHashTests`, which is named
`SkiaFaceImageNormalizerEncodingTests`.

---

## Fixture-Bank Honesty Check

The bank is procedurally generated (fBm noise), so no fixture carries authentic camera encoder
output. **The tests do not over-claim.** `tests/assets/PROVENANCE.md` states the limit in its own
words — *"They carry no authentic camera encoder output … A green suite here is not proof of
real-world coverage"* — ties it to A-13's standing Phase 3 obligation, and explains why fractal
noise rather than gradients is the right entropy regime for an entropy-sensitive pipeline. The
one deliberate compromise is documented at `EncodingTests.cs:78-80`: the downscale branch does not
fire at the shipped 200 KB ceiling with the current large fixture, so it is exercised through a
tightened 60 KB configuration. That is stated in the test, not hidden, and Mutation 14 confirms the
branch is genuinely sensed.

**A-13 Phase 3 obligation**: noted and correctly carried in the spec (§ Open questions) and in
`FaceImageOptions.cs:7-11`, which holds the envelope as configuration precisely so Phase 3 can
correct it without a code change. Not a defect of this feature.

---

## Code Quality

| Principle | Status |
| --- | --- |
| Minimum code — no features beyond the spec | ✅ |
| No abstractions for single-use code | ✅ — three per-slice `UserResponse` records rather than one shared DTO, matching the existing device slices |
| Surgical changes — only files the tasks required | ✅ — `Program.cs`, `AppDbContext.cs` and `ErrorHandlingTests.cs` are the only pre-existing files touched |
| Matches existing patterns/style | ✅ — `Create`/`FromPersistence` value objects, named-index constants, `OneOf` result arms, `.Match()` in the endpoint with no `if` (AD-003) |
| Spec-anchored outcome check | ✅ — 45/45 |
| Per-layer coverage: domain 1:1 with ACs; routes happy + edge + error | ✅ — all four routes have 201/200/204/400/404/409/413/503 paths asserted |
| Every test maps to a spec AC, edge case or Done-when criterion | ✅ — section comments carry the USR/A- id; no unclaimed test found |
| Documented guidelines followed | ✅ — `CLAUDE.md` (test-project-declares-level, AD-026), `docs/test-patterns.md` (behaviour-based naming; the trace-correlation rule is followed literally at `UserObservabilityTests.cs:41`), `docs/slice-anatomy.md` (three files per slice) |

---

## Gate Check

- **Build**: `dotnet build HikvisionReplicator.slnx --no-incremental` → **0 Errors, 14 Warnings**,
  matching the pre-existing baseline (10 CA + 2 CS0618 + 2 NU1903). `--no-incremental` used
  throughout, per L-007.
- **Unit**: `dotnet test src/HikvisionReplicator.Tests` → **278 passed, 0 failed, 0 skipped**
- **Integration**: `dotnet test src/HikvisionReplicator.IntegrationTests` → **223 passed, 0 failed,
  0 skipped**
- **E2E**: 17 tests, deliberately excluded from CI (needs a live API on :5000) — not re-run here.
  **Superseded**: the E2E project was deleted later on this branch (**AD-035**), so the merged
  state has no e2e level and this line describes a tree that no longer exists.
- **Test integrity**: no test count decreased; no assertion was weakened; no test is skipped.
- Re-run **after** all mutations were reverted: identical results, `git status` clean at
  `944922f`.

---

## Requirement Traceability Update

| Requirement | Previous | New |
| --- | --- | --- |
| USR-01 … USR-11 | Implementing | ✅ Verified |
| USR-12 … USR-22 | Implementing | ✅ Verified |
| USR-23 … USR-28 | Implementing | ✅ Verified |
| USR-29 … USR-34 | Implementing | ✅ Verified |
| USR-35 … USR-37 | Implementing | ✅ Verified |
| USR-38 … USR-41 | Implementing | ✅ Verified |
| USR-42 … USR-45 | Implementing | ✅ Verified |

---

## Summary

**Overall**: ✅ Ready

**Spec-anchored check**: 45/45 ACs matched the spec-defined outcome · 6 precision notes flagged
**Sensor**: 14 mutations, 13 killed, 1 survived (equivalent)
**Gate**: 501 passed, 0 failed, 0 skipped · build 0 errors, 14 baseline warnings

**What works**: The write path is genuinely discriminating where the product's worst failure mode
lives. The 40 KB *lower* bound is asserted in its own right and killed a fault that an
upper-bound-only pipeline would have shipped. Both unique indexes are asserted from both sides —
each one's filter has a test that fails when the other's is copied onto it. The biometric's
destruction is read out of `face_pictures` directly rather than inferred from the API. A-1's
"no bytes in the catalogue" is enforced by a SQL recorder, which was the *only* thing that caught
an added `.Include`. Golden derivative hashes make normalization determinism — the invariant
USR-26 stands on — a byte-level assertion rather than a claim.

**Issues found**: no failing criterion, no surviving non-equivalent mutant. Six precision notes,
all documented above and none blocking: two missing degenerate fixtures (1×1, CMYK), one comment
that overstates a distinction the code cannot make, metrics recorded without an exporter,
`EnsureCreated` absence proven by inspection, and two invariants argued rather than sensed.

**Next steps**: merge. Carry Notes 1, 2 and 4 into the Phase 3 `isapi-device-client` work, where
A-13's standing verification obligation already requires exercising the normalizer against real
camera files; a CMYK and a 1×1 fixture belong in that same pass.

# replication-worker Validation

**Date**: 2026-10-26
**Spec**: `.specs/features/replication-worker/spec.md` (35 criteria, WRK-01…WRK-35)
**Diff range**: `171c298..HEAD` on `feat/replication-worker`: 28 commits, `e8081eb`…`944b426`
**Verifier**: independent sub-agent (author ≠ verifier, AD-028). Coverage was re-derived from
`spec.md` and the diff, evidence-or-zero. Task checkboxes and commit messages were not taken as
evidence.

**Verdict (iteration 3, current): PASS ✅.** Iteration 2's one remaining gap (Fix 5) is closed by
`6d93588`. The relaxed assertion in `A_deleted_reader_is_called_no_more` matches design.md:234
exactly and lost no discrimination: the old strict form did not kill the relevant mutant either.
The production reorder keeps WRK-21 and WRK-33. The gate is green on three consecutive full
integration runs. No gaps remain. See § Iteration 3.

<details><summary>Iteration 2 verdict (superseded)</summary>

**Verdict: FAIL ❌.** All four iteration-1 gaps are closed: M15, M16,
M20 and M21 are now killed. The production fix `f082fae` is sound, and its regression mutant M22
is killed 5 of 5 runs. One gap remains, and it is minor and test-only. The gate is not reliably
green: `DeviceChurnTests.A_deleted_reader_is_called_no_more` failed in 1 of 3 full integration
runs. The test asserts more than design.md:234 promises, and the reload-to-call gap in
`DeviceLoop` lets a call start just after a delete commits. See § Iteration 2.

</details>

**Correction to iteration 1.** Iteration 1 said "No production defect was found." That was
wrong. The supervisor had a WRK-34 race: it read the debts, then the catalogue. A reader
registered between those two reads got a loop with no debt recorded against it, so its spectator
could be enrolled twice. The race was found while F4 was being written and fixed in `f082fae`
(§ Iteration 2.3).

<details><summary>Iteration 1 verdict (superseded)</summary>

**Verdict: FAIL ❌.** Every criterion's main outcome is implemented and asserted, and the gate is
green. Four behaviour-level mutants survive, though. Three of them sit on parts of a criterion
that no test asserts (WRK-03's claim timestamp, WRK-26's interval, WRK-31's idle wait), and one
on the WRK-34 loop gate. `validate.md` § 5 says surviving mutants become fix tasks before the
feature is marked done. All four gaps are minor and test-only. No production defect was found
*(corrected above)*.

</details>

---

## Iteration 3 — re-verification of Fix 5 (2026-10-26)

**Scope**: `0cab553..6d93588`, one commit. It reorders the device reload in
`Api/Infrastructure/Worker/DeviceLoop.cs` and relaxes one assertion in
`IntegrationTests/DeviceChurnTests.cs`. Paths are relative to `src/`. Every claim below was re-run.
The commit message was not taken as evidence.

### 3.1 The relaxed assertion against WRK-25 and design § Risks

- **What the contract promises.** WRK-25 says no further call after a delete. design.md:234 records
  that this cannot be absolute: no call **starts once the delete is observed**, since the device
  is reloaded right before the call. A call already on the wire completes, and its outcome is
  discarded (WRK-24).
- **What the test can observe.** The fake stamps a call when it **starts**
  (`Api/Infrastructure/FakeDeviceClient.cs:174-179`, `timeProvider.GetUtcNow()` before the latency
  wait). So a call that was on the wire at the delete is never counted. The only call that can
  start after `deletedAt` is the item the loop already holds, whose reload
  (`DeviceLoop.cs:226-230`) ran just before the delete committed. One loop is serial, so it holds
  at most one item. The cascade (`Api/Infrastructure/ReplicationConfiguration.cs:126`) leaves
  nothing for a later claim. The supervisor also stops the loop (`ReplicationWorker.cs:169-180`).
- **Judgement: exact, not weaker.** `<= 1` (`DeviceChurnTests.cs:85`) is the precise size of the
  window the design accepts. A second call can only mean the loop took new work after the delete,
  and that is what the assertion message names. The old `DoesNotContain` asserted more than the
  design promises. That was iteration 2's finding, and it was the source of the 1-in-3 flake.

### 3.2 The production reorder

| Concern | Evidence | Status |
| --- | --- | --- |
| WRK-21: tombstone decided before the face is read | `DeviceLoop.cs:213` (`BeforeCall`) still precedes `:220` (`LoadPictureAsync`) | ✅ |
| WRK-33: address and credentials read for this call | Reload at `:226-228`, after the user and picture loads, before `CallAsync` at `:240`. `A_reader_whose_address_and_password_changed_is_reached_at_the_new_ones` (`DeviceChurnTests.cs:201-226`) passes and kills M23 below | ✅ |
| WRK-25: reload sits right before the call | Nothing between `:230` and `:240` but building the endpoint and decrypting | ✅ Window narrowed, as design.md:234 asks |
| New path: tombstoned spectator on a reader deleted since the claim | It now reaches `SupersedeWithoutCallAsync` before the reload. That save then hits a cascade-deleted row, and `ReplicationOutcomeRecorder.cs:187` catches the concurrency failure and discards it (WRK-24). No call is made | ✅ No fault and no call |

**Precision note (not a gap).** design.md § DeviceLoop step 4 (line 128) still lists "Load device"
before "load user". The binding text is the Risks row (`:234`), which the new order satisfies more
tightly. Refresh the step order the next time design.md is touched.

### 3.3 Discrimination

Each mutant was injected in the working tree and run, then removed with `git checkout -- src/`.
`git status` was clean afterwards.

| ID | Mutation | Result | Killed by |
| --- | --- | --- | --- |
| M23 | Reload and `ReaderGone` check removed. The device is read once per reader and reused for every later call | ✅ Killed 3/3 | `DeviceChurnTests.A_reader_whose_address_and_password_changed_is_reached_at_the_new_ones`. `A_deleted_reader_is_called_no_more` survives |
| M24 | The loop ignores `Cycle.ReaderGone` and keeps claiming (`DeviceLoop.cs:82` `return` → `continue`) | ⚪ Equivalent: survives churn ×3 and the full suite (340/340) | — |

- **No discrimination lost by the relaxation.** The old strict assertion (`<= 0`, equivalent to the
  former `DoesNotContain`) was put back temporarily and run 6× under M23. It passed 6/6. So the old
  test did not kill M23 either, and its only extra power was the flake.
- **Why M24 is equivalent.** After the delete, the cascade removes every row for that reader, so a
  loop that keeps claiming finds nothing. It idles until the supervisor stops it within one tick.
  It cannot start a call. The `ReaderGone` exit is an early stop, not the WRK-25 guard. The guard is
  the reload plus the cascade.
- **Not discriminated, by nature: the reorder itself.** Moving the reload after the user and picture
  loads only narrows a timing window that was already accepted. No deterministic test can tell the
  iteration-2 order from the new one, and none is required, because both satisfy design.md:234.

### 3.4 Gate

| Check | Result |
| --- | --- |
| `dotnet build HikvisionReplicator.slnx --no-incremental` | 0 errors, 17 warnings (baseline, CS0618 Testcontainers ctor) |
| `dotnet test src/HikvisionReplicator.Tests` | 465 passed, 0 failed |
| `dotnet test src/HikvisionReplicator.IntegrationTests` | 340/340 passed on **three** consecutive full runs (two before the mutants, one after a clean rebuild) |

### 3.5 Outcome

Fix 5 is closed. All 35 criteria are verified, and iterations 1–2 have no open survivors. M24 is
classified as equivalent. **PASS ✅.**

---

## Iteration 2 — re-verification (2026-10-26)

**Scope**: `a59b39e..HEAD`. That is four test-only fixes, F1 `f3b4272` (WRK-26), F2 `85e7bd4`
(WRK-03), F3 `e5f4622` (WRK-31) and F4 `9ebe6ac` (WRK-34), plus one production change,
`f082fae`, with its regression test. Paths are relative to `src/`, and **IT** =
`HikvisionReplicator.IntegrationTests/`. Commit messages were not taken as evidence: every
claim below was re-run.

### 2.1 Gate

| Gate | Result |
| --- | --- |
| `dotnet build HikvisionReplicator.slnx --no-incremental` | 0 errors, **17 warnings** (baseline 17) |
| `dotnet test src/HikvisionReplicator.Tests` | **465 / 0 failed / 0 skipped** |
| `dotnet test src/HikvisionReplicator.IntegrationTests`, run 1 | **339 / 1 failed** / 0 skipped. The failure is `DeviceChurnTests.A_deleted_reader_is_called_no_more` (`IT/DeviceChurnTests.cs:78`) |
| same, run 2 | **340 / 0** |
| same, run 3 (added to characterise the flake) | **340 / 0** |

Integration grew 337 → 340 (+3: F3, F4, and the `f082fae` regression test). F1 and F2 rewrote
existing tests in place. No test was deleted or skipped. After the sensor, the tree was rebuilt
clean and `git status src` was empty after every mutant.

**The flake.** It does not come from this iteration's changes. The failing assertion is
`DoesNotContain(call => call.DeviceId == reader && call.At > deletedAt)`, where `deletedAt` is
read after the `DELETE` returns 204. `DeliverAsync` reloads the device at `DeviceLoop.cs:210-212`,
then reads the user, loads the face picture and decrypts the password. Only then does it call the
device. A delete that commits inside that gap produces one call stamped after `deletedAt`. The fake
stamps a call when it starts, even on a cancelled token (`FakeDeviceClient.cs:174`). The
supervisor cancelling the removed reader's loop (`ReplicationWorker.cs:189-191`) is the only
other guard, and on a 20 ms poll it does not reliably land inside a gap of a few milliseconds.

Reproduction:
- 12 of 12 isolated runs pass.
- With a 60 ms delay that ignores the token, inserted directly before `CallAsync` (temporary, reverted),
  2 of 3 runs fail on the same assertion.
- With the delay placed before the user read, all runs pass. Those awaits honour the stopping
  token, so a cancel landing there throws before any call starts.

The behaviour matches design.md:234's interpretation, "no call starts after the delete is
*observed* (device reloaded right before the call)". The test asserts something stricter: no
call after the delete *committed*. `DeviceLoop` has not changed since `5aa2c74`, and `f082fae`
touches only the supervisor. → Fix 5.

### 2.2 Gaps 1–4 re-checked

Each original mutant was re-applied with the same exact-string edit as in iteration 1. Each was
built, run against its new test, and reverted.

| Gap | Mutant | Result | Killed by | Asserts the spec outcome? |
| --- | --- | --- | --- | --- |
| 1 · WRK-26 | **M15**: histogram measures claim → success | ✅ Killed | `IT/WorkerObservabilityTests.cs:120` (`A_delivery_records_the_seconds_from_queueing_to_success…`), failing at `:157` `Assert.Equal(3.0 + 7.0, duration.GetHistogramSum())`. Claim → success gives 3 + 4 = 7 | ✅ Yes. Two rows are created at Kickoff, and the second waits 3 s in the queue, so creation → success (3 s + 7 s) and claim → success (3 s + 4 s) differ. `:154-156` also pins lane `Live`, op `Add` and count 2 |
| 2 · WRK-03 | **M20**: claim stamped with `DateTime.UtcNow` | ✅ Killed | `IT/DeliveryTests.cs:81` (`Work_is_in_progress_while_its_call_is_with_the_reader`), failing at `:100` `Assert.Equal(Kickoff.AddMinutes(1).UtcDateTime, inHand.UpdatedAt)` | ✅ Yes. The second row is claimed after the injected clock moves to +1 min, and `:99` pins `CreatedAt == Kickoff`, so the claim's own stamp is told apart from both creation time and wall time |
| 3 · WRK-31 | **M21**: idle cycle skips the poll wait | ✅ Killed | `IT/DeliveryTests.cs:486` (`An_idle_reader_is_checked_once_a_poll_interval_rather_than_continuously`), failing at `:513` `Assert.InRange(looks, expected - 2, expected + 2)` | ✅ Yes. It counts claim queries (the only query filtering on `"NextAttemptAt" <=`), which is the "wait one idle poll interval between checks" rate. `:514` covers "no device call" |
| 4 · WRK-34 | **M16**: supervisor gate removed | ✅ Killed **3/3** | `IT/BackfillDeliveryTests.cs:327` (`A_reader_whose_expansion_failed_is_not_served_until_its_debt_is_expanded`), failing at `:356` `Assert.Equal(0, hook.CallsWhenExpanded)` | ✅ Yes. `:357-358` assert the spec outcome, "queued once": a single `Live` row and a single `TICKET-1` call. `:356` is the deterministic guard. The other test in the class (`:233`) passed under M16, as expected |

Line numbers name the assertion each mutation must break. The run recorded each killing test
by name as `Failed`. Its assertion line was recorded verbatim for M16 (`:356`) and M22 (`:263`)
and inferred for M15, M20 and M21: each of those tests has exactly one assertion that the
mutation's value can falsify.

**Soundness and flake risk of the new tests:**

- **F1, F2**: frozen-clock steps, each with a margin of one reader latency (500 ms and 1 s). The
  test must move the clock while a call is out. The only way to lose is for the test thread to
  take longer than the latency between seeing the call and freezing, so the risk is low.
- **F3, fixed window** (`DeliveryTests.cs:486-515`): 250 ms poll, 2 s window, bound 8 ± 2 looks.
  A real time window is justified here because the criterion is a rate, and the test documents
  that. The mutant produces hundreds or thousands of looks, so the kill is far outside the bound.
  The lower bound (6) fails only if each idle cycle averages more than about 333 ms, roughly
  80 ms of overhead per two-query cycle. That is unlikely, but it is the one new assertion
  sensitive to a loaded machine. ⚠️ Watch it, no fix asked. It passed in every run here: 3 full
  runs and 5 targeted runs.
- **F4, "refuse two saves"**: sound. The arrangement is committed by the test, not inside a tick,
  so it can land between a tick's catalogue read and its debt read. That tick has the reader in
  `owed` but not in `readers`, so the gate is not what holds it. A second refusal guarantees one
  tick with the reader in both, so the gate is always exercised. `Assert.Equal(2, hook.Refused)`
  is deterministic. The SaveChanges interceptor picks expansions by a `BackfillIntent` modified to
  `Expanded`, which matches nothing else. This is a declared deviation from Fix 4's "refuse once",
  and it is a correct one: under `f082fae`'s read order, one refusal could fall wholly on the
  ungated tick.
- All five new tests passed **5 of 5** targeted runs on HEAD, and in all 3 full runs.

### 2.3 `f082fae` reviewed as production code

**Defect.** Before `f082fae`, `ExpandBackfillsAsync` read the pending debts, and then
`SyncLoopsAsync` read the catalogue. A registration (the device and its `BackfillIntent`) that
committed between the two reads was in `readers` but not in `owed`, so it got a loop. That loop
delivered the pending live row, and the next tick's expansion no longer saw a *pending* live
row for the spectator (REP-19), so it queued a Bulk duplicate. That breaks WRK-34 ("queued
once"). It is a real production defect, not a test artefact.

**The fix is correct under PostgreSQL read committed.**
1. The device and its intent commit in **one transaction**. `ReplicationFanOut.HandleAsync(DeviceRegistered)`
   adds the intent during domain-event dispatch, which runs inside the same
   `AppDbContext.SaveChangesAsync` before `base.SaveChangesAsync` (`AppDbContext.cs:70-77`,
   `ReplicationFanOut.cs:103-110`).
2. The catalogue read (T1) and the debt read (T2 > T1) are separate statements in separate
   scopes, so each takes its own snapshot. Any reader visible at T1 committed before T1, so its
   intent is visible at T2. At T2 the intent is either still `Pending`, so it is in `owed`, or
   already `Expanded` by another instance, which correctly needs no gate. The debt read is
   therefore a superset of what the catalogue read admits.
3. A reader registered after T1 is absent from `readers`, and gets its loop on the next tick,
   after its expansion.
4. `DeviceRegistered` is the only producer of an intent (`ReplicationFanOut.cs:107`), so no debt
   can appear for a reader whose loop is already running.

**New windows the change opens, judged harmless:**
- *Reader deleted between T1 and `SyncLoopsAsync`.* The reader is in `readers`, and its intent
  was cascade-deleted (`BackfillIntentConfiguration.cs:56`), so it is not in `owed`. A loop starts
  for a reader that is gone. Its rows went with it (REP-16 cascade), so it claims nothing, and any
  claim that survived meets the device reload (`DeviceLoop.cs:210-212`) → `ReaderGone`. The next
  tick stops the loop. No call results.
- *Delete committing during the expansion phase of a tick.* The catalogue snapshot is now older
  by the length of the expansion, so a removed reader's loop is cancelled up to one tick later
  than before. WRK-10's "within one poll interval" is unchanged in the worst case, because a
  delete just after the catalogue read was always seen only on the next tick. WRK-25 is still
  guarded per call by the reload. This lengthens how long the cancel (the secondary guard) takes
  to arrive. It does not cause the run-1 flake, whose window is reload → call (§ 2.1).
- *Expansion fails.* The reader stays in `owed`, so the gate holds it, which is exactly what F4
  proves (M16 killed).

**M22 — read order swapped back (debts before catalogue).** ✅ Killed **5 of 5** by
`IT/BackfillDeliveryTests.cs:233` (`A_reader_registered_while_the_worker_reads_its_debts_is_not_served_before_they_are_expanded`),
failing at `:263` `Assert.Single(queue)`: "The collection contained 2 items". The second row is
created about 0.58 s after the first, the next tick's Bulk duplicate. The result is
deterministic, not timing-dependent. The `AfterDebtRead` interceptor (`:192-222`) runs the
registration *inside* the worker's debt-read command (`ReaderExecutedAsync`), so the tick cannot
go on until the registration has committed. The landing is forced rather than raced. The test
runs on an isolated database, so the first `backfill_intents` read after `Arm()` is the
supervisor's debt read.

### 2.4 Previously killed mutants, re-run after `f082fae`

| # | Mutation | Result |
| --- | --- | --- |
| M1 | Live/Bulk claim order swapped | ✅ Killed (3 failed in `DeliveryTests`/`LiveSyncSloTests`) |
| M9b | newer-pending rule removed | ✅ Killed (4) |
| M10 | tombstone check removed | ✅ Killed (1) |
| M11 | lease sweep `<` → `>` | ✅ Killed (2, `WorkerRecoveryTests`) |
| M13 | row-version token removed | ✅ Killed (4) |
| M14 | deleted reader's outcome rethrown | ✅ Killed (1, `DeviceChurnTests`) |

**Sensor total, iteration 2**: 11 mutants applied (M15, M16, M20, M21, M22, M1, M9b, M10, M11,
M13, M14). **11 killed, 0 survived.** The repeats were M22 ×5 and M16 ×3.

### 2.5 Spec-anchored status

**35/35** criteria now match their spec-defined outcome. WRK-03, WRK-26 and WRK-31 have moved
from partial to ✅ (§ 2.2), and WRK-34's guard is now discriminated deterministically.

Spec-precision notes carried forward, none of them a missing outcome:
- WRK-01 and WRK-10: "within one poll interval" is asserted loosely.
- WRK-07: the paused half is structural, with no test of its own.
- WRK-08: the released half is not exercised.
- WRK-20: the lease variant's last error is not asserted.
- WRK-25: the spec says "no further call … even if an item had been read before the delete",
  but design.md:234 interprets it as "after the delete is observed". The run-1 flake is that
  difference showing up in a test.

That is **6 notes**.

### 2.6 Remaining gap

**Fix 5 (minor, test-only): WRK-25 test is stricter than the design and flaky under load**
- **Root cause**: `IT/DeviceChurnTests.cs:78-81` bounds calls from the moment the test saw the
  204. The design bounds them from the moment the loop reloads the device. The reload → call gap
  at `DeviceLoop.cs:210-239` (user read, picture load, decrypt) lets the call already in hand
  start just after the commit. It failed in 1 of 3 full runs.
- **Fix task**: assert the design's guarantee. At most **one** call to the reader may start after
  `deletedAt`, and it must be the replication that was already `InProgress` at the delete. No
  other call may start after it. Optionally, also narrow the production gap by reloading the
  device after the picture load, immediately before `CallAsync`. That narrows the gap but does not
  close it, so the test change is still required.
- **Done when**: the test asserts the design.md:234 interpretation, and it still kills M14 and a
  mutant that skips the reload (`device is null → ReaderGone` removed). The full integration
  suite passes 5 consecutive runs.

**Ranked gaps**: 1. Fix 5 (above). That is the only gap.

**Lessons recorded** (candidates, via `scripts/lessons.py add`, with the auto-pruned entries
restored so the store only grew): L-054 is for the supervisor's read order, and L-055 for
check-then-act tests bounded from the wrong instant.

---

## Gate Check

| Gate | Command | Result |
| --- | --- | --- |
| Build | `dotnet build HikvisionReplicator.slnx --no-incremental` | 0 errors, **17 warnings**, equal to the baseline (L-007) |
| Unit | `dotnet test src/HikvisionReplicator.Tests` | **465 passed**, 0 failed, 0 skipped |
| Integration | `dotnet test src/HikvisionReplicator.IntegrationTests` (Testcontainers) | **337 passed**, 0 failed, 0 skipped (1 m 12 s) |

- **Test count**: before the feature it was 364 unit and 286 integration (tasks.md baseline). After
  it is 465 and 337, a delta of +101 and +51.
- **Integrity**: the only edited pre-existing tests are the sanctioned ones. These are the
  AD-044 amendments in `Tests/Domain/ReplicationTransitionTests.cs` and the REP-34 replacement
  in `IntegrationTests/StartupTests.cs:260`. The job-runner sweep `StartupTests.cs:271` is
  kept unchanged. No skipped tests.
- After the sensor, the tree was rebuilt and the unit suite re-run green on clean binaries
  (L-042). `git status` shows only the untracked `.agents/`, `.claude/` and `.design/`.

---

## Task Completion

T1–T22 are all marked done in `tasks.md`. Each claim was spot-checked against the tree rather than
the checkbox. The four claims below were re-derived and hold.

| Claim | Verified |
| --- | --- |
| T11 replaced REP-34 with WRK-35 and kept the job-runner sweep | `StartupTests.cs:260` and `:271` |
| T20 metrics are read through the deployment provider (L-037) | `WorkerObservabilityTests.cs:62-81` (in-memory exporter on the real provider) |
| T21's 5-run timing claim | not re-run 5×. One green run in the full gate is the evidence here |
| T15's "Rejected does not pause" was deferred to T16 | `RejectionTests.cs:187` |

---

## Spec-Anchored Acceptance Criteria

Paths are relative to `src/`. **IT** = `HikvisionReplicator.IntegrationTests/` and
**UT** = `HikvisionReplicator.Tests/Infrastructure/Worker/`. Integration tests run on the harness
intervals: 20 ms poll, 100→400 ms backoffs and probes, 5 s timeout, 30 s lease. The spec's
default numbers are pinned separately by `UT/ReplicationWorkerOptionsValidatorTests.cs:53-67`
(1 s, 10 s, 120 s, 5, 2 s, 60 s, 2, 5 s, 60 s), and the *rules* (doubling, cap, threshold) are
pinned with those defaults in the unit tests cited.

### P1 — Deliver queued work

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-01 | Pending + eligible + registered → operation delivered via `IDeviceClient` within one poll + call | `IT/DeliveryTests.cs:55` `Assert.Equal(readers.Order(), calls.Select(c => c.DeviceId).Order())`, with `:60` operation `Add`. The latency bound is asserted only through WRK-09's 5 s (`IT/LiveSyncSloTests.cs:113`) | ✅ (⚠️ the "one poll interval" bound itself is not asserted) |
| WRK-02 | Add/Update carry ExternalRef, access code, normalized face. Remove carries ExternalRef only | `IT/DeliveryTests.cs:61-63` ref `"TICKET-1"`, code `"123456"`, `FacePicture == StoredPictureAsync` (the stored, normalized bytes). Update: `:138-141` `Update`, `"654321"`, face. Remove: `:162-164` `Remove`, `ExternalRef == "TICKET-1"`, `Assert.Null(removal.Enrolment)` | ✅ payload complete |
| WRK-03 | Pending → InProgress before the call. → Succeeded after. **Each** on the injected clock | `IT/DeliveryTests.cs:80` `InProgress` while the call is out. `:112-113` `CreatedAt == Kickoff`, `UpdatedAt == Kickoff+1min` (success stamp on the frozen clock) | ❌ **partial**: the InProgress stamp is never asserted on the injected clock. Mutant M20 (`Begin(DateTime.UtcNow)`) survives |
| WRK-04 | Backfill expanded by `ExpandBackfillAsync` in one save, and Bulk rows delivered | `IT/BackfillDeliveryTests.cs:50-61` every row `Bulk`/`Add` on the reader, calls `["TICKET-1","TICKET-2"]`, refunded absent, intent `Expanded`. Single-settlement: `:85-95` (two workers, 10 rows, 10 distinct calls) and `IT/ReplicationQueueContractTests.cs:913` | ✅ |

### P1 — Live first, one per device

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-05 | Live before Bulk. Oldest first within a lane | `IT/DeliveryTests.cs:192` `["LIVE-1","BULK-1"]` (Bulk older). `:223-226` `TICKET-1..4` with insertion order opposite to age | ✅ |
| WRK-06 | ≤ 1 InProgress per device across instances, enforced by a DB constraint | `IT/ReplicationQueueContractTests.cs:290` 23505 on `"IX_replications_device_in_progress"`. `:238` UNIQUE, `("DeviceId")`, filter `'InProgress'`. `:319` different readers allowed. `IT/DeliveryTests.cs:269` `MaxConcurrentCalls(reader) == 1` with two hosts. Race closure: `IT/UserPersistenceContractTests.cs:498`, `IT/DevicePersistenceContractTests.cs:110` | ✅ |
| WRK-07 | A slow **or paused** device never delays others | `IT/DeliveryTests.cs:316-320` the fast reader drains within 2 s while the slow one has exactly 1 call | ✅ (⚠️ the "paused" half has no test of its own; independence is structural, one loop per reader) |
| WRK-08 | Newer Pending not delivered before the older InProgress is terminal **or released** | `IT/DeliveryTests.cs:372` no call to the blocked reader. `:387-389` single `Update`, `At >= finishedAt` | ✅ (⚠️ the "released" branch is not exercised) |
| WRK-09 | ≥100 Bulk/device, 20 devices @50 ms, upsert → Succeeded on all 20 ≤ 5 s, Bulk still Pending on every device | `IT/LiveSyncSloTests.cs:83` `PendingBulk ≥ 100` each. `:100` `== Readers20` Live Succeeded. `:114` `sinceUpsert.Elapsed <= 5 s`. `:124` `PendingBulk(settled, reader) > 0` for all | ✅ every number matches |
| WRK-10 | Register/delete → start/stop within one poll, no restart | `IT/DeviceChurnTests.cs:40-48` joined reader called within 2 s. `:78-81` no call after `deletedAt` | ✅ (⚠️ asserted at 2 s against a 20 ms poll, so the "one interval" bound is loose) |

### P1 — Survive an offline reader

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-11 | Unreachable or > 10 s timeout → Pending, attempts **unchanged**, position kept | Timeout: `IT/OfflineReaderTests.cs:64` `AttemptCount == 0`, status `Pending` (`:60`). Unreachable: `:86` all attempts 0 during the outage, and `:90` every probe carries the head `"TICKET-0"`. Default 10 s: `UT/ReplicationWorkerOptionsValidatorTests.cs:59` | ✅ |
| WRK-12 | Paused after **2** consecutive Unreachable | `UT/DevicePauseTests.cs:38-39` one → `null`. `:51-52` two → `Paused`, `CanAttempt == false`. `:64` answer between resets. `IT/OfflineReaderTests.cs:131` gaps after call 2 ≥ probe delay | ✅ |
| WRK-13 | Probe after 5 s, doubling to 60 s. Success resumes | `UT/DevicePauseTests.cs:85-87` due at +5 s, not a tick before. `:114` `[5,10,20,40,60,60]`. `:141` holds at 60 after 2,000 probes. `:154-155` `Resumed` once. `IT/OfflineReaderTests.cs:131` gap ≥ 100/200/400/400 ms | ✅ (the integration test is a lower bound only. The ceiling is proved in unit tests, see M5) |
| WRK-14 | Any outage → no `Failed`. All delivered on return, in WRK-05 order | `IT/OfflineReaderTests.cs:85` no `Failed` after 4 probes. `:99` attempts 0. `:102` last five calls `== tickets` in order | ✅ |
| WRK-15 | Rejected → not paused, next item taken | `IT/RejectionTests.cs:203-207` TICKET-3 delivered after two refusals on a frozen clock. `IT/RejectionTests.cs:236-240` same after an adapter throw. `UT/DevicePauseTests.cs:147` | ✅ |

### P1 — Retry, then dead-letter

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-16 | Pending, attempts +1, last error stored, not taken before `2 s × 2^(n−1)` capped 60 s | `IT/RejectionTests.cs:60` `Pending && AttemptCount == 1`. `:63` `LastError == "Device {r} refused TICKET-1."`. `:64` `NextAttemptAt == Kickoff + 100 ms`. `:70` not re-offered 1 ms short. `:80-81` delivered at the boundary, attempt 1, 2 calls. Formula: `UT/RetryScheduleTests.cs:27` (2,4,8,16), `:39` 60 cap. `UT/OutcomeDecisionTests.cs:68`, `:77` (+2 s, +16 s from the count before the call) | ✅ attempt + error + not-before all asserted |
| WRK-17 | At max (5) → stays Failed, never taken again | `IT/RejectionTests.cs:95-97` `AttemptCount == 5`, error kept, 5 calls. `:109-110` still 5 calls and `Failed` after 2× the longest backoff. `UT/RetryScheduleTests.cs:76`, `:84` (4 false, 5 true) | ✅ |
| WRK-18 | Newer intent after a dead letter delivered. Dead letter untouched | `IT/RejectionTests.cs:131-132` Update Succeeded, attempt 0, code `"654321"`. `:135-138` dead letter `Failed`, same attempts, error and `UpdatedAt` | ✅ |
| WRK-19 | Newer intent during backoff eligible immediately | `IT/RejectionTests.cs:163-175` Update Succeeded on a frozen clock. Old row `Superseded`, attempt 1. Calls `[Add, Update]` | ✅ |

### P1 — Never replay stale intent

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-20 | Non-success + newer Pending → `Superseded`, keeping **attempt count and last error of that attempt**, never Pending. Final rejection with a newer Pending → Superseded, not dead-lettered (confirmed assumption) | Unreachable: `IT/StaleIntentTests.cs:72-73` attempt 0, `LastError == "Device {r} did not answer."`. Rejected: `:105-106` attempt 1, `"Device {r} refused TICKET-1."`. Race at the save: `:145-146`. Expired lease: `IT/WorkerRecoveryTests.cs:127-128` `Superseded`, attempt 0. Final-rejection precedence: `UT/OutcomeDecisionTests.cs:111` `SupersedeAfterAttempt` at attempt 5 | ✅ (⚠️ the lease variant's last error is not asserted. The timeout variant shares the Unreachable path) |
| WRK-21 | Add/Update for a tombstoned user → no call, `Superseded` | `IT/StaleIntentTests.cs:181-185` Add `Superseded`, attempt 0, single call = `Remove`. `UT/OutcomeDecisionTests.cs:121`, `:129` | ✅ |

### P1 — Recover from a crash

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-22 | InProgress > 120 s lease → Pending, attempts unchanged (or Superseded). Device takes work again | `IT/WorkerRecoveryTests.cs:74` abandoned → `Succeeded`. `:78` `AttemptCount == 1` (prior refusal kept, not reset). `:81-83` young claim untouched, `UpdatedAt` unchanged, no call. Default 120 s: `UT/ReplicationWorkerOptionsValidatorTests.cs:60` | ✅ |
| WRK-23 | Stop → in-flight cancelled, released, no attempt | `IT/WorkerRecoveryTests.cs:158-159` `Pending`, `AttemptCount == 0` after host disposal mid-call | ✅ |
| WRK-24 | Row gone at save → outcome discarded, other loops unaffected | `IT/DeviceChurnTests.cs:173-183` "Replication {id} ended Delivered … discarded" log. `:186-189` other reader delivered, no worker error about the deleted reader. `:138-143` call-out variant, worker still running | ✅ |
| WRK-25 | Deleted device → no further call | `IT/DeviceChurnTests.cs:78-81` `DoesNotContain(call.DeviceId == reader && call.At > deletedAt)` | ✅ under design's interpretation (no call *starts* after the delete) |

### P2 — See the worker draining

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-26 | Histogram of seconds **creation → success**, tagged lane + operation | `IT/WorkerObservabilityTests.cs:140-143` lane `"Live"`, op `"Add"`, count 1, sum `7.0` | ❌ **partial**: the test freezes the clock so that creation and claim are the same instant, so claim → success also reads 7.0 s. Mutant M15 survives. Bulk/Update/Remove tag values are never observed |
| WRK-27 | Counter tagged `succeeded` / `rejected` / `unreachable` | `IT/WorkerObservabilityTests.cs:146` succeeded = 1. `:177-178` rejected = 5, succeeded = 0. `:218-223` unreachable ≥ 2 | ✅ |
| WRK-28 | Dead-letter counter + log with replication id, user id, device id, attempts, last error | `IT/WorkerObservabilityTests.cs:174` counter = 1. `:192-196` `Replication {id}`, `user {uid}`, `device {r}`, `after 5 attempts`, `Device {r} refused TICKET-1.` | ✅ all five fields |
| WRK-29 | Pause/resume logged with device id + transition. Counter tagged by transition | `IT/WorkerObservabilityTests.cs:216-217`, `:233-234` paused 1 / resumed 1. `:252-254` two lines naming `Device {r}`, `paused` then `resumed` | ✅ |

### P1 — Configuration

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-30 | Non-positive interval/timeout/lease, max attempts < 1, lease ≤ timeout → startup fails naming the setting | `UT/ReplicationWorkerOptionsValidatorTests.cs:99` `"ReplicationWorker:{setting} must be positive"` for all 7 spans × {0, −1}. `:110` MaxAttempts. `:148-151` lease ≤ timeout (equal and below). `:155` lease just above accepted. Host-level: `IT/StartupTests.cs:111` `OptionsValidationException` naming `ReplicationWorker:MaxAttempts` | ✅ |

### Edge cases

| ID | Spec-defined outcome | `file:line` + assertion | Result |
| --- | --- | --- | --- |
| WRK-31 | Empty queue → no device call, **and wait one idle poll between checks** | `IT/DeliveryTests.cs:427-428` no call to the idle reader | ❌ **partial**: the idle wait is not asserted. Mutant M21 (idle → `continue`, a DB hot loop) survives |
| WRK-32 | No devices → runs, does nothing | `IT/DeliveryTests.cs:447-449` `ExecuteTask` not completed, no calls, empty queue | ✅ |
| WRK-33 | Address/credentials updated → next call uses them | `IT/DeviceChurnTests.cs:218-220` `IpAddress == "10.0.2.99"`, decrypted `Password == "n3w-Passw0rd"`, port 80 | ✅ |
| WRK-34 | Expansion racing a live upsert → queued once (REP-19/REP-46) | `IT/BackfillDeliveryTests.cs:141-142` single row `Live`, single call. `:183-195` each pair once, 6 calls per reader over 5 racing rounds | ✅ outcome asserted (⚠️ the supervisor's loop gate is not discriminated, see M16) |
| WRK-35 | No job-runner package in the solution | `IT/StartupTests.cs:286-289` `DoesNotContain(JobRunners, …)` over referenced and shipped assemblies (Hangfire, Quartz, Coravel, …) | ✅ |

**Status**: 32/35 matched the spec-defined outcome in full. **3 are partial** (WRK-03, WRK-26,
WRK-31). In each, one conjunct has no discriminating assertion, and each is shown by a surviving
mutant. Five further ⚠️ notes record spec-precision softness: WRK-01/WRK-10's "within one poll
interval", WRK-07's paused half, WRK-08's released half, and WRK-20's lease last error. None
of these five is a missing outcome.

---

## Discrimination Sensor

**Depth: P0** (data integrity and concurrency). 25 behaviour-level mutants were applied one at a
time with `git checkout -- <file>` revert. Each was built, then the targeted classes were run with
`--filter`. The tree was confirmed clean after every mutant. One mutant (M7) failed to build on
IDE0055 and was replaced by M7b. **24 valid: 19 killed, 5 survived (1 equivalent, 4 real).**

| # | File | Mutation | Result | Killed by |
| --- | --- | --- | --- | --- |
| M1 | `Infrastructure/Worker/DeviceLoop.cs:136,140` | Live/Bulk claim order swapped | ✅ Killed | `DeliveryTests.Live_work_is_taken_before_older_bulk_work`, `…Within_a_lane…`, `LiveSyncSloTests` |
| M2 | `Infrastructure/Worker/DevicePause.cs:51` | `<` → `<=` (pause at 3) | ✅ Killed | 8 `DevicePauseTests`. `OfflineReaderTests.A_paused_reader_is_probed_on_the_doubling_schedule…` |
| M3 | `DevicePause.cs:51` | threshold − 1 (pause at 1) | ✅ Killed | 7 `DevicePauseTests`. `OfflineReaderTests.Work_whose_call_outlives_the_timeout…` |
| M4 | `DevicePause.cs:80` | probe delay never doubles | ✅ Killed | `DevicePauseTests.Each_failed_probe_doubles…`, `…holds_at_the_ceiling…`. `OfflineReaderTests` probe schedule |
| M5 | `DevicePause.cs:82` | probe delay has no ceiling | ✅ Killed (unit only) | `DevicePauseTests` ×2. Integration survives (lower-bound gaps), which is acceptable because the rule is pure and unit-owned |
| M6 | `Infrastructure/Worker/OutcomeDecision.cs:86-87` | Unreachable counted as an attempt (retry/dead-letter) | ✅ Killed | 2 `OutcomeDecisionTests`. 3 in `OfflineReaderTests`/`StaleIntentTests` (attempt 0 assertions) |
| M7 | `Domain/Specs/NextEligibleReplicationSpec.cs:28` | backoff predicate deleted | — build failed (IDE0055), not counted | — |
| M7b | same | `<= now` → `<= now.AddYears(1000)` (backoff ignored) | ✅ Killed | `RejectionTests` ×3 (`…waits_out_its_backoff`, `…overtaking_a_backoff…`, `…not_paused`) |
| M8 | `Infrastructure/Worker/RetrySchedule.cs:33` | dead letter `>=` → `>` | ✅ Killed | 3 unit (`RetryScheduleTests`, `OutcomeDecisionTests`). 3 integration (`RejectionTests`, `WorkerObservabilityTests`) |
| M9a | `Infrastructure/Worker/ReplicationOutcomeRecorder.cs:138` | recorder's newer-pending look disabled | ⚪ Survived, **equivalent** | the `IX_replications_pending` backstop (`:151-157` → `SupersedeOvertakenAsync`) produces the identical outcome. This is defence in depth, not a weak test |
| M9b | `OutcomeDecision.cs:83-84` | newer-pending rule removed (stale intent re-queued) | ✅ Killed | 3 `OutcomeDecisionTests`. 4 in `StaleIntentTests`/`WorkerRecoveryTests`/`RejectionTests` |
| M10 | `DeviceLoop.cs:222` | tombstone check removed | ✅ Killed | `StaleIntentTests.A_spectator_removed_while_their_enrolment_was_in_hand…` |
| M11 | `Domain/Specs/ExpiredLeasesSpec.cs:18` | lease sweep `<` → `>` | ✅ Killed | `WorkerRecoveryTests` ×2 |
| M12 | `Migrations/20261026194242_AddReplicationWorker.cs:33` | in-progress index filter dropped | ✅ Killed | 20 tests (contract + delivery) |
| M12b | same | in-progress index not unique | ✅ Killed | `ReplicationQueueContractTests` ×3, `DeliveryTests.Two_workers…` |
| M13 | `Infrastructure/ReplicationConfiguration.cs:83` | row-version token removed (still mapped to `xmin`) | ✅ Killed | `UserPersistenceContractTests` and `DevicePersistenceContractTests` race tests. `DeliveryTests.Two_workers…`. `BackfillDeliveryTests.Two_workers…` (not a startup failure) |
| M14 | `ReplicationOutcomeRecorder.cs:163` | deleted reader's outcome rethrown, not discarded | ✅ Killed | `DeviceChurnTests.The_answer_of_a_reader_deleted_before_it_was_saved…` |
| M15 | `ReplicationOutcomeRecorder.cs:242` | histogram measures **claim → success** | ❌ **Survived** | — (`WorkerObservabilityTests:246` freezes the clock before the upsert, so creation and claim coincide) |
| M15b | same | histogram measures from the success stamp (0) | ✅ Killed | `WorkerObservabilityTests.A_delivery_records_the_seconds…` |
| M16 | `Infrastructure/Worker/ReplicationWorker.cs:192` | loop started for a reader with an unexpanded backfill | ❌ **Survived** (confirms batch B's self-report) | — |
| M17 | `DeviceLoop.cs:187` | shutdown leaves in-flight work InProgress | ✅ Killed | `WorkerRecoveryTests.Work_whose_call_was_cut_off_by_a_shutdown…` |
| M18 | `DeviceLoop.cs:281-284` | call timeout not linked into the call token | ✅ Killed | `OfflineReaderTests.Work_whose_call_outlives_the_timeout…` |
| M19 | `RetrySchedule.cs:24` | rejection backoff never doubles | ✅ Killed (unit only) | 10 `RetryScheduleTests`/`OutcomeDecisionTests`. Integration only asserts the first backoff |
| M20 | `DeviceLoop.cs:149` | claim stamped with `DateTime.UtcNow` instead of the injected clock | ❌ **Survived** | — |
| M21 | `DeviceLoop.cs:85` | idle cycle does not wait a poll (hot loop) | ❌ **Survived** | — |

**M16 classification (the self-reported weak spot).** The supervisor runs `ExpandBackfillsAsync`
before `SyncLoopsAsync` within every tick (`ReplicationWorker.cs:56-57`), so on the tick a debt
first appears it is already expanded before any loop could start. The `owed` gate only matters
when an expansion **fails or loses a race** and the debt is still owed on a later tick while a
live row is pending. `BackfillDeliveryTests.cs:107` arranges its rows so the first tick
always expands them successfully, so it never enters that window. The production code is correct.
The test does not reach the race it was written for, the scheduling-dependent-guard shape
`docs/test-patterns.md` warns about. A deterministic guard needs the first expansion to fail
once, for example a `ReplicationSaveHook` that refuses the first expansion save, with a live row pending.

**Result**: 19/24 killed, 4 real survivors → **FAIL ❌**.

---

## Payload / Conjunction Rule

| Criterion | Conjuncts | Asserted on value? |
| --- | --- | --- |
| WRK-02 | ref, code, face (Add/Update). Ref only (Remove) | all, `DeliveryTests.cs:61-63`, `:139-141`, `:163-164` ✅ |
| WRK-16 | attempt +1, last error, not-before | all three, `RejectionTests.cs:60-70` ✅ |
| WRK-20 | Superseded + attempt count + last error of *that* attempt | Unreachable and rejected variants ✅. Lease variant's error not asserted ⚠️ |
| WRK-26 | seconds from **creation**, lane tag, operation tag | tags ✅ (only `Live`/`Add`). Interval start ❌ (M15) |
| WRK-27 | three outcome tag values | all three ✅ |
| WRK-28 | counter + 5 log fields | all ✅ |
| WRK-03 | InProgress stamp and Succeeded stamp on the injected clock | Succeeded ✅. InProgress ❌ (M20) |

---

## Declared Deviations Reviewed

Five `// SPEC_DEVIATION` markers in `src/`. All are justified, none leaves a criterion
unimplemented, and each is reflected in the tree.

| Marker | Judgement |
| --- | --- |
| `Infrastructure/BackfillIntentConfiguration.cs:35`: `xmin` token extended to `backfill_intents` | ✅ Justified and necessary: two hosts could double-expand once the first host's rows leave `Pending`. Recorded in AD-044 Scope ("Widened during Execute"). Proved by `ReplicationQueueContractTests.cs:913` |
| `Infrastructure/Worker/ReplicationOutcomeRecorder.cs:304`: unanswered + overtaken goes `Release → Supersede`, not `SupersedeAfterAttempt` | ✅ Correct reading of WRK-20 ("last error **of that attempt**"). The saved transition is identical (`InProgress → Superseded`). Proved by `StaleIntentTests.cs:73` |
| `Infrastructure/Worker/OutcomeDecision.cs:45`: two functions (`BeforeCall`/`AfterCall`) instead of one | ✅ Structural only. Both halves are unit-tested |
| `Infrastructure/Worker/ReplicationWorker.cs:42`: always registered, stands down when `Enabled=false` | ✅ Reasonable (late config sources). Design said "registered only when Enabled". The off switch is still honoured, as shown by the 286 pre-existing integration tests passing with it off |
| `Infrastructure/Worker/ReplicationWorker.cs:173`: a call in flight to a removed reader is cancelled with its loop | ✅ Consistent with WRK-25 and makes WRK-24's discard trivially true for that landing. Both landings are tested (`DeviceChurnTests.cs:121`, `:153`) |

---

## Code Quality

| Principle | Status |
| --- | --- |
| Minimum code / no scope creep | ✅ (`FakeDeviceClient` scripting surface is used by tests only, as designed) |
| Surgical changes | ✅ Only the sanctioned pre-existing tests changed |
| Matches patterns | ✅ Options + `IValidateOptions` + `ValidateOnStart`, specs in `Domain/Specs`, aggregate transitions only, no raw SQL (AD-005/006) |
| Spec-anchored outcome check | ⚠️ 3 partial criteria (above) |
| Black-box rule (AD-036/AD-040) | ✅ Worker tests drive through routes and verify by reading the DB and fake log. Hand-arranged rows are only used where no route can stage them (Bulk lane, chosen timestamps, abandoned leases). The four below-HTTP tests each carry their blind-spot sentence |
| Naming | ✅ Behaviour-named, situation-named classes as listed in `docs/test-patterns.md` |
| Documented guidelines followed | ✅ `CLAUDE.md`, `docs/test-patterns.md`, STATE AD-036/040/043/044 |

---

## Fix Plans

All four are **minor**, test-only, and need no production change.

### Fix 1: WRK-26 histogram interval start is undiscriminated (M15)
- **Root cause**: `WorkerObservabilityTests.cs:124` freezes the clock before the upsert, so the
  row's `CreatedAt` and its claim stamp are the same instant.
- **Fix task**: queue at `Kickoff`, move the clock to `Kickoff+3s` before the worker claims (for
  example, hold the claim with a `ReplicationSaveHook` or a slow first reader), answer at `Kickoff+7s`, and
  assert a sum of `7.0`, not `4.0`. Optionally also observe a `Bulk` lane tag.
- **Done when**: M15 is killed.

### Fix 2: WRK-03 claim timestamp not asserted on the injected clock (M20)
- **Fix task**: in `DeliveryTests.Work_is_in_progress_while_its_call_is_with_the_reader`, freeze
  the clock at a chosen instant and assert the in-progress row's `UpdatedAt` equals it.
- **Done when**: M20 is killed.

### Fix 3: WRK-31 idle wait not asserted (M21)
- **Fix task**: with one registered reader and an empty queue, count claim queries over a window
  of N poll intervals, either through a query-counting interceptor beside `ReplicationSaveHook`
  or through `pg_stat_statements`. Assert the count is ≤ about N + slack, not hundreds.
- **Done when**: M21 is killed.

### Fix 4: WRK-34 supervisor gate only reachable by race (M16)
- **Fix task**: make the first expansion save for a reader fail once (save hook), with a live row
  for an active spectator pending on that reader. Assert the spectator is queued and delivered
  exactly once after the retry tick.
- **Done when**: M16 is killed deterministically.

---

## Requirement Traceability Update

| Requirement | Previous | New |
| --- | --- | --- |
| WRK-01, 02, 04–25, 27–30, 32–35 | Pending | ✅ Verified |
| WRK-03, WRK-26, WRK-31 | Pending | ❌ Needs fix (test strengthening) |
| WRK-34 | Pending | ✅ Verified outcome. Guard deterministically unproven (Fix 4) |

---

## Summary

**Overall**: ❌ Not ready. The fixes are small, and the code itself showed no defect under 24 mutants.

**Spec-anchored check**: 32/35 matched. 3 partial (WRK-03, WRK-26, WRK-31). 5 precision notes.
**Sensor**: 24 valid mutants, 19 killed, 5 survived (M9a equivalent. M15, M16, M20, M21 real).
**Gate**: 465 unit / 337 integration passed, 0 failed. 17 warnings (baseline).

**What works**: live-first claiming, per-device exclusivity in the database (index and row
version, both proven by contract tests and killed mutants), the offline-reader model (no attempt,
pause at 2, doubling capped probes, in-order drain), rejection backoff and dead-lettering at 5,
stale-intent supersession including the save-time race, lease recovery, shutdown release,
device churn, and the WRK-09 SLO with every number as specified.

**Next steps**: route Fixes 1–4 to an implementer as test-only tasks, then re-verify against
the M15, M16, M20 and M21 mutants.

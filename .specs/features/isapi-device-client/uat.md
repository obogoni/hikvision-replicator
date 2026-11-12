# isapi-device-client — Bench UAT runbook

**Reader**: DS-K1T342MFWX · **Criteria**: ISA-30…ISA-36 · **Evidence goes to**: `validation.md` § UAT

Work top to bottom. **Part A probes the reader directly and must come first**, because its
answers configure the adapter for Part B. Paste each recorded value into the evidence table at
the end. A "✗" is not a failure of the runbook: it becomes a fix task (T17.n).

Throughout: `R=<reader IP>`, `U=admin`, `P='<reader password>'`. Every `curl` uses
`--digest -k` (digest auth, self-signed certificate).

---

## 0. Prepare the reader

1. Web UI → confirm **HTTPS is enabled** (port 443) and **ISAPI is enabled**.
2. Web UI → **time sync** (NTP or "sync with computer"). Digest is refused when the clocks drift
   more than ~5 minutes.
3. Web UI → **authentication mode** allows **face** and **PIN** separately (for example "Card or
   Face or PIN"), so ISA-30 can test each one.
4. Delete any test persons left over from earlier experiments.
5. Have two **real photos of yourself** (phone JPEGs are fine; the API normalizes them), plus a
   third, different person's photo for the "old face refused" check.

## A. Probe the reader (before starting the app)

```bash
# A1. Model and firmware
curl -s --digest -k -u "$U:$P" "https://$R/ISAPI/System/deviceInfo"

# A2. Person limits: employeeNo and name max lengths, PIN field name (A-2, A-6, A-7)
curl -s --digest -k -u "$U:$P" "https://$R/ISAPI/AccessControl/UserInfo/capabilities?format=json"

# A3. Face library limits and FDSetUp support (A-1, the Update risk in design.md)
curl -s --digest -k -u "$U:$P" "https://$R/ISAPI/Intelligent/FDLib/capabilities?format=json"

# A4. "Nothing to delete" answers: these become Isapi:AbsentSubStatusCodes (ISA-11)
curl -s --digest -k -u "$U:$P" -X PUT \
  "https://$R/ISAPI/Intelligent/FDLib/FDSearch/Delete?format=json&FDID=1&faceLibType=blackFD" \
  -d '{"FPID":[{"value":"00000000000000000000000000000000"}]}'
curl -s --digest -k -u "$U:$P" -X PUT \
  "https://$R/ISAPI/AccessControl/UserInfo/Delete?format=json" \
  -d '{"UserInfoDelCond":{"EmployeeNoList":[{"employeeNo":"00000000000000000000000000000000"}]}}'
```

Record: firmware version · `employeeNo` max · name max · whether a PIN/`password` field exists and
its max length · face size and dimension limits · whether `FDSetUp` is listed · **the exact
`statusCode`/`subStatusCode` of both A4 answers**.

> **A4 is critical.** If either answer is not `statusCode 1`, put its `subStatusCode` in
> `Isapi:AbsentSubStatusCodes`. Otherwise every first enrolment fails at `face-delete` and is
> dead-lettered (seen in the stub harness with `notExist`).

## B. Run the replicator against the reader

```bash
docker compose up -d
DeviceClient__Adapter=Isapi \
Isapi__AbsentSubStatusCodes__0='<code from A4, if any>' \
Isapi__NameMaxLength='<name max from A2>' \
ReplicationWorker__ProbeInitialDelay=00:00:05 \
dotnet run --project src/HikvisionReplicator.Api   # http://localhost:5000
```

Watch the log for the adapter's per-call entry (EventId 40: `DeviceId`, `Operation`, `Outcome`,
`FailingStep`, `HttpStatus`, `SubStatusCode`, `ElapsedMs`).

Replication state (there is no read API until feature 8):

```bash
docker compose exec postgres psql -U hikvision -d hikvision -c '\d replications'   # confirm column names once
docker compose exec postgres psql -U hikvision -d hikvision -c \
  'select "Id","UserId","Operation","Status","AttemptCount","LastError" from replications order by "Id" desc limit 20;'
```

### B1 · ISA-30 — enrol, then recognised by face and by PIN

```bash
curl -s -X POST localhost:5000/api/devices -H 'content-type: application/json' -d "{
  \"name\":\"Bench\",\"ipAddress\":\"$R\",\"httpPort\":443,
  \"username\":\"$U\",\"password\":\"$P\",\"faceCapacity\":1500 }"
```
`httpPort` holds the **HTTPS** port (A-9). The field name predates this feature.

```bash
PHOTO1=$(base64 -w0 me-1.jpg)
curl -s -X PUT localhost:5000/api/users/UAT-0001 -H 'content-type: application/json' -d "{
  \"name\":\"UAT Tester\",\"accessCode\":\"4821\",\"facePicture\":\"$PHOTO1\" }"
```

- [ ] Replication row → `Succeeded`; log shows `Delivered` with `ElapsedMs` (record it)
- [ ] Reader user list shows the person: **name "UAT Tester"**, employee no. = 32 hex characters
- [ ] **Face alone** opens the reader
- [ ] **PIN 4821 alone** opens the reader

### B2 · ISA-31 — update name, PIN and face

```bash
PHOTO2=$(base64 -w0 me-2.jpg)
curl -s -X PUT localhost:5000/api/users/UAT-0001 -H 'content-type: application/json' -d "{
  \"name\":\"UAT Tester Renamed\",\"accessCode\":\"7390\",\"facePicture\":\"$PHOTO2\" }"
```
- [ ] Replication `Update` → `Succeeded`
- [ ] Reader shows "UAT Tester Renamed"; **PIN 7390 opens**, **PIN 4821 is refused**; face still opens

Optional, for the Update risk in the design: update UAT-0001 with the **third person's** photo. The
reader then knows that face, and **your face stops opening**. That confirms the face was replaced
and not added.

### B3 · ISA-32 — delete

```bash
curl -s -X DELETE localhost:5000/api/users/UAT-0001
```
- [ ] Replication `Remove` → `Succeeded`
- [ ] Neither face nor PIN opens; the reader's user list no longer contains the person
- [ ] Run the delete twice → still `Succeeded` (ISA-11 on hardware)

### B4 · ISA-33 — A-13 envelope

1. Enrol 3–5 users with different source photos (large phone shot, small crop, low-light). For
   each, record the stored size and dimensions:
   `select "ExternalRef", "FaceByteSize", "FaceWidth", "FaceHeight" from users;` (confirm column
   names with `\d users`). All must be `Succeeded`.
2. Outside the band, informational, probing the reader directly:
   ```bash
   # a ~30 KB JPEG, a ~300 KB JPEG, and a 320x240 JPEG; FPID must be an existing employeeNo
   curl -s --digest -k -u "$U:$P" -X POST \
     "https://$R/ISAPI/Intelligent/FDLib/FaceDataRecord?format=json" \
     -F 'FaceDataRecord={"faceLibType":"blackFD","FDID":"1","FPID":"<employeeNo>"};type=application/json' \
     -F 'FaceImage=@small-30k.jpg;type=image/jpeg'
   ```
- [ ] Smallest and largest stored sizes accepted (record both)
- [ ] Reader's answers for 30 KB, 300 KB and 320×240 recorded → A-13 confirmed or corrected

### B5 · ISA-34 — wrong password: paused, nothing dead-lettered, account not locked

```bash
curl -s -X PUT localhost:5000/api/devices/<id> -H 'content-type: application/json' -d "{
  \"name\":\"Bench\",\"ipAddress\":\"$R\",\"httpPort\":443,
  \"username\":\"$U\",\"password\":\"wrong-on-purpose\",\"faceCapacity\":1500 }"
# then enrol UAT-0002 and wait ~3 minutes
```
- [ ] Log shows **one** `authentication failed` entry that contacted the reader; later attempts
      are answered from the hold, with no `HttpStatus`
- [ ] UAT-0002's row is **not** `DeadLettered`
- [ ] **Web UI login with the real password still works** (account not locked)
- [ ] Restore the real password → UAT-0002 reaches `Succeeded`

### B6 · ISA-35 — offline mid-backfill

1. Delete the device. Enrol 30 users (`UAT-0100`…`UAT-0129`).
2. Register the reader again, so a backfill of 30 starts. After about 5 have succeeded,
   **unplug the reader's network** (or power) for 2 minutes, then reconnect.
- [ ] While unplugged, the log shows the reader paused (no dead letters, attempts not climbing)
- [ ] After reconnecting, all 30 reach `Succeeded`, with **zero** `DeadLettered`

---

## Evidence (paste into `validation.md` § UAT)

| Item | Recorded value | ✓/✗ |
| --- | --- | --- |
| Firmware version (A1) | | |
| `employeeNo` max (A2), A-2 | | |
| Name max (A2), A-6 → `NameMaxLength` | | |
| PIN field name and max (A2), A-7 | | |
| Face limits from capabilities (A3) | | |
| `FDSetUp` supported? (A3) | | |
| Absent answer: face delete (A4) | | |
| Absent answer: person delete (A4) | | |
| ISA-30 enrol `ElapsedMs` | | |
| ISA-30 face opens / PIN opens | | |
| ISA-31 rename / new PIN / old PIN refused | | |
| ISA-32 gone after delete / double delete | | |
| ISA-33 stored sizes min…max, dimensions | | |
| ISA-33 reader answer at 30 KB / 300 KB / 320×240 | | |
| ISA-34 reader-contacting auth failures (count) / account locked? | | |
| ISA-35 dead letters after reconnect | | |
| Per-step timings: `person` / `face-delete` / `face` (ms) | | |

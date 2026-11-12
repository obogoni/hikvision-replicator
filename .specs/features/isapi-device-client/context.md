# isapi-device-client Context

**Gathered:** 2026-11-12
**Spec:** `.specs/features/isapi-device-client/spec.md`
**Status:** Ready for design, once the spec is confirmed

---

## Feature Boundary

The real `IDeviceClient` adapter: add, update and remove a spectator (person, PIN, face) on a
Hikvision reader over ISAPI. It classifies every answer onto `Delivered`, `Rejected` or
`Unreachable`, and is proven on the bench DS-K1T342MFWX. It owns no retries, timeouts, pausing or
state; those stay with the worker (AD-043).

---

## Implementation Decisions

### Identity on the reader

- `employeeNo` is a deterministic hash of `ExternalRef`: 32 lowercase hex characters, from the
  first 16 bytes of SHA-256 over UTF-8. There is no new column. The reader shows the hash, not
  the integrator's key.

### Partial enrolment

- Add: if the person is written and the face is refused, delete the person and report
  `Rejected`. A reader never holds a PIN-only spectator.
- Update is different (agent's call, flagged in the spec): a refused face does **not** delete an
  already-enrolled person.

### Failure classification

- 401 after digest (wrong credentials) is `Unreachable`, so the reader is paused rather than its
  roster dead-lettered. This also keeps us under the reader's failed-login lockout.
- Generalised into one rule, confirmed by the user: reader-wide failures (403, 404/405, 5xx,
  unparseable answers) are `Unreachable`, failures of this record are `Rejected`. The point is that
  a reader can be updated or fixed and the replicator adapts, with nothing dead-lettered.

### Transport

- HTTPS only. The reader's self-signed certificate is accepted, scoped to the adapter's HTTP
  client, never process-wide. No per-device scheme field.

### Name

- `DeviceEnrolment` gains `Name`, so turnstile staff see who walked through. If the name is longer
  than the reader allows, it is truncated, never refused.

### Verification

- Automated tests drive an in-process ISAPI stub. A manual, documented UAT on the bench reader
  confirms the ISAPI shapes and closes A-13.

### Agent's Discretion

- Exact ISAPI calls (`UserInfo/SetUp` vs Record+Modify; `FDSetUp` vs `FaceDataRecord` + delete),
  chosen at design and confirmed at UAT.
- The validity window and door right constants (A-8).
- Where the stub lives and which test project it belongs to, per `docs/test-patterns.md`.

### Declined / Undiscussed Gray Areas → Assumptions

- A-7 (PIN field), A-8 (access right), A-9 (`Port` is the HTTPS port), A-12 (no compensation on
  cancel), A-14 (lockout) are logged in the spec as assumptions with defaults.

---

## Specific References

- Bench reader: **DS-K1T342MFWX** (MinMoe value series, Wi-Fi, fingerprint, M1 card). Datasheet:
  1,500 faces, 1,500 users, ISAPI + ISUP 5.0, "platform-applied personal PIN".
- Firmware release note V4.48.0 (DS-K1T342 series): "supports to assign face pictures that are
  large than 200KB".
- Community ISAPI reference (`uchkunr/hikvision-best-practices`): endpoints, `employeeNo` ≤ 32,
  face ≤ 200 KB and ≥ 80×80, `deviceUserAlreadyExist`, lockout after about 5 failures for 30 minutes.
  Secondary source: confirm at UAT.

---

## Deferred Ideas

- A 10,000-face reader for full-scale validation, and correcting AD-020/AD-021's bench-unit
  description (only a 1,500-face DS-K1T342MFWX exists). Logged in `ROADMAP.md` § Known Gaps.
- Certificate pinning for readers (hardening, later).
- Reading face capacity and firmware from the reader at registration (`device-registry` follow-up).

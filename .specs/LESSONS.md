# LESSONS — auto-maintained by scripts/lessons.py

> Machine-owned. Do NOT hand-edit. Changes are overwritten on the next `lessons.py` write.
> Canonical state lives in `.specs/lessons.json`. Edit lessons only via the script.
> promote_threshold=2 distinct features · window_days=45 · quarantine_threshold=2

## Confirmed (load these at Specify/Design)

Corroborated across multiple features. Safe to apply as guidance.

_none_

## Candidates (under observation — do NOT load as guidance yet)

Seen once or not yet corroborated. Tracked, not trusted.

### L-001 — Assert the observable side effect itself, not the DI registration that is supposed to produce it.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `observability` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 1 mutation 7 — Program.cs:56-57 (.AddAspNetCoreInstrumentation / .AddEntityFrameworkCoreInstrumentation removed, suite green) (observability)
- last seen: 2026-08-12T16:24:04Z

### L-002 — When an acceptance criterion names several channels, assert the outcome in every named channel, not only the convenient ones.
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `security` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 1 — DEV-07 trace-attribute clause had no file:line (security)
- last seen: 2026-08-12T16:24:04Z

### L-003 — Assert both the omitted case and the blank case for every required field, on every route that accepts it.
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `validation` · harmful: 0
- features: device-registry
- evidence: validation.md iteration 2 mutation M11 — RegisterDeviceService.cs:23 (blank password accepted at registration, suite green) (validation)
- last seen: 2026-08-12T16:24:04Z

## Quarantined (failed when applied — ignore)

A confirmed lesson that recurred alongside failure. Kept for the maintainer to review.

_none_

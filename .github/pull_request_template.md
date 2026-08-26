<!--
  Title must be a valid conventional-commit subject — PRs are squash-merged (AD-025),
  so this PR's title becomes the commit message on main.
  Example: feat(devices): add device registry slice
-->

## What & why

<!-- One paragraph. What changes, and what problem it solves. -->

## Spec traceability

- **Feature spec:** `.specs/features/<feature>/spec.md` <!-- or "n/a — no spec (small change)" -->
- **Requirements covered:** <!-- e.g. DEV-01 … DEV-25, or n/a -->
- **Decisions applied / added:** <!-- e.g. AD-024, AD-025, or none -->
- **Verifier report:** `.specs/features/<feature>/validation.md` — <!-- PASS / FAIL / n/a -->

## Gate results

<!-- Paste the actual counts. "Passed" without numbers is not evidence. -->

```
dotnet build HikvisionReplicator.slnx
dotnet test src/HikvisionReplicator.Tests               # unit, Docker-free
dotnet test src/HikvisionReplicator.IntegrationTests    # integration, needs Docker
```

- Unit: <!-- N passed --> · Integration: <!-- N passed -->
- Build warnings: <!-- none / list -->

## Migrations & config

- [ ] No EF Core migration in this PR
- [ ] Adds a migration — applied automatically at startup; note anything needing an out-of-band `dotnet ef database update`
- [ ] No new configuration or secrets
- [ ] Adds configuration — documented in the README

## Deviations from spec or design

<!-- Anything implemented differently than specified, deferred, or explicitly out of scope.
     "None" is a valid answer; silence is not. -->

## Checklist

- [ ] Branch is `<type>/<kebab-slug>`, branched off `main` (or a stacked base named below)
- [ ] Every commit follows Conventional Commits; spec-driven work is one atomic commit per task
- [ ] Tests assert spec-defined outcomes, not the implementation
- [ ] No test was weakened, skipped, or deleted to make the gate pass

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

Always run, whatever the PR changes:

```
dotnet build HikvisionReplicator.slnx
dotnet test src/HikvisionReplicator.Tests               # unit, Docker-free
```

- Unit: <!-- N passed -->
- Build warnings: <!-- none / list -->

### Integration tests

Required **only when this PR changes compiled code or the build** — any `.cs`, `.csproj`,
`.slnx`, `Directory.Build.props`, or `.editorconfig`. The last three are in the list because
a project-file or ruleset change can break the integration suite without a single `.cs` line
moving. A docs- or `.specs/`-only PR does not need a local Docker run.

```bash
# Does this PR touch compiled code or the build?
git diff --name-only origin/main...HEAD \
  | grep -E '\.cs$|\.csproj$|\.slnx$|Directory\.Build|\.editorconfig' || echo "no — skip"
```

Note the **three** dots: `origin/main...HEAD` diffs from the merge base, so it shows what this
branch introduces rather than everything that has landed on `main` since you branched.

Tick one:

- [ ] **Not required** — the command above printed `no`. Paste its output as the evidence.
- [ ] **Required and run** — Integration: <!-- N passed -->

```
dotnet test src/HikvisionReplicator.IntegrationTests    # integration, needs Docker
```

Skipping locally never skips the gate: `.github/workflows/ci.yml` runs the **full** gate on
every PR regardless of what changed, and `build-and-test` is a required check (AD-025/AD-027).
This box decides what *you* run before pushing, not what merges.

### E2E

```
dotnet test src/HikvisionReplicator.E2E                 # e2e, needs a running API
```

- E2E: <!-- N passed / not run — say why -->

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

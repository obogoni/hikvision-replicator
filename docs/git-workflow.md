# Git Workflow

Source: `.specs/STATE.md` — **AD-025** (active, 2026-08-14), which is the authority on the
exact branch-protection payload. This document is the working reference; the decision log is
the record of why.

`CLAUDE.md` carries the four rules that apply to every session — branch first, one commit per
task, base every PR on `main`, merge by squash PR. Everything below is the detail behind them.

## One branch per change

Branch off `main`, named `<type>/<kebab-slug>`, using the same type vocabulary as the commit
message: `feat/device-registry`, `fix/ip-normalization`, `docs/test-patterns`,
`chore/repo-conventions`.

When work is genuinely stacked on an unmerged branch, say so and note the rebase needed once
the base lands.

## Conventional Commits

Every commit message is `type(scope): subject`. Scope is optional but encouraged.

| Type | Use for |
|---|---|
| `feat` | New user-visible capability |
| `fix` | Bug fix |
| `docs` | Documentation, including `.specs/` and `docs/` |
| `test` | Tests only, no production-code change |
| `refactor` | Behaviour-preserving restructuring |
| `perf` | Performance work |
| `build` | Project files, NuGet dependencies, Docker |
| `ci` | Pipeline configuration |
| `chore` | Anything else that ships no behaviour |

Subject is imperative mood, lower case, no trailing period. Scopes in use: `domain`, `devices`,
`infra`, `tests`, `e2e`, `specs`, `deps`.

Spec-driven work keeps **one atomic commit per task** — never batch tasks into one commit.

## Pull requests

Merge into `main` only through a pull request, and PRs are **squash-merged**. Open one with
`gh pr create` and fill in `.github/pull_request_template.md`; the user reviews and merges.

Because the merge is a squash, **the PR title becomes the commit on `main`** and must itself be
a valid conventional-commit subject. Per-task commits survive in the PR, not on `main` — record
any commit SHAs that matter (e.g. in `validation.md`) as **pre-squash references**, which
resolve only through the PR.

A PR is required but needs **no approval** (`required_approving_review_count=0`): a solo
maintainer cannot approve their own pull request.

## Base every PR on `main`

A PR whose base is another *branch* merges into that branch, **not** into `main`. GitHub
retargets a child PR only when the base branch is deleted on merge.

**This stranded work off `main` twice — PR #2 and PR #4.** Both times the PR showed a "Merged"
badge and both times the commits landed on the intermediate branch instead of `main`. A merged
badge only means something merged *somewhere*.

Stack only when genuinely required. When you do, either merge the base PR with its branch
deleted, or retarget the child to `main` before merging. **After any merge, verify `main`
itself:**

```bash
git fetch --prune && git log --oneline -3 origin/main && git ls-tree -d --name-only origin/main src/
```

## Branch protection is enforced by the server

`main` rejects a direct push outright:

```
remote: - Changes must be made through a pull request.
remote: - Required status check "build-and-test" is expected.
 ! [remote rejected] HEAD -> main (push declined due to repository rule violations)
```

So if you are on `main` and about to commit, **branch first** — otherwise you will do the work
and then discover it cannot be pushed. `enforce_admins=true`, so being the repo owner does not
exempt you.

**`git push --dry-run` does not test this.** A dry run sends no pack, so the server never
evaluates protection and the push appears to succeed. The first real signal is the real push.

Check live state rather than trusting any document, including this one:

```bash
gh api repos/obogoni/hikvision-replicator/branches/main/protection
```

## CI is a required check

`build-and-test` is a required status check with `strict=true`, so a PR cannot merge until CI
passes **and** the branch is up to date with `main`. If another PR merges ahead of yours, update
from `main` and let CI re-run. `.github/workflows/ci.yml` runs the full gate on every PR to
`main` and is the enforcement boundary — local runs are advisory (AD-027).

## Repository settings

Squash-only merges and auto-delete-on-merge are enforced by repository settings, not just by
documentation:

```bash
gh repo edit --enable-squash-merge=true --enable-merge-commit=false \
  --enable-rebase-merge=false --delete-branch-on-merge
```

These differ from a fresh clone's defaults, so check rather than assume:

```bash
gh repo view --json deleteBranchOnMerge,squashMergeAllowed,mergeCommitAllowed,rebaseMergeAllowed
```

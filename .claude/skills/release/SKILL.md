---
name: release
description: This skill should be used when the user asks to "release", "cut a release", "publish a new version", "バージョンを上げてリリース", or names a version number to ship this repository at. Rewrites the tool version the skills pin, commits it, and starts the Release workflow at that same number. Not for publishing to NuGet.org, which stays a manual step.
argument-hint: <version to release, such as 0.2.0>
allowed-tools: Read, Edit, Grep, Bash, AskUserQuestion
---

# Release

Ship one version of this repository. The version the user names is the authority: the pins under `plugin/`
are rewritten to it, committed, and handed to `release.yml` as its input, so the skills an install carries
and the package that gets built cannot name different versions.

This skill stops at the draft release. Publishing it triggers `publish.yml`, which pushes to NuGet.org, and
a version on NuGet.org can never be replaced — that button stays in the user's hands.

## 1. Check the preconditions

Refuse to start unless all of these hold, and say which one failed:

```bash
git rev-parse --abbrev-ref HEAD        # must be main
git status --porcelain                 # must be empty
git fetch origin main
git rev-list --left-right --count origin/main...HEAD   # must be 0	0
git tag --list "v$VERSION"             # must be empty
```

The version must match `^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$`, the same shape `release.yml` accepts,
and must differ from the version currently pinned (`jq -r .version plugin/.claude-plugin/plugin.json`).
A rerun at the version already pinned is a mistake, not a no-op: the tag would collide.

## 2. Rewrite the pins

Find them rather than editing a fixed list of files, so a pin added later is not left behind:

```bash
rg -n 'Aetos\.RoslynSkills\.Tools@' plugin
jq -r .version plugin/.claude-plugin/plugin.json
```

Rewrite every hit to the new version with `Edit`, along with `version` in
`plugin/.claude-plugin/plugin.json`. Nothing outside `plugin/` carries the version: the package version
comes from the workflow's `--version` argument, so `Aetos.RoslynSkills.Tools.csproj` has no `<Version>` to
touch.

## 3. Verify nothing was missed

```bash
rg -n 'Aetos\.RoslynSkills\.Tools@' plugin | grep -vF "Aetos.RoslynSkills.Tools@$VERSION"
```

That must print nothing, and `jq -r .version plugin/.claude-plugin/plugin.json` must print the new version.
`release.yml` runs the same check and fails the build, but finding it here costs a workflow run less.

## 4. Show the diff and confirm

Show `git diff` and ask the user once, with `AskUserQuestion`, before committing. Everything after this
point is visible outside the machine.

## 5. Commit, push, dispatch

```bash
git commit -am "Pin the skills to Aetos.RoslynSkills.Tools $VERSION"
git push origin main
gh workflow run release.yml -f version="$VERSION" --ref main
```

Then find the run it started and follow it:

```bash
gh run list --workflow release.yml --limit 1 --json databaseId,url
gh run watch <id> --exit-status
```

## 6. Report

On success, print the draft release's URL (`gh release view "v$VERSION" --json url`) and tell the user that
publishing it is what sends the package to NuGet.org, irreversibly. Do not publish it.

On failure, say which step failed and stop. The bump commit is already on `main` by then, which is expected:
`main` pins a version that is not on NuGet.org yet from the moment of the push until the release is
published, so the window is not new. Fix the cause in a follow-up commit and dispatch the workflow again at
the same version — never rewrite or force-push the bump commit, and never delete the tag if one was created.

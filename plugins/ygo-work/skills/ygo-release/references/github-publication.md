# GitHub release publication

## Access

Use the actual `SeyTi01/ygo-advanced-probability-calculator-web` repository and [execution environment](../../ygo-verify/references/environment.md). Discover the current connected GitHub tools; use them for operations they support. If tag or Release writes are absent, use authenticated `gh` for those operations. Do not assume a GitHub connection authenticates local Git/CLI, or that a successful repository read proves every token write permission.

Resolve the installed executable, run `gh auth status` without showing the token, and verify the expected account and repository. On Windows, a missing PATH entry does not prove `gh` is uninstalled: check `GitHub CLI/gh.exe` under Program Files and reuse its full path. Request the host's scoped execution approval for restricted network/credential access; a sandbox connection failure is not evidence that the saved token expired. Do not reinstall, replace credentials or start a browser login merely because PATH or the sandbox prevented access. Never print tokens or put them in commands, repository files or chat; preserve existing authentication.

Establish an available route for both merges, tag creation, Release publication and read-back before changing the version or merging. Prefer `gh` over browser automation when the connector lacks publication support. If CLI access is unavailable or lacks the needed capability, use a supported authenticated browser under [computer-use authorization](../../ygo-implement/references/computer-use.md); inspect its actual controls and leave credential entry to the user. If no route is usable, report the specific missing capability before leaving a release partly promoted. A real permission rejection is a blocker, not permission to weaken protections or broaden the token automatically.

## Predecessor and exact target

For a new stable release, record the currently published latest stable release and its tag before any publication; verify its commit belongs to the intended release history and the requested version is newer. Inspect existing requested tags/releases first on resume. Reuse the recorded predecessor or verified existing compare link; never choose the requested release itself as its predecessor. If recovery leaves that baseline ambiguous, resolve it from published release history rather than selecting the most recent arbitrary Git tag. For a repository with no prior release, record that absence explicitly.

Always name `dev` and `main` explicitly: the repository's default branch is `dev`. Refresh remote refs and required rules/checks for each merge. `gh pr merge` supports `--match-head-commit` to reject a changed head; also re-read the base and mergeability. If using the connector, supply its expected-head guard when supported. Do not treat a combined commit status or an empty list of checks as proof that all required check runs passed; inspect check runs and rules as well. The current tests workflow runs on pushes, so a PR-event-only workflow query can miss its runs.

After [promotion](promotion.md), let `TAG` denote the requested `vX.Y.Z` tag and `MAIN_SHA` the verified full remote `main` commit SHA, not a moving branch name. Before creating it, check the remote tag. Resolve annotated tags through their tag objects to the final commit, rather than comparing the tag object's SHA with a commit SHA.

If absent, create the tag at `MAIN_SHA`, preserving the established lightweight/annotated convention. Prefer guarded local Git when authenticated, with [publication identity](../../ygo-pr/references/publication-identity.md) checks before publishing; push only the intended tag, not every local tag. An authenticated GitHub API route may create `refs/tags/TAG` explicitly with that SHA after the same content/identity review. Never use a force update. Read the remote tag back and verify its resolved commit before proceeding. An existing matching tag is reusable; a tag at another commit is a blocker. Never rely on automatic tag creation from the default branch.

## Publish and verify

Write the reviewed [notes](release-notes.md) to an exact UTF-8 temporary file outside tracked source. For a normal new Release, use the equivalent of this command with real verified values (shell quoting must match the host):

```text
gh release create TAG --repo SeyTi01/ygo-advanced-probability-calculator-web --verify-tag --latest --title "TAG Yu-Gi-Oh! Advanced Probability Calculator" --notes-file NOTES_FILE
```

Do not add `--generate-notes` or `--notes-from-tag`. `--notes-start-tag` affects generated notes only; it does not set a predecessor for a custom body. In a REST route, use the existing `tag_name`, the curated `name`/`body`, `draft: false`, `prerelease: false`, `make_latest: "true"`, and `generate_release_notes: false`. Setting `target_commitish` cannot correct an existing tag at the wrong commit. For a draft left by this same release task, inspect its target/body and update/publish that draft instead of creating another Release. Preserve unrelated release assets and metadata.

For an explicitly requested prerelease, use `--prerelease --latest=false` (REST: `prerelease: true`, `make_latest: "false"`); prereleases cannot be latest. An explicitly historical release keeps the latest marker unchanged unless the user requested otherwise. Apply the corresponding mode during read-back rather than enforcing the normal stable defaults.

Immediately before publication, recheck that `main` and the predecessor/latest release have not changed since verification. If `main` moved after tagging, do not retarget the tag to follow it; reconcile the race before publishing. If a newer release appeared, do not take over its latest marker. On a failed or timed-out write, read the tag and Release by tag before retrying. Do not overwrite a conflicting published body or target as an automatic recovery step.

Read the Release by tag and `GET /repos/SeyTi01/ygo-advanced-probability-calculator-web/releases/latest`. Verify the Release ID/tag, publication/prerelease state, exact body and compare URL, resolved tag commit and latest selection against the task record and requested mode. A normal new stable release must be published, non-prerelease and latest. A release's `target_commitish` text alone is not proof of its tag's commit. Treat any failed verification as unfinished work and report the exact stage; do not create another release to test permissions.

References: [GitHub release API](https://docs.github.com/en/rest/releases/releases), [gh release create](https://cli.github.com/manual/gh_release_create), [gh pr merge](https://cli.github.com/manual/gh_pr_merge).

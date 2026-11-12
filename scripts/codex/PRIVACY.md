# Publication guard

The guard verifies approved public attribution before commits and pushes. Agent procedures live in YGO Work: [publication identity](../../plugins/ygo-work/skills/ygo-pr/references/publication-identity.md), [missing guard setup](../../plugins/ygo-work/skills/ygo-pr/references/git-identity-setup.md) and [public-content review](../../plugins/ygo-work/skills/ygo-pr/references/public-content.md).

## Installation and policy

`setup.ps1` installs the guard before restoring .NET dependencies and accepts `-GitPublicName` and `-GitPublicEmail`. The portable installer writes project-local `user.name`, `user.email` and `user.useConfigOnly`; unrelated global defaults remain untouched. An unconfigured installation blocks commits and pushes.

The policy, installed validator, original hooks and private settings backup live in the shared Git directory, outside tracked files. Installation is idempotent and applies to linked worktrees using default shared hooks. Existing pre-commit, commit-msg and pre-push hooks are preserved as `.privacy-original` and chained with their original arguments and complete stdin. A custom `core.hooksPath` or hook manager stops installation without changing its configuration or hooks. Setup files alone do not install hooks until executed.

`policy.json` contains exact approved `{ "name": "PUBLIC_LOGIN", "email": "ISSUED_NOREPLY_ADDRESS" }` pairs in an `identities` array. Rerunning setup preserves reviewed entries.

## Hook behavior and publication boundary

Pre-commit checks both identities Git actually resolves, including environment and command overrides and reused authors. Commit-msg checks `Signed-off-by` and `Co-authored-by` lines. Pre-push reads every proposed ref update, checks raw commit objects across the complete outgoing range, and validates newly published annotated taggers and identity trailers. Inspection disables replacement objects and rejects grafted history so local history views cannot hide outgoing metadata. Unsupported refs and tags not backed by commits fail closed. Deletions introduce no identities and are forwarded to existing hooks.

The publication boundary is the target remote's **currently advertised heads and tags**, queried during each push. Their reachable commit ancestry is already published and is excluded. A new branch with an all-zero remote tip still checks all newly introduced history; an unapproved intermediate commit cannot hide behind an approved tip. Cached tracking refs, dates and arbitrary last-N limits are never used as this boundary. Inspection requires all advertised objects locally and complete history. A changed remote advertisement fails rather than trusting an ambiguous range.

## Limits

Hooks are local and bypassable. They do not govern website/API writes or arbitrary identifying strings/images in source. GitHub's **Keep my email addresses private** and **Block command line pushes that expose my email** provide an additional account layer, but the latter checks the most recent commit's author email, not every author, committer, name, trailer or file. See [GitHub's documented scope](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/blocking-command-line-pushes-that-expose-your-personal-email-address).

## Guard verification

Run the local-only integration tests with:

```sh
node --test scripts/codex/privacy-guard.test.mjs
```

They install the real hooks in disposable repositories and push only to local bare remotes. No negative fixture is uploaded.

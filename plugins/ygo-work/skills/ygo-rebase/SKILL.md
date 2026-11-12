---
name: ygo-rebase
description: "Rebase a YGO task branch onto current dev and resolve integration conflicts, including already-published task branches. Also used before PR delivery when upstream has advanced."
---

# Integrate current dev

Read [context](../ygo-implement/references/context.md), [authorization](../ygo-implement/references/authorization.md), [environment](../ygo-verify/references/environment.md) and `repo:scripts/codex/PRIVACY.md` for publication. Confirm the task branch, protect unrelated working changes, and fetch `origin/dev`.

If the branch is behind, rebase it and resolve routine conflicts without asking again. Keep history linear; do not merge `dev`/`origin/dev` or use an implicit merge-producing pull. Inspect both sides' intended behavior rather than choosing ours/theirs wholesale. Resolve a genuine product-contract conflict from evidence or ask only for the missing decision.

Before rebasing an already-published task branch, record its exact current remote SHA and preserve the old local head in a backup ref. Reconcile remote commits missing locally before rebasing. After integration, inspect the changed behavior and rerun affected checks and the full suite when feasible via [verification](../ygo-verify/SKILL.md).

For CLI publication, use explicit `--force-with-lease=<ref>:<expected-sha>` for a rewritten branch. If the lease fails, fetch and reconcile concurrent work before retrying. Never force unconditionally. Verify the remote commit and tree afterward.

For an API-only host, use [API publication](../ygo-pr/references/api-publication.md). A forced API ref update is not an atomic lease: reread the exact remote SHA immediately before the update, abort on an unexpected change, and verify afterward; retain this concurrency limitation when choosing the route. Do not apply task-branch rewriting authorization to protected or unrelated branches. Return to the caller after integration; do not restart PR delivery recursively.

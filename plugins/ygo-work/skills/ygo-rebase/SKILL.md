---
name: ygo-rebase
description: "Rebase a YGO task branch onto current dev and resolve integration conflicts, including already-published task branches. Also used before PR delivery when upstream has advanced."
---

# Integrate current dev

Read [context](../ygo-implement/references/context.md), [authorization](../ygo-implement/references/authorization.md) and [environment](../ygo-verify/references/environment.md). Use [publication identity](../ygo-pr/references/publication-identity.md) for local commits or publication. Confirm the task branch, protect unrelated working changes, and fetch `origin/dev`.

If behind, rebase and resolve conflicts. Keep history linear; do not merge `dev`/`origin/dev` or use a merge-producing pull. Inspect both sides' intended behavior rather than choosing ours/theirs wholesale. Resolve product-contract conflicts from evidence or ask only for the missing decision.

Before rebasing an already-published task branch, record its exact current remote SHA and preserve the old local head in a backup ref. Reconcile remote commits missing locally before rebasing. After integration, inspect the changed behavior and rerun affected checks and the full suite when feasible via [verification](../ygo-verify/SKILL.md).

For CLI publication, use explicit `--force-with-lease=<ref>:<expected-sha>` for a rewritten branch. If the lease fails, fetch and reconcile concurrent work before retrying. Never force unconditionally. Verify the remote commit and tree afterward.

For an API-only host, use [API publication](../ygo-pr/references/api-publication.md). A forced API ref update is not an atomic lease: reread the exact remote SHA immediately before updating, abort on unexpected changes, and verify afterward; account for this race when choosing the route.

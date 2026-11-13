# Publication without CLI credentials

Use this fallback when authenticated CLI publication is unavailable. Apply [publication identity](publication-identity.md) before creating commit objects; report unresolved verification limits through the writing policy.

The owner's established GitHub connection has confirmed API commit identity and task-scoped publication authorization, including when CLI credentials or CI verification are unavailable. Local Git settings do not configure API identity: revalidate when the account, connection or API path changes. An unverified route needs explicit approved author/committer controls or guarded Git; never publish probe objects to test defaults. The Git commit API supports explicit identity objects.

Prefer publishing the reviewed local commit/tree through available Git-object tools. If only file-creation/update tools are available, preserve every intended add/edit/delete and current base, and inspect the resulting diff/tree rather than assuming it matches local Git. Serialize dependent writes and use current blob/ref SHAs where supported.

For normal ref updates, use a non-forced update from the observed remote base and reconcile concurrent changes if rejected. Rewritten branch publication follows [rebase](../../ygo-rebase/SKILL.md), which owns the backup, remote-SHA checks and API race limitation.

Verify the remote commit, tree and intended changed files before creating/updating the PR. If the API lacks a required capability, use the shared [browser fallback](../../ygo-implement/references/authorization.md#browser-and-computer-use) with the same identity/content checks. If no available route can represent the complete change, preserve the local result and report the transport limitation; do not omit deletions, binary files or history.

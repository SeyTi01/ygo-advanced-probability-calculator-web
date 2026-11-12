# Publication without CLI credentials

Use the authenticated GitHub connector/API when CLI publication is unavailable. Do not spend the task repeatedly attempting login or infer CLI credentials from connector access. Source edits, local tests and local commits can proceed without network write credentials.

Read `repo:scripts/codex/PRIVACY.md` for the approved identity route before creating commit objects. Report unresolved verification limits through the writing policy.

Prefer publishing the reviewed local commit/tree through available Git-object tools. If only file-creation/update tools are available, preserve every intended add/edit/delete and current base, and inspect the resulting diff/tree rather than assuming it matches local Git. Serialize dependent writes and use current blob/ref SHAs where supported.

For normal ref updates, use a non-forced update from the observed remote base and reconcile concurrent changes if rejected. Rewritten branch publication follows [rebase](../../ygo-rebase/SKILL.md), which owns the backup, remote-SHA checks and API race limitation.

Verify the remote commit, tree and intended changed files after publication before creating/updating the PR. If the available API cannot represent the complete change, preserve the reviewed local result and report the specific transport limitation; do not silently omit deletions, binary files or history. A connector-only issue/PR text write does not require local hook installation.

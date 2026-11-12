---
name: ygo-release
description: "Perform a YGO calculator version release, prepare release PRs, or write release notes. Complete release requests include authorized PR merges; preparation-only or notes-only requests stay limited."
---

# Release the calculator

Read [context](../ygo-implement/references/context.md) and [authorization](../ygo-implement/references/authorization.md). Determine the requested version and deliverable: complete release, preparation, or notes. Inspect current `dev`, `main`, existing release PRs/tags and the canonical version owner in source. Reuse completed stages and existing PRs; do not duplicate them.

For a complete release:

1. Inspect changes since the previous release and identify blockers. Update the canonical version through a feature branch and [PR delivery](../ygo-pr/SKILL.md) to `dev`; apply relevant [verification](../ygo-verify/SKILL.md).
2. Inspect the exact PR head, incoming commit identities under `repo:scripts/codex/PRIVACY.md`, required checks and mergeability. Merge the release preparation PR using the repository-supported method when ready. Do not bypass protection or force a failed merge.
3. Refresh branch state, create or reuse the `dev` to `main` release PR, check its exact contents/head, and merge when required checks pass. Promotion must preserve the intended release contents; do not rebase shared `dev` onto `main` as though it were an ordinary task branch.
4. Perform requested tagging, GitHub release publication or deployment steps using the established release convention; inspect existing releases first. Follow [infrastructure](../ygo-implement/references/infrastructure.md) for Cloudflare operations and verify the resulting version/deployment provenance.

No additional merge permission is needed for a complete release task. A request only to prepare PRs stops before merging; notes-only work reads history and returns text. Draft concise notes matching useful prior release descriptions, emphasizing user-visible changes rather than routine verification. Root README work remains separately scoped by the authorization reference.

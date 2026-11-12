# Complete release promotion

Use only for a complete release task, after preparation; reuse completed stages.

1. Inspect the preparation PR's exact head, incoming identities under [publication identity](../../ygo-pr/references/publication-identity.md), required checks and mergeability. Merge using the repository-supported method; do not bypass protection or force a failed merge.
2. Refresh branch state. Create/reuse the `dev` to `main` release PR, verify its exact contents/head, and merge when required checks pass. Preserve the intended release contents; never rebase shared `dev` onto `main` as a task branch.
3. Perform requested tags, GitHub release or deployment steps using the established convention, inspecting previous releases first. Use [infrastructure](../../ygo-implement/references/infrastructure.md) for Cloudflare operations and verify resulting version/deployment provenance.

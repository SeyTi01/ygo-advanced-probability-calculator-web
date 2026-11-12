---
name: ygo-release
description: "Perform a YGO calculator version release, prepare release PRs, or write release notes. Complete release requests include authorized PR merges; preparation-only or notes-only requests stay limited."
---

# Release the calculator

Read [context](../ygo-implement/references/context.md) and identify the requested version and deliverable.

For **notes only**, inspect the relevant release history and changes, draft concise user-visible notes matching useful prior descriptions, and stop. Runtime setup, version edits and merge checks are unnecessary.

For **preparation or a complete release**, read [authorization](../ygo-implement/references/authorization.md). Inspect changes since the previous release, current `dev`/`main`, release PRs/tags and the canonical version owner. Identify blockers and reuse completed stages or existing PRs. Update the version through a feature branch using [implementation preparation](../ygo-implement/SKILL.md#prepare-source-changes), relevant [verification](../ygo-verify/SKILL.md) and [PR delivery](../ygo-pr/SKILL.md) to `dev`.

Preparation-only work stops with the requested PRs open. For a **complete release**, continue with [promotion](references/promotion.md).

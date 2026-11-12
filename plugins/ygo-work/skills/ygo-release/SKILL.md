---
name: ygo-release
description: "Complete a YGO calculator release from one request: update the version, merge into dev and main, tag main, and publish the latest GitHub Release with minimal user-facing notes. Also handles preparation-only and notes-only requests without expanding their scope."
---

# Release the calculator

Read [context](../ygo-implement/references/context.md) and identify the requested version and deliverable. A request such as "release vX.Y.Z" means the complete workflow below, including both merges, the tag and the published latest GitHub Release. Continue through pending checks and completed stages without asking for routine authorization again. Ask for the version only if it cannot be established from the request or settled context.

For **notes only**, use [release notes](references/release-notes.md) and stop with the draft. Runtime setup, version edits, write authentication and merge checks are unnecessary.

For **preparation or a complete release**, read [authorization](../ygo-implement/references/authorization.md). Inspect the requested tag/release and existing PRs before making changes. Resolve current `dev`/`main`, the prior published stable release, current rules/checks, and `repo:YGOProbabilityCalculatorBlazor/Constants/ProjectConstants.cs` (`ProjectConstants.Version`). Keep the application version, session/engine schema versions and plugin package version separate.

If the requested release is already correctly published, verify its tag, version and release metadata, report the existing result and stop before preparing current `dev`. Do not reformat historical notes just to apply the new template. If a newer release now owns latest, report that fact without taking the marker back. Do not downgrade a newer `dev` version or promote later work merely to repeat an earlier release; reconcile conflicting in-progress release versions before proceeding.

For a complete release, establish the [GitHub publication route](references/github-publication.md#access) before starting version changes or merges. Keep a compact task record of the requested version/tag, predecessor tag, PR URLs and inspected heads, completed merges, final `main` SHA and release URL. Reconcile this record with remote state on resume; do not commit a release-state file.

Resume at the furthest verified stage before doing any preparation: a matching tag or merged promotion PR goes directly to final-commit verification and publication in [promotion](references/promotion.md), using the recorded release commit rather than preparing later `dev` changes; an already-merged version PR skips the version bump; an open version PR is reused. Inspect an existing draft Release together with its tag. A conflicting tag, release or advanced branch is reconciled before another write.

Only when version preparation remains necessary, update the canonical application version through a feature branch using [implementation preparation](../ygo-implement/SKILL.md#prepare-source-changes), relevant [verification](../ygo-verify/SKILL.md) and [PR delivery](../ygo-pr/SKILL.md) to `dev`. Do not create an empty bump. Inspect the actual release range and draft [release notes](references/release-notes.md).

**Preparation only** stops with the requested PRs open; it does not authorize a merge just to make a later PR possible. Report an unmet stage dependency. For a **complete release**, continue with [promotion](references/promotion.md) through verified publication. Ordinary stable releases are the default; explicit prerelease or historical-release instructions override latest-release behavior.
